using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Services
{
    public class OAuthService
    {
        private readonly string _serverUrl = "http://localhost:5000";
        private readonly HttpClient _httpClient;
        private HttpListener _httpListener;
        private string _accessToken;

        // PKCE параметри
        private string _codeVerifier;
        private string _codeChallenge;
        private string _state;

        // Вибір методу зберігання токенів
        private readonly bool _useCredentialManager = true;

        public OAuthService()
        {
            _httpClient = new HttpClient();
        }

        public string AccessToken => _accessToken;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

        /// <summary>
        /// Генерація PKCE параметрів
        /// </summary>
        private void GeneratePKCE()
        {
            // Генерація code_verifier (43-128 символів)
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            _codeVerifier = Base64UrlEncode(randomBytes);

            // Генерація code_challenge = BASE64URL(SHA256(code_verifier))
            using (var sha256 = SHA256.Create())
            {
                var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_codeVerifier));
                _codeChallenge = Base64UrlEncode(challengeBytes);
            }

            // Генерація state для захисту від CSRF
            var stateBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(stateBytes);
            }
            _state = Base64UrlEncode(stateBytes);
        }

        /// <summary>
        /// Base64 URL кодування
        /// </summary>
        private string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        /// <summary>
        /// Початок процесу OAuth аутентифікації
        /// </summary>
        public async Task<bool> AuthenticateAsync()
        {
            Debug.WriteLine("Starting authentication process...");
            try
            {
                // Генерація PKCE параметрів
                GeneratePKCE();
                Debug.WriteLine($"Code Verifier: {_codeVerifier}");
                Debug.WriteLine($"Code Challenge: {_codeChallenge}");
                Debug.WriteLine($"State: {_state}");

                // ВИПРАВЛЕННЯ: Запускаємо listener і відразу відкриваємо браузер
                var callbackTask = StartLocalCallbackServerAsync();

                // Невелика затримка для гарантії що listener запущений
                await Task.Delay(500);

                // Відкриття браузера для аутентифікації
                var authUrl = $"{_serverUrl}/auth/google?code_challenge={_codeChallenge}&state={_state}";
                Debug.WriteLine($"Opening browser to: {authUrl}");

                var psi = new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);

                // Чекаємо завершення callback
                await callbackTask;

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Authentication error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Запуск локального HTTP сервера для отримання callback
        /// </summary>
        private async Task StartLocalCallbackServerAsync()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://localhost:5005/callback/");
                _httpListener.Start();
                Debug.WriteLine("Listening for OAuth callback on http://localhost:5005/callback/");

                // Очікуємо запит від браузера
                var context = await _httpListener.GetContextAsync();
                Debug.WriteLine("OAuth callback received.");

                var request = context.Request;
                var response = context.Response;

                // Отримання параметрів з URL
                var code = request.QueryString["code"];
                var state = request.QueryString["state"];
                var error = request.QueryString["error"];

                Debug.WriteLine($"Callback parameters - code: {code?.Substring(0, Math.Min(10, code?.Length ?? 0))}..., state: {state}, error: {error}");

                // Відправка відповіді в браузер
                string responseString;
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"Authentication failed with error: {error}");
                    responseString = @"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>Authentication Failed</title>
                            <style>
                                body { font-family: Arial, sans-serif; text-align: center; padding: 50px; }
                                h1 { color: #dc3545; }
                            </style>
                        </head>
                        <body>
                            <h1>❌ Authentication Failed</h1>
                            <p>An error occurred during authentication.</p>
                            <p>You can close this window and return to the application.</p>
                        </body>
                        </html>";
                }
                else
                {
                    Debug.WriteLine("Authentication successful, sending success page.");
                    responseString = @"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>Authentication Successful</title>
                            <style>
                                body { font-family: Arial, sans-serif; text-align: center; padding: 50px; }
                                h1 { color: #28a745; }
                            </style>
                        </head>
                        <body>
                            <h1>✓ Authentication Successful</h1>
                            <p>You have been successfully authenticated.</p>
                            <p>You can close this window and return to the application.</p>
                            <script>
                                setTimeout(function() { window.close(); }, 3000);
                            </script>
                        </body>
                        </html>";
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                response.Close();

                _httpListener.Stop();
                Debug.WriteLine("HTTP listener stopped.");

                // Обробка отриманого коду
                if (!string.IsNullOrEmpty(code) && state == _state)
                {
                    Debug.WriteLine("State verified successfully, exchanging code for token...");
                    await ExchangeCodeForToken(code);
                    Debug.WriteLine("Token exchange completed.");
                }
                else if (state != _state)
                {
                    Debug.WriteLine($"ERROR: State mismatch! Expected: {_state}, Received: {state}");
                }
                else
                {
                    Debug.WriteLine("ERROR: No authorization code received.");
                }
            }
            catch (HttpListenerException ex)
            {
                Debug.WriteLine($"HttpListener error: {ex.Message}");
                Debug.WriteLine($"Error code: {ex.ErrorCode}");

                if (ex.ErrorCode == 5) // Access denied
                {
                    Debug.WriteLine("Access denied. Try running the application as Administrator or use a different port.");
                }
                else if (ex.ErrorCode == 32) // Port already in use
                {
                    Debug.WriteLine("Port 5005 is already in use. Close any other applications using this port.");
                }

                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Callback server error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Обмін authorization code на JWT токен
        /// </summary>
        private async Task ExchangeCodeForToken(string code)
        {
            try
            {
                Debug.WriteLine("Exchanging authorization code for token...");

                var tokenRequest = new
                {
                    code = code,
                    code_verifier = _codeVerifier,
                    state = _state
                };

                var json = JsonConvert.SerializeObject(tokenRequest);
                Debug.WriteLine($"Token request: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_serverUrl}/auth/token", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Token response status: {response.StatusCode}");
                Debug.WriteLine($"Token response content: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);

                    _accessToken = tokenResponse.AccessToken;
                    Debug.WriteLine($"Access token received: {_accessToken.Substring(0, Math.Min(20, _accessToken.Length))}...");

                    // Зберігання токену
                    SaveTokenSecurely(_accessToken);
                    Debug.WriteLine("Token saved securely.");
                }
                else
                {
                    Debug.WriteLine($"ERROR: Token exchange failed with status {response.StatusCode}");
                    Debug.WriteLine($"Response: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token exchange error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Отримання інформації про користувача
        /// </summary>
        public async Task<UserInfo> GetUserInfoAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                Debug.WriteLine("No access token available.");
                return null;
            }

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.GetAsync($"{_serverUrl}/auth/me");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"User info received: {content}");
                    return JsonConvert.DeserializeObject<UserInfo>(content);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("Token expired or invalid.");
                    _accessToken = null;
                    return null;
                }
                else
                {
                    Debug.WriteLine($"Get user info failed with status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Get user info error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Оновлення токену
        /// </summary>
        public async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
                return false;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.PostAsync($"{_serverUrl}/auth/refresh", null);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);

                    _accessToken = tokenResponse.AccessToken;
                    SaveTokenSecurely(_accessToken);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Refresh token error: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Вихід з системи
        /// </summary>
        public void Logout()
        {
            _accessToken = null;
            DeleteStoredToken();
            Debug.WriteLine("User logged out.");
        }

        /// <summary>
        /// Безпечне зберігання токену
        /// </summary>
        private void SaveTokenSecurely(string token)
        {
            if (_useCredentialManager)
            {
                CredentialManagerStorage.SaveToken(token);
            }
        }

        /// <summary>
        /// Видалення збереженого токену
        /// </summary>
        private void DeleteStoredToken()
        {
            if (_useCredentialManager)
            {
                CredentialManagerStorage.DeleteToken();
            }
        }

        /// <summary>
        /// Завантаження збереженого токену
        /// </summary>
        public void LoadStoredToken()
        {
            if (_useCredentialManager)
            {
                _accessToken = CredentialManagerStorage.LoadToken();
                if (!string.IsNullOrEmpty(_accessToken))
                {
                    Debug.WriteLine("Token loaded from secure storage.");
                }
            }
        }
    }

    // Моделі даних
    public class TokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public class UserInfo
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}