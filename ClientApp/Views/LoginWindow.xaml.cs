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
        public ApiService ApiService { get; }
        private readonly OAuthService _oAuthService = new OAuthService();
        public LoginWindow()
        {
            InitializeComponent();
            _oAuthService = new OAuthService();
            CheckStoredToken();
        }

        private async void CheckStoredToken()
        {
            _oAuthService.LoadStoredToken();

            if (_oAuthService.IsAuthenticated)
            {
                StatusText.Text = "Verifying stored credentials...";
                
                var userInfo = await _oAuthService.GetUserInfoAsync();
                if (userInfo != null)
                {
                    OpenMainWindow(userInfo);
                }
                else
                {
                    StatusText.Text = "Sign in to access your files.";
                }
            }
        }


        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            LoadingPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Opening browser for authentication...";

            try
            {
                await _oAuthService.AuthenticateAsync();

                // Чекаємо на завершення аутентифікації
                await Task.Delay(3000);

                if (_oAuthService.IsAuthenticated)
                {
                    StatusText.Text = "Getting user information...";
                    var userInfo = await _oAuthService.GetUserInfoAsync();

                    if (userInfo != null)
                    {
                        StatusText.Text = "Authentication successful!";
                        await Task.Delay(500);

                        OpenMainWindow(userInfo);
                    }
                    else
                    {
                        ShowError("Failed to get user information. Please try again.");
                    }
                }
                else
                {
                    ShowError("Authentication failed. Please try again.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Error during authentication: {ex.Message}");
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenMainWindow(UserInfo userInfo)
        {
            var mainWindow = new MainWindow(_oAuthService, userInfo);
            mainWindow.Show();
            this.Close();
        }

        private void ShowError(string message)
        {
            StatusText.Text = message;
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}