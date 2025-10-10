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

        // Ідентифікація клієнта
        private const string CLIENT_TYPE = "desktop";
        private const string REDIRECT_URI = "http://localhost:5005/callback";

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
            Debug.WriteLine("=== Starting Desktop Client Authentication ===");
            try
            {
                // Генерація PKCE параметрів
                GeneratePKCE();
                Debug.WriteLine($"Code Verifier: {_codeVerifier}");
                Debug.WriteLine($"Code Challenge: {_codeChallenge}");
                Debug.WriteLine($"State: {_state}");

                // Створюємо структурований state об'єкт
                var stateObject = new
                {
                    value = _state,
                    client = CLIENT_TYPE,
                    redirect = REDIRECT_URI
                };

                var stateJson = JsonConvert.SerializeObject(stateObject);
                Debug.WriteLine($"State object: {stateJson}");

                // Кодуємо state в Base64URL
                var stateBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(stateJson));
                Debug.WriteLine($"Encoded state: {stateBase64}");

                // Запускаємо listener і відразу відкриваємо браузер
                var callbackTask = StartLocalCallbackServerAsync();

                await Task.Delay(500);

                // Відкриття браузера для аутентифікації
                var authUrl = $"{_serverUrl}/auth/google?code_challenge={_codeChallenge}&state={WebUtility.UrlEncode(stateBase64)}";
                Debug.WriteLine($"Opening browser to: {authUrl}");

                var psi = new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);

                // Чекаємо завершення callback
                var success = await callbackTask;

                Debug.WriteLine($"=== Authentication Process Completed: {(success ? "Success" : "Failed")} ===");
                return success;
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
        private async Task<bool> StartLocalCallbackServerAsync()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"{REDIRECT_URI}/");
                _httpListener.Start();
                Debug.WriteLine($"Listening for OAuth callback on {REDIRECT_URI}");

                // Очікуємо запит від браузера
                var context = await _httpListener.GetContextAsync();
                Debug.WriteLine("OAuth callback received.");

                var request = context.Request;
                var response = context.Response;

                // Отримання параметрів з URL
                var code = request.QueryString["code"];
                var state = request.QueryString["state"];
                var error = request.QueryString["error"];

                Debug.WriteLine($"Callback parameters:");
                Debug.WriteLine($"  - code: {code?.Substring(0, Math.Min(10, code?.Length ?? 0))}...");
                Debug.WriteLine($"  - state: {state}");
                Debug.WriteLine($"  - error: {error}");

                bool authSuccess = false;

                // Відправка відповіді в браузер
                string responseString;
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"❌ Authentication failed with error: {error}");
                    responseString = GenerateErrorPage(error);
                }
                else if (string.IsNullOrEmpty(code))
                {
                    Debug.WriteLine("❌ No authorization code received");
                    responseString = GenerateErrorPage("no_code");
                }
                else if (state != _state)
                {
                    Debug.WriteLine($"❌ State mismatch! Expected: {_state}, Received: {state}");
                    responseString = GenerateErrorPage("state_mismatch");
                }
                else
                {
                    Debug.WriteLine("✅ State verified successfully");
                    Debug.WriteLine("Exchanging code for token...");

                    authSuccess = await ExchangeCodeForToken(code);

                    if (authSuccess)
                    {
                        Debug.WriteLine("✅ Authentication successful!");
                        responseString = GenerateSuccessPage();
                    }
                    else
                    {
                        Debug.WriteLine("❌ Token exchange failed");
                        responseString = GenerateErrorPage("token_exchange_failed");
                    }
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

                return authSuccess;
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
        private async Task<bool> ExchangeCodeForToken(string code)
        {
            try
            {
                Debug.WriteLine("=== Token Exchange ===");

                var tokenRequest = new
                {
                    code = code,
                    code_verifier = _codeVerifier,
                    state = _state
                };

                var json = JsonConvert.SerializeObject(tokenRequest);
                Debug.WriteLine($"Token request payload: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_serverUrl}/auth/token", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Token response status: {response.StatusCode}");
                Debug.WriteLine($"Token response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);

                    _accessToken = tokenResponse.AccessToken;
                    Debug.WriteLine($"✅ Access token received: {_accessToken.Substring(0, Math.Min(20, _accessToken.Length))}...");
                    Debug.WriteLine($"   Token type: {tokenResponse.TokenType}");
                    Debug.WriteLine($"   Client type: {tokenResponse.ClientType}");
                    Debug.WriteLine($"   Expires in: {tokenResponse.ExpiresIn} seconds");

                    // Зберігання токену
                    SaveTokenSecurely(_accessToken);
                    Debug.WriteLine("✅ Token saved securely");

                    return true;
                }
                else
                {
                    Debug.WriteLine($"❌ Token exchange failed with status {response.StatusCode}");
                    Debug.WriteLine($"Response: {responseContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Token exchange error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Генерація HTML сторінки успіху
        /// </summary>
        private string GenerateSuccessPage()
        {
            return @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Authentication Successful</title>
                    <meta charset='utf-8'>
                    <style>
                        body { 
                            font-family: 'Segoe UI', Arial, sans-serif; 
                            text-align: center; 
                            padding: 50px;
                            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                            color: white;
                        }
                        .container {
                            background: white;
                            padding: 40px;
                            border-radius: 15px;
                            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
                            max-width: 500px;
                            margin: 0 auto;
                            color: #333;
                        }
                        h1 { color: #28a745; margin-top: 0; }
                        .icon { font-size: 64px; margin-bottom: 20px; }
                        .client-badge {
                            display: inline-block;
                            background: #667eea;
                            color: white;
                            padding: 5px 15px;
                            border-radius: 20px;
                            font-size: 14px;
                            margin-top: 10px;
                        }
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='icon'>✓</div>
                        <h1>Authentication Successful</h1>
                        <p>You have been successfully authenticated.</p>
                        <div class='client-badge'>🖥️ Desktop Client</div>
                        <p style='margin-top: 30px; color: #666;'>You can close this window and return to the application.</p>
                    </div>
                    <script>
                        setTimeout(function() { window.close(); }, 3000);
                    </script>
                </body>
                </html>";
        }

        /// <summary>
        /// Генерація HTML сторінки помилки
        /// </summary>
        private string GenerateErrorPage(string error)
        {
            string errorMessage = error switch
            {
                "state_mismatch" => "Security validation failed. Please try again.",
                "no_code" => "No authorization code received from server.",
                "token_exchange_failed" => "Failed to exchange authorization code for token.",
                _ => "An error occurred during authentication."
            };

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Authentication Failed</title>
                    <meta charset='utf-8'>
                    <style>
                        body {{ 
                            font-family: 'Segoe UI', Arial, sans-serif; 
                            text-align: center; 
                            padding: 50px;
                            background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
                            color: white;
                        }}
                        .container {{
                            background: white;
                            padding: 40px;
                            border-radius: 15px;
                            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
                            max-width: 500px;
                            margin: 0 auto;
                            color: #333;
                        }}
                        h1 {{ color: #dc3545; margin-top: 0; }}
                        .icon {{ font-size: 64px; margin-bottom: 20px; }}
                        .error-code {{
                            background: #f8d7da;
                            color: #721c24;
                            padding: 10px;
                            border-radius: 5px;
                            margin-top: 20px;
                            font-family: monospace;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='icon'>❌</div>
                        <h1>Authentication Failed</h1>
                        <p>{errorMessage}</p>
                        <div class='error-code'>Error: {error}</div>
                        <p style='margin-top: 30px; color: #666;'>You can close this window and return to the application.</p>
                    </div>
                </body>
                </html>";
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
        public object ClientType { get; internal set; }
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