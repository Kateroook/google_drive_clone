using ClientApp.Models;
using ClientApp.Services;
using ClientApp.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClientApp
{
    public partial class MainWindow : Window
    {
        private readonly OAuthService _oAuthService;
        private readonly FileService _fileService;
        private readonly SyncService _syncService;

        private ObservableCollection<FileItem> _allFiles;
        private ObservableCollection<FolderItem> _allFolders;
        private List<FileItem> _filteredFiles;
        private List<FolderItem> _filteredFolders;

        private long? _currentFolderId = null;
        private Stack<FolderItem> _navigationStack;
        private FolderItem _currentFolder;
        private object _selectedItem; // Може бути FileItem або FolderItem
        private bool _showAllFiles = false; // Режим "All Files"

        public MainWindow(OAuthService oAuthService, UserInfo userInfo)
        {
            InitializeComponent();

            _oAuthService = oAuthService;
            _fileService = new FileService(_oAuthService);
            _syncService = new SyncService(_fileService);

            _allFiles = new ObservableCollection<FileItem>();
            _allFolders = new ObservableCollection<FolderItem>();
            _filteredFiles = new List<FileItem>();
            _filteredFolders = new List<FolderItem>();
            _navigationStack = new Stack<FolderItem>();

            UserNameText.Text = userInfo.Name;
            _syncService.SyncStatusChanged += OnSyncStatusChanged;

            // ЗАМІСТЬ LoadDataAsync() тут, використовуйте Loaded event
            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        #region Data Loading

        private async Task LoadDataAsync()
        {
            await LoadFoldersAsync();
            await LoadFilesAsync();
            UpdateUI();
            UpdateBreadcrumbs();
            UpdateSyncStatus();
        }

        private async Task LoadFoldersAsync()
        {
            try
            {
                var folders = _showAllFiles
                    ? new List<FolderItem>() // В режимі "All Files" папки не показуємо
                    : await _fileService.GetFoldersAsync(_currentFolderId);

                _allFolders.Clear();
                foreach (var folder in folders)
                {
                    _allFolders.Add(folder);
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
                StatusText.Text = "Loading...";

                var files = _showAllFiles
                    ? await _fileService.GetAllFilesAsync() // Всі файли користувача
                    : await _fileService.GetFilesAsync(_currentFolderId);

                _allFiles.Clear();
                foreach (var file in files)
                {
                    _allFiles.Add(file);
                }

                StatusText.Text = "Ready";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error";
            }
        }

        private void ApplyFilter()
        {
            // Перевірка чи все ініціалізовано
            if (FilterComboBox == null || _allFiles == null)
                return;

            var filterIndex = FilterComboBox.SelectedIndex;
            IEnumerable<FileItem> filtered = _allFiles;

            switch (filterIndex)
            {
                case 1:
                    filtered = _allFiles.Where(f => f.Extension == ".py");
                    break;
                case 2:
                    filtered = _allFiles.Where(f => f.IsImage);
                    break;
                case 3:
                    filtered = _allFiles.Where(f => f.IsCode);
                    break;
                default:
                    filtered = _allFiles;
                    break;
            }

            _filteredFiles = filtered.ToList();
            _filteredFolders = _allFolders?.ToList() ?? new List<FolderItem>();
        }

        private void UpdateUI()
        {
            ApplyFilter();

            // КРИТИЧНА ПЕРЕВІРКА
            if (ItemsPanel == null)
            {
                Debug.WriteLine("UpdateUI: ItemsPanel is null, skipping");
                return;
            }

            ItemsPanel.Children.Clear();

            foreach (var folder in _filteredFolders ?? new List<FolderItem>())
            {
                ItemsPanel.Children.Add(CreateFolderItemUI(folder));
            }

            foreach (var file in _filteredFiles ?? new List<FileItem>())
            {
                ItemsPanel.Children.Add(CreateFileItemUI(file));
            }

            if (ItemCountText != null)
            {
                ItemCountText.Text = $"{_filteredFolders?.Count ?? 0} folders, {_filteredFiles?.Count ?? 0} files";
            }
        }

        #endregion

        #region UI Creation

        private Border CreateFolderItemUI(FolderItem folder)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(16, 12, 16, 12),
                Cursor = Cursors.Hand,
                Tag = folder
            };

            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(241, 243, 244));
            border.MouseLeave += (s, e) =>
            {
                if (_selectedItem != folder)
                    border.Background = Brushes.Transparent;
            };
            border.MouseLeftButtonDown += (s, e) => SelectItem(folder, border);
            border.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    OpenFolder(folder);
                    e.Handled = true;
                }
            }; border.MouseRightButtonDown += (s, e) => ShowFolderContextMenu(folder, border, e);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            // Name
            var nameStack = new StackPanel { Orientation = Orientation.Horizontal };
            nameStack.Children.Add(new TextBlock { Text = "📁", FontSize = 20, Margin = new Thickness(0, 0, 12, 0) });
            nameStack.Children.Add(new TextBlock { Text = folder.Name, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });

            if (folder.IsSynced)
            {
                nameStack.Children.Add(new TextBlock
                {
                    Text = " 🔄",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = $"Synced: {folder.SyncPath}"
                });
            }
            Grid.SetColumn(nameStack, 0);
            grid.Children.Add(nameStack);

            // Size
            Grid.SetColumn(AddText("—"), 1);
            grid.Children.Add(AddText("—"));

            // Owner
            var ownerText = AddText("me");
            Grid.SetColumn(ownerText, 2);
            grid.Children.Add(ownerText);

            // Modified
            var modText = AddText(folder.UpdatedAt.ToString("MMM dd, yyyy HH:mm:ss"));
            Grid.SetColumn(modText, 3);
            grid.Children.Add(modText);

            // Editor (empty for folders)
            var editorText = AddText("—");
            Grid.SetColumn(editorText, 4);
            grid.Children.Add(editorText);

            border.Child = grid;
            return border;
        }

        private Border CreateFileItemUI(FileItem file)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(16, 12, 16, 12),
                Cursor = Cursors.Hand,
                Tag = file
            };

            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(241, 243, 244));
            border.MouseLeave += (s, e) =>
            {
                if (_selectedItem != file)
                    border.Background = Brushes.Transparent;
            };
            border.MouseLeftButtonDown += (s, e) => SelectItem(file, border);
            border.MouseRightButtonDown += (s, e) => ShowFileContextMenu(file, border, e);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            // Name
            var nameStack = new StackPanel { Orientation = Orientation.Horizontal };
            string icon = file.IsImage ? "🖼️" : file.IsCode ? "📄" : "📎";
            nameStack.Children.Add(new TextBlock { Text = icon, FontSize = 20, Margin = new Thickness(0, 0, 12, 0) });
            nameStack.Children.Add(new TextBlock { Text = file.Name, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(nameStack, 0);
            grid.Children.Add(nameStack);

            // Size
            var sizeText = AddText(file.SizeFormatted);
            Grid.SetColumn(sizeText, 1);
            grid.Children.Add(sizeText);

            // Owner
            var ownerText = AddText(file.UploadedByName ?? "me");
            Grid.SetColumn(ownerText, 2);
            grid.Children.Add(ownerText);

            // Modified
            var modText = AddText(file.UpdatedAt.ToString("MMM dd, yyyy HH:mm:ss"));
            Grid.SetColumn(modText, 3);
            grid.Children.Add(modText);

            // Edited by
            var editorText = AddText(file.EditedByName ?? "—");
            Grid.SetColumn(editorText, 4);
            grid.Children.Add(editorText);

            border.Child = grid;
            return border;
        }

        private TextBlock AddText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        #endregion

        #region Selection & Context Menus

        private void SelectItem(object item, Border border)
        {
            // Зняти виділення з попереднього
            foreach (var child in ItemsPanel.Children)
            {
                if (child is Border b) b.Background = Brushes.Transparent;
            }

            // Виділити поточний
            border.Background = new SolidColorBrush(Color.FromRgb(200, 230, 255));
            _selectedItem = item;

            if (item is FileItem file)
            {
                _ = ShowFileDetails(file);
            }
            else
            {
                HideFileDetails();
            }
        }

        private void ShowFolderContextMenu(FolderItem folder, Border border, MouseButtonEventArgs e)
        {
            SelectItem(folder, border);

            var menu = new ContextMenu();

            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += (s, ev) => OpenFolder(folder);
            menu.Items.Add(openItem);

            var syncItem = new MenuItem { Header = folder.IsSynced ? "❌ Remove Sync" : "🔄 Setup Sync" };
            syncItem.Click += async (s, ev) => await ToggleFolderSync(folder);
            menu.Items.Add(syncItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Delete Folder", Foreground = Brushes.Red };
            deleteItem.Click += async (s, ev) => await DeleteFolder(folder);
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        private void ShowFileContextMenu(FileItem file, Border border, MouseButtonEventArgs e)
        {
            SelectItem(file, border);

            var menu = new ContextMenu();

            var downloadItem = new MenuItem { Header = "📥 Download" };
            downloadItem.Click += async (s, ev) => await DownloadFile(file);
            menu.Items.Add(downloadItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Delete", Foreground = Brushes.Red };
            deleteItem.Click += async (s, ev) => await DeleteFile(file);
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        #endregion

        #region Navigation

        private void UpdateBreadcrumbs()
        {
            BreadcrumbsPanel.Children.Clear();

            if (_showAllFiles)
            {
                var allFilesBtn = new Button
                {
                    Content = "📄 All Files",
                    Style = (Style)FindResource("IconButton"),
                    Padding = new Thickness(12, 6, 12, 6),
                    FontWeight = FontWeights.SemiBold
                };
                BreadcrumbsPanel.Children.Add(allFilesBtn);
                return;
            }

            var homeButton = new Button
            {
                Content = "🏠 My Drive",
                Style = (Style)FindResource("IconButton"),
                Padding = new Thickness(12, 6, 12, 6)
            };
            homeButton.Click += (s, e) => GoToRoot_Click(s, e);
            BreadcrumbsPanel.Children.Add(homeButton);

            if (_navigationStack.Count > 0)
            {
                var folders = _navigationStack.Reverse().ToList();
                foreach (var folder in folders)
                {
                    BreadcrumbsPanel.Children.Add(new TextBlock
                    {
                        Text = " / ",
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104))
                    });

                    var folderButton = new Button
                    {
                        Content = folder.Name,
                        Style = (Style)FindResource("IconButton"),
                        Tag = folder,
                        Padding = new Thickness(12, 6, 12, 6)
                    };
                    folderButton.Click += BreadcrumbFolder_Click;
                    BreadcrumbsPanel.Children.Add(folderButton);
                }
            }
        }

        private void BreadcrumbFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is FolderItem folder)
            {
                while (_navigationStack.Count > 0 && _navigationStack.Peek().Id != folder.Id)
                {
                    _navigationStack.Pop();
                }

                _currentFolderId = folder.Id;
                _currentFolder = folder;
                _ = LoadDataAsync();
            }
        }

        private async void GoToRoot_Click(object sender, RoutedEventArgs e)
        {
            _showAllFiles = false;
            _currentFolderId = null;
            _currentFolder = null;
            _navigationStack.Clear();
            await LoadDataAsync();
        }

        private async void ShowAllFiles_Click(object sender, RoutedEventArgs e)
        {
            _showAllFiles = true;
            _currentFolderId = null;
            _currentFolder = null;
            _navigationStack.Clear();
            await LoadDataAsync();
        }

        private async void OpenFolder(FolderItem folder)
        {
            _showAllFiles = false;
            _navigationStack.Push(folder);
            _currentFolderId = folder.Id;
            _currentFolder = folder;
            await LoadDataAsync();
        }

        #endregion

        #region File Operations

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = true };
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = "Uploading...";
                int count = 0;
                foreach (var filePath in dialog.FileNames)
                {
                    var result = await _fileService.UploadFileAsync(filePath, _currentFolderId);
                    if (result != null) count++;
                }
                await LoadDataAsync();
                StatusText.Text = $"Uploaded {count} file(s)";
            }
        }

        private async void UploadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderPickerDialog();
            if (dialog.ShowDialog() == true)
            {
                var localPath = dialog.SelectedPath;
                var folderName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));

                StatusText.Text = $"Creating folder '{folderName}'...";

                // Створити віртуальну папку
                var folder = await _fileService.CreateFolderAsync(folderName, _currentFolderId, localPath);
                if (folder != null)
                {
                    StatusText.Text = "Uploading folder contents...";

                    // Завантажити всі файли
                    var files = Directory.GetFiles(localPath);
                    int uploaded = 0;
                    foreach (var file in files)
                    {
                        var result = await _fileService.UploadFileAsync(file, folder.Id);
                        if (result != null) uploaded++;
                    }

                    // КРИТИЧНО: чекаємо 2 секунди перед активацією синхронізації
                    await Task.Delay(2000);

                    // Тепер активуємо синхронізацію
                    await _syncService.ConfigureSyncAsync(localPath, folder.Id, true);

                    await LoadDataAsync();
                    MessageBox.Show($"Folder '{folderName}' created with {uploaded} files.\nAuto-sync enabled!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextInputDialog("Enter folder name:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
            {
                var folder = await _fileService.CreateFolderAsync(dialog.ResponseText, _currentFolderId);
                if (folder != null)
                {
                    await LoadDataAsync();
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUI();
        }

        private async void ShowSyncedFolders_Click(object sender, RoutedEventArgs e)
        {
            var allFolders = await _fileService.GetAllFoldersAsync();
            var syncedFolders = allFolders.Where(f => f.IsSynced).ToList();

            if (syncedFolders.Count == 0)
            {
                MessageBox.Show("No synced folders found", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var message = "Synced Folders:\n\n" + string.Join("\n",
                syncedFolders.Select(f => $"📁 {f.Name}\n   ↔ {f.SyncPath}\n"));
            MessageBox.Show(message, "Synced Folders", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task DownloadFile(FileItem file)
        {
            var dialog = new FolderPickerDialog();
            if (dialog.ShowDialog() == true)
            {
                var savePath = Path.Combine(dialog.SelectedPath, file.Name);
                var success = await _fileService.DownloadFileAsync(file.Id, savePath);
                if (success)
                {
                    MessageBox.Show($"File saved to:\n{savePath}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private async Task DeleteFile(FileItem file)
        {
            var result = MessageBox.Show($"Delete '{file.Name}'?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var success = await _fileService.DeleteFileAsync(file.Id);
                if (success)
                {
                    await LoadDataAsync();
                    HideFileDetails();
                }
            }
        }

        private async Task DeleteFolder(FolderItem folder)
        {
            var result = MessageBox.Show($"Delete folder '{folder.Name}' and all its contents?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var success = await _fileService.DeleteFolderAsync(folder.Id);
                if (success)
                {
                    await LoadDataAsync();
                    MessageBox.Show("Folder deleted", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private async void DeleteSelectedFile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem is FileItem file)
            {
                await DeleteFile(file);
            }
        }

        #endregion

        #region File Details & Preview

        private async Task ShowFileDetails(FileItem file)
        {
            PreviewContainer.Visibility = Visibility.Visible;
            FileInfoPanel.Visibility = Visibility.Visible;

            FileNameInfo.Text = file.Name;
            FileSizeInfo.Text = $"Size: {file.SizeFormatted}";
            FileTypeInfo.Text = $"Type: {file.FileType}";
            FileCreatedInfo.Text = $"Created: {file.CreatedAt:yyyy-MM-dd HH:mm:ss}";
            FileUpdatedInfo.Text = $"Modified: {file.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
            FileUploaderInfo.Text = $"Uploaded by: {file.UploadedByName}";
            FileEditorInfo.Text = file.EditedByName != null
                ? $"Edited by: {file.EditedByName}"
                : "Not edited";

            ImagePreview.Visibility = Visibility.Collapsed;
            CodePreview.Visibility = Visibility.Collapsed;

            try
            {
                if (file.IsImage)
                {
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
                    var content = await _fileService.GetFileContentAsync(file.Id);
                    if (content != null)
                    {
                        CodePreview.Text = content;
                        CodePreview.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Preview error: {ex.Message}");
            }
        }

        private void HideFileDetails()
        {
            PreviewContainer.Visibility = Visibility.Collapsed;
            FileInfoPanel.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Sync Operations

        private void UpdateSyncStatus()
        {
            if (_currentFolder != null && !string.IsNullOrEmpty(_currentFolder.SyncPath))
            {
                SyncStatusText.Text = $"Synced with:\n{_currentFolder.SyncPath}";
                AutoSyncCheckBox.IsChecked = true;
                AutoSyncCheckBox.IsEnabled = true;
            }
            else if (_currentFolder != null)
            {
                SyncStatusText.Text = "Not synced\n(Enable auto-sync to choose folder)";
                AutoSyncCheckBox.IsChecked = false;
                AutoSyncCheckBox.IsEnabled = true;
            }
            else
            {
                SyncStatusText.Text = "Not synced\n(Open a folder first)";
                AutoSyncCheckBox.IsChecked = false;
                AutoSyncCheckBox.IsEnabled = false;
            }
        }

        private async void AutoSync_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) return;

            if (AutoSyncCheckBox.IsChecked == true)
            {
                // Увімкнути синхронізацію
                if (string.IsNullOrEmpty(_currentFolder.SyncPath))
                {
                    // Вибрати локальну папку
                    var dialog = new FolderPickerDialog();
                    if (dialog.ShowDialog() == true)
                    {
                        var updated = await _fileService.UpdateFolderSyncAsync(_currentFolder.Id, dialog.SelectedPath);
                        if (updated != null)
                        {
                            _currentFolder.SyncPath = dialog.SelectedPath;
                            await _syncService.ConfigureSyncAsync(dialog.SelectedPath, _currentFolder.Id, true);
                            UpdateSyncStatus();
                        }
                        else
                        {
                            AutoSyncCheckBox.IsChecked = false;
                        }
                    }
                    else
                    {
                        AutoSyncCheckBox.IsChecked = false;
                    }
                }
                else
                {
                    // Папка вже налаштована, просто активувати
                    await _syncService.ConfigureSyncAsync(_currentFolder.SyncPath, _currentFolder.Id, true);
                }
            }
            else
            {
                // Вимкнути синхронізацію і видалити шлях
                if (_currentFolder != null && !string.IsNullOrEmpty(_currentFolder.SyncPath))
                {
                    var result = MessageBox.Show(
                        "Remove sync configuration for this folder?\nThis will not delete any files.",
                        "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await _fileService.UpdateFolderSyncAsync(_currentFolder.Id, null);
                        _currentFolder.SyncPath = null;
                        _syncService.StopAutoSync();
                        UpdateSyncStatus();
                    }
                    else
                    {
                        AutoSyncCheckBox.IsChecked = true;
                    }
                }
            }
        }

        private async void SyncUpload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null || string.IsNullOrEmpty(_currentFolder.SyncPath))
            {
                MessageBox.Show("Please enable auto-sync first", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var count = await _syncService.SyncLocalToRemoteAsync();
            await LoadDataAsync();
            MessageBox.Show($"Uploaded {count} file(s)", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void SyncDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null || string.IsNullOrEmpty(_currentFolder.SyncPath))
            {
                MessageBox.Show("Please enable auto-sync first", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var count = await _syncService.SyncRemoteToLocalAsync();
            MessageBox.Show($"Downloaded {count} file(s)", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void SyncBoth_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null || string.IsNullOrEmpty(_currentFolder.SyncPath))
            {
                MessageBox.Show("Please enable auto-sync first", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (up, down) = await _syncService.SyncBothWaysAsync();
            await LoadDataAsync();
            MessageBox.Show($"Sync complete!\nUploaded: {up}\nDownloaded: {down}",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task ToggleFolderSync(FolderItem folder)
        {
            if (folder.IsSynced)
            {
                var result = MessageBox.Show($"Remove sync for '{folder.Name}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await _fileService.UpdateFolderSyncAsync(folder.Id, null);
                    await LoadDataAsync();
                }
            }
            else
            {
                var dialog = new FolderPickerDialog();
                if (dialog.ShowDialog() == true)
                {
                    await _fileService.UpdateFolderSyncAsync(folder.Id, dialog.SelectedPath);
                    await LoadDataAsync();
                }
            }
        }

        #endregion

        private void OnSyncStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() => StatusText.Text = status);
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _oAuthService.Logout();
            _syncService.StopAutoSync();
            new LoginWindow().Show();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _syncService.StopAutoSync();
            base.OnClosed(e);
        }
    }

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