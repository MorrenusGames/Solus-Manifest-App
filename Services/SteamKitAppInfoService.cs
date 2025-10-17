using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2;

namespace SolusManifestApp.Services
{
    /// <summary>
    /// Service for fetching app information directly from Steam using SteamKit2
    /// This replaces the dependency on the SteamCMD API
    /// </summary>
    public class SteamKitAppInfoService
    {
        private readonly LoggerService _logger;
        private SteamClient? _steamClient;
        private SteamApps? _steamApps;
        private CallbackManager? _callbacks;
        private bool _isConnected = false;
        private bool _isAnonymousLoggedIn = false;

        public SteamKitAppInfoService()
        {
            _logger = new LoggerService();
        }

        /// <summary>
        /// Initialize anonymous Steam connection
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                if (_isConnected && _isAnonymousLoggedIn)
                    return true;

                _logger.Info("[SteamKit] Initializing anonymous Steam connection...");

                var clientConfiguration = SteamConfiguration.Create(config =>
                    config.WithHttpClientFactory(DepotDownloader.HttpClientFactory.CreateHttpClient)
                );

                _steamClient = new SteamClient(clientConfiguration);
                var steamUser = _steamClient.GetHandler<SteamUser>();
                _steamApps = _steamClient.GetHandler<SteamApps>();
                _callbacks = new CallbackManager(_steamClient);

                var connectedTcs = new TaskCompletionSource<bool>();
                var loggedOnTcs = new TaskCompletionSource<bool>();

                // Subscribe to connection callback
                _callbacks.Subscribe<SteamClient.ConnectedCallback>(callback =>
                {
                    _logger.Info("[SteamKit] Connected to Steam. Logging in anonymously...");
                    _isConnected = true;
                    steamUser?.LogOnAnonymous();
                    connectedTcs.TrySetResult(true);
                });

                // Subscribe to logon callback
                _callbacks.Subscribe<SteamUser.LoggedOnCallback>(callback =>
                {
                    if (callback.Result == EResult.OK)
                    {
                        _logger.Info("[SteamKit] Logged in anonymously successfully");
                        _isAnonymousLoggedIn = true;
                        loggedOnTcs.TrySetResult(true);
                    }
                    else
                    {
                        _logger.Error($"[SteamKit] Failed to log in anonymously: {callback.Result}");
                        loggedOnTcs.TrySetResult(false);
                    }
                });

                // Subscribe to disconnection callback
                _callbacks.Subscribe<SteamClient.DisconnectedCallback>(callback =>
                {
                    _logger.Warning("[SteamKit] Disconnected from Steam");
                    _isConnected = false;
                    _isAnonymousLoggedIn = false;
                });

                // Connect to Steam
                _steamClient.Connect();

                // Start callback processing in background
                _ = Task.Run(async () =>
                {
                    while (_steamClient != null && _steamClient.IsConnected)
                    {
                        _callbacks?.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                        await Task.Delay(100);
                    }
                });

                // Wait for connection
                await Task.WhenAny(connectedTcs.Task, Task.Delay(10000));
                if (!connectedTcs.Task.IsCompleted || !await connectedTcs.Task)
                {
                    _logger.Error("[SteamKit] Failed to connect to Steam (timeout)");
                    return false;
                }

                // Wait for login
                await Task.WhenAny(loggedOnTcs.Task, Task.Delay(10000));
                if (!loggedOnTcs.Task.IsCompleted || !await loggedOnTcs.Task)
                {
                    _logger.Error("[SteamKit] Failed to log in anonymously (timeout)");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamKit] Error initializing: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get app info from Steam directly
        /// </summary>
        public async Task<AppInfoResult?> GetAppInfoAsync(uint appId)
        {
            try
            {
                if (!_isAnonymousLoggedIn)
                {
                    _logger.Warning("[SteamKit] Not logged in, initializing...");
                    if (!await InitializeAsync())
                    {
                        _logger.Error("[SteamKit] Failed to initialize Steam connection");
                        return null;
                    }
                }

                if (_steamApps == null)
                {
                    _logger.Error("[SteamKit] SteamApps handler is null");
                    return null;
                }

                _logger.Info($"[SteamKit] Requesting app info for {appId}...");

                // Request app info
                var appInfoResponse = await _steamApps.PICSGetProductInfo(
                    new List<SteamApps.PICSRequest> { new SteamApps.PICSRequest(appId) },
                    new List<SteamApps.PICSRequest>()
                );

                if (appInfoResponse.Results == null || !appInfoResponse.Results.Any())
                {
                    _logger.Error($"[SteamKit] No results returned for app {appId}");
                    return null;
                }

                var firstResult = appInfoResponse.Results.First();

                if (firstResult.Apps == null || !firstResult.Apps.ContainsKey(appId))
                {
                    _logger.Error($"[SteamKit] App {appId} not found in results");
                    return null;
                }

                var appInfo = firstResult.Apps[appId];
                _logger.Info($"[SteamKit] Successfully retrieved app info for {appId}");

                // Parse the KeyValues data
                return ParseAppInfo(appId, appInfo);
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamKit] Error getting app info for {appId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse SteamKit2 KeyValues into our app info structure
        /// </summary>
        private AppInfoResult ParseAppInfo(uint appId, SteamApps.PICSProductInfoCallback.PICSProductInfo appInfo)
        {
            var result = new AppInfoResult
            {
                AppId = appId,
                AppName = "Unknown"
            };

            try
            {
                var keyValues = appInfo.KeyValues;

                // Get app name from common section
                var common = keyValues["common"];
                if (common != KeyValue.Invalid && common["name"] != KeyValue.Invalid)
                {
                    result.AppName = common["name"].Value ?? "Unknown";
                }

                _logger.Info($"[SteamKit] App Name: {result.AppName}");

                // Get depots section
                var depots = keyValues["depots"];
                if (depots == KeyValue.Invalid)
                {
                    _logger.Warning($"[SteamKit] No depots section found for app {appId}");
                    return result;
                }

                // Parse each depot
                foreach (var depotKv in depots.Children)
                {
                    // Skip non-numeric keys (like "branches")
                    if (!uint.TryParse(depotKv.Name, out var depotId))
                        continue;

                    var depotInfo = new DepotInfoResult
                    {
                        DepotId = depotId.ToString()
                    };

                    // Get depot config
                    var config = depotKv["config"];
                    if (config != KeyValue.Invalid)
                    {
                        // Language
                        if (config["language"] != KeyValue.Invalid)
                        {
                            depotInfo.Language = config["language"].Value;
                        }

                        // OS list
                        if (config["oslist"] != KeyValue.Invalid)
                        {
                            depotInfo.OsList = config["oslist"].Value;
                        }

                        // OS arch
                        if (config["osarch"] != KeyValue.Invalid)
                        {
                            depotInfo.OsArch = config["osarch"].Value;
                        }

                        // Low violence
                        if (config["lowviolence"] != KeyValue.Invalid)
                        {
                            depotInfo.LowViolence = config["lowviolence"].AsBoolean();
                        }
                    }

                    // Get depot from app (shared depot)
                    if (depotKv["depotfromapp"] != KeyValue.Invalid)
                    {
                        depotInfo.DepotFromApp = depotKv["depotfromapp"].AsUnsignedInteger().ToString();
                    }

                    // Get shared install
                    if (depotKv["sharedinstall"] != KeyValue.Invalid)
                    {
                        depotInfo.SharedInstall = depotKv["sharedinstall"].Value;
                    }

                    // Get DLC appid
                    if (depotKv["dlcappid"] != KeyValue.Invalid)
                    {
                        depotInfo.DlcAppId = depotKv["dlcappid"].AsUnsignedInteger().ToString();
                    }

                    // Check if depot has manifests
                    var manifests = depotKv["manifests"];
                    if (manifests != KeyValue.Invalid && manifests.Children.Any())
                    {
                        depotInfo.HasManifests = true;

                        // Get public manifest if available
                        if (manifests["public"] != KeyValue.Invalid)
                        {
                            if (manifests["public"]["gid"] != KeyValue.Invalid)
                            {
                                depotInfo.ManifestGid = manifests["public"]["gid"].Value;
                            }
                        }
                    }

                    _logger.Debug($"[SteamKit]   Depot {depotId}: Lang={depotInfo.Language ?? "none"}, OS={depotInfo.OsList ?? "any"}, DLC={depotInfo.DlcAppId ?? "no"}, Shared={depotInfo.DepotFromApp ?? "no"}");

                    result.Depots.Add(depotInfo);
                }

                _logger.Info($"[SteamKit] Parsed {result.Depots.Count} depots");
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamKit] Error parsing app info: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Get app info in SteamCMD API compatible format (drop-in replacement)
        /// </summary>
        public async Task<SteamCmdDepotData?> GetDepotInfoAsync(string appId)
        {
            if (!uint.TryParse(appId, out var appIdUint))
            {
                _logger.Error($"[SteamKit] Invalid app ID: {appId}");
                return null;
            }

            var appInfo = await GetAppInfoAsync(appIdUint);
            if (appInfo == null)
                return null;

            // Convert to SteamCMD API format
            return ConvertToSteamCmdFormat(appInfo, appId);
        }

        /// <summary>
        /// Convert SteamKit app info to SteamCMD API format
        /// </summary>
        private SteamCmdDepotData ConvertToSteamCmdFormat(AppInfoResult appInfo, string appId)
        {
            var result = new SteamCmdDepotData
            {
                Status = "success",
                Data = new Dictionary<string, AppData>()
            };

            var appData = new AppData
            {
                Common = new CommonData
                {
                    Name = appInfo.AppName
                },
                Depots = new Dictionary<string, DepotData>()
            };

            foreach (var depot in appInfo.Depots)
            {
                var depotData = new DepotData
                {
                    DlcAppId = depot.DlcAppId,
                    DepotFromApp = depot.DepotFromApp,
                    SharedInstall = depot.SharedInstall
                };

                // Convert depot config
                if (!string.IsNullOrEmpty(depot.Language) ||
                    !string.IsNullOrEmpty(depot.OsList) ||
                    depot.LowViolence)
                {
                    depotData.Config = new DepotConfig
                    {
                        Language = depot.Language,
                        OsList = depot.OsList,
                        LowViolence = depot.LowViolence ? "1" : null
                    };
                }

                // Convert manifests
                if (depot.HasManifests && !string.IsNullOrEmpty(depot.ManifestGid))
                {
                    depotData.Manifests = new Dictionary<string, ManifestData>
                    {
                        ["public"] = new ManifestData
                        {
                            Gid = depot.ManifestGid,
                            Size = 0, // Size not available from SteamKit
                            Download = 0
                        }
                    };
                }

                appData.Depots[depot.DepotId] = depotData;
            }

            result.Data[appId] = appData;

            return result;
        }

        /// <summary>
        /// Disconnect from Steam
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _logger.Info("[SteamKit] Disconnecting from Steam...");
                _steamClient?.Disconnect();
                _isConnected = false;
                _isAnonymousLoggedIn = false;
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamKit] Error disconnecting: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// App info result from SteamKit2
    /// </summary>
    public class AppInfoResult
    {
        public uint AppId { get; set; }
        public string AppName { get; set; } = "";
        public List<DepotInfoResult> Depots { get; set; } = new();
    }

    /// <summary>
    /// Depot info from SteamKit2
    /// </summary>
    public class DepotInfoResult
    {
        public string DepotId { get; set; } = "";
        public string? Language { get; set; }
        public string? OsList { get; set; }
        public string? OsArch { get; set; }
        public bool LowViolence { get; set; }
        public string? DepotFromApp { get; set; }
        public string? SharedInstall { get; set; }
        public string? DlcAppId { get; set; }
        public bool HasManifests { get; set; }
        public string? ManifestGid { get; set; }
    }
}
