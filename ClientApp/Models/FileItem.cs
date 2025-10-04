using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ClientApp.Models
{
    public class FileItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("folder_id")]
        public long? FolderId { get; set; }

        [JsonProperty("uploaded_by")]
        public long UploadedBy { get; set; }

        [JsonProperty("uploaded_by_name")]
        public string UploadedByName { get; set; }

        [JsonProperty("edited_by")]
        public long? EditedBy { get; set; }

        [JsonProperty("edited_by_name")]
        public string EditedByName { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        // Додаткові властивості для UI
        public string Extension => System.IO.Path.GetExtension(Name)?.ToLower();

        public string FileType
        {
            get
            {
                if (string.IsNullOrEmpty(Extension)) return "File";
                return Extension.TrimStart('.');
            }
        }

        public string SizeFormatted
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = Size;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        public bool IsImage => Extension == ".jpg" || Extension == ".jpeg" ||
                               Extension == ".png" || Extension == ".gif" ||
                               Extension == ".bmp";

        public bool IsCode => Extension == ".cs" || Extension == ".py" ||
                             Extension == ".js" || Extension == ".cpp" ||
                             Extension == ".java";

        public bool IsPreviewable => IsImage || IsCode;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FolderItem : INotifyPropertyChanged
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("parent_id")]
        public long? ParentId { get; set; }

        [JsonProperty("user_id")]
        public long UserId { get; set; }

        [JsonProperty("sync_path")]
        public string SyncPath { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public bool IsSynced => !string.IsNullOrEmpty(SyncPath);

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SyncConfig
    {
        public string LocalPath { get; set; }
        public long? RemoteFolderId { get; set; }
        public bool AutoSync { get; set; }
        public DateTime? LastSyncTime { get; set; }
    }

    public enum SortDirection
    {
        Ascending,
        Descending
    }

    public enum FileFilterType
    {
        All,
        Python,
        Images,
        Code
    }
}