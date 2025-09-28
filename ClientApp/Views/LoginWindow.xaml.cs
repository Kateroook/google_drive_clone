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

namespace ClientApp.Views
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly ApiService _api;
        public ApiService ApiService { get; private set; }
        public LoginWindow()
        {
            InitializeComponent();
            _api = new ApiService("http://localhost:5000");
            ApiService = new ApiService("http://localhost:5000");
        }
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameBox.Text;
            string password = PasswordBox.Password;

            bool success = await _api.LoginAsync(username, password);
            if (success)
            {
                MainWindow mainWindow = new MainWindow(_api);
                mainWindow.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid credentials!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private async void GoogleLoginButton_Click(object sender, RoutedEventArgs e)
        {
            var oauth = new OAuthService();
            string token = await oauth.LoginAsync();

            if (!string.IsNullOrEmpty(token))
            {
                _api.SetToken(token); // Save token in ApiService
                MessageBox.Show("Logged in with Google!");

                MainWindow mainWindow = new MainWindow(_api);
                mainWindow.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Login failed");
            }
        }
    }
}
