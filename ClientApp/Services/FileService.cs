using ClientApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Services
{
    public class FileService
    {
        private readonly string _serverUrl = "http://localhost:5000";
        private readonly HttpClient _httpClient;
        private readonly OAuthService _oAuthService;

        public FileService(OAuthService oAuthService)
        {
            _oAuthService = oAuthService;
            _httpClient = new HttpClient();
        }

        private void SetAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_oAuthService.AccessToken}");
        }

        /// <summary>
        /// Отримання списку всіх файлів користувача
        /// </summary>
        public async Task<List<FileItem>> GetFilesAsync(long? folderId = null)
        {
            try
            {
                SetAuthHeader();
                var url = folderId.HasValue
                    ? $"{_serverUrl}/api/files?folder_id={folderId}"
                    : $"{_serverUrl}/api/files";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ApiResponse<List<FileItem>>>(content);
                    return result.Data ?? new List<FileItem>(); // Порожній список = успіх, але папка порожня
                }

               
                Debug.WriteLine($"Failed to get files: {response.StatusCode}");
                return null; // null = помилка сервера/мережі
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting files: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримання ВСІХ файлів користувача (незалежно від папки)
        /// </summary>
        public async Task<List<FileItem>> GetAllFilesAsync()
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/files");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ApiResponse<List<FileItem>>>(content);
                    return result.Data ?? new List<FileItem>();
                }

                return new List<FileItem>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting all files: {ex.Message}");
                return new List<FileItem>();
            }
        }

        /// <summary>
        /// Отримання ВСІХ папок користувача
        /// </summary>
        public async Task<List<FolderItem>> GetAllFoldersAsync()
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/folders/all");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ApiResponse<List<FolderItem>>>(content);
                    return result.Data ?? new List<FolderItem>();
                }

                return new List<FolderItem>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting all folders: {ex.Message}");
                return new List<FolderItem>();
            }
        }

        /// <summary>
        /// Завантаження файлу на сервер
        /// </summary>
        public async Task<FileItem> UploadFileAsync(string filePath, long? folderId = null)
        {
            try
            {
                SetAuthHeader();

                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath));
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

                    content.Add(fileContent, "file", Path.GetFileName(filePath));

                    if (folderId.HasValue)
                    {
                        content.Add(new StringContent(folderId.Value.ToString()), "folder_id");
                    }

                    var response = await _httpClient.PostAsync($"{_serverUrl}/api/files/upload", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<ApiResponse<FileItem>>(responseContent);
                        return result.Data;
                    }

                    Debug.WriteLine($"Upload failed: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error uploading file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Завантаження файлу з сервера
        /// </summary>
        public async Task<bool> DownloadFileAsync(long fileId, string savePath)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/files/{fileId}/download");

                if (response.IsSuccessStatusCode)
                {
                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                    File.WriteAllBytes(savePath, fileBytes);
                    return true;
                }

                Debug.WriteLine($"Download failed: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Видалення файлу
        /// </summary>
        public async Task<bool> DeleteFileAsync(long fileId)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.DeleteAsync($"{_serverUrl}/api/files/{fileId}");

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                Debug.WriteLine($"Delete failed: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Оновлення існуючого файлу (перезавантаження)
        /// </summary>
    public async Task<FileItem> UpdateFileAsync(long fileId, string newFilePath)
{
    try
    {
        SetAuthHeader();

        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(File.ReadAllBytes(newFilePath));
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            
            content.Add(fileContent, "file", Path.GetFileName(newFilePath));

            var response = await _httpClient.PutAsync($"{_serverUrl}/api/files/{fileId}", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ApiResponse<FileItem>>(responseContent);
                return result.Data;
            }

            Debug.WriteLine($"Update failed: {response.StatusCode}");
            var errorContent = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"Error content: {errorContent}");
            return null;
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Error updating file: {ex.Message}");
        return null;
    }
}

        /// <summary>
        /// Отримання вмісту файлу для попереднього перегляду
        /// </summary>
        public async Task<string> GetFileContentAsync(long fileId)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/files/{fileId}/content");

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();

                    var result = JsonConvert.DeserializeObject<ApiResponse<string>>(jsonContent);

                    return result?.Data;
                }

                Debug.WriteLine($"Failed to get file content: {response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting file content: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Отримання зображення для попереднього перегляду
        /// </summary>
        public async Task<byte[]> GetImageAsync(long fileId)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/files/{fileId}/download");

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отримання папок
        /// </summary>
        public async Task<List<FolderItem>> GetFoldersAsync(long? parentId = null)
        {
            try
            {
                SetAuthHeader();
                var url = parentId.HasValue
                    ? $"{_serverUrl}/api/folders?parent_id={parentId}"
                    : $"{_serverUrl}/api/folders";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ApiResponse<List<FolderItem>>>(content);
                    return result.Data ?? new List<FolderItem>();
                }

                return new List<FolderItem>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting folders: {ex.Message}");
                return new List<FolderItem>();
            }
        }

        /// <summary>
        /// Створення нової папки
        /// </summary>
        public async Task<FolderItem> CreateFolderAsync(string name, long? parentId = null, string localSyncPath = "")
        {
            try
            {
                SetAuthHeader();

                Debug.WriteLine($"Creating folder: name='{name}', parentId={parentId}, syncPath={localSyncPath}");

                var data = new { name, parent_id = parentId, sync_path = localSyncPath };
                var json = JsonConvert.SerializeObject(data);
                Debug.WriteLine($"Request JSON: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_serverUrl}/api/folders", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Response status: {response.StatusCode}");
                Debug.WriteLine($"Response content: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<ApiResponse<FolderItem>>(responseContent);
                    return result.Data;
                }
                else
                {
                    Debug.WriteLine($"Failed to create folder: {response.StatusCode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating folder: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Оновлення папки (встановлення/видалення синхронізації)
        /// </summary>
        public async Task<FolderItem> UpdateFolderSyncAsync(long folderId, string localSyncPath)
        {
            try
            {
                SetAuthHeader();

                var data = new { sync_path = localSyncPath };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"{_serverUrl}/api/folders/{folderId}", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ApiResponse<FolderItem>>(responseContent);
                    return result.Data;
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating folder sync: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Видалення папки
        /// </summary>
        public async Task<bool> DeleteFolderAsync(long folderId)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.DeleteAsync($"{_serverUrl}/api/folders/{folderId}");

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Folder deleted: {folderId}");
                    return true;
                }

                Debug.WriteLine($"Delete folder failed: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting folder: {ex.Message}");
                return false;
            }
        }
    }

    public class ApiResponse<T>
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public T Data { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}