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
                StatusText.Text = "Перевірка облікових даних...";
                
                var userInfo = await _oAuthService.GetUserInfoAsync();
                if (userInfo != null)
                {
                    OpenMainWindow(userInfo);
                }
                else
                {
                    StatusText.Text = "Увійдіть для отримання доступу до файлів.";
                }
            }
        }


        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            LoadingPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Відкриваю браузер для автентифікації...";

            try
            {
                await _oAuthService.AuthenticateAsync();

                // Чекаємо на завершення аутентифікації
                await Task.Delay(3000);

                if (_oAuthService.IsAuthenticated)
                {
                    StatusText.Text = "Отримання даних користувача...";
                    var userInfo = await _oAuthService.GetUserInfoAsync();

                    if (userInfo != null)
                    {
                        StatusText.Text = "Автентифікація успішна!";
                        await Task.Delay(500);

                        OpenMainWindow(userInfo);
                    }
                    else
                    {
                        ShowError("Не знаю хто ви. Спробуйте знову.");
                    }
                }
                else
                {
                    ShowError("Невдалось автентифікувати. Спробуйте знову.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Помилка під час автентифікації: {ex.Message}");
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