using SolusManifestApp.Interfaces;
using SolusManifestApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class ManifestApiService : IManifestApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CacheService? _cacheService;
        private const string BaseUrl = "https://manifest.morrenus.xyz/api/v1";
        private readonly TimeSpan _statusCacheExpiration = TimeSpan.FromMinutes(5); // Cache status for 5 minutes

        public ManifestApiService(IHttpClientFactory httpClientFactory, CacheService? cacheService = null)
        {
            _httpClientFactory = httpClientFactory;
            _cacheService = cacheService;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient("Default");
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        public async Task<Manifest?> GetManifestAsync(string appId, string apiKey)
        {
            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/manifest/{appId}?api_key={apiKey}";
                var response = await client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var preview = json.Length > 200 ? json.Substring(0, 200) : json;
                    throw new Exception($"Manifest not available for App ID {appId}. API returned {response.StatusCode}: {preview}");
                }

                try
                {
                    var manifest = JsonConvert.DeserializeObject<Manifest>(json);
                    return manifest;
                }
                catch (JsonException jex)
                {
                    var preview = json.Length > 200 ? json.Substring(0, 200) : json;
                    throw new Exception($"Invalid JSON from API for App ID {appId}. Response: {preview}", jex);
                }
            }
            catch (Exception ex) when (ex is not JsonException)
            {
                throw new Exception($"Failed to fetch manifest for {appId}: {ex.Message}", ex);
            }
        }

        public async Task<List<Manifest>?> SearchGamesAsync(string query, string apiKey)
        {
            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}&api_key={apiKey}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API returned {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var results = JsonConvert.DeserializeObject<List<Manifest>>(json);
                return results;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to search games: {ex.Message}", ex);
            }
        }

        public async Task<List<Manifest>?> GetAllGamesAsync(string apiKey)
        {
            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/games?api_key={apiKey}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API returned {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var results = JsonConvert.DeserializeObject<List<Manifest>>(json);
                return results;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch games list: {ex.Message}", ex);
            }
        }

        public bool ValidateApiKey(string apiKey)
        {
            return !string.IsNullOrWhiteSpace(apiKey) && apiKey.StartsWith("smm", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> TestApiKeyAsync(string apiKey)
        {
            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/status/10?api_key={apiKey}";
                var response = await client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<GameStatus?> GetGameStatusAsync(string appId, string apiKey)
        {
            // Check cache first if CacheService is available
            if (_cacheService != null && _cacheService.IsGameStatusCacheValid(appId, _statusCacheExpiration))
            {
                var (cachedJson, _) = _cacheService.GetCachedGameStatus(appId);
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    try
                    {
                        return JsonConvert.DeserializeObject<GameStatus>(cachedJson);
                    }
                    catch
                    {
                        // Ignore deserialization errors - will fetch fresh data
                    }
                }
            }

            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/status/{appId}?api_key={apiKey}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var status = JsonConvert.DeserializeObject<GameStatus>(json);

                // Cache the response
                _cacheService?.CacheGameStatus(appId, json);

                return status;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch status for {appId}: {ex.Message}", ex);
            }
        }

        public async Task<LibraryResponse?> GetLibraryAsync(string apiKey, int limit = 100, int offset = 0, string? search = null, string sortBy = "updated")
        {
            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/library?api_key={apiKey}&limit={limit}&offset={offset}&sort_by={sortBy}";
                if (!string.IsNullOrEmpty(search))
                {
                    url += $"&search={Uri.EscapeDataString(search)}";
                }

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API returned {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LibraryResponse>(json);
                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch library: {ex.Message}", ex);
            }
        }

        public async Task<SearchResponse?> SearchLibraryAsync(string query, string apiKey, int limit = 50, bool searchByAppId = false)
        {
            try
            {
                var client = CreateClient();
                var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}&api_key={apiKey}&limit={limit}&appid={searchByAppId.ToString().ToLower()}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API returned {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SearchResponse>(json);
                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to search library: {ex.Message}", ex);
            }
        }
    }
}
