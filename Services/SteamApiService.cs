using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class SteamApp
    {
        [JsonProperty("appid")]
        public int AppId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class SteamAppList
    {
        [JsonProperty("apps")]
        public List<SteamApp> Apps { get; set; } = new();
    }

    public class SteamApiResponse
    {
        [JsonProperty("applist")]
        public SteamAppList? AppList { get; set; }

        [JsonProperty("response")]
        public SteamStoreServiceResponse? Response { get; set; }
    }

    // New IStoreService/GetAppList response models
    public class SteamStoreServiceResponse
    {
        [JsonProperty("apps")]
        public List<SteamStoreApp> Apps { get; set; } = new();

        [JsonProperty("have_more_results")]
        public bool HaveMoreResults { get; set; }

        [JsonProperty("last_appid")]
        public int LastAppId { get; set; }
    }

    public class SteamStoreApp
    {
        [JsonProperty("appid")]
        public int AppId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("last_modified")]
        public long LastModified { get; set; }

        [JsonProperty("price_change_number")]
        public long PriceChangeNumber { get; set; }
    }

    // Steam Store Search Models
    public class SteamStoreSearchItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("tiny_image")]
        public string TinyImage { get; set; } = string.Empty;

        [JsonProperty("capsule_image")]
        public string CapsuleImage { get; set; } = string.Empty;

        [JsonProperty("header_image")]
        public string HeaderImage { get; set; } = string.Empty;

        [JsonProperty("metascore")]
        public string Metascore { get; set; } = string.Empty;

        [JsonProperty("price")]
        public SteamPrice? Price { get; set; }
    }

    public class SteamPrice
    {
        [JsonProperty("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonProperty("initial")]
        public int Initial { get; set; }

        [JsonProperty("final")]
        public int Final { get; set; }

        [JsonProperty("discount_percent")]
        public int DiscountPercent { get; set; }

        [JsonProperty("initial_formatted")]
        public string InitialFormatted { get; set; } = string.Empty;

        [JsonProperty("final_formatted")]
        public string FinalFormatted { get; set; } = string.Empty;
    }

    public class SteamStoreSearchResponse
    {
        [JsonProperty("items")]
        public List<SteamStoreSearchItem> Items { get; set; } = new();

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class SteamApiService
    {
        private readonly HttpClient _httpClient;
        private readonly CacheService _cacheService;
        private SteamApiResponse? _cachedData;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromDays(7); // Cache for 7 days

        public SteamApiService(CacheService cacheService)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            // Add headers to mimic browser requests
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _cacheService = cacheService;
        }

        // For cases where CacheService is not available (backward compatibility)
        public SteamApiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _cacheService = new CacheService();
        }

        private async Task<SteamApiResponse?> GetAppListFromMorrenusApiAsync()
        {
            try
            {
                var url = "https://applist.morrenus.xyz/";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();

                // The API returns a dictionary of appid -> name
                var appDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                if (appDict == null || appDict.Count == 0)
                    return null;

                // Convert to SteamApiResponse format
                var apps = appDict.Select(kvp => new SteamApp
                {
                    AppId = int.TryParse(kvp.Key, out var id) ? id : 0,
                    Name = kvp.Value
                }).Where(a => a.AppId > 0).ToList();

                return new SteamApiResponse
                {
                    AppList = new SteamAppList
                    {
                        Apps = apps
                    }
                };
            }
            catch
            {
                // Fallback API failed - will try Steam API next
                return null;
            }
        }

        public async Task<SteamApiResponse?> GetAppListAsync(bool forceRefresh = false)
        {
            // Return in-memory cache if available
            if (!forceRefresh && _cachedData != null)
            {
                return _cachedData;
            }

            // Check disk cache
            if (!forceRefresh && _cacheService.IsSteamAppListCacheValid(_cacheExpiration))
            {
                var (cachedJson, _) = _cacheService.GetCachedSteamAppList();
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    try
                    {
                        _cachedData = JsonConvert.DeserializeObject<SteamApiResponse>(cachedJson);
                        return _cachedData;
                    }
                    catch
                    {
                        // Ignore deserialization errors - will fetch fresh data
                    }
                }
            }

            // Try new Morrenus applist API first (faster and no API key required)
            try
            {
                var morrenusData = await GetAppListFromMorrenusApiAsync();
                if (morrenusData != null)
                {
                    _cachedData = morrenusData;
                    var cacheJson = JsonConvert.SerializeObject(morrenusData);
                    _cacheService.CacheSteamAppList(cacheJson);
                    return morrenusData;
                }
            }
            catch
            {
                // Morrenus API failed - fall through to Steam API
            }

            // Fetch from Morrenus App List API
            try
            {
                var url = "https://applist.morrenus.xyz";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var appList = JsonConvert.DeserializeObject<List<SteamApp>>(json);

                // Build response in old format for backward compatibility
                var data = new SteamApiResponse
                {
                    AppList = new SteamAppList
                    {
                        Apps = appList ?? new List<SteamApp>()
                    }
                };

                // Cache to memory
                _cachedData = data;

                // Cache to disk
                var cacheJson = JsonConvert.SerializeObject(data);
                _cacheService.CacheSteamAppList(cacheJson);

                return data;
            }
            catch (Exception ex)
            {
                // Try to return stale cache if API fails
                var (cachedJson, _) = _cacheService.GetCachedSteamAppList();
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    try
                    {
                        _cachedData = JsonConvert.DeserializeObject<SteamApiResponse>(cachedJson);
                        return _cachedData;
                    }
                    catch
                    {
                        // Stale cache deserialization failed - throw original error
                    }
                }

                throw new Exception($"Failed to fetch Steam app list: {ex.Message}", ex);
            }
        }

        public string GetGameName(string appId, SteamApiResponse? steamData = null)
        {
            var data = steamData ?? _cachedData;

            if (data == null)
                return "Unknown Game";

            var app = data.AppList!.Apps.FirstOrDefault(a => a.AppId.ToString() == appId);
            return app?.Name ?? "Unknown Game";
        }

        public async Task<string> GetGameNameAsync(string appId)
        {
            var data = await GetAppListAsync();
            return GetGameName(appId, data);
        }

        public Dictionary<string, string> BuildAppIdToNameDictionary(SteamApiResponse? steamData = null)
        {
            var data = steamData ?? _cachedData;

            if (data == null)
                return new Dictionary<string, string>();

            return data.AppList!.Apps.ToDictionary(
                app => app.AppId.ToString(),
                app => app.Name
            );
        }

        // Steam Store Search - Matching your bot's implementation
        public async Task<SteamStoreSearchResponse?> SearchStoreAsync(string searchTerm, int limit = 25)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return null;

            try
            {
                var cleanedTerm = searchTerm.Trim();

                // Build URL exactly like the working browser URL
                var baseUrl = "https://store.steampowered.com/api/storesearch/";
                var queryParams = new Dictionary<string, string>
                {
                    { "term", cleanedTerm },
                    { "l", "english" },
                    { "cc", "US" },
                    { "realm", "1" },
                    { "origin", "https://store.steampowered.com" },
                    { "f", "jsonfull" },
                    { "start", "0" },
                    { "count", Math.Min(limit * 3, 50).ToString() }
                };

                var queryString = string.Join("&", queryParams.Select(kvp =>
                    $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                var fullUrl = $"{baseUrl}?{queryString}";

                var response = await _httpClient.GetAsync(fullUrl);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Steam API returned {response.StatusCode}: {errorContent}. URL: {fullUrl}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var searchResponse = JsonConvert.DeserializeObject<SteamStoreSearchResponse>(json);

                return searchResponse;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to search Steam store: {ex.Message}", ex);
            }
        }

        // Cache search results
        public async Task<SteamStoreSearchResponse?> SearchStoreWithCacheAsync(string searchTerm, int limit = 25)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return null;

            var cacheKey = $"search_{searchTerm.ToLower().Trim()}_{limit}";

            // Check cache first
            if (_cacheService.IsGameStatusCacheValid(cacheKey, TimeSpan.FromMinutes(30)))
            {
                var (cachedJson, _) = _cacheService.GetCachedGameStatus(cacheKey);
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    try
                    {
                        return JsonConvert.DeserializeObject<SteamStoreSearchResponse>(cachedJson);
                    }
                    catch
                    {
                        // Ignore deserialization errors - will fetch fresh data
                    }
                }
            }

            // Fetch from API
            var result = await SearchStoreAsync(searchTerm, limit);

            // Cache result
            if (result != null)
            {
                var json = JsonConvert.SerializeObject(result);
                _cacheService.CacheGameStatus(cacheKey, json);
            }

            return result;
        }
    }
}
