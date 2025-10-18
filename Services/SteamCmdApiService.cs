using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class SteamCmdDepotData
    {
        [JsonProperty("data")]
        public Dictionary<string, AppData> Data { get; set; } = new();

        [JsonProperty("status")]
        public string Status { get; set; } = "";
    }

    public class AppData
    {
        [JsonProperty("depots")]
        public Dictionary<string, DepotData> Depots { get; set; } = new();

        [JsonProperty("common")]
        public CommonData Common { get; set; } = new();
    }

    public class CommonData
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";
    }

    public class DepotData
    {
        [JsonProperty("config")]
        public DepotConfig? Config { get; set; }

        [JsonProperty("manifests")]
        public Dictionary<string, ManifestData>? Manifests { get; set; }

        [JsonProperty("dlcappid")]
        public string? DlcAppId { get; set; }

        [JsonProperty("depotfromapp")]
        public string? DepotFromApp { get; set; }

        [JsonProperty("sharedinstall")]
        public string? SharedInstall { get; set; }
    }

    public class DepotConfig
    {
        [JsonProperty("oslist")]
        public string? OsList { get; set; }

        [JsonProperty("language")]
        public string? Language { get; set; }

        [JsonProperty("lowviolence")]
        public string? LowViolence { get; set; }

        [JsonProperty("realm")]
        public string? Realm { get; set; }
    }

    public class ManifestData
    {
        [JsonProperty("gid")]
        public string? Gid { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("download")]
        public long Download { get; set; }
    }

    public class SteamCmdApiService
    {
        private readonly HttpClient _httpClient;

        public SteamCmdApiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<SteamCmdDepotData?> GetDepotInfoAsync(string appId)
        {
            try
            {
                var url = $"https://api.steamcmd.net/v1/info/{appId}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<SteamCmdDepotData>(json);

                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching SteamCMD data for {appId}: {ex.Message}");
                return null;
            }
        }

        public string? GetGameName(SteamCmdDepotData? data, string appId)
        {
            if (data?.Data == null || !data.Data.ContainsKey(appId))
                return null;

            return data.Data[appId]?.Common?.Name;
        }
    }
}
