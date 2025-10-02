using ClientApp.Models;
using ClientApp.Services;
using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.Generic;

using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using ClientApp.Views;
using Path = System.IO.Path;

namespace ClientApp.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly OAuthService _oAuthService;
        private readonly FileService _fileService;
        private readonly SyncService _syncService;
        private ObservableCollection<FileItem> _allFiles;
        private ObservableCollection<FileItem> _filteredFiles;
        private ObservableCollection<FolderItem> _folders;
        private FileItem _selectedFile;
        private long? _currentFolderId = null;

        public MainWindow(OAuthService oAuthService, UserInfo userInfo)
        {
            InitializeComponent();

            _oAuthService = oAuthService;
            _fileService = new FileService(_oAuthService);
            _syncService = new SyncService(_fileService);

            _allFiles = new ObservableCollection<FileItem>();
            _filteredFiles = new ObservableCollection<FileItem>();
            _folders = new ObservableCollection<FolderItem>();

            FilesDataGrid.ItemsSource = _filteredFiles;
            FoldersListBox.ItemsSource = _folders;

            UserNameText.Text = userInfo.Name;

            _syncService.SyncStatusChanged += OnSyncStatusChanged;

            _ = LoadDataAsync();
        }

        #region File Operations

        private async Task LoadDataAsync()
        {
            await LoadFoldersAsync();
            await LoadFilesAsync();
        }

        private async Task LoadFoldersAsync()
        {
            try
            {
                var folders = await _fileService.GetFoldersAsync(_currentFolderId);

                _folders.Clear();
                foreach (var folder in folders)
                {
                    _folders.Add(folder);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading folders: {ex.Message}");
            }
        }

        private async Task LoadFilesAsync()
        {
            try
            {
                StatusText.Text = "Loading files...";

                var files = await _fileService.GetFilesAsync(_currentFolderId);

                _allFiles.Clear();
                foreach (var file in files)
                {
                    _allFiles.Add(file);
                }

                ApplyFilter();

                FileCountText.Text = $"{_filteredFiles.Count} files, {_folders.Count} folders";
                StatusText.Text = "Ready";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error loading files";
            }
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to upload"
            };

            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = "Uploading files...";
                int uploaded = 0;

                foreach (var filePath in dialog.FileNames)
                {
                    var result = await _fileService.UploadFileAsync(filePath, _currentFolderId);
                    if (result != null)
                    {
                        _allFiles.Add(result);
                        uploaded++;
                    }
                }

                ApplyFilter();
                FileCountText.Text = $"{_filteredFiles.Count} files, {_folders.Count} folders";
                StatusText.Text = $"Uploaded {uploaded} file(s)";
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = FilesDataGrid.SelectedItems.Cast<FileItem>().ToList();

            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Please select files to download",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new FolderPickerDialog();
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = "Downloading files...";
                int downloaded = 0;

                foreach (var file in selectedFiles)
                {
                    var savePath = Path.Combine(dialog.SelectedPath, file.Name);
                    var success = await _fileService.DownloadFileAsync(file.Id, savePath);
                    if (success) downloaded++;
                }

                StatusText.Text = $"Downloaded {downloaded} file(s)";
                MessageBox.Show($"Downloaded {downloaded} file(s) successfully!",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = FilesDataGrid.SelectedItems.Cast<FileItem>().ToList();

            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Please select files to delete",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedFiles.Count} file(s)?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                StatusText.Text = "Deleting files...";
                int deleted = 0;

                foreach (var file in selectedFiles)
                {
                    var success = await _fileService.DeleteFileAsync(file.Id);
                    if (success)
                    {
                        _allFiles.Remove(file);
                        deleted++;
                    }
                }

                ApplyFilter();
                FileCountText.Text = $"{_filteredFiles.Count} files, {_folders.Count} folders";
                StatusText.Text = $"Deleted {deleted} file(s)";
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextInputDialog("Enter folder name:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
            {
                var folder = await _fileService.CreateFolderAsync(dialog.ResponseText, _currentFolderId);
                if (folder != null)
                {
                    _folders.Add(folder);
                    FileCountText.Text = $"{_filteredFiles.Count} files, {_folders.Count} folders";
                    StatusText.Text = $"Folder '{folder.Name}' created";
                    MessageBox.Show($"Folder '{folder.Name}' created successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to create folder. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void FoldersListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FoldersListBox.SelectedItem is FolderItem folder)
            {
                _currentFolderId = folder.Id;
                StatusText.Text = $"Opening folder: {folder.Name}";
                BackButton.IsEnabled = true;
                await LoadDataAsync();
            }
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentFolderId = null;
            BackButton.IsEnabled = false;
            StatusText.Text = "Root folder";
            await LoadDataAsync();
        }

        #endregion

        #region Filter and Sort

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySort();
        }

        private void ApplyFilter()
        {
            if (FilterComboBox == null || _allFiles == null) return;

            var filterIndex = FilterComboBox.SelectedIndex;
            IEnumerable<FileItem> filtered = _allFiles;

            switch (filterIndex)
            {
                case 1: // Python files
                    filtered = _allFiles.Where(f => f.Extension == ".py");
                    break;
                case 2: // Images
                    filtered = _allFiles.Where(f => f.IsImage);
                    break;
                case 3: // Code files
                    filtered = _allFiles.Where(f => f.IsCode);
                    break;
                default: // All files
                    filtered = _allFiles;
                    break;
            }

            _filteredFiles.Clear();
            foreach (var file in filtered)
            {
                _filteredFiles.Add(file);
            }

            ApplySort();
        }

        private void ApplySort()
        {
            if (SortComboBox == null || _filteredFiles == null || _filteredFiles.Count == 0) return;

            var sortIndex = SortComboBox.SelectedIndex;
            List<FileItem> sorted;

            switch (sortIndex)
            {
                case 0: // Name A-Z
                    sorted = _filteredFiles.OrderBy(f => f.Name).ToList();
                    break;
                case 1: // Name Z-A
                    sorted = _filteredFiles.OrderByDescending(f => f.Name).ToList();
                    break;
                case 2: // Date Newest
                    sorted = _filteredFiles.OrderByDescending(f => f.CreatedAt).ToList();
                    break;
                case 3: // Date Oldest
                    sorted = _filteredFiles.OrderBy(f => f.CreatedAt).ToList();
                    break;
                case 4: // Uploader A-Z
                    sorted = _filteredFiles.OrderBy(f => f.UploadedByName).ToList();
                    break;
                case 5: // Uploader Z-A
                    sorted = _filteredFiles.OrderByDescending(f => f.UploadedByName).ToList();
                    break;
                default:
                    return;
            }

            _filteredFiles.Clear();
            foreach (var file in sorted)
            {
                _filteredFiles.Add(file);
            }
        }

        #endregion

        #region Column Visibility

        private void ColumnVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (CreatedColumn == null) return;

            CreatedColumn.Visibility = ShowCreatedDateCheckBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            UpdatedColumn.Visibility = ShowUpdatedDateCheckBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            UploaderColumn.Visibility = ShowUploaderCheckBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            EditorColumn.Visibility = ShowEditorCheckBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            SizeColumn.Visibility = ShowSizeCheckBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Preview

        private async void FilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is FileItem file)
            {
                _selectedFile = file;
                await ShowPreview(file);
            }
        }

        private async void FilesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_selectedFile != null && _selectedFile.IsPreviewable)
            {
                await ShowPreview(_selectedFile);
            }
        }

        private async Task ShowPreview(FileItem file)
        {
            // Reset preview
            NoPreviewText.Visibility = Visibility.Collapsed;
            ImagePreview.Visibility = Visibility.Collapsed;
            CodePreviewBorder.Visibility = Visibility.Collapsed;
            FileInfoPanel.Visibility = Visibility.Visible;

            // Show file info
            FileNameInfo.Text = $"Name: {file.Name}";
            FileSizeInfo.Text = $"Size: {file.SizeFormatted}";
            FileTypeInfo.Text = $"Type: {file.FileType}";
            FileCreatedInfo.Text = $"Created: {file.CreatedAt:yyyy-MM-dd HH:mm:ss}";
            FileUpdatedInfo.Text = $"Updated: {file.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
            FileUploaderInfo.Text = $"Uploaded by: {file.UploadedByName}";

            try
            {
                if (file.IsImage)
                {
                    // Show image preview
                    var imageBytes = await _fileService.GetImageAsync(file.Id);
                    if (imageBytes != null)
                    {
                        using (var ms = new MemoryStream(imageBytes))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            ImagePreview.Source = bitmap;
                        }
                        ImagePreview.Visibility = Visibility.Visible;
                    }
                }
                else if (file.IsCode)
                {
                    // Show code preview
                    var content = await _fileService.GetFileContentAsync(file.Id);
                    if (content != null)
                    {
                        CodePreview.Text = content;
                        CodePreviewBorder.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    NoPreviewText.Text = "Preview not available for this file type";
                    NoPreviewText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Preview error: {ex.Message}");
                NoPreviewText.Text = "Error loading preview";
                NoPreviewText.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region Sync

        private async void SelectSyncFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderPickerDialog();
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = "Configuring sync...";
                await _syncService.ConfigureSyncAsync(dialog.SelectedPath, _currentFolderId, AutoSyncCheckBox.IsChecked == true);
                SyncFolderText.Text = dialog.SelectedPath;
                StatusText.Text = "Sync folder configured";
                
                // Refresh folders to show the newly created one
                await LoadFoldersAsync();
            }
        }

        private async void SyncUpload_Click(object sender, RoutedEventArgs e)
        {
            if (_syncService.GetConfig() == null)
            {
                MessageBox.Show("Please select a sync folder first",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var count = await _syncService.SyncLocalToRemoteAsync();
            await LoadFilesAsync();
        }

        private async void SyncDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_syncService.GetConfig() == null)
            {
                MessageBox.Show("Please select a sync folder first",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await _syncService.SyncRemoteToLocalAsync();
        }

        private async void SyncBoth_Click(object sender, RoutedEventArgs e)
        {
            if (_syncService.GetConfig() == null)
            {
                MessageBox.Show("Please select a sync folder first",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (uploaded, downloaded) = await _syncService.SyncBothWaysAsync();
            await LoadFilesAsync();
            MessageBox.Show(
                $"Sync complete!\nUploaded: {uploaded} files\nDownloaded: {downloaded} files",
                "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void AutoSync_Changed(object sender, RoutedEventArgs e)
        {
            var config = _syncService.GetConfig();
            if (config != null)
            {
                await _syncService.ConfigureSyncAsync(config.LocalPath, config.RemoteFolderId,
                    AutoSyncCheckBox.IsChecked == true);
            }
        }

        private void OnSyncStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        #endregion

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _oAuthService.Logout();
            _syncService.StopAutoSync();

            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _syncService.StopAutoSync();
            base.OnClosed(e);
        }
    }

    // Helper dialog for text input
    public class TextInputDialog : Window
    {
        private TextBox _textBox;
        public string ResponseText => _textBox.Text;

        public TextInputDialog(string question)
        {
            Title = "Input";
            Width = 400;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock { Text = question, Margin = new Thickness(0, 0, 0, 10) });

            _textBox = new TextBox { Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(_textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += (s, e) => { DialogResult = true; Close(); };
            var cancelButton = new Button { Content = "Cancel", Width = 80 };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(buttonPanel);

            Content = stack;
        }
    }
}