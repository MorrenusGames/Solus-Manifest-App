using SolusManifestApp.Interfaces;
using SolusManifestApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class CacheService : ICacheService
    {
        private readonly string _cacheFolder;
        private readonly string _iconCacheFolder;
        private readonly string _dataCacheFolder;
        private readonly HttpClient _httpClient;
        private readonly LoggerService? _logger;

        public CacheService(LoggerService? logger = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheFolder = Path.Combine(appData, "SolusManifestApp", "Cache");
            _iconCacheFolder = Path.Combine(_cacheFolder, "Icons");
            _dataCacheFolder = Path.Combine(_cacheFolder, "Data");

            Directory.CreateDirectory(_iconCacheFolder);
            Directory.CreateDirectory(_dataCacheFolder);

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SolusManifestApp/1.0");

            _logger = logger;
        }

        // Icon Caching
        public async Task<string?> GetIconAsync(string appId, string iconUrl)
        {
            if (string.IsNullOrEmpty(appId))
                return null;

            var iconPath = Path.Combine(_iconCacheFolder, $"{appId}.jpg");

            // Return cached icon if exists
            if (File.Exists(iconPath))
            {
                return iconPath;
            }

            // 1. Try downloading from the provided URL first (from manifest API)
            if (!string.IsNullOrEmpty(iconUrl))
            {
                try
                {
                    _logger?.Debug($"[1/4] Downloading icon for {appId} from provided URL: {iconUrl}");
                    var response = await _httpClient.GetAsync(iconUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        if (bytes.Length > 0)
                        {
                            await WriteFileSafelyAsync(iconPath, bytes);
                            _logger?.Info($"✓ Downloaded icon for {appId} from provided URL ({bytes.Length} bytes)");
                            ManageIconCacheSize();
                            return iconPath;
                        }
                    }
                    _logger?.Debug($"✗ Provided URL failed: Status {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"✗ Provided URL exception: {ex.Message}");
                }
            }

            // 2. Fallback to SteamCMD API
            _logger?.Debug($"[2/4] Trying SteamCMD API for {appId}");
            try
            {
                var steamCmdUrl = $"https://api.steamcmd.net/v1/info/{appId}";
                var response = await _httpClient.GetAsync(steamCmdUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    dynamic? data = JsonConvert.DeserializeObject<dynamic>(json);

                    if (data != null && data!["data"] != null && data!["data"]!["header_image"] != null)
                    {
                        string headerImagePath = data!["data"]!["header_image"]!.ToString();
                        if (!string.IsNullOrEmpty(headerImagePath))
                        {
                            var imageUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/{headerImagePath}";
                            _logger?.Debug($"Found header_image from SteamCMD: {imageUrl}");

                            var imageResponse = await _httpClient.GetAsync(imageUrl);
                            if (imageResponse.IsSuccessStatusCode)
                            {
                                var bytes = await imageResponse.Content.ReadAsByteArrayAsync();
                                if (bytes.Length > 0)
                                {
                                    await WriteFileSafelyAsync(iconPath, bytes);
                                    _logger?.Info($"✓ Downloaded icon for {appId} from SteamCMD API ({bytes.Length} bytes)");
                                    ManageIconCacheSize();
                                    return iconPath;
                                }
                            }
                        }
                    }
                }
                _logger?.Debug($"✗ SteamCMD API failed for {appId}");
            }
            catch (Exception ex)
            {
                _logger?.Debug($"✗ SteamCMD API exception: {ex.Message}");
            }

            // 3. Fallback to Steam Store API
            _logger?.Debug($"[3/4] Trying Steam Store API for {appId}");
            try
            {
                var storeApiUrl = $"https://store.steampowered.com/api/appdetails?appids={appId}";
                var storeResponse = await _httpClient.GetAsync(storeApiUrl);
                if (storeResponse.IsSuccessStatusCode)
                {
                    var json = await storeResponse.Content.ReadAsStringAsync();
                    dynamic? data = JsonConvert.DeserializeObject<dynamic>(json);

                    if (data != null && data![appId] != null && data![appId]!["success"] == true)
                    {
                        var gameData = data![appId]!["data"];
                        string? imageUrl = gameData["header_image"]?.ToString();

                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            _logger?.Debug($"Found header_image from Steam Store API: {imageUrl}");

                            var imageResponse = await _httpClient.GetAsync(imageUrl);
                            if (imageResponse.IsSuccessStatusCode)
                            {
                                var bytes = await imageResponse.Content.ReadAsByteArrayAsync();
                                if (bytes.Length > 0)
                                {
                                    await WriteFileSafelyAsync(iconPath, bytes);
                                    _logger?.Info($"✓ Downloaded icon for {appId} from Steam Store API ({bytes.Length} bytes)");
                                    ManageIconCacheSize();
                                    return iconPath;
                                }
                            }
                        }
                    }
                }
                _logger?.Debug($"✗ Steam Store API failed for {appId}");
            }
            catch (Exception ex)
            {
                _logger?.Debug($"✗ Steam Store API exception: {ex.Message}");
            }

            // 4. Last resort: Direct CDN URLs
            _logger?.Debug($"[4/4] Trying direct CDN URLs for {appId}");
            var fallbackUrls = new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg"
            };

            foreach (var url in fallbackUrls)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        if (bytes.Length > 0)
                        {
                            await WriteFileSafelyAsync(iconPath, bytes);
                            _logger?.Info($"✓ Downloaded icon for {appId} from direct CDN: {url} ({bytes.Length} bytes)");
                            ManageIconCacheSize();
                            return iconPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"✗ Direct CDN failed for {url}: {ex.Message}");
                }
            }

            _logger?.Warning($"✗✗✗ All 4 fallback methods failed for {appId}");
            return null;
        }

        public bool HasCachedIcon(string appId)
        {
            var iconPath = Path.Combine(_iconCacheFolder, $"{appId}.jpg");
            return File.Exists(iconPath);
        }

        public string? GetCachedIconPath(string appId)
        {
            var iconPath = Path.Combine(_iconCacheFolder, $"{appId}.jpg");
            return File.Exists(iconPath) ? iconPath : null;
        }

        public async Task<string?> GetSteamGameIconAsync(string appId, string? localSteamIconPath, string cdnIconUrl)
        {
            // Check if already cached
            var cachedPath = Path.Combine(_iconCacheFolder, $"steam_{appId}.jpg");
            if (File.Exists(cachedPath))
            {
                return cachedPath;
            }

            // Try to copy from Steam's local cache first
            if (!string.IsNullOrEmpty(localSteamIconPath) && File.Exists(localSteamIconPath))
            {
                try
                {
                    File.Copy(localSteamIconPath, cachedPath, overwrite: true);
                    ManageIconCacheSize();
                    return cachedPath;
                }
                catch
                {
                    // Fall through to CDN download
                }
            }

            // Try header images only
            var cdnUrls = new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg"
            };

            _logger?.Debug($"Trying {cdnUrls.Length} CDN URLs for AppId {appId}");

            foreach (var url in cdnUrls)
            {
                try
                {
                    _logger?.Debug($"Attempting: {url}");
                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        await WriteFileSafelyAsync(cachedPath, bytes);
                        _logger?.Info($"✓ Success! Downloaded {bytes.Length} bytes from {url}");
                        ManageIconCacheSize();
                        return cachedPath;
                    }
                    else
                    {
                        _logger?.Debug($"✗ Failed: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"✗ Exception: {ex.Message}");
                }
            }

            // Fallback to Steam Store API
            _logger?.Info($"All CDN URLs failed, trying Steam Store API for AppId {appId}");
            try
            {
                var storeApiUrl = $"https://store.steampowered.com/api/appdetails/?appids={appId}";
                _logger?.Debug($"Fetching: {storeApiUrl}");

                var storeResponse = await _httpClient.GetAsync(storeApiUrl);
                if (storeResponse.IsSuccessStatusCode)
                {
                    var json = await storeResponse.Content.ReadAsStringAsync();

                    // Parse JSON to get header_image or capsule_image
                    dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                    if (data != null && data![appId] != null && data![appId]!["success"] == true)
                    {
                        var gameData = data![appId]!["data"];
                        string? imageUrl = gameData["header_image"]?.ToString();

                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            _logger?.Info($"Found image URL from Steam Store API: {imageUrl}");

                            var imageResponse = await _httpClient.GetAsync(imageUrl);
                            if (imageResponse.IsSuccessStatusCode)
                            {
                                var bytes = await imageResponse.Content.ReadAsByteArrayAsync();
                                await WriteFileSafelyAsync(cachedPath, bytes);
                                _logger?.Info($"✓ Success! Downloaded {bytes.Length} bytes from Steam Store API");
                                ManageIconCacheSize();
                                return cachedPath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Steam Store API fallback failed: {ex.Message}");
            }

            _logger?.Warning($"✗ All methods failed for AppId {appId}");
            return null;
        }

        public void ClearIconCache()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_iconCacheFolder))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to clear icon cache: {ex.Message}");
            }
        }

        private void ManageIconCacheSize()
        {
            try
            {
                const long maxCacheSizeBytes = 200 * 1024 * 1024; // 200 MB
                const long targetSizeBytes = 180 * 1024 * 1024;   // 180 MB (buffer)

                var iconFiles = new DirectoryInfo(_iconCacheFolder).GetFiles("*.jpg");

                // Calculate total cache size
                long totalSize = 0;
                foreach (var file in iconFiles)
                {
                    totalSize += file.Length;
                }

                _logger?.Debug($"Icon cache size: {totalSize / 1024 / 1024} MB ({iconFiles.Length} files)");

                // If cache is under limit, no action needed
                if (totalSize <= maxCacheSizeBytes)
                {
                    return;
                }

                _logger?.Info($"Icon cache exceeded 200MB ({totalSize / 1024 / 1024} MB). Cleaning up oldest files...");

                // Sort files by last access time (oldest first)
                var sortedFiles = iconFiles.OrderBy(f => f.LastAccessTime).ToArray();

                // Delete oldest files until we're under target size
                long currentSize = totalSize;
                int deletedCount = 0;

                foreach (var file in sortedFiles)
                {
                    if (currentSize <= targetSizeBytes)
                    {
                        break;
                    }

                    try
                    {
                        var fileSize = file.Length;
                        file.Delete();
                        currentSize -= fileSize;
                        deletedCount++;
                        _logger?.Debug($"Deleted old cache file: {file.Name} ({fileSize / 1024} KB)");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to delete cache file {file.Name}: {ex.Message}");
                    }
                }

                _logger?.Info($"Cache cleanup complete. Deleted {deletedCount} files. New size: {currentSize / 1024 / 1024} MB");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error managing icon cache size: {ex.Message}");
            }
        }

        // Data Caching for Offline Mode
        public void CacheManifests(List<Manifest> manifests)
        {
            try
            {
                var json = JsonConvert.SerializeObject(manifests, Formatting.Indented);
                var filePath = Path.Combine(_dataCacheFolder, "manifests.json");
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to cache manifests: {ex.Message}");
            }
        }

        public List<Manifest>? GetCachedManifests()
        {
            try
            {
                var filePath = Path.Combine(_dataCacheFolder, "manifests.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<List<Manifest>>(json);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to read cached manifests: {ex.Message}");
            }

            return null;
        }

        public void CacheManifest(Manifest manifest)
        {
            try
            {
                var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                var filePath = Path.Combine(_dataCacheFolder, $"manifest_{manifest.AppId}.json");
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to cache manifest for {manifest.AppId}: {ex.Message}");
            }
        }

        public Manifest? GetCachedManifest(string appId)
        {
            try
            {
                var filePath = Path.Combine(_dataCacheFolder, $"manifest_{appId}.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<Manifest>(json);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to read cached manifest for {appId}: {ex.Message}");
            }

            return null;
        }

        public bool IsOfflineMode()
        {
            // Check if we have cached data
            var filePath = Path.Combine(_dataCacheFolder, "manifests.json");
            return File.Exists(filePath);
        }

        public void ClearDataCache()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_dataCacheFolder))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to clear data cache: {ex.Message}");
            }
        }

        public void ClearAllCache()
        {
            _logger?.Info("Clearing all cache");
            ClearIconCache();
            ClearDataCache();
            _logger?.Info("Cache cleared successfully");
        }

        public long GetCacheSize()
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.GetFiles(_cacheFolder, "*", SearchOption.AllDirectories))
                {
                    size += new FileInfo(file).Length;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Error calculating cache size: {ex.Message}");
            }

            return size;
        }

        // Steam App List Caching (for game name lookups)
        public void CacheSteamAppList(string jsonData)
        {
            try
            {
                var filePath = Path.Combine(_dataCacheFolder, "steam_applist.json");
                var cacheInfo = new
                {
                    timestamp = DateTime.Now,
                    data = jsonData
                };
                var json = JsonConvert.SerializeObject(cacheInfo, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to cache Steam app list: {ex.Message}");
            }
        }

        public (string? data, DateTime? timestamp) GetCachedSteamAppList()
        {
            try
            {
                var filePath = Path.Combine(_dataCacheFolder, "steam_applist.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var obj = JsonConvert.DeserializeObject<dynamic>(json);
                    return (obj?.data?.ToString(), obj?.timestamp != null ? (DateTime)obj.timestamp : null);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to read cached Steam app list: {ex.Message}");
            }

            return (null, null);
        }

        public bool IsSteamAppListCacheValid(TimeSpan maxAge)
        {
            var (_, timestamp) = GetCachedSteamAppList();
            if (timestamp.HasValue)
            {
                return DateTime.Now - timestamp.Value < maxAge;
            }
            return false;
        }

        // Game Status Caching (from manifest API)
        public void CacheGameStatus(string appId, string jsonData)
        {
            try
            {
                var filePath = Path.Combine(_dataCacheFolder, $"status_{appId}.json");
                var cacheInfo = new
                {
                    timestamp = DateTime.Now,
                    data = jsonData
                };
                var json = JsonConvert.SerializeObject(cacheInfo, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to cache game status for {appId}: {ex.Message}");
            }
        }

        public (string? data, DateTime? timestamp) GetCachedGameStatus(string appId)
        {
            try
            {
                var filePath = Path.Combine(_dataCacheFolder, $"status_{appId}.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var obj = JsonConvert.DeserializeObject<dynamic>(json);
                    return (obj?.data?.ToString(), obj?.timestamp != null ? (DateTime)obj.timestamp : null);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to read cached game status for {appId}: {ex.Message}");
            }

            return (null, null);
        }

        public bool IsGameStatusCacheValid(string appId, TimeSpan maxAge)
        {
            var (_, timestamp) = GetCachedGameStatus(appId);
            if (timestamp.HasValue)
            {
                return DateTime.Now - timestamp.Value < maxAge;
            }
            return false;
        }

        // Helper method to write files safely with explicit flush to disk
        private async Task WriteFileSafelyAsync(string filePath, byte[] bytes)
        {
            await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await fileStream.WriteAsync(bytes, 0, bytes.Length);
                await fileStream.FlushAsync();
                fileStream.Close();
            }
        }
    }
}
