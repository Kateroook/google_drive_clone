using ClientApp.Models;
using ClientApp.Services;
using ClientApp.Views;
using Microsoft.Win32;
using System;
using System.CodeDom;
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
        private object _selectedItem;
        private bool _showAllFiles = false;

        // Видимість колонок
        private Dictionary<string, bool> _columnVisibility;

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

            // Ініціалізація видимості колонок (всі видимі за замовчуванням)
            _columnVisibility = new Dictionary<string, bool>
            {
                { "Size", true },
                { "Owner", true },
                { "Created", true },
                { "Modified", true },
                { "Editor", true }
            };

            UserNameText.Text = userInfo.Name;
            _syncService.SyncStatusChanged += OnSyncStatusChanged;

            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadColumnSettings();
            ApplyColumnVisibility();
            await LoadDataAsync();
        }

        #region Column Management

        private void ColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void ColumnVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnName)
            {
                _columnVisibility[columnName] = menuItem.IsChecked;
                SaveColumnSettings();
                ApplyColumnVisibility();
                UpdateUI();
            }
        }

        private void ApplyColumnVisibility()
        {
            // Headers
            SetColumnVisibility("Size", SizeColumn, SizeHeader);
            SetColumnVisibility("Owner", OwnerColumn, OwnerHeader);
            SetColumnVisibility("Created", CreatedColumn, CreatedHeader);
            SetColumnVisibility("Modified", ModifiedColumn, ModifiedHeader);
            SetColumnVisibility("Editor", EditorColumn, EditorHeader);
        }

        private void SetColumnVisibility(string columnName, ColumnDefinition column, TextBlock header)
        {
            var isVisible = _columnVisibility[columnName];
            column.Width = isVisible ? new GridLength(columnName == "Size" ? 100 : columnName == "Owner" ? 120 : 140) : new GridLength(0);
            header.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadColumnSettings()
        {
            try
            {
                _columnVisibility["Size"] = Settings.Default.ShowSizeColumn;
                _columnVisibility["Owner"] = Settings.Default.ShowOwnerColumn;
                _columnVisibility["Created"] = Settings.Default.ShowCreatedColumn;
                _columnVisibility["Modified"] = Settings.Default.ShowModifiedColumn;
                _columnVisibility["Editor"] = Settings.Default.ShowEditorColumn;

                // Оновлюємо чекбокси в меню
                foreach (MenuItem item in ColumnsMenu.Items)
                {
                    if (item.Tag is string columnName && _columnVisibility.ContainsKey(columnName))
                    {
                        item.IsChecked = _columnVisibility[columnName];
                    }
                }
            }
            catch
            {
                // defaults
            }
        }

        private void SaveColumnSettings()
        {
            Settings.Default.ShowSizeColumn = _columnVisibility["Size"];
            Settings.Default.ShowOwnerColumn = _columnVisibility["Owner"];
            Settings.Default.ShowCreatedColumn = _columnVisibility["Created"];
            Settings.Default.ShowModifiedColumn = _columnVisibility["Modified"];
            Settings.Default.ShowEditorColumn = _columnVisibility["Editor"];
            Settings.Default.Save();
        }

        #endregion

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
                List<FolderItem> folders;

                if (_showAllFiles)
                {
                    // Режим "All Files" - не показуємо папки
                    folders = new List<FolderItem>();
                }
                else if (_currentFolderId == null)
                {
                    // Головна сторінка - тільки папки верхнього рівня
                    var allFolders = await _fileService.GetFoldersAsync(null);
                    folders = allFolders;
                }
                else
                {
                    // Всередині папки - підпапки
                    folders = await _fileService.GetFoldersAsync(_currentFolderId);
                }

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
                StatusText.Text = "Завантаження...";

                List<FileItem> files;

                if (_showAllFiles)
                {
                    // Режим "All Files" - GetFilesAsync() без параметрів = всі файли
                    files = await _fileService.GetFilesAsync();
                }
                else if (_currentFolderId == null)
                {
                    // Головна сторінка - передаємо 0 як сигнал для файлів без папки
                    files = await _fileService.GetFilesAsync(0);
                }
                else
                {
                    // Всередині папки
                    files = await _fileService.GetFilesAsync(_currentFolderId);
                }

                _allFiles.Clear();
                foreach (var file in files)
                {
                    _allFiles.Add(file);
                }

                StatusText.Text = "Ready";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Помилка";
            }
        }

        private void ApplyFilter()
        {
            if (FilterComboBox == null || _allFiles == null)
                return;

            var filterIndex = FilterComboBox.SelectedIndex;
            IEnumerable<FileItem> filtered = _allFiles;

            switch (filterIndex)
            {
                case 1:
                    filtered = _allFiles.Where(f => f.Extension == ".js");
                    break;
                case 2:
                    filtered = _allFiles.Where(f => f.Extension == ".png");
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
        private void ApplySort()
        {
            if (SortComboBox == null || _filteredFiles == null) return;

            var sortIndex = SortComboBox.SelectedIndex;

            switch (sortIndex)
            {
                case 0: // Type A-Z (Extension)
                    _filteredFiles = _filteredFiles.OrderBy(f => f.Extension ?? string.Empty).ThenBy(f => f.Name).ToList();
                    break;
                case 1: // Type Z-A (Extension)
                    _filteredFiles = _filteredFiles.OrderByDescending(f => f.Extension ?? string.Empty).ThenBy(f => f.Name).ToList();
                    break;
            }
            _filteredFolders = _filteredFolders?.OrderBy(f => f.Name).ToList() ?? new List<FolderItem>();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            ApplyFilter();
            ApplySort();

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
                ItemCountText.Text = $"{_filteredFolders?.Count ?? 0} папок, {_filteredFiles?.Count ?? 0} файлів";
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
            };
            border.MouseRightButtonDown += (s, e) => ShowFolderContextMenu(folder, border, e);

            var grid = new Grid();

            // Динамічні колонки на основі видимості
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (_columnVisibility["Size"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            if (_columnVisibility["Owner"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            if (_columnVisibility["Created"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            if (_columnVisibility["Modified"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            if (_columnVisibility["Editor"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            // Name (завжди видимий)
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
                    ToolTip = $"Синхронізовано з: {folder.SyncPath}"
                });
            }
            Grid.SetColumn(nameStack, 0);
            grid.Children.Add(nameStack);

            int colIndex = 1;

            // Size
            if (_columnVisibility["Size"])
            {
                var sizeText = AddText("—");
                Grid.SetColumn(sizeText, colIndex++);
                grid.Children.Add(sizeText);
            }

            // Owner
            if (_columnVisibility["Owner"])
            {
                var ownerText = AddText("me");
                Grid.SetColumn(ownerText, colIndex++);
                grid.Children.Add(ownerText);
            }

            // Created
            if (_columnVisibility["Created"])
            {
                var createdText = AddText(folder.CreatedAt.ToString("MMM dd, yyyy HH:mm:ss"));
                Grid.SetColumn(createdText, colIndex++);
                grid.Children.Add(createdText);
            }

            // Modified
            if (_columnVisibility["Modified"])
            {
                var modText = AddText(folder.UpdatedAt.ToString("MMM dd, yyyy HH:mm:ss"));
                Grid.SetColumn(modText, colIndex++);
                grid.Children.Add(modText);
            }

            // Editor
            if (_columnVisibility["Editor"])
            {
                var editorText = AddText("—");
                Grid.SetColumn(editorText, colIndex++);
                grid.Children.Add(editorText);
            }

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

            // Динамічні колонки
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (_columnVisibility["Size"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            if (_columnVisibility["Owner"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            if (_columnVisibility["Created"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            if (_columnVisibility["Modified"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            if (_columnVisibility["Editor"])
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            // Name (завжди видимий)
            var nameStack = new StackPanel { Orientation = Orientation.Horizontal };
            string icon = file.IsImage ? "🖼️" : file.IsCode ? "📄" : "📎";
            nameStack.Children.Add(new TextBlock { Text = icon, FontSize = 20, Margin = new Thickness(0, 0, 12, 0) });
            nameStack.Children.Add(new TextBlock { Text = file.Name, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(nameStack, 0);
            grid.Children.Add(nameStack);

            int colIndex = 1;

            // Size
            if (_columnVisibility["Size"])
            {
                var sizeText = AddText(file.SizeFormatted);
                Grid.SetColumn(sizeText, colIndex++);
                grid.Children.Add(sizeText);
            }

            // Owner
            if (_columnVisibility["Owner"])
            {
                var ownerText = AddText(file.UploadedByName ?? "me");
                Grid.SetColumn(ownerText, colIndex++);
                grid.Children.Add(ownerText);
            }

            // Created
            if (_columnVisibility["Created"])
            {
                var createdText = AddText(file.CreatedAt.ToString("MMM dd, yyyy HH:mm:ss"));
                Grid.SetColumn(createdText, colIndex++);
                grid.Children.Add(createdText);
            }

            // Modified
            if (_columnVisibility["Modified"])
            {
                var modText = AddText(file.UpdatedAt.ToString("MMM dd, yyyy HH:mm:ss"));
                Grid.SetColumn(modText, colIndex++);
                grid.Children.Add(modText);
            }

            // Edited by
            if (_columnVisibility["Editor"])
            {
                var editorText = AddText(file.EditedByName ?? "—");
                Grid.SetColumn(editorText, colIndex++);
                grid.Children.Add(editorText);
            }

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
            foreach (var child in ItemsPanel.Children)
            {
                if (child is Border b) b.Background = Brushes.Transparent;
            }

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

            var openItem = new MenuItem { Header = "Відкрити" };
            openItem.Click += (s, ev) => OpenFolder(folder);
            menu.Items.Add(openItem);

            var syncItem = new MenuItem { Header = folder.IsSynced ? "❌ Припинити синхронізацію" : "🔄 Налаштування синхронізації" };
            syncItem.Click += async (s, ev) => await ToggleFolderSync(folder);
            menu.Items.Add(syncItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Видалити папку", Foreground = Brushes.Red };
            deleteItem.Click += async (s, ev) => await DeleteFolder(folder);
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        private void ShowFileContextMenu(FileItem file, Border border, MouseButtonEventArgs e)
        {
            SelectItem(file, border);

            var menu = new ContextMenu();

            var downloadItem = new MenuItem { Header = "📥 Вивантажити" };
            downloadItem.Click += async (s, ev) => await DownloadFile(file);
            menu.Items.Add(downloadItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Видалити", Foreground = Brushes.Red };
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
                    Content = "📄 Всі файли",
                    Style = (Style)FindResource("IconButton"),
                    Padding = new Thickness(12, 6, 12, 6),
                    FontWeight = FontWeights.SemiBold
                };
                BreadcrumbsPanel.Children.Add(allFilesBtn);
                return;
            }

            var homeButton = new Button
            {
                Content = "🏠 Головна",
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
                StatusText.Text = "Вивантаження на сервер...";
                int count = 0;
                foreach (var filePath in dialog.FileNames)
                {
                    var result = await _fileService.UploadFileAsync(filePath, _currentFolderId);
                    if (result != null) count++;
                }
                await LoadDataAsync();
                StatusText.Text = $"Вивантажено на сервер {count} файлів";
            }
        }

        private async void UploadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderPickerDialog();
            if (dialog.ShowDialog() == true)
            {
                var localPath = dialog.SelectedPath;
                var folderName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));

                StatusText.Text = $"Створення папки '{folderName}'...";

                var folder = await _fileService.CreateFolderAsync(folderName, _currentFolderId, localPath);
                if (folder != null)
                {
                    StatusText.Text = "Завантаження вмісту папки...";

                    var files = Directory.GetFiles(localPath);
                    int uploaded = 0;
                    foreach (var file in files)
                    {
                        var result = await _fileService.UploadFileAsync(file, folder.Id);
                        if (result != null) uploaded++;
                    }

                    await Task.Delay(1000);

                    // Нова папка - потрібна початкова синхронізація
                    Debug.WriteLine($"[MAIN] Setting up sync for newly uploaded folder {folder.Id}");
                    await _syncService.ConfigureSyncAsync(localPath, folder.Id, autoSync: true, performInitialSync: true);

                    await LoadDataAsync();
                    MessageBox.Show($"Папка '{folderName}' створена з {uploaded} файлами.\nАвто-синхронізація застосована!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextInputDialog("Введіть назву папки:");
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
                MessageBox.Show("Немає синхронізованих папок", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var message = "Синхронізовані папки:\n\n" + string.Join("\n",
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
                    MessageBox.Show($"Файл збережено у:\n{savePath}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private async Task DeleteFile(FileItem file)
        {
            var result = MessageBox.Show($"Видалити '{file.Name}'?", "Confirm",
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
            var result = MessageBox.Show($"Видалити папку '{folder.Name}' і весь її вміст?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var success = await _fileService.DeleteFolderAsync(folder.Id);
                if (success)
                {
                    await LoadDataAsync();
                    MessageBox.Show("Папку видалено", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
            FileSizeInfo.Text = $"Розмір: {file.SizeFormatted}";
            FileTypeInfo.Text = $"Тип: {file.FileType}";
            FileCreatedInfo.Text = $"Створено: {file.CreatedAt:yyyy-MM-dd HH:mm:ss}";
            FileUpdatedInfo.Text = $"Редаговано: {file.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
            FileUploaderInfo.Text = $"Створив: {file.UploadedByName}";
            FileEditorInfo.Text = file.EditedByName != null
                ? $"Редагував: {file.EditedByName}"
                : "Без змін";

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
                        DisplayCodeWithLineNumbers(content, file.Extension);
                        CodePreview.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Preview error: {ex.Message}");
            }
        }

        private void DisplayCodeWithLineNumbers(string code, string extension)
        {
            // Нормалізуємо line endings
            code = code.Replace("\r\n", "\n").Replace("\r", "\n");

            var lines = code.Split('\n');
            var lineCount = lines.Length;

            // Генеруємо номери рядків
            var lineNumbers = string.Join("\n", Enumerable.Range(1, lineCount));
            LineNumbers.Text = lineNumbers;

            // Застосовуємо базову підсвітку синтаксису
            CodeContent.Inlines.Clear();

            foreach (var line in lines)
            {
                ApplySyntaxHighlighting(line, extension);
                CodeContent.Inlines.Add(new System.Windows.Documents.LineBreak());
            }
        }

        private void ApplySyntaxHighlighting(string line, string extension)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            // Базова підсвітка для популярних мов
            var keywords = GetKeywordsForExtension(extension);
            var words = System.Text.RegularExpressions.Regex.Split(line, @"(\s+|[{}()\[\];,\.])");

            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    CodeContent.Inlines.Add(new System.Windows.Documents.Run(word));
                    continue;
                }

                System.Windows.Documents.Run run;

                // Коментарі
                if (word.TrimStart().StartsWith("//") || word.TrimStart().StartsWith("#"))
                {
                    run = new System.Windows.Documents.Run(word)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(106, 153, 85)) // Green
                    };
                }
                // Ключові слова
                else if (keywords.Contains(word))
                {
                    run = new System.Windows.Documents.Run(word)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214)) // Blue
                    };
                }
                // Строки
                else if (word.StartsWith("\"") || word.StartsWith("'"))
                {
                    run = new System.Windows.Documents.Run(word)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(206, 145, 120)) // Orange
                    };
                }
                // Числа
                else if (double.TryParse(word, out _))
                {
                    run = new System.Windows.Documents.Run(word)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(181, 206, 168)) // Light green
                    };
                }
                // Функції (слово перед дужкою)
                else if (word.Contains("("))
                {
                    run = new System.Windows.Documents.Run(word)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 170)) // Yellow
                    };
                }
                else
                {
                    run = new System.Windows.Documents.Run(word)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212)) // Light gray
                    };
                }

                CodeContent.Inlines.Add(run);
            }
        }

        private HashSet<string> GetKeywordsForExtension(string extension)
        {
            switch (extension?.ToLower())
            {
                case ".cs":
                    return new HashSet<string>
                    {
                        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
                        "checked", "class", "const", "continue", "decimal", "default", "delegate",
                        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
                        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
                        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
                        "new", "null", "object", "operator", "out", "override", "params", "private",
                        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
                        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
                        "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while",
                        "async", "await"
                    };
                case ".py":
                    return new HashSet<string>
                    {
                        "False", "None", "True", "and", "as", "assert", "async", "await", "break",
                        "class", "continue", "def", "del", "elif", "else", "except", "finally",
                        "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal",
                        "not", "or", "pass", "raise", "return", "try", "while", "with", "yield"
                    };

                case ".js":
                case ".ts":
                    return new HashSet<string>
                    {
                        "abstract", "arguments", "await", "boolean", "break", "byte", "case", "catch",
                        "char", "class", "const", "continue", "debugger", "default", "delete", "do",
                        "double", "else", "enum", "eval", "export", "extends", "false", "final",
                        "finally", "float", "for", "function", "goto", "if", "implements", "import",
                        "in", "instanceof", "int", "interface", "let", "long", "native", "new",
                        "null", "package", "private", "protected", "public", "return", "short",
                        "static", "super", "switch", "synchronized", "this", "throw", "throws",
                        "transient", "true", "try", "typeof", "var", "void", "volatile", "while",
                        "with", "yield"
                    };

                case ".cpp":
                case ".c":
                case ".h":
                    return new HashSet<string>
                    {
                        "auto", "break", "case", "char", "const", "continue", "default", "do",
                        "double", "else", "enum", "extern", "float", "for", "goto", "if", "inline",
                        "int", "long", "register", "restrict", "return", "short", "signed", "sizeof",
                        "static", "struct", "switch", "typedef", "union", "unsigned", "void",
                        "volatile", "while", "bool", "class", "namespace", "private", "public",
                        "protected", "virtual", "template", "typename"
                    };

                case ".java":
                    return new HashSet<string>
                    {
                        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char",
                        "class", "const", "continue", "default", "do", "double", "else", "enum",
                        "extends", "final", "finally", "float", "for", "goto", "if", "implements",
                        "import", "instanceof", "int", "interface", "long", "native", "new", "package",
                        "private", "protected", "public", "return", "short", "static", "strictfp",
                        "super", "switch", "synchronized", "this", "throw", "throws", "transient",
                        "try", "void", "volatile", "while"
                    };
                default:
                    return new HashSet<string>();
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
                SyncStatusText.Text = $"Синхронізовано з:\n{_currentFolder.SyncPath}";
                AutoSyncCheckBox.IsChecked = true;
                AutoSyncCheckBox.IsEnabled = true;
            }
            else if (_currentFolder != null)
            {
                SyncStatusText.Text = "Несинхронізовано\n(Активуйте синхронізацію, щоб вибрати папку)";
                AutoSyncCheckBox.IsChecked = false;
                AutoSyncCheckBox.IsEnabled = true;
            }
            else
            {
                SyncStatusText.Text = "Несинхронізовано\n(Відкрийте спершу папку)";
                AutoSyncCheckBox.IsChecked = false;
                AutoSyncCheckBox.IsEnabled = false;
            }
        }

        private async void AutoSync_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) return;

            if (AutoSyncCheckBox.IsChecked == true)
            {
                if (string.IsNullOrEmpty(_currentFolder.SyncPath))
                {
                    // НОВА синхронізація - потрібна початкова синхронізація
                    var dialog = new FolderPickerDialog();
                    if (dialog.ShowDialog() == true)
                    {
                        Debug.WriteLine($"[MAIN] Setting up NEW sync for folder {_currentFolder.Id}");
                        var updated = await _fileService.UpdateFolderSyncAsync(_currentFolder.Id, dialog.SelectedPath);
                        if (updated != null)
                        {
                            _currentFolder.SyncPath = dialog.SelectedPath;
                            // performInitialSync = true (за замовчуванням)
                            await _syncService.ConfigureSyncAsync(dialog.SelectedPath, _currentFolder.Id, autoSync: true, performInitialSync: true);
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
                    // ІСНУЮЧА синхронізація - просто активуємо без початкової синхронізації
                    Debug.WriteLine($"[MAIN] Activating EXISTING sync for folder {_currentFolder.Id}");
                    await _syncService.ActivateExistingSyncAsync(_currentFolder.SyncPath, _currentFolder.Id);
                }
            }
            else
            {
                if (_currentFolder != null && !string.IsNullOrEmpty(_currentFolder.SyncPath))
                {
                    var result = MessageBox.Show(
                        "Remove sync configuration for this folder?\nThis will not delete any files.",
                        "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Debug.WriteLine($"[MAIN] Removing sync for folder {_currentFolder.Id}");
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
            Title = "Введення";
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
            var cancelButton = new Button { Content = "Скасувати", Width = 80 };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(buttonPanel);

            Content = stack;
        }
    }
}