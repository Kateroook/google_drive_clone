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

        //Files
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


        //Folders
        public async Task<List<FolderItem>> GetFoldersAsync(long? parentId = null)
        {
            string url = parentId.HasValue ? $"/folders?parentId={parentId}" : "/folders";
            return await _client.GetFromJsonAsync<List<FolderItem>>(url);
        }

        public async Task CreateFolderAsync(string name, long? parentId = null)
        {
            await _client.PostAsJsonAsync("/folders", new { name, parentId });
        }

        public async Task RenameFolderAsync(long id, string newName)
        {
            await _client.PutAsJsonAsync($"/folders/{id}", new { name = newName });
        }

        public async Task DeleteFolderAsync(long id)
        {
            await _client.DeleteAsync($"/folders/{id}");
        }

    }
}
