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
    /// Interaction logic for TextViewer.xaml
    /// </summary>
    public partial class TextViewer : Window
    {
        private readonly FileItem _file;
        private readonly ApiService _api;

        public TextViewer(FileItem file, ApiService api)
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
            TextContent.Text = await File.ReadAllTextAsync(tempPath);
        }
    }
}
