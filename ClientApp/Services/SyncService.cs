using ClientApp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ClientApp.Services
{
    public partial class SyncService
    {
        private readonly FileService _fileService;
        private FileSystemWatcher _watcher;
        private SyncConfig _config;
        private Timer _serverPollingTimer;
        private readonly ConcurrentDictionary<string, DateTime> _lastEventAt =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastUploadedAt =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private bool _isPolling = false;

        public event EventHandler<string> SyncStatusChanged;

        public SyncService(FileService fileService)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// Налаштування синхронізації з початковою повною синхронізацією
        /// </summary>
        public async Task ConfigureSyncAsync(string localPath, long? remoteFolderId = null, bool autoSync = true, bool performInitialSync = true)
        {
            if (!remoteFolderId.HasValue)
            {
                var localFolderName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));
                var createdFolder = await _fileService.CreateFolderAsync(localFolderName, null, localPath);
                if (createdFolder != null)
                {
                    remoteFolderId = createdFolder.Id;
                    Debug.WriteLine($"[SYNC] Created remote folder: {localFolderName} (ID: {remoteFolderId})");
                }
                else
                {
                    Debug.WriteLine($"[SYNC] Failed to create remote folder: {localFolderName}");
                }
            }
            else
            {
                var folders = await _fileService.GetAllFoldersAsync();
                var folder = folders.FirstOrDefault(f => f.Id == remoteFolderId.Value);

                if (folder != null && folder.SyncPath != localPath)
                {
                    Debug.WriteLine($"[SYNC] Updating sync_path for folder {remoteFolderId.Value}");
                    await _fileService.UpdateFolderSyncAsync(remoteFolderId.Value, localPath);
                }
            }

            _config = new SyncConfig
            {
                LocalPath = localPath,
                RemoteFolderId = remoteFolderId,
                AutoSync = autoSync,
                LastSyncTime = DateTime.Now
            };

            // Початкова синхронізація ТІЛЬКИ якщо це нова конфігурація
            if (performInitialSync)
            {
                OnSyncStatusChanged("Початкова синхронізація...");
                await SyncBothWaysAsync();
            }
            else
            {
                Debug.WriteLine("[SYNC] Skipping initial sync - activating existing configuration");
                OnSyncStatusChanged("Синхронізація активована");
            }

            if (autoSync)
            {
                StartAutoSync();
                StartServerPolling();
            }
            else
            {
                StopAutoSync();
                StopServerPolling();
            }
        }

        /// <summary>
        /// Активація існуючої синхронізації без початкової синхронізації
        /// </summary>
        public async Task ActivateExistingSyncAsync(string localPath, long remoteFolderId)
        {
            Debug.WriteLine($"[SYNC] Activating existing sync for folder {remoteFolderId}");
            await ConfigureSyncAsync(localPath, remoteFolderId, autoSync: true, performInitialSync: false);
        }

        /// <summary>
        /// Запуск автоматичної синхронізації
        /// </summary>
        private void StartAutoSync()
        {
            if (_watcher != null)
            {
                _watcher.Dispose();
            }

            Debug.WriteLine($"=== Starting Auto-Sync for: {_config.LocalPath} ===");

            _watcher = new FileSystemWatcher(_config.LocalPath)
            {
                NotifyFilter = NotifyFilters.FileName |
                              NotifyFilters.LastWrite |
                              NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Created += OnFileSystemCreated;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Deleted += OnFileSystemDeleted;
            _watcher.Error += OnFileSystemError;

            OnSyncStatusChanged($"Автосинхронізація активована для {_config.LocalPath}");
        }

        /// <summary>
        /// Запуск періодичної перевірки змін на сервері
        /// </summary>
        private void StartServerPolling()
        {
            // Перевіряємо зміни на сервері кожні 10 секунд
            _serverPollingTimer = new Timer(async _ => await PollServerChanges(), null,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            Debug.WriteLine("[SYNC] Server polling started (every 10 seconds)");
        }

        /// <summary>
        /// Зупинка періодичної перевірки
        /// </summary>
        private void StopServerPolling()
        {
            if (_serverPollingTimer != null)
            {
                _serverPollingTimer.Dispose();
                _serverPollingTimer = null;
                Debug.WriteLine("[SYNC] Server polling stopped");
            }
        }

        /// <summary>
        /// Перевірка змін на сервері
        /// </summary>
        private async Task PollServerChanges()
        {
            if (_isPolling || _config == null) return;

            _isPolling = true;
            try
            {
                Debug.WriteLine("[SYNC] Polling server for changes...");
                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId);

                if (remoteFiles == null)
                {
                    Debug.WriteLine("[SYNC] Failed to poll server - will retry next cycle");
                    return;
                }

                var localFiles = Directory.GetFiles(_config.LocalPath)
                    .Select(f => Path.GetFileName(f))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var remote in remoteFiles)
                {
                    var localPath = Path.Combine(_config.LocalPath, remote.Name);

                    if (!File.Exists(localPath))
                    {
                        Debug.WriteLine($"[SYNC] New file on server, downloading: {remote.Name}");
                        var success = await _fileService.DownloadFileAsync(remote.Id, localPath);
                        if (success)
                        {
                            // ВАЖЛИВО: зберігаємо час завантаження, щоб не тригерити Changed
                            _lastUploadedAt[localPath] = DateTime.UtcNow;
                            OnSyncStatusChanged($"📥 Завантажено з сервера: {remote.Name}");
                        }
                    }
                    else
                    {
                        var localInfo = new FileInfo(localPath);

                        // Перевіряємо чи файл на сервері новіший
                        if (remote.UpdatedAt > localInfo.LastWriteTime.AddSeconds(2))
                        {
                            Debug.WriteLine($"[SYNC] Server file is newer, downloading: {remote.Name}");
                            var success = await _fileService.DownloadFileAsync(remote.Id, localPath);
                            if (success)
                            {
                                // ВАЖЛИВО: оновлюємо timestamp
                                _lastUploadedAt[localPath] = DateTime.UtcNow;
                                OnSyncStatusChanged($"🔄 Оновлено з сервера: {remote.Name}");
                            }
                        }
                    }
                }

                var remoteNames = remoteFiles.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var localFileName in localFiles)
                {
                    if (!remoteNames.Contains(localFileName))
                    {
                        var localPath = Path.Combine(_config.LocalPath, localFileName);
                        try
                        {
                            File.Delete(localPath);
                            // Видаляємо з кешу
                            _lastUploadedAt.TryRemove(localPath, out _);
                            _lastEventAt.TryRemove(localPath, out _);

                            Debug.WriteLine($"[SYNC] File deleted from server, removing local: {localFileName}");
                            OnSyncStatusChanged($"🗑 Видалено локально: {localFileName}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SYNC] Failed to delete local file: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error polling server: {ex.Message}");
            }
            finally
            {
                _isPolling = false;
            }
        }

        private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine($"[FSW] Created: {e.Name}");
            _ = OnFileCreated(e.FullPath);
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine($"[FSW] Changed: {e.Name}");
            _ = OnFileChanged(e.FullPath);
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            Debug.WriteLine($"[FSW] Renamed: {e.OldName} -> {e.Name}");
            _ = OnFileCreated(e.FullPath);
        }

        private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine($"[FSW] Deleted: {e.Name}");
            _ = OnFileDeleted(e.Name);
        }

        private void OnFileSystemError(object sender, ErrorEventArgs e)
        {
            Debug.WriteLine($"[FSW] Error: {e.GetException()?.Message}");
        }

        /// <summary>
        /// Зупинка автоматичної синхронізації
        /// </summary>
        public void StopAutoSync()
        {
            if (_watcher != null)
            {
                Debug.WriteLine("=== Stopping Auto-Sync ===");
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
            StopServerPolling();
            OnSyncStatusChanged("Автосинхронізація зупинена");
        }

        /// <summary>
        /// Ручна синхронізація (завантаження всіх файлів з локальної папки)
        /// </summary>
        public async Task<int> SyncLocalToRemoteAsync()
        {
            if (_config == null || string.IsNullOrEmpty(_config.LocalPath))
            {
                OnSyncStatusChanged("Налаштування синхронізації не знайдено");
                return 0;
            }

            try
            {
                var files = Directory.GetFiles(_config.LocalPath);
                int uploaded = 0;

                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId)
                                  ?? new List<FileItem>();
                var remoteByName = remoteFiles.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var localInfo = new FileInfo(file);

                    if (remoteByName.TryGetValue(fileName, out var remote))
                    {
                        // Файл існує - перевіряємо чи потрібно оновлення
                        if (localInfo.LastWriteTime > remote.UpdatedAt.AddSeconds(2))
                        {
                            await _fileService.UpdateFileAsync(remote.Id, file);
                            uploaded++;
                            OnSyncStatusChanged($"📤 Оновлено: {fileName}");
                        }
                    }
                    else
                    {
                        // Новий файл - завантажуємо
                        var result = await _fileService.UploadFileAsync(file, _config.RemoteFolderId);
                        if (result != null)
                        {
                            uploaded++;
                            OnSyncStatusChanged($"📤 Завантажено: {fileName}");
                        }
                    }
                }

                _config.LastSyncTime = DateTime.Now;
                return uploaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sync error: {ex.Message}");
                OnSyncStatusChanged($"Помилка синхронізації: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Синхронізація з сервера на локальний диск
        /// </summary>
        public async Task<int> SyncRemoteToLocalAsync()
        {
            if (_config == null || string.IsNullOrEmpty(_config.LocalPath))
            {
                OnSyncStatusChanged("Налаштування синхронізації не знайдено");
                return 0;
            }

            try
            {
                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId);

                // Перевірка на помилку отримання файлів
                if (remoteFiles == null)
                {
                    Debug.WriteLine("[SYNC] Failed to fetch remote files - skipping sync");
                    OnSyncStatusChanged("⚠️ Помилка зв'язку з сервером");
                    return 0;
                }

                var localFiles = Directory.GetFiles(_config.LocalPath)
                    .Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var remoteByName = remoteFiles.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

                int downloaded = 0;
                int deleted = 0;

                // Завантажуємо нові та оновлені файли з сервера
                foreach (var file in remoteFiles)
                {
                    var localPath = Path.Combine(_config.LocalPath, file.Name);

                    if (File.Exists(localPath))
                    {
                        var localFileInfo = new FileInfo(localPath);
                        if (file.UpdatedAt > localFileInfo.LastWriteTime.AddSeconds(2))
                        {
                            var success = await _fileService.DownloadFileAsync(file.Id, localPath);
                            if (success)
                            {
                                downloaded++;
                                OnSyncStatusChanged($"📥 Оновлено: {file.Name}");
                            }
                        }
                    }
                    else
                    {
                        var success = await _fileService.DownloadFileAsync(file.Id, localPath);
                        if (success)
                        {
                            downloaded++;
                            OnSyncStatusChanged($"📥 Завантажено: {file.Name}");
                        }
                    }
                }

                // Видаляємо локальні файли, які більше не існують на сервері
                foreach (var localFileName in localFiles.ToList())
                {
                    if (!remoteByName.ContainsKey(localFileName) &&
                        !ShouldIgnore(Path.Combine(_config.LocalPath, localFileName)))
                    {
                        var localPath = Path.Combine(_config.LocalPath, localFileName);
                        try
                        {
                            File.Delete(localPath);
                            deleted++;
                            OnSyncStatusChanged($"🗑 Видалено: {localFileName}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SYNC] Failed to delete local file {localFileName}: {ex.Message}");
                        }
                    }
                }

                _config.LastSyncTime = DateTime.Now;
                return downloaded + deleted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sync error: {ex.Message}");
                OnSyncStatusChanged($"Помилка синхронізації: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Двостороння синхронізація
        /// </summary>
        public async Task<(int uploaded, int downloaded)> SyncBothWaysAsync()
        {
            var uploaded = await SyncLocalToRemoteAsync();
            await Task.Delay(500);
            var downloaded = await SyncRemoteToLocalAsync();

            return (uploaded, downloaded);
        }

        private async Task OnFileCreated(string filePath)
        {
            try
            {
                if (ShouldIgnore(filePath)) return;

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[SYNC] File no longer exists: {Path.GetFileName(filePath)}");
                    return;
                }

                if (IsRapidDuplicate(filePath)) return;

                await WaitForFileReady(filePath);

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[SYNC] File disappeared during wait: {Path.GetFileName(filePath)}");
                    return;
                }

                var fileName = Path.GetFileName(filePath);
                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId) ?? new List<FileItem>();
                var existing = remoteFiles.FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    Debug.WriteLine($"[SYNC] File exists on server, updating: {fileName}");
                    var updated = await _fileService.UpdateFileAsync(existing.Id, filePath);
                    if (updated != null)
                    {
                        // Зберігаємо час завантаження в UTC
                        _lastUploadedAt[filePath] = DateTime.UtcNow;
                        OnSyncStatusChanged($"🔄 Оновлено: {fileName}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[SYNC] Uploading new file: {fileName}");
                    var result = await _fileService.UploadFileAsync(filePath, _config.RemoteFolderId);

                    if (result != null)
                    {
                        _lastUploadedAt[filePath] = DateTime.UtcNow;
                        OnSyncStatusChanged($"📤 Завантажено: {fileName}");
                    }
                    else
                    {
                        Debug.WriteLine($"[SYNC] Failed to upload: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error in OnFileCreated: {ex.Message}");
            }
        }
        private async Task OnFileChanged(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                Debug.WriteLine($"[FSW-CHANGE] Event for: {fileName}");

                if (ShouldIgnore(filePath))
                {
                    Debug.WriteLine($"[FSW-CHANGE] Ignored: {fileName}");
                    return;
                }

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[FSW-CHANGE] File doesn't exist: {fileName}");
                    return;
                }

                if (IsRapidDuplicate(filePath, 3000))
                {
                    Debug.WriteLine($"[FSW-CHANGE] Debounced: {fileName}");
                    return;
                }

                var localInfo = new FileInfo(filePath);

                if (_lastUploadedAt.TryGetValue(filePath, out var lastUp))
                {
                    var timeSince = (DateTime.UtcNow - lastUp).TotalSeconds;
                    Debug.WriteLine($"[FSW-CHANGE] Last upload was {timeSince:F1}s ago for: {fileName}");

                    if (timeSince < 10)
                    {
                        Debug.WriteLine($"[FSW-CHANGE] ✓ SKIPPED - recently uploaded: {fileName}");
                        return;
                    }

                    if (localInfo.LastWriteTimeUtc <= lastUp.AddSeconds(2))
                    {
                        Debug.WriteLine($"[FSW-CHANGE] ✓ SKIPPED - file timestamp hasn't changed: {fileName}");
                        return;
                    }
                }

                await WaitForFileReady(filePath);

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[FSW-CHANGE] File disappeared: {fileName}");
                    return;
                }

                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId) ?? new List<FileItem>();
                var remote = remoteFiles.FirstOrDefault(f =>
                    string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                if (remote != null)
                {
                    bool localNewer = localInfo.LastWriteTime > remote.UpdatedAt.AddSeconds(2);
                    bool sizeDiffers = Math.Abs(remote.Size - localInfo.Length) > 100;

                    Debug.WriteLine($"[FSW-CHANGE] Comparison for {fileName}:");
                    Debug.WriteLine($"  Local time: {localInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                    Debug.WriteLine($"  Remote time: {remote.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
                    Debug.WriteLine($"  Local size: {localInfo.Length} bytes");
                    Debug.WriteLine($"  Remote size: {remote.Size} bytes");
                    Debug.WriteLine($"  LocalNewer: {localNewer}, SizeDiffers: {sizeDiffers}");

                    if (localNewer || sizeDiffers)
                    {
                        Debug.WriteLine($"[FSW-CHANGE] ⚠ UPDATING file: {fileName}");
                        var updated = await _fileService.UpdateFileAsync(remote.Id, filePath);

                        if (updated != null)
                        {
                            _lastUploadedAt[filePath] = DateTime.UtcNow;
                            OnSyncStatusChanged($"🔄 Оновлено: {fileName}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[FSW-CHANGE] ✓ SKIPPED - no real changes: {fileName}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[FSW-CHANGE] File not on server, creating: {fileName}");
                    await OnFileCreated(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error in OnFileChanged: {ex.Message}");
            }
        }
        private async Task OnFileDeleted(string fileName)
        {
            try
            {
                Debug.WriteLine($"[SYNC] OnFileDeleted: {fileName}");
                await Task.Delay(500);

                var fullPath = Path.Combine(_config.LocalPath, fileName);
                _lastUploadedAt.TryRemove(fullPath, out _);
                _lastEventAt.TryRemove(fullPath, out _);

                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId) ?? new List<FileItem>();
                var remote = remoteFiles.FirstOrDefault(f =>
                    string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                if (remote != null)
                {
                    Debug.WriteLine($"[SYNC] Deleting from server: {fileName}");
                    var success = await _fileService.DeleteFileAsync(remote.Id);

                    if (success)
                    {
                        OnSyncStatusChanged($"🗑 Видалено з сервера: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error in OnFileDeleted: {ex.Message}");
            }
        }

        protected virtual void OnSyncStatusChanged(string status)
        {
            SyncStatusChanged?.Invoke(this, status);
        }

        public SyncConfig GetConfig() => _config;
    }

    // Helpers
    partial class SyncService
    {
        private bool ShouldIgnore(string filePath)
        {
            try
            {
                var name = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(name)) return true;
                if (Directory.Exists(filePath)) return true;

                if (name.StartsWith("~$")) return true;
                if (name.StartsWith(".")) return true;
                var ext = Path.GetExtension(name)?.ToLower();
                if (ext == ".tmp" || ext == ".temp" || ext == ".part" || ext == ".crdownload") return true;
                return false;
            }
            catch { return true; }
        }

        private bool IsRapidDuplicate(string filePath, int debounceMs = 2000)
        {
            var now = DateTime.UtcNow;

            if (_lastEventAt.TryGetValue(filePath, out var last))
            {
                var isDuplicate = (now - last).TotalMilliseconds < debounceMs;
                if (isDuplicate)
                {
                    Debug.WriteLine($"[SYNC] Debouncing rapid event for: {Path.GetFileName(filePath)}");
                    return true;
                }
            }

            _lastEventAt[filePath] = now;
            return false;
        }
        private async Task WaitForFileReady(string filePath)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return;
                    }
                }
                catch
                {
                    await Task.Delay(250);
                }
            }
        }
    }
}