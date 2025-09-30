using ClientApp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ClientApp.Services
{
    public class SyncService
    {
        private readonly FileService _fileService;
        private FileSystemWatcher _watcher;
        private SyncConfig _config;

        public event EventHandler<string> SyncStatusChanged;

        public SyncService(FileService fileService)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// Налаштування синхронізації папки
        /// </summary>
        public void ConfigureSync(string localPath, long? remoteFolderId = null, bool autoSync = false)
        {
            _config = new SyncConfig
            {
                LocalPath = localPath,
                RemoteFolderId = remoteFolderId,
                AutoSync = autoSync,
                LastSyncTime = DateTime.Now
            };

            if (autoSync)
            {
                StartAutoSync();
            }
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

            _watcher = new FileSystemWatcher(_config.LocalPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Created += async (s, e) => await OnFileCreated(e.FullPath);
            _watcher.Changed += async (s, e) => await OnFileChanged(e.FullPath);
            _watcher.Deleted += async (s, e) => await OnFileDeleted(e.Name);

            Debug.WriteLine($"Auto-sync started for: {_config.LocalPath}");
            OnSyncStatusChanged($"Автосинхронізація активована для {_config.LocalPath}");
        }

        /// <summary>
        /// Зупинка автоматичної синхронізації
        /// </summary>
        public void StopAutoSync()
        {
            if (_watcher != null)
            {
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

                foreach (var file in files)
                {
                    var result = await _fileService.UploadFileAsync(file, _config.RemoteFolderId);
                    if (result != null)
                    {
                        uploaded++;
                        OnSyncStatusChanged($"Завантажено: {Path.GetFileName(file)}");
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
                int downloaded = 0;

                foreach (var file in remoteFiles)
                {
                    var localPath = Path.Combine(_config.LocalPath, file.Name);

                    // Пропускаємо якщо файл вже існує і не змінювався
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

                _config.LastSyncTime = DateTime.Now;
                OnSyncStatusChanged($"Синхронізація завершена. Завантажено файлів: {downloaded}");

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
            await Task.Delay(1000); // Невелика затримка між операціями
            var downloaded = await SyncRemoteToLocalAsync();

            return (uploaded, downloaded);
        }

        private async Task OnFileCreated(string filePath)
        {
            try
            {
                await Task.Delay(500); // Чекаємо поки файл буде повністю записаний

                var result = await _fileService.UploadFileAsync(filePath, _config.RemoteFolderId);
                if (result != null)
                {
                    OnSyncStatusChanged($"Автоматично завантажено: {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error auto-uploading file: {ex.Message}");
            }
        }

        private async Task OnFileChanged(string filePath)
        {
            try
            {
                await Task.Delay(500);

                // Тут можна додати логіку оновлення існуючого файлу
                Debug.WriteLine($"File changed: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling file change: {ex.Message}");
            }
        }

        private async Task OnFileDeleted(string fileName)
        {
            try
            {
                // Тут можна додати логіку видалення файлу з сервера
                Debug.WriteLine($"File deleted: {fileName}");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling file deletion: {ex.Message}");
            }
        }

        protected virtual void OnSyncStatusChanged(string status)
        {
            SyncStatusChanged?.Invoke(this, status);
        }

        public SyncConfig GetConfig() => _config;
    }
}