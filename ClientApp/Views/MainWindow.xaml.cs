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

namespace ClientApp.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ApiService _api;
        private List<FileItem> _files;

        public MainWindow()
        {
            InitializeComponent();
            //_api = new ApiService("http://localhost:3000"); // або підтягуй з .env
        }
        public MainWindow(ApiService api)
        {
            InitializeComponent();
            _api = api;
            LoadFiles();
        }

        private async void LoadFiles()
        {
            _files = await _api.GetFilesAsync();
            FilesGrid.ItemsSource = _files;
        }

        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                await _api.UploadFileAsync(dlg.FileName);
                LoadFiles();
            }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            if (FilesGrid.SelectedItem is FileItem file)
            {
                SaveFileDialog dlg = new SaveFileDialog { FileName = file.Name };
                if (dlg.ShowDialog() == true)
                {
                    await _api.DownloadFileAsync(file.Name, dlg.FileName);
                    MessageBox.Show("Downloaded successfully!");
                }
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (FilesGrid.SelectedItem is FileItem file)
            {
                await _api.DeleteFileAsync(file.Name);
                LoadFiles();
            }
        }

        private void FilterBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_files == null) return;

            string filter = (FilterBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content.ToString();
            if (filter == "All")
                FilesGrid.ItemsSource = _files;
            else
                FilesGrid.ItemsSource = _files.Where(f => f.Name.EndsWith(filter));
        }

        private void SortAsc_Click(object sender, RoutedEventArgs e)
        {
            FilesGrid.ItemsSource = _files.OrderBy(f => f.UploadedBy).ToList();
        }

        private void SortDesc_Click(object sender, RoutedEventArgs e)
        {
            FilesGrid.ItemsSource = _files.OrderByDescending(f => f.UploadedBy).ToList();
        }

        private void FilesGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FilesGrid.SelectedItem is FileItem file)
            {
                if (file.Name.EndsWith(".cs"))
                {
                    TextViewer viewer = new TextViewer(file, _api);
                    viewer.Show();
                }
                else if (file.Name.EndsWith(".jpg"))
                {
                    ImageViewer viewer = new ImageViewer(file, _api);
                    viewer.Show();
                }
            }
        }
    }
}