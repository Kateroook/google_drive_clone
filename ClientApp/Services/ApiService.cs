using ClientApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Json;

namespace ClientApp.Services
{
    public class ApiService
    {
        private readonly HttpClient _client;
        public string Token { get; set; }

        public ApiService(string baseUrl)
        {
            _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        }

        public void SetToken(string token)
        {
            Token = token;
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        }

        public async Task<List<FileItem>> GetFilesAsync()
        {
            return await _client.GetFromJsonAsync<List<FileItem>>("/files");
        }

        public async Task UploadFileAsync(string filePath)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(File.OpenRead(filePath)), "file", Path.GetFileName(filePath));
            await _client.PostAsync("/upload", content);
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            var response = await _client.PostAsJsonAsync("/login", new { username, password });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                Token = result.Token;
                _client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
                return true;
            }
            return false;
        }

        public async Task DownloadFileAsync(string fileId, string savePath)
        {
            var response = await _client.GetAsync($"/download/{fileId}");
            using var fs = new FileStream(savePath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
        }

        public async Task DeleteFileAsync(string fileId)
        {
            await _client.DeleteAsync($"/files/{fileId}");
        }
    }
}
