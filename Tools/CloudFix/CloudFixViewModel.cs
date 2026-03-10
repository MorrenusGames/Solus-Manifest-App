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

namespace SolusManifestApp.Tools.CloudFix
{
    public partial class CloudFixViewModel : ObservableObject
    {
        readonly CloudFixService _cloudFix = new();
        readonly CloudFixConfigService _configService = new();
        readonly SteamGamesService _gamesService;
        readonly SteamService _steamService;
        CloudFixService.AttachResult? _ctx;
        CloudFixConfigService.Config _config;
        CloudFixConfigService.PublisherCache _publisherCache;
        HashSet<string> _steamToolsAppIds = new();
        readonly HashSet<string> _userTouchedAppIds = new();
        CancellationTokenSource? _cts;

        public CloudFixViewModel(SteamGamesService gamesService, SteamService steamService)
        {
            _gamesService = gamesService;
            _steamService = steamService;
            _config = _configService.LoadConfig();
            _publisherCache = _configService.LoadPublisherCache();
        }

        [ObservableProperty] string _statusText = "Initializing...";
        [ObservableProperty] string _hookState = "Not installed";
        [ObservableProperty] bool _isConnected;
        [ObservableProperty] bool _hasUnsavedChanges;

        public ObservableCollection<string> LogEntries { get; } = new();
        public ObservableCollection<GamePublisherInfo> InstalledGames { get; } = new();

        public async Task InitializeAsync()
        {
            _cts = new CancellationTokenSource();
            LoadSteamToolsAppIds();
            await LoadGamesAsync(_cts.Token);
            await ConnectAndHookAsync();
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

            if (!_configService.SaveConfig(_config))
                AddLog("Warning: Failed to save config to disk.");

            _userTouchedAppIds.Clear();
            HasUnsavedChanges = false;

            PushBlockedList();
            AddLog("Settings saved.");
        }

        void OnGamePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(GamePublisherInfo.IsCloudFixDisabled)) return;
            if (sender is GamePublisherInfo game)
                _userTouchedAppIds.Add(game.Game.AppId);
            HasUnsavedChanges = true;
        }

        uint[] GetBlockedAppIds()
        {
            var blocked = new List<uint>();
            foreach (var g in InstalledGames)
            {
                if (!g.IsCloudFixDisabled) continue;
                if (uint.TryParse(g.Game.AppId, out uint id))
                    blocked.Add(id);
            }
            return blocked.ToArray();
        }

        void PushBlockedList()
        {
            if (_ctx == null) return;

            var list = GetBlockedAppIds();
            bool ok = _cloudFix.UpdateBlockedList(_ctx, list);
            if (ok)
            {
                StatusText = $"Hook active \u2014 {list.Length} app(s) blocked.";
                AddLog($"Blocked list updated: {list.Length} app(s).");
            }
            else
                AddLog("Failed to update blocked list in Steam process.");
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

        async Task ConnectAndHookAsync()
        {
            StatusText = "Scanning Steam process for payload...";
            AddLog("Scanning for SteamTools payload...");

            try
            {
                CloudFixService.AttachResult? result = null;
                string attachError = "";
                await Task.Run(() => { result = _cloudFix.Attach(out attachError); });

                if (result == null)
                {
                    StatusText = "Could not find payload or hook point. Is Steam running with SteamTools?";
                    AddLog($"Attach failed: {attachError}");
                    return;
                }

                _ctx = result;
                IsConnected = true;

                if (_ctx.WasAlreadyHooked)
                    AddLog($"Recovered existing hook at 0x{_ctx.HookAddr:X}. Cave at 0x{_ctx.CaveAddr:X}.");
                else
                    AddLog($"Payload found at 0x{_ctx.PayloadBase:X}. Hook point at 0x{_ctx.HookAddr:X}. Cave at 0x{_ctx.CaveAddr:X}.");

                var blockedList = GetBlockedAppIds();

                if (_ctx.WasAlreadyHooked)
                {
                    bool ok = _cloudFix.UpdateBlockedList(_ctx, blockedList);
                    if (ok)
                    {
                        HookState = "Installed (recovered)";
                        StatusText = $"Hook active \u2014 {blockedList.Length} app(s) blocked.";
                        AddLog($"Updated recovered hook with {blockedList.Length} blocked app(s).");
                    }
                    else
                    {
                        HookState = "Failed";
                        StatusText = "Failed to update recovered hook's blocked list.";
                        AddLog("Failed to write blocked list to recovered cave.");
                    }
                }
                else
                {
                    bool hookOk = _cloudFix.InstallHook(_ctx, blockedList, out string hookError);
                    if (hookOk)
                    {
                        HookState = "Installed";
                        StatusText = $"Hook active \u2014 {blockedList.Length} app(s) blocked.";
                        AddLog($"Inline hook installed. Blocking {blockedList.Length} app(s).");
                    }
                    else
                    {
                        HookState = "Failed";
                        StatusText = "Failed to install hook.";
                        AddLog($"Hook installation failed: {hookError}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                AddLog($"Error: {ex.Message}");
            }
        }

        async Task LoadGamesAsync(CancellationToken ct)
        {
            StatusText = "Loading installed games...";

            try
            {
                var games = await Task.Run(() => _gamesService.GetInstalledGames(), ct);

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
                    await FetchPublisherInfoAsync(uncached, ct);
                }
                else
                {
                    StatusText = $"Loaded {games.Count} games (all publisher info cached).";
                    ApplyAutoBlocking();
                }
            }
            catch (OperationCanceledException) { }
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
        }

        public void AddLog(string message)
        {
            LogEntries.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogEntries.Count > 200)
                LogEntries.RemoveAt(LogEntries.Count - 1);
        }

        async Task FetchPublisherInfoAsync(List<GamePublisherInfo> games, CancellationToken ct)
        {
            int done = 0;

            try
            {
                foreach (var g in games)
                {
                    ct.ThrowIfCancellationRequested();

                    if (uint.TryParse(g.Game.AppId, out uint appId))
                    {
                        var info = await _cloudFix.QueryAppInfoAsync(appId, ct);

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
                    await Task.Delay(150, ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    AddLog($"Publisher fetch error: {ex.Message}"));
                return;
            }

            _configService.SavePublisherCache(_publisherCache);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ApplyAutoBlocking();
                PushBlockedList();
            });
        }

        public void Cleanup()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_ctx != null)
            {
                _cloudFix.Detach(_ctx);
                _ctx = null;
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
