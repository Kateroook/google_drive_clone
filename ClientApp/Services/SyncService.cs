using ClientApp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ClientApp.Services
{
    public partial class SyncService
    {
        private readonly FileService _fileService;
        private FileSystemWatcher _watcher;
        private SyncConfig _config;
        private readonly ConcurrentDictionary<string, DateTime> _lastEventAt =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastUploadedAt =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<string> SyncStatusChanged;

        public SyncService(FileService fileService)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// Налаштування синхронізації папки
        /// </summary>
        public async Task ConfigureSyncAsync(string localPath, long? remoteFolderId = null, bool autoSync = true)
        {
            // Якщо remoteFolderId не вказано, створюємо папку з ім'ям локальної папки
            if (!remoteFolderId.HasValue)
            {
                var localFolderName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));
                var createdFolder = await _fileService.CreateFolderAsync(localFolderName, null);
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

            _config = new SyncConfig
            {
                LocalPath = localPath,
                RemoteFolderId = remoteFolderId,
                AutoSync = autoSync,
                LastSyncTime = DateTime.Now
            };

            // Виконуємо початкову синхронізацію - завантажуємо всі існуючі файли
            OnSyncStatusChanged("Початкова синхронізація...");
            await SyncLocalToRemoteAsync();

            if (autoSync)
            {
                StartAutoSync();
            }
            else
            {
                StopAutoSync();
            }
        }

        /// <summary>
        /// Налаштування синхронізації папки (синхронна версія для зворотної сумісності)
        /// </summary>
        public void ConfigureSync(string localPath, long? remoteFolderId = null, bool autoSync = false)
        {
            _ = ConfigureSyncAsync(localPath, remoteFolderId, autoSync);
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
                              NotifyFilters.Size |
                              NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Created += OnFileSystemCreated;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Deleted += OnFileSystemDeleted;
            _watcher.Error += OnFileSystemError;

            Debug.WriteLine($"FileSystemWatcher configured:");
            Debug.WriteLine($"  - Path: {_watcher.Path}");
            Debug.WriteLine($"  - NotifyFilter: {_watcher.NotifyFilter}");
            Debug.WriteLine($"  - EnableRaisingEvents: {_watcher.EnableRaisingEvents}");

            OnSyncStatusChanged($"Автосинхронізація активована для {_config.LocalPath}");
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
                OnSyncStatusChanged("Автосинхронізація зупинена");
            }
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
                OnSyncStatusChanged("Початок синхронізації...");
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
                        if (remote.UpdatedAt >= localInfo.LastWriteTime)
                        {
                            continue;
                        }

                        await _fileService.DeleteFileAsync(remote.Id);
                    }

                    var result = await _fileService.UploadFileAsync(file, _config.RemoteFolderId);
                    if (result != null)
                    {
                        uploaded++;
                        OnSyncStatusChanged($"Завантажено: {fileName}");
                    }
                }

                _config.LastSyncTime = DateTime.Now;
                OnSyncStatusChanged($"Синхронізація завершена. Завантажено файлів: {uploaded}");

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
                OnSyncStatusChanged("Завантаження файлів з хмари...");

                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId);
                var localFiles = Directory.GetFiles(_config.LocalPath).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                        if (localFileInfo.LastWriteTime >= file.UpdatedAt)
                        {
                            continue;
                        }
                    }

                    var success = await _fileService.DownloadFileAsync(file.Id, localPath);
                    if (success)
                    {
                        downloaded++;
                        OnSyncStatusChanged($"Завантажено: {file.Name}");
                    }
                }

                // Видаляємо локальні файли, які більше не існують на сервері
                foreach (var localFileName in localFiles.ToList())
                {
                    if (!remoteByName.ContainsKey(localFileName))
                    {
                        var localPath = Path.Combine(_config.LocalPath, localFileName);
                        try
                        {
                            File.Delete(localPath);
                            deleted++;
                            OnSyncStatusChanged($"Видалено локально: {localFileName}");
                            Debug.WriteLine($"[SYNC] Deleted local file: {localFileName}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SYNC] Failed to delete local file {localFileName}: {ex.Message}");
                        }
                    }
                }

                _config.LastSyncTime = DateTime.Now;
                OnSyncStatusChanged($"Синхронізація завершена. Завантажено: {downloaded}, видалено: {deleted}");

                return downloaded;
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
            await Task.Delay(1000);
            var downloaded = await SyncRemoteToLocalAsync();

            return (uploaded, downloaded);
        }

        private async Task OnFileCreated(string filePath)
        {
            try
            {
                Debug.WriteLine($"[SYNC] OnFileCreated: {filePath}");

                if (ShouldIgnore(filePath))
                {
                    Debug.WriteLine($"[SYNC] Ignored (system file): {filePath}");
                    return;
                }

                // Для Office файлів, що були перейменовані з тимчасових, чекаємо довше
                var fileName = Path.GetFileName(filePath);
                var isOfficeFile = fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                                  fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                                  fileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) ||
                                  fileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase);

                if (isOfficeFile)
                {
                    // Для Office файлів чекаємо більше часу та менш суворо дебаунс
                    if (IsRapidDuplicate(filePath, 1000)) // 1 сек замість 2
                    {
                        Debug.WriteLine($"[SYNC] Ignored (duplicate event): {filePath}");
                        return;
                    }
                    await Task.Delay(1000); // Додаткова затримка для Office
                }
                else
                {
                    if (IsRapidDuplicate(filePath))
                    {
                        Debug.WriteLine($"[SYNC] Ignored (duplicate event): {filePath}");
                        return;
                    }
                }

                await WaitForFileReady(filePath);

                var localInfo = new FileInfo(filePath);
                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId) ?? new List<FileItem>();
                var existing = remoteFiles.FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Якщо файл існує, оновлюємо його замість створення нового
                    Debug.WriteLine($"[SYNC] File exists, updating: {fileName}");
                    var updated = await _fileService.UpdateFileAsync(existing.Id, filePath);
                    if (updated != null)
                    {
                        _lastUploadedAt[filePath] = DateTime.UtcNow;
                        OnSyncStatusChanged($"Оновлено: {fileName}");
                        Debug.WriteLine($"[SYNC] Update successful: {fileName}");
                    }
                    return;
                }

                Debug.WriteLine($"[SYNC] Uploading new file: {fileName}");
                var result = await _fileService.UploadFileAsync(filePath, _config.RemoteFolderId);

                if (result != null)
                {
                    _lastUploadedAt[filePath] = DateTime.UtcNow;
                    OnSyncStatusChanged($"Автоматично завантажено: {fileName}");
                    Debug.WriteLine($"[SYNC] Upload successful: {fileName}");
                }
                else
                {
                    Debug.WriteLine($"[SYNC] Upload failed: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error auto-uploading file: {ex.Message}");
                Debug.WriteLine($"[SYNC] Stack trace: {ex.StackTrace}");
            }
        }

        private async Task OnFileChanged(string filePath)
        {
            try
            {
                Debug.WriteLine($"[SYNC] OnFileChanged: {filePath}");

                if (ShouldIgnore(filePath))
                {
                    Debug.WriteLine($"[SYNC] Ignored (system file): {filePath}");
                    return;
                }

                if (IsRapidDuplicate(filePath, 3000)) // 3 секунди для Changed
                {
                    Debug.WriteLine($"[SYNC] Ignored (duplicate event): {filePath}");
                    return;
                }

                if (_lastUploadedAt.TryGetValue(filePath, out var lastUp) &&
                    (DateTime.UtcNow - lastUp).TotalSeconds < 10)
                {
                    Debug.WriteLine($"[SYNC] Ignored (recently uploaded): {filePath}");
                    return;
                }

                await WaitForFileReady(filePath);
                var fileName = Path.GetFileName(filePath);

                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId)
                                  ?? new List<FileItem>();
                var remote = remoteFiles.FirstOrDefault(f =>
                    string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                 if (remote != null)
                 {
                     var localInfo = new FileInfo(filePath);
                     bool localNewer = localInfo.LastWriteTime > remote.UpdatedAt.AddSeconds(2);
                     bool sizeDiffers = Math.Abs(remote.Size - localInfo.Length) > 100; // Толерантність 100 байт

                     Debug.WriteLine($"[SYNC] Comparison for {fileName}:");
                     Debug.WriteLine($"  Local time: {localInfo.LastWriteTime}, Remote time: {remote.UpdatedAt}");
                     Debug.WriteLine($"  Local size: {localInfo.Length}, Remote size: {remote.Size}");
                     Debug.WriteLine($"  LocalNewer: {localNewer}, SizeDiffers: {sizeDiffers}");

                     if (localNewer || sizeDiffers)
                     {
                         Debug.WriteLine($"[SYNC] Updating file on server: {fileName}");
                         var updated = await _fileService.UpdateFileAsync(remote.Id, filePath);

                         if (updated != null)
                         {
                             _lastUploadedAt[filePath] = DateTime.UtcNow;
                             OnSyncStatusChanged($"Оновлено на сервері: {fileName}");
                             Debug.WriteLine($"[SYNC] Update successful: {fileName}");
                         }
                         else
                         {
                             Debug.WriteLine($"[SYNC] Update failed: {fileName}");
                             // Fallback: try delete and re-upload
                             Debug.WriteLine($"[SYNC] Trying fallback: delete and re-upload");
                             await _fileService.DeleteFileAsync(remote.Id);
                             var uploaded = await _fileService.UploadFileAsync(filePath, _config.RemoteFolderId);
                             if (uploaded != null)
                             {
                                 _lastUploadedAt[filePath] = DateTime.UtcNow;
                                 OnSyncStatusChanged($"Перезавантажено на сервері: {fileName}");
                                 Debug.WriteLine($"[SYNC] Fallback upload successful: {fileName}");
                             }
                         }
                     }
                     else
                     {
                         Debug.WriteLine($"[SYNC] No update needed: {fileName}");
                     }
                 }
                 else
                 {
                     Debug.WriteLine($"[SYNC] File not found on server, uploading: {fileName}");
                     await OnFileCreated(filePath);
                 }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error handling file change: {ex.Message}");
                Debug.WriteLine($"[SYNC] Stack trace: {ex.StackTrace}");
            }
        }

        private async Task OnFileDeleted(string fileName)
        {
            try
            {
                Debug.WriteLine($"[SYNC] OnFileDeleted: {fileName}");

                // Додаємо невелику затримку перед видаленням
                await Task.Delay(500);

                var remoteFiles = await _fileService.GetFilesAsync(_config.RemoteFolderId)
                                  ?? new List<FileItem>();
                var remote = remoteFiles.FirstOrDefault(f =>
                    string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                if (remote != null)
                {
                    Debug.WriteLine($"[SYNC] Deleting from server: {fileName} (ID: {remote.Id})");
                    var success = await _fileService.DeleteFileAsync(remote.Id);

                    if (success)
                    {
                        OnSyncStatusChanged($"Видалено з сервера: {fileName}");
                        Debug.WriteLine($"[SYNC] Delete successful: {fileName}");
                    }
                    else
                    {
                        Debug.WriteLine($"[SYNC] Delete failed: {fileName}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[SYNC] File not found on server: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC] Error handling file deletion: {ex.Message}");
                Debug.WriteLine($"[SYNC] Stack trace: {ex.StackTrace}");
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

                // Ignore common temp/lock artifacts
                if (name.StartsWith("~$")) return true;
                if (name.StartsWith(".")) return true; // Hidden files
                var ext = Path.GetExtension(name)?.ToLower();
                if (ext == ".tmp" || ext == ".temp" || ext == ".part" || ext == ".crdownload") return true;
                return false;
            }
            catch { return true; }
        }

        private bool IsRapidDuplicate(string filePath, int debounceMs = 2000)
        {
            var now = DateTime.UtcNow;
            var last = _lastEventAt.GetOrAdd(filePath, now);
            var isDuplicate = (now - last).TotalMilliseconds < debounceMs;
            _lastEventAt[filePath] = now;
            
            // Для файлів, що не існують (були видалені або перейменовані), не вважаємо дублікатом
            if (!File.Exists(filePath))
            {
                return false;
            }
            
            return isDuplicate;
        }

        private async Task WaitForFileReady(string filePath)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        Debug.WriteLine($"[SYNC] File ready: {filePath}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SYNC] File not ready (attempt {i + 1}): {ex.Message}");
                    await Task.Delay(250);
                }
            }
            Debug.WriteLine($"[SYNC] WARNING: File may not be fully ready: {filePath}");
        }
    }
}