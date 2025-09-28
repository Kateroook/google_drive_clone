using ClientApp.Models;
using ClientApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;
using Path = System.IO.Path;

namespace ClientApp.Views
{
    /// <summary>
    /// Interaction logic for ImageViewer.xaml
    /// </summary>
    public partial class ImageViewer : Window
    {
        private readonly FileItem _file;
        private readonly ApiService _api;

        public ImageViewer(FileItem file, ApiService api)
        {
            InitializeComponent();
            _file = file;
            _api = api;
            LoadFile();
        }

        private async Task LoadFile()
        {
            string tempPath = Path.GetTempFileName();
            await _api.DownloadFileAsync(_file.Name, tempPath);

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(tempPath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            ImageContent.Source = bitmap;
        }
    }
}
