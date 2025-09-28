using ClientApp.Views;
using System.Configuration;
using System.Data;
using System.Windows;

namespace ClientApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Спочатку відкриваємо LoginWindow
            var loginWindow = new LoginWindow();
            bool? result = loginWindow.ShowDialog(); // ShowDialog чекає на закриття вікна

            // 2. Якщо успішний логін (ShowDialog() повернув true)
            if (result == true)
            {
                var mainWindow = new MainWindow(loginWindow.ApiService); // передаємо ApiService
                mainWindow.Show();
            }
            else
            {
                // Якщо користувач скасував або не залогінився
                Shutdown();
            }
        }
    }

}
