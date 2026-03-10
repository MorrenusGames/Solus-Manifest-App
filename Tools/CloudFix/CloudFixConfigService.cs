using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace SolusManifestApp.Tools.CloudFix
{
    public class CloudFixConfigService
    {
        readonly string _configPath;
        readonly string _cachePath;

        public CloudFixConfigService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "SolusManifestApp");
            Directory.CreateDirectory(appFolder);
            _configPath = Path.Combine(appFolder, "cloudfix_config.json");
            _cachePath = Path.Combine(appFolder, "cloudfix_publisher_cache.json");
        }

        public class Config
        {
            public Dictionary<string, bool> Overrides { get; set; } = new();
        }

        public Config LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CloudFix: Failed to load config: {ex.Message}");
            }
            return new Config();
        }

        public void SaveConfig(Config config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CloudFix: Failed to save config: {ex.Message}");
            }
        }

        public class PublisherEntry
        {
            public string Publisher { get; set; } = "";
            public string Developer { get; set; } = "";
            public bool IsBlockedPublisher { get; set; }
            public long FetchedUtc { get; set; }
        }

        public class PublisherCache
        {
            public Dictionary<string, PublisherEntry> Entries { get; set; } = new();
        }

        public PublisherCache LoadPublisherCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    var json = File.ReadAllText(_cachePath);
                    return JsonConvert.DeserializeObject<PublisherCache>(json) ?? new PublisherCache();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CloudFix: Failed to load publisher cache: {ex.Message}");
            }
            return new PublisherCache();
        }

        public void SavePublisherCache(PublisherCache cache)
        {
            try
            {
                var json = JsonConvert.SerializeObject(cache, Formatting.Indented);
                File.WriteAllText(_cachePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CloudFix: Failed to save publisher cache: {ex.Message}");
            }
        }
    }
}
