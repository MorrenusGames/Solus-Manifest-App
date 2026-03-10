using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;

namespace SolusManifestApp.Tools.CloudFix
{
    public partial class CloudFixViewModel : ObservableObject
    {
        readonly CloudFixService _cloudFix = new();
        readonly CloudFixConfigService _configService = new();
        readonly SteamGamesService _gamesService;
        readonly SteamService _steamService;
        CloudFixService.PayloadInfo? _payload;
        CancellationTokenSource? _monitorCts;
        CloudFixConfigService.Config _config;
        CloudFixConfigService.PublisherCache _publisherCache;
        HashSet<string> _steamToolsAppIds = new();
        readonly HashSet<string> _userTouchedAppIds = new();

        public CloudFixViewModel(SteamGamesService gamesService, SteamService steamService)
        {
            _gamesService = gamesService;
            _steamService = steamService;
            _config = _configService.LoadConfig();
            _publisherCache = _configService.LoadPublisherCache();
        }

        [ObservableProperty] string _statusText = "Initializing...";
        [ObservableProperty] string _cloudFixState = "Unknown";
        [ObservableProperty] string _currentGameText = "None";
        [ObservableProperty] bool _isConnected;
        [ObservableProperty] bool _isMonitoring;
        [ObservableProperty] bool _isBusy;
        [ObservableProperty] bool _hasUnsavedChanges;

        public string MonitorButtonText => IsMonitoring ? "Stop Monitor" : "Start Monitor";
        partial void OnIsMonitoringChanged(bool value) => OnPropertyChanged(nameof(MonitorButtonText));

        public ObservableCollection<string> LogEntries { get; } = new();
        public ObservableCollection<GamePublisherInfo> InstalledGames { get; } = new();

        public async Task InitializeAsync()
        {
            LoadSteamToolsAppIds();
            await LoadGamesAsync();
            await ConnectAsync();

            if (IsConnected)
                StartMonitor();
        }

        [RelayCommand]
        void Disable()
        {
            if (_payload == null) return;
            IsBusy = true;
            try
            {
                bool ok = _cloudFix.DisableCloudFix(_payload);
                AddLog(ok ? "Cloud fix DISABLED (replacement ID zeroed)." : "Failed to write to process memory.");
                RefreshStatus();
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        void Restore()
        {
            if (_payload == null) return;

            var result = CustomMessageBox.Show(
                "Reverting to SteamTools behavior will re-enable cloud save rewriting through app 760 (Steam Screenshots). " +
                "This is known to corrupt or lose save data for Capcom games.\n\n" +
                "Are you sure you want to do this?",
                "Warning",
                CustomMessageBoxButton.YesNo);

            if (result != CustomMessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                bool ok = _cloudFix.RestoreCloudFix(_payload);
                AddLog(ok ? "Reverted to SteamTools behavior (760)." : "Failed to write to process memory.");
                RefreshStatus();
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        void ToggleMonitor()
        {
            if (IsMonitoring) StopMonitor();
            else StartMonitor();
        }

        [RelayCommand]
        void SaveSettings()
        {
            foreach (var appId in _userTouchedAppIds)
            {
                var game = InstalledGames.FirstOrDefault(g => g.Game.AppId == appId);
                if (game != null)
                    _config.Overrides[appId] = game.IsCloudFixDisabled;
            }

            _configService.SaveConfig(_config);
            _userTouchedAppIds.Clear();
            HasUnsavedChanges = false;
            AddLog("Settings saved.");
        }

        void OnGamePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(GamePublisherInfo.IsCloudFixDisabled)) return;
            if (sender is GamePublisherInfo game)
                _userTouchedAppIds.Add(game.Game.AppId);
            HasUnsavedChanges = true;
        }

        async Task<bool> ShouldDisableForApp(uint appId)
        {
            var appIdStr = appId.ToString();

            // Saved override takes priority
            if (_config.Overrides.TryGetValue(appIdStr, out bool savedOverride))
                return savedOverride;

            // Check live UI state for unsaved toggles
            var game = InstalledGames.FirstOrDefault(g => g.Game.AppId == appIdStr);
            if (game != null && _userTouchedAppIds.Contains(appIdStr))
                return game.IsCloudFixDisabled;

            // Auto-logic: only block unowned games from blocked publishers
            if (!_steamToolsAppIds.Contains(appIdStr))
                return false;

            var info = await _cloudFix.QueryAppInfoAsync(appId);
            return info?.IsBlocked ?? false;
        }

        void LoadSteamToolsAppIds()
        {
            _steamToolsAppIds.Clear();
            var stPluginPath = _steamService.GetStPluginPath();
            if (string.IsNullOrEmpty(stPluginPath) || !Directory.Exists(stPluginPath))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(stPluginPath))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("steamtools.lua", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("steamtools.lua.disabled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".lua.disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var appId = fileName.Replace(".lua.disabled", "").Replace(".lua", "");
                        if (uint.TryParse(appId, out _))
                            _steamToolsAppIds.Add(appId);
                    }
                }
                AddLog($"Found {_steamToolsAppIds.Count} SteamTools-unlocked game(s) in stplug-in.");
            }
            catch (Exception ex)
            {
                AddLog($"Could not read stplug-in directory: {ex.Message}");
            }
        }

        async Task ConnectAsync()
        {
            StatusText = "Scanning Steam process for payload.dll...";
            AddLog("Scanning for SteamTools payload...");

            try
            {
                await Task.Run(() => { _payload = _cloudFix.Attach(); });

                if (_payload == null)
                {
                    StatusText = "Could not find payload. Is Steam running with SteamTools?";
                    AddLog("Payload not found. Steam may not be running or SteamTools not active.");
                    return;
                }

                IsConnected = true;
                StatusText = $"Connected — payload base 0x{_payload.PayloadBase:X}";
                AddLog("Attached to Steam process.");
                RefreshStatus();
            }
            catch (Exception ex)
            {
                StatusText = $"Connection error: {ex.Message}";
                AddLog($"Error: {ex.Message}");
            }
        }

        async Task LoadGamesAsync()
        {
            StatusText = "Loading installed games...";

            try
            {
                var games = await Task.Run(() => _gamesService.GetInstalledGames());

                InstalledGames.Clear();
                int cachedCount = 0;

                foreach (var g in games.OrderBy(g => g.Name))
                {
                    var info = new GamePublisherInfo
                    {
                        Game = g,
                        IsSteamToolsUnlocked = _steamToolsAppIds.Contains(g.AppId)
                    };

                    if (_config.Overrides.TryGetValue(g.AppId, out bool savedDisable))
                        info.IsCloudFixDisabled = savedDisable;

                    if (_publisherCache.Entries.TryGetValue(g.AppId, out var cached))
                    {
                        info.Publisher = cached.Publisher;
                        info.Developer = cached.Developer;
                        info.IsBlockedPublisher = cached.IsBlockedPublisher;
                        cachedCount++;
                    }

                    info.PropertyChanged += OnGamePropertyChanged;
                    InstalledGames.Add(info);
                }

                AddLog($"Found {games.Count} installed games ({cachedCount} with cached publisher info).");

                var uncached = InstalledGames.Where(g => !_publisherCache.Entries.ContainsKey(g.Game.AppId)).ToList();
                if (uncached.Count > 0)
                {
                    StatusText = $"Loaded {games.Count} games. Fetching publisher info for {uncached.Count} uncached...";
                    _ = FetchPublisherInfoAsync(uncached).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Application.Current?.Dispatcher.Invoke(() =>
                                AddLog($"Publisher fetch failed: {t.Exception?.InnerException?.Message}"));
                    });
                }
                else
                {
                    StatusText = $"Loaded {games.Count} games (all publisher info cached).";
                    ApplyAutoBlocking();
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error loading games: {ex.Message}";
                AddLog($"Error: {ex.Message}");
            }
        }

        void ApplyAutoBlocking()
        {
            foreach (var g in InstalledGames)
                g.PropertyChanged -= OnGamePropertyChanged;

            int autoBlocked = 0;
            int autoRemoved = 0;
            foreach (var g in InstalledGames)
            {
                bool hasUserOverride = _userTouchedAppIds.Contains(g.Game.AppId);
                if (hasUserOverride) continue;

                if (g.IsBlockedPublisher && g.IsSteamToolsUnlocked)
                {
                    g.IsCloudFixDisabled = true;
                    _config.Overrides[g.Game.AppId] = true;
                    autoBlocked++;
                }
                else if (_config.Overrides.TryGetValue(g.Game.AppId, out bool saved) && saved
                         && g.IsBlockedPublisher && !g.IsSteamToolsUnlocked)
                {
                    // User bought the game — remove the auto-block
                    g.IsCloudFixDisabled = false;
                    _config.Overrides.Remove(g.Game.AppId);
                    autoRemoved++;
                }
            }

            foreach (var g in InstalledGames)
                g.PropertyChanged += OnGamePropertyChanged;

            if (autoBlocked > 0 || autoRemoved > 0)
                _configService.SaveConfig(_config);

            if (autoBlocked > 0)
                AddLog($"Auto-blocked {autoBlocked} unowned Capcom game(s).");
            if (autoRemoved > 0)
                AddLog($"Removed auto-block for {autoRemoved} now-owned game(s).");

            StatusText = IsConnected
                ? $"Connected. {autoBlocked} unowned Capcom game(s) auto-blocked."
                : $"Not connected. {autoBlocked} unowned Capcom game(s) auto-blocked.";
        }

        void RefreshStatus()
        {
            if (_payload == null) return;

            var status = _cloudFix.GetStatus(_payload);
            if (status == null)
            {
                CloudFixState = "Lost connection";
                CurrentGameText = "N/A";
                return;
            }

            CloudFixState = status.IsActive ? "ACTIVE (rewriting to 760)" :
                            status.IsDisabled ? "DISABLED (not rewriting)" :
                            $"Unknown ({status.ReplacementId})";

            if (status.GameAppId != 0)
                CurrentGameText = $"app {status.GameAppId}";
            else
                CurrentGameText = "None";
        }

        void StartMonitor()
        {
            if (_payload == null || IsMonitoring) return;

            _monitorCts = new CancellationTokenSource();
            IsMonitoring = true;
            AddLog("Monitor started.");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _cloudFix.MonitorAsync(_payload, ShouldDisableForApp, msg =>
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            AddLog(msg);
                            RefreshStatus();
                        });
                    }, _monitorCts.Token);
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() => AddLog($"Monitor error: {ex.Message}"));
                }
                finally
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsMonitoring = false;
                        AddLog("Monitor stopped.");
                    });
                }
            });
        }

        void StopMonitor()
        {
            _monitorCts?.Cancel();
        }

        public void AddLog(string message)
        {
            LogEntries.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogEntries.Count > 200)
                LogEntries.RemoveAt(LogEntries.Count - 1);
        }

        async Task FetchPublisherInfoAsync(List<GamePublisherInfo> games)
        {
            int done = 0;

            foreach (var g in games)
            {
                if (uint.TryParse(g.Game.AppId, out uint appId))
                {
                    var info = await _cloudFix.QueryAppInfoAsync(appId);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (info != null)
                        {
                            g.Publisher = string.Join(", ", info.Publishers);
                            g.Developer = string.Join(", ", info.Developers);
                            g.IsBlockedPublisher = info.IsBlocked;
                        }

                        _publisherCache.Entries[g.Game.AppId] = new CloudFixConfigService.PublisherEntry
                        {
                            Publisher = g.Publisher,
                            Developer = g.Developer,
                            IsBlockedPublisher = g.IsBlockedPublisher,
                            FetchedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        };
                    });
                }

                done++;
                if (done % 10 == 0)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        StatusText = $"Fetching publisher info... {done}/{games.Count}");
                }
                await Task.Delay(150);
            }

            _configService.SavePublisherCache(_publisherCache);
            Application.Current?.Dispatcher.Invoke(() => ApplyAutoBlocking());
        }

        public void Cleanup()
        {
            _monitorCts?.Cancel();
            if (_payload != null)
            {
                _cloudFix.Detach(_payload);
                _payload = null;
            }
            _cloudFix.Dispose();
        }
    }

    public partial class GamePublisherInfo : ObservableObject
    {
        [ObservableProperty] SteamGame _game = new();
        [ObservableProperty] string _publisher = "...";
        [ObservableProperty] string _developer = "...";
        [ObservableProperty] bool _isBlockedPublisher;
        [ObservableProperty] bool _isSteamToolsUnlocked;
        [ObservableProperty] bool _isCloudFixDisabled;

        public string OwnershipLabel => IsSteamToolsUnlocked ? "Unlocked" : "Owned";
        partial void OnIsSteamToolsUnlockedChanged(bool value) => OnPropertyChanged(nameof(OwnershipLabel));
    }
}
