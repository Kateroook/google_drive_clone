using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Services
{
    public class SyncService
    {
        private readonly FileSystemWatcher _watcher;
        private readonly ApiService _api;

        public SyncService(string localPath, ApiService api)
        {
            _api = api;
            _watcher = new FileSystemWatcher(localPath);
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnDeleted;
            _watcher.EnableRaisingEvents = true;
        }

        private async void OnChanged(object sender, FileSystemEventArgs e)
        {
            await _api.UploadFileAsync(e.FullPath);
        }

        private async void OnDeleted(object sender, FileSystemEventArgs e)
        {
            // TODO: знайти відповідний fileId та викликати _api.DeleteFileAsync(fileId);
        }
    }
}
