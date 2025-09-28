using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Services
{
    public class OAuthService
    {

        private readonly HttpClient _client;

        public OAuthService()
        {
            _client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        }

        public async Task<string> LoginAsync()
        {
            try
            {
                // 1. Відкрити Google OAuth сторінку в браузері
                string loginUrl = "http://localhost:5000/auth/google";
                Process.Start(new ProcessStartInfo
                {
                    FileName = loginUrl,
                    UseShellExecute = true
                });

                // 2. Очікувати токен (сервер може тимчасово зберегти токен у /auth/google/token)
                //    Тут для простоти клієнт просто стукає у бекенд
                for (int i = 0; i < 20; i++) // ~20 секунд очікування
                {
                    await Task.Delay(1000);
                    var response = await _client.GetAsync("/auth/google/token");
                    if (response.IsSuccessStatusCode)
                    {
                        string token = await response.Content.ReadAsStringAsync();
                        return token;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("OAuth Error: " + ex.Message);
                return null;
            }
        }
    }
}