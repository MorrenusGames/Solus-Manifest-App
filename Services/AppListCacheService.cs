using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SolusManifestApp.Services
{
    public class AppListEntry
    {
        [JsonProperty("appid")]
        public int AppId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class AppListCacheService
    {
        private const string AppListUrl = "https://applist.morrenus.xyz/";
        private readonly string _cachePath;
        private List<AppListEntry> _appList = new();
        private bool _isLoaded = false;

        public bool IsLoaded => _isLoaded;
        public int Count => _appList.Count;

        public AppListCacheService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "SolusManifestApp");
            Directory.CreateDirectory(appFolder);
            _cachePath = Path.Combine(appFolder, "applist.json");
        }

        public async Task InitializeAsync()
        {
            if (File.Exists(_cachePath))
            {
                var fileInfo = new FileInfo(_cachePath);
                if (fileInfo.LastWriteTime > DateTime.Now.AddHours(-24))
                {
                    await LoadFromCacheAsync();
                    return;
                }
            }

            await DownloadAndCacheAsync();
        }

        private async Task LoadFromCacheAsync()
        {
            try
            {
                var json = await File.ReadAllTextAsync(_cachePath);
                _appList = JsonConvert.DeserializeObject<List<AppListEntry>>(json) ?? new();
                _isLoaded = true;
            }
            catch
            {
                _appList = new();
                _isLoaded = false;
            }
        }

        public async Task DownloadAndCacheAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(2);
                var json = await client.GetStringAsync(AppListUrl);

                await File.WriteAllTextAsync(_cachePath, json);
                _appList = JsonConvert.DeserializeObject<List<AppListEntry>>(json) ?? new();
                _isLoaded = true;
            }
            catch
            {
                if (File.Exists(_cachePath))
                {
                    await LoadFromCacheAsync();
                }
            }
        }

        public List<AppListEntry> Search(string query, int limit = 10)
        {
            if (!_isLoaded || string.IsNullOrWhiteSpace(query))
                return new();

            var results = new List<AppListEntry>();
            var lowerQuery = query.ToLower();

            if (int.TryParse(query, out int appId))
            {
                foreach (var app in _appList)
                {
                    if (app.AppId == appId)
                    {
                        results.Add(app);
                        break;
                    }
                }
            }

            foreach (var app in _appList)
            {
                if (results.Count >= limit) break;

                if (app.Name.ToLower().StartsWith(lowerQuery))
                {
                    if (!results.Contains(app))
                        results.Add(app);
                }
            }

            if (results.Count < limit)
            {
                foreach (var app in _appList)
                {
                    if (results.Count >= limit) break;

                    if (app.Name.ToLower().Contains(lowerQuery))
                    {
                        if (!results.Contains(app))
                            results.Add(app);
                    }
                }
            }

            return results;
        }
    }
}
