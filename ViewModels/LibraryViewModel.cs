using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SolusManifestApp.ViewModels
{
    public partial class LibraryViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly FileInstallService _fileInstallService;
        private readonly SteamService _steamService;
        private readonly SteamGamesService _steamGamesService;
        private readonly ManifestApiService _manifestApiService;
        private readonly SettingsService _settingsService;
        private readonly CacheService _cacheService;
        private readonly NotificationService _notificationService;
        private readonly LuaFileManager _luaFileManager;
        private readonly ArchiveExtractionService _archiveExtractor;
        private readonly SteamApiService _steamApiService;
        private readonly LoggerService _logger;
        private readonly LibraryDatabaseService _dbService;
        private readonly LibraryRefreshService _refreshService;
        private readonly RecentGamesService _recentGamesService;
        private readonly ImageCacheService _imageCacheService;

        private List<LibraryItem> _allItems = new();

        [ObservableProperty]
        private ObservableCollection<LibraryItem> _displayedItems = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "No items";

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All";

        [ObservableProperty]
        private string _selectedSort = "Name";

        [ObservableProperty]
        private int _totalLua;

        [ObservableProperty]
        private int _totalSteamGames;

        [ObservableProperty]
        private long _totalSize;

        [ObservableProperty]
        private bool _showLua = true;

        [ObservableProperty]
        private bool _showSteamGames = true;

        [ObservableProperty]
        private bool _isSelectMode;

        [ObservableProperty]
        private ObservableCollection<string> _filterOptions = new();

        [ObservableProperty]
        private bool _isListView;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private int _itemsPerPage = 20;

        [ObservableProperty]
        private bool _canGoPrevious;

        [ObservableProperty]
        private bool _canGoNext;

        public List<string> SortOptions { get; } = new() { "Name", "Size", "Install Date", "Last Updated" };

        public LibraryViewModel(
            FileInstallService fileInstallService,
            SteamService steamService,
            SteamGamesService steamGamesService,
            ManifestApiService manifestApiService,
            SettingsService settingsService,
            CacheService cacheService,
            NotificationService notificationService,
            LoggerService logger,
            LibraryDatabaseService dbService,
            LibraryRefreshService refreshService,
            RecentGamesService recentGamesService)
        {
            _fileInstallService = fileInstallService;
            _steamService = steamService;
            _logger = logger;
            _steamGamesService = steamGamesService;
            _manifestApiService = manifestApiService;
            _settingsService = settingsService;
            _cacheService = cacheService;
            _notificationService = notificationService;
            _dbService = dbService;
            _refreshService = refreshService;
            _recentGamesService = recentGamesService;

            var stpluginPath = _steamService.GetStPluginPath() ?? "";
            _luaFileManager = new LuaFileManager(stpluginPath);
            _archiveExtractor = new ArchiveExtractionService();
            _imageCacheService = new ImageCacheService(logger);

            var settings = _settingsService.LoadSettings();
            _steamApiService = new SteamApiService(_cacheService);
            IsListView = settings.LibraryListView;
            ItemsPerPage = settings.LibraryPageSize;

            _refreshService.GameInstalled += OnGameInstalled;
        }

        [RelayCommand]
        private void ToggleView()
        {
            IsListView = !IsListView;
            var settings = _settingsService.LoadSettings();
            settings.LibraryListView = IsListView;
            _settingsService.SaveSettings(settings);
        }

        private async void OnGameInstalled(object? sender, GameInstalledEventArgs e)
        {
            await AddGameToLibraryAsync(e.AppId);
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilters();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            UpdateVisibilityFilters();
            ApplyFilters();
        }

        partial void OnSelectedSortChanged(string value)
        {
            ApplyFilters();
        }

        private void UpdateVisibilityFilters()
        {
            ShowLua = SelectedFilter is "All" or "Lua Only";
            ShowSteamGames = SelectedFilter is "All" or "Steam Games Only";
        }

        public async Task LoadFromCache()
        {
            FilterOptions.Clear();
            FilterOptions.Add("All");
            FilterOptions.Add("Lua Only");
            FilterOptions.Add("Steam Games Only");

            if (!FilterOptions.Contains(SelectedFilter))
            {
                SelectedFilter = "All";
            }

            var cachedItems = _dbService.GetAllLibraryItems();

            if (cachedItems.Count > 0)
            {
                _allItems = cachedItems;

                TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                TotalSize = _allItems.Sum(i => i.SizeBytes);

                ApplyFilters();
                StatusMessage = $"{_allItems.Count} item(s) - Click 'Update Library' to refresh";

                _ = LoadMissingIconsAsync();
                _ = CacheImagesAsync();
            }
            else
            {
                StatusMessage = "Library is empty - Click 'Update Library' to scan";
                _allItems.Clear();
                ApplyFilters();
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        public async Task RefreshLibrary()
        {
            await RefreshLibrary(forceFullScan: false);
        }

        public async Task RefreshLibrary(bool forceFullScan)
        {
            IsLoading = true;
            StatusMessage = "Loading library...";

            var settings = _settingsService.LoadSettings();

            FilterOptions.Clear();
            FilterOptions.Add("All");
            FilterOptions.Add("Lua Only");
            FilterOptions.Add("Steam Games Only");

            // Reset filter to "All" if current filter doesn't exist in new options
            if (!FilterOptions.Contains(SelectedFilter))
            {
                SelectedFilter = "All";
            }

            try
            {
                // Check if we have recent database cache (< 5 minutes old)
                if (!forceFullScan && _dbService.HasRecentData(TimeSpan.FromMinutes(5)))
                {
                    _logger.Info("Loading library from database cache (fast path)");
                    var cachedItems = _dbService.GetAllLibraryItems();

                    // Only use cache if it has items
                    if (cachedItems.Count > 0)
                    {
                        _allItems = cachedItems;

                        TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                        TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                        TotalSize = _allItems.Sum(i => i.SizeBytes);

                        ApplyFilters();
                        StatusMessage = $"{_allItems.Count} item(s) loaded from cache";
                        IsLoading = false;

                        // Load missing icons AND cache BitmapImages in background
                        _ = LoadMissingIconsAsync();
                        _ = CacheImagesAsync();
                        return;
                    }
                    else
                    {
                        _logger.Info("Database cache is empty, performing full scan instead");
                    }
                }

                _logger.Info("Performing full library scan");
                _allItems.Clear();

                // Validate and clean up deleted Lua files from database
                var stpluginPath = _steamService.GetStPluginPath();
                if (!string.IsNullOrEmpty(stpluginPath))
                {
                    var dbItems = _dbService.GetAllLibraryItems();

                    // Validate Lua files
                    foreach (var item in dbItems.Where(i => i.ItemType == LibraryItemType.Lua))
                    {
                        var luaFile = Path.Combine(stpluginPath, $"{item.AppId}.lua");
                        var disabledFile = Path.Combine(stpluginPath, $"{item.AppId}.lua.disabled");

                        // If neither file exists, remove from database
                        if (!File.Exists(luaFile) && !File.Exists(disabledFile))
                        {
                            _logger.Info($"Removing deleted Lua file from library: {item.AppId}");
                            _dbService.DeleteLibraryItem(item.AppId);
                        }
                    }
                }

                var steamGames = await Task.Run(() => _steamGamesService.GetInstalledGames());
                var steamGameDict = steamGames.ToDictionary(g => g.AppId, g => g);

                var steamAppList = await _steamApiService.GetAppListAsync();

                var luaGames = await Task.Run(() => _fileInstallService.GetInstalledGames());

                foreach (var mod in luaGames)
                {
                    var cachedManifest = _cacheService.GetCachedManifest(mod.AppId);
                    if (cachedManifest != null)
                    {
                        mod.Name = cachedManifest.Name;
                        mod.Description = cachedManifest.Description;
                        mod.Version = cachedManifest.Version;
                        mod.IconUrl = cachedManifest.IconUrl;
                    }
                    else
                    {
                        mod.Name = _steamApiService.GetGameName(mod.AppId, steamAppList);
                    }

                    if (steamGameDict.TryGetValue(mod.AppId, out var steamGame))
                    {
                        mod.SizeBytes = steamGame.SizeOnDisk;
                    }
                    else
                    {
                        mod.SizeBytes = 0;
                    }

                    var item = LibraryItem.FromGame(mod);
                    _allItems.Add(item);
                }

                // Load icons in background with throttling
                _ = Task.Run(async () =>
                {
                    var semaphore = new System.Threading.SemaphoreSlim(5, 5); // Limit to 5 concurrent downloads
                    var tasks = _allItems.Where(i => i.ItemType == LibraryItemType.Lua).Select(async item =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            _logger.Info($"Loading icon for {item.Name} (AppId: {item.AppId})");
                            var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                            _logger.Debug($"Using CDN URL: {cdnIconUrl}");

                            var iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, null, cdnIconUrl);

                            if (!string.IsNullOrEmpty(iconPath))
                            {
                                _logger.Info($"✓ Icon loaded successfully for {item.Name}: {iconPath}");
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    item.CachedIconPath = iconPath;
                                });
                                _dbService.UpdateIconPath(item.AppId, iconPath);
                            }
                            else
                            {
                                _logger.Warning($"✗ Failed to load icon for {item.Name} - No path returned");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"✗ Exception loading icon for {item.Name}: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);
                });

                // Add Steam games that don't have lua files
                try
                {
                    if (steamGames.Count == 0)
                    {
                        StatusMessage = "No Steam games found. Check Steam installation.";
                    }

                    var luaAppIds = _allItems.Where(i => i.ItemType == LibraryItemType.Lua)
                                             .Select(i => i.AppId)
                                             .ToHashSet();

                    // Only add Steam games that don't already have lua files
                    foreach (var steamGame in steamGames)
                    {
                        if (!luaAppIds.Contains(steamGame.AppId))
                        {
                            var item = LibraryItem.FromSteamGame(steamGame);
                            _allItems.Add(item);
                        }
                    }

                    // Load Steam game icons in background with throttling
                    _ = Task.Run(async () =>
                    {
                        var semaphore = new System.Threading.SemaphoreSlim(5, 5);
                        var tasks = _allItems.Where(i => i.ItemType == LibraryItemType.SteamGame).Select(async item =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                var localIconPath = _steamGamesService.GetLocalIconPath(item.AppId);
                                var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                                var iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, localIconPath, cdnIconUrl);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    item.CachedIconPath = iconPath;
                                });
                                if (!string.IsNullOrEmpty(iconPath))
                                {
                                    _dbService.UpdateIconPath(item.AppId, iconPath);
                                }
                            }
                            catch { }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        await Task.WhenAll(tasks);
                    });
                }
                catch (Exception ex)
                {
                    _notificationService.ShowError($"Failed to load Steam games: {ex.Message}");
                }

                TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                TotalSize = _allItems.Sum(i => i.SizeBytes);

                ApplyFilters();

                StatusMessage = $"{_allItems.Count} item(s) loaded";

                // Save to database in background (don't block UI)
                _ = Task.Run(() =>
                {
                    try
                    {
                        _logger.Info($"Saving {_allItems.Count} items to database");
                        _dbService.BulkUpsertLibraryItems(_allItems);
                        _logger.Info("Database save complete");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to save to database: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading library: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadMissingIconsAsync()
        {
            var itemsMissingIcons = _allItems.Where(i => string.IsNullOrEmpty(i.CachedIconPath)).ToList();
            if (itemsMissingIcons.Count == 0)
                return;

            _logger.Info($"Loading {itemsMissingIcons.Count} missing icons in background");

            var semaphore = new System.Threading.SemaphoreSlim(5, 5);
            var tasks = itemsMissingIcons.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    string? iconPath = null;
                    if (item.ItemType == LibraryItemType.SteamGame)
                    {
                        var localIconPath = _steamGamesService.GetLocalIconPath(item.AppId);
                        var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                        iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, localIconPath, cdnIconUrl);
                    }
                    else
                    {
                        var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                        iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, null, cdnIconUrl);
                    }

                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.CachedIconPath = iconPath;
                        });

                        // Update database with new icon path
                        _dbService.UpdateIconPath(item.AppId, iconPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to load icon for {item.Name}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            _logger.Info("Background icon loading complete");
        }

        // Method to instantly add newly installed game to library (no full scan needed)
        public async Task AddGameToLibraryAsync(string appId)
        {
            try
            {
                _logger.Info($"Adding game {appId} to library instantly");

                // Check if already exists
                if (_allItems.Any(i => i.AppId == appId))
                {
                    _logger.Info($"Game {appId} already in library");
                    return;
                }

                // Load the game data
                var luaGames = await Task.Run(() => _fileInstallService.GetInstalledGames());
                var game = luaGames.FirstOrDefault(g => g.AppId == appId);

                if (game == null)
                {
                    _logger.Warning($"Could not find installed game {appId}");
                    return;
                }

                // Get Steam app list for name enrichment
                var steamAppList = await _steamApiService.GetAppListAsync();

                // Try cache first
                var cachedManifest = _cacheService.GetCachedManifest(appId);
                if (cachedManifest != null)
                {
                    game.Name = cachedManifest.Name;
                    game.Description = cachedManifest.Description;
                    game.Version = cachedManifest.Version;
                    game.IconUrl = cachedManifest.IconUrl;
                }
                else
                {
                    // Get name from Steam app list
                    game.Name = _steamApiService.GetGameName(appId, steamAppList);
                }

                // Check if game is installed via Steam for size
                var steamGames = await Task.Run(() => _steamGamesService.GetInstalledGames());
                var steamGame = steamGames.FirstOrDefault(g => g.AppId == appId);
                if (steamGame != null)
                {
                    game.SizeBytes = steamGame.SizeOnDisk;
                }

                // Create library item
                var item = LibraryItem.FromGame(game);

                // Add to memory
                _allItems.Add(item);

                // Save to database
                _dbService.UpsertLibraryItem(item);

                // Update UI
                ApplyFilters();
                TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                TotalSize = _allItems.Sum(i => i.SizeBytes);

                _logger.Info($"✓ Game {appId} added to library");

                // Load icon in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(appId);
                        var iconPath = await _cacheService.GetSteamGameIconAsync(appId, null, cdnIconUrl);

                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                item.CachedIconPath = iconPath;
                            });
                            _dbService.UpdateIconPath(appId, iconPath);
                            _logger.Info($"✓ Icon loaded for {game.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to load icon for {appId}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to add game to library: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            // Do filtering/sorting on background thread
            Task.Run(() =>
            {
                var filtered = _allItems.AsEnumerable();

                // Filter by type
                if (!ShowLua)
                    filtered = filtered.Where(i => i.ItemType != LibraryItemType.Lua);
                if (!ShowSteamGames)
                    filtered = filtered.Where(i => i.ItemType != LibraryItemType.SteamGame);

                // Search filter
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    var query = SearchQuery.Trim();
                    if (uint.TryParse(query, out _))
                    {
                        filtered = filtered.Where(i => i.AppId == query);
                    }
                    else
                    {
                        var lowerQuery = query.ToLower();
                        filtered = filtered.Where(i =>
                            i.Name.ToLower().Contains(lowerQuery) ||
                            i.AppId.ToLower().Contains(lowerQuery) ||
                            i.Description.ToLower().Contains(lowerQuery));
                    }
                }

                // Sort
                filtered = SelectedSort switch
                {
                    "Size" => filtered.OrderByDescending(i => i.SizeBytes),
                    "Install Date" => filtered.OrderByDescending(i => i.InstallDate),
                    "Last Updated" => filtered.OrderByDescending(i => i.LastUpdated),
                    _ => filtered.OrderBy(i => i.Name)
                };

                var filteredList = filtered.ToList();

                // Update UI on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Handle pagination
                    List<LibraryItem> pagedItems;

                    if (ItemsPerPage <= 0)
                    {
                        // Show all items (no pagination)
                        pagedItems = filteredList;
                        TotalPages = 1;
                        CurrentPage = 1;
                    }
                    else
                    {
                        // Calculate pagination
                        TotalPages = (int)Math.Ceiling((double)filteredList.Count / ItemsPerPage);
                        if (TotalPages == 0) TotalPages = 1;

                        // Ensure current page is within bounds
                        if (CurrentPage > TotalPages)
                            CurrentPage = TotalPages;
                        if (CurrentPage < 1)
                            CurrentPage = 1;

                        // Get items for current page
                        pagedItems = filteredList
                            .Skip((CurrentPage - 1) * ItemsPerPage)
                            .Take(ItemsPerPage)
                            .ToList();
                    }

                    // Update displayed items
                    DisplayedItems.Clear();
                    foreach (var item in pagedItems)
                    {
                        DisplayedItems.Add(item);
                    }

                    // Update pagination state
                    CanGoPrevious = CurrentPage > 1;
                    CanGoNext = CurrentPage < TotalPages;

                    // Update status message
                    if (ItemsPerPage <= 0)
                    {
                        StatusMessage = $"{filteredList.Count} of {_allItems.Count} item(s)";
                    }
                    else
                    {
                        StatusMessage = $"Page {CurrentPage} of {TotalPages}: Showing {DisplayedItems.Count} of {filteredList.Count} filtered item(s) ({_allItems.Count} total)";
                    }
                });
            });
        }

        [RelayCommand]
        private void NextPage()
        {
            if (CanGoNext)
            {
                CurrentPage++;
                ApplyFilters();
            }
        }

        [RelayCommand]
        private void PreviousPage()
        {
            if (CanGoPrevious)
            {
                CurrentPage--;
                ApplyFilters();
            }
        }

        [RelayCommand]
        private async Task UninstallItem(LibraryItem item)
        {
            var itemType = item.ItemType switch
            {
                LibraryItemType.Lua => "lua file",
                _ => "Steam game"
            };

            var message = item.ItemType switch
            {
                LibraryItemType.Lua => "This will remove the lua file from your system.",
                _ => "This will delete the game files and remove it from Steam."
            };

            var result = MessageBoxHelper.Show(
                $"Are you sure you want to uninstall {item.Name}?\n\n{message}",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool success = false;

                    if (item.ItemType == LibraryItemType.Lua)
                    {
                        success = await Task.Run(() => _fileInstallService.UninstallGame(item.AppId));
                    }
                    else if (item.ItemType == LibraryItemType.SteamGame)
                    {
                        success = await Task.Run(() => _steamGamesService.UninstallGame(item.AppId));
                    }

                    if (success)
                    {
                        _allItems.Remove(item);
                        _dbService.DeleteLibraryItem(item.AppId);
                        ApplyFilters();

                        TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                        TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                        TotalSize = _allItems.Sum(i => i.SizeBytes);

                        _notificationService.ShowSuccess($"{item.Name} uninstalled successfully");
                    }
                    else
                    {
                        _notificationService.ShowError($"Failed to uninstall {item.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _notificationService.ShowError($"Failed to uninstall: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void RestartSteam()
        {
            try
            {
                _steamService.RestartSteam();
                _notificationService.ShowSuccess("Steam is restarting...");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to restart Steam: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleSelectMode()
        {
            IsSelectMode = !IsSelectMode;
            if (!IsSelectMode)
            {
                // Deselect all
                foreach (var item in _allItems)
                {
                    item.IsSelected = false;
                }
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var item in DisplayedItems)
            {
                item.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAll()
        {
            foreach (var item in DisplayedItems)
            {
                item.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task UninstallSelected()
        {
            var selected = DisplayedItems.Where(i => i.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBoxHelper.Show("No items selected", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var luaCount = selected.Count(i => i.ItemType == LibraryItemType.Lua);
            var gameCount = selected.Count(i => i.ItemType == LibraryItemType.SteamGame);
            var message = luaCount > 0 && gameCount > 0
                ? $"Are you sure you want to uninstall {luaCount} lua file(s) and {gameCount} Steam game(s)?"
                : luaCount > 0
                    ? $"Are you sure you want to uninstall {luaCount} lua file(s)?"
                    : $"Are you sure you want to uninstall {gameCount} Steam game(s)?";

            var result = MessageBoxHelper.Show(
                message,
                "Confirm Batch Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                int successCount = 0;
                foreach (var item in selected)
                {
                    try
                    {
                        bool success = false;

                        if (item.ItemType == LibraryItemType.Lua)
                        {
                            success = await Task.Run(() => _fileInstallService.UninstallGame(item.AppId));
                        }
                        else if (item.ItemType == LibraryItemType.SteamGame)
                        {
                            success = await Task.Run(() => _steamGamesService.UninstallGame(item.AppId));
                        }

                        if (success)
                        {
                            _allItems.Remove(item);
                            _dbService.DeleteLibraryItem(item.AppId);
                            successCount++;
                        }
                    }
                    catch { }
                }

                ApplyFilters();
                TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                TotalSize = _allItems.Sum(i => i.SizeBytes);

                _notificationService.ShowSuccess($"{successCount} item(s) uninstalled successfully");
                IsSelectMode = false;
            }
        }

        [RelayCommand]
        private void OpenInExplorer(LibraryItem item)
        {
            // Track as recently accessed
            _recentGamesService.MarkAsRecentlyAccessed(item.AppId);

            try
            {
                string? pathToOpen = null;

                // Try to find the path based on item type
                if (!string.IsNullOrEmpty(item.LocalPath) && (File.Exists(item.LocalPath) || Directory.Exists(item.LocalPath)))
                {
                    pathToOpen = item.LocalPath;
                }
                else if (item.ItemType == LibraryItemType.Lua)
                {
                    // Try to find the .lua file
                    var stpluginPath = _steamService.GetStPluginPath();
                    if (!string.IsNullOrEmpty(stpluginPath))
                    {
                        var luaFile = Path.Combine(stpluginPath, $"{item.AppId}.lua");
                        if (File.Exists(luaFile))
                        {
                            pathToOpen = luaFile;
                        }
                        else
                        {
                            var luaFileDisabled = Path.Combine(stpluginPath, $"{item.AppId}.lua.disabled");
                            if (File.Exists(luaFileDisabled))
                            {
                                pathToOpen = luaFileDisabled;
                            }
                        }
                    }
                }
                else if (item.ItemType == LibraryItemType.SteamGame)
                {
                    // Try to find the Steam game folder
                    var steamGames = _steamGamesService.GetInstalledGames();
                    var steamGame = steamGames.FirstOrDefault(g => g.AppId == item.AppId);
                    if (steamGame != null && !string.IsNullOrEmpty(steamGame.LibraryPath) && Directory.Exists(steamGame.LibraryPath))
                    {
                        pathToOpen = steamGame.LibraryPath;
                    }
                }

                if (!string.IsNullOrEmpty(pathToOpen))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = File.Exists(pathToOpen) ? $"/select,\"{pathToOpen}\"" : $"\"{pathToOpen}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    _notificationService.ShowWarning($"Could not find local path for {item.Name}");
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to open explorer: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ShowDetails(LibraryItem item)
        {
            try
            {
                // This will open a details window - to be implemented
                var details = $"Name: {item.Name}\n" +
                             $"App ID: {item.AppId}\n" +
                             $"Type: {item.TypeBadge}\n" +
                             $"Size: {item.SizeFormatted}\n" +
                             $"Install Date: {item.InstallDate?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}\n" +
                             $"Last Updated: {item.LastUpdated?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}\n" +
                             $"Path: {(string.IsNullOrEmpty(item.LocalPath) ? "Not available" : item.LocalPath)}";

                MessageBoxHelper.Show(details, $"Details: {item.Name}", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to show details: {ex.Message}");
            }
        }

        public string GetStatisticsSummary()
        {
            return $"Lua: {TotalLua} | Steam Games: {TotalSteamGames} | Total Size: {FormatBytes(TotalSize)}";
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        [RelayCommand]
        private async Task PatchAll()
        {
            try
            {
                var result = MessageBoxHelper.Show(
                    "This will patch all .lua files by commenting out setManifestid lines.\n\nContinue?",
                    "Patch All .lua Files",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                IsLoading = true;
                StatusMessage = "Patching .lua files...";

                var (luaFiles, _) = _luaFileManager.FindLuaFiles();
                int patchedCount = 0;

                foreach (var luaFile in luaFiles)
                {
                    var patchResult = _luaFileManager.PatchLuaFile(luaFile);
                    if (patchResult == "patched")
                    {
                        patchedCount++;
                    }
                }

                _notificationService.ShowSuccess($"Patched {patchedCount} file(s). Restart Steam for changes to take effect.");
                await RefreshLibrary();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to patch files: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task EnableGame(string appId)
        {
            try
            {
                var (success, message) = _luaFileManager.EnableGame(appId);
                if (success)
                {
                    _notificationService.ShowSuccess(message);
                    await RefreshLibrary();
                }
                else
                {
                    _notificationService.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to enable game: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DisableGame(string appId)
        {
            try
            {
                var (success, message) = _luaFileManager.DisableGame(appId);
                if (success)
                {
                    _notificationService.ShowSuccess(message);
                    await RefreshLibrary();
                }
                else
                {
                    _notificationService.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to disable game: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DeleteLua(string appId)
        {
            try
            {
                var result = MessageBoxHelper.Show(
                    $"Are you sure you want to permanently delete the .lua file for App ID {appId}?\n\nThis cannot be undone!",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                var (success, message) = _luaFileManager.DeleteLuaFile(appId);
                if (success)
                {
                    _notificationService.ShowSuccess(message);
                    await RefreshLibrary();
                }
                else
                {
                    _notificationService.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to delete lua file: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ProcessDroppedFiles(string[] filePaths)
        {
            try
            {
                var luaFiles = new List<string>();
                var tempDirs = new List<string>();

                foreach (var filePath in filePaths)
                {
                    if (filePath.ToLower().EndsWith(".lua"))
                    {
                        if (ArchiveExtractionService.IsValidLuaFilename(Path.GetFileName(filePath)))
                        {
                            luaFiles.Add(filePath);
                        }
                    }
                    else if (filePath.ToLower().EndsWith(".zip"))
                    {
                        var (archiveLuaFiles, tempDir) = _archiveExtractor.ExtractLuaFromArchive(filePath);
                        luaFiles.AddRange(archiveLuaFiles);
                        if (!string.IsNullOrEmpty(tempDir))
                        {
                            tempDirs.Add(tempDir);
                        }
                    }
                }

                if (luaFiles.Count == 0)
                {
                    _notificationService.ShowWarning("No valid .lua files found");
                    return;
                }

                // Copy files to stplug-in directory
                var stpluginPath = _steamService.GetStPluginPath();
                if (string.IsNullOrEmpty(stpluginPath))
                {
                    _notificationService.ShowError("Could not find Steam stplug-in directory");
                    return;
                }

                int copiedCount = 0;
                var installedAppIds = new List<string>();

                foreach (var luaFile in luaFiles)
                {
                    var fileName = Path.GetFileName(luaFile);
                    var destPath = Path.Combine(stpluginPath, fileName);

                    // Extract appId from filename (e.g., "123456.lua" -> "123456")
                    var appId = Path.GetFileNameWithoutExtension(fileName);

                    // Remove existing files
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    if (File.Exists(destPath + ".disabled"))
                        File.Delete(destPath + ".disabled");

                    File.Copy(luaFile, destPath, true);
                    copiedCount++;
                    installedAppIds.Add(appId);
                }

                // Cleanup temp directories
                foreach (var tempDir in tempDirs)
                {
                    _archiveExtractor.CleanupTempDirectory(tempDir);
                }

                // Only show notification if user hasn't disabled it
                var settings = _settingsService.LoadSettings();
                if (settings.ShowGameAddedNotification)
                {
                    _notificationService.ShowSuccess($"Successfully added {copiedCount} file(s)! Restart Steam for changes to take effect.");
                }

                // Add games to library instantly instead of full refresh
                foreach (var appId in installedAppIds)
                {
                    await AddGameToLibraryAsync(appId);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to process files: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task EnableAutoUpdates()
        {
            // Check if in SteamTools mode
            var settings = _settingsService.LoadSettings();
            if (settings.Mode != ToolMode.SteamTools)
            {
                _notificationService.ShowWarning("Auto-Update Enabler is only available in SteamTools mode");
                return;
            }

            try
            {
                // Get all .lua files
                var (luaFiles, _) = _luaFileManager.FindLuaFiles();
                if (luaFiles.Count == 0)
                {
                    _notificationService.ShowWarning("No .lua files found");
                    return;
                }

                // Build list of apps that currently have updates disabled
                var selectableApps = new List<SelectableApp>();
                foreach (var luaFile in luaFiles)
                {
                    var appId = _luaFileManager.ExtractAppId(luaFile);
                    bool isEnabled = _luaFileManager.IsAutoUpdatesEnabled(appId);

                    // Only show apps that have updates disabled
                    if (!isEnabled)
                    {
                        var name = await _steamApiService.GetGameNameAsync(appId) ?? $"App {appId}";

                        selectableApps.Add(new SelectableApp
                        {
                            AppId = appId,
                            Name = name,
                            IsSelected = false,
                            IsUpdateEnabled = isEnabled
                        });
                    }
                }

                if (selectableApps.Count == 0)
                {
                    _notificationService.ShowWarning("All games already have auto-updates enabled");
                    return;
                }

                // Show dialog
                var dialog = new UpdateEnablerDialog(selectableApps);
                var result = dialog.ShowDialog();

                if (result == true && dialog.SelectedApps.Count > 0)
                {
                    // Enable updates for selected apps
                    int successCount = 0;
                    int failCount = 0;

                    foreach (var app in dialog.SelectedApps)
                    {
                        var (success, _) = _luaFileManager.EnableAutoUpdatesForApp(app.AppId);
                        if (success)
                            successCount++;
                        else
                            failCount++;
                    }

                    if (failCount == 0)
                    {
                        _notificationService.ShowSuccess($"Successfully enabled auto-updates for {successCount} app(s)");
                    }
                    else
                    {
                        _notificationService.ShowWarning($"Enabled auto-updates for {successCount} app(s), {failCount} failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to enable auto-updates: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DisableAutoUpdates()
        {
            // Check if in SteamTools mode
            var settings = _settingsService.LoadSettings();
            if (settings.Mode != ToolMode.SteamTools)
            {
                _notificationService.ShowWarning("Auto-Update Disabler is only available in SteamTools mode");
                return;
            }

            try
            {
                // Get all .lua files
                var (luaFiles, _) = _luaFileManager.FindLuaFiles();
                if (luaFiles.Count == 0)
                {
                    _notificationService.ShowWarning("No .lua files found");
                    return;
                }

                // Build list of apps that currently have updates enabled
                var selectableApps = new List<SelectableApp>();
                foreach (var luaFile in luaFiles)
                {
                    var appId = _luaFileManager.ExtractAppId(luaFile);
                    bool isEnabled = _luaFileManager.IsAutoUpdatesEnabled(appId);

                    // Only show apps that have updates enabled
                    if (isEnabled)
                    {
                        var name = await _steamApiService.GetGameNameAsync(appId) ?? $"App {appId}";

                        selectableApps.Add(new SelectableApp
                        {
                            AppId = appId,
                            Name = name,
                            IsSelected = false,
                            IsUpdateEnabled = isEnabled
                        });
                    }
                }

                if (selectableApps.Count == 0)
                {
                    _notificationService.ShowWarning("All games already have auto-updates disabled");
                    return;
                }

                // Show dialog
                var dialog = new UpdateDisablerDialog(selectableApps);
                var result = dialog.ShowDialog();

                if (result == true && dialog.SelectedApps.Count > 0)
                {
                    // Disable updates for selected apps
                    int successCount = 0;
                    int failCount = 0;

                    foreach (var app in dialog.SelectedApps)
                    {
                        var (success, _) = _luaFileManager.DisableAutoUpdatesForApp(app.AppId);
                        if (success)
                            successCount++;
                        else
                            failCount++;
                    }

                    if (failCount == 0)
                    {
                        _notificationService.ShowSuccess($"Successfully disabled auto-updates for {successCount} app(s)");
                    }
                    else
                    {
                        _notificationService.ShowWarning($"Disabled auto-updates for {successCount} app(s), {failCount} failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to disable auto-updates: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task UpdateSteamAppCache()
        {
            try
            {
                var settings = _settingsService.LoadSettings();

                IsLoading = true;
                StatusMessage = "Updating Steam app list cache...";

                _logger.Info("Starting Steam app list cache update");

                // Force refresh the app list from API
                var result = await _steamApiService.GetAppListAsync(forceRefresh: true);

                if (result != null && result.AppList?.Apps.Count > 0)
                {
                    _logger.Info($"Successfully updated cache with {result.AppList.Apps.Count} apps");
                    _notificationService.ShowSuccess($"Steam app list cache updated! Loaded {result.AppList.Apps.Count:N0} apps.");
                }
                else
                {
                    _logger.Warning("Cache update returned empty result");
                    _notificationService.ShowWarning("Cache update completed but no apps were retrieved. Check your API key.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update Steam app cache: {ex.Message}");
                _notificationService.ShowError($"Failed to update cache: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                StatusMessage = $"{_allItems.Count} item(s) loaded";
            }
        }

        /// <summary>
        /// Caches BitmapImages in memory for all library items with available icon paths.
        /// Runs asynchronously in background to improve Library page loading performance.
        /// </summary>
        private async Task CacheImagesAsync()
        {
            try
            {
                _logger.Info($"Starting image caching for {_allItems.Count} library items...");

                var imagesToCache = new Dictionary<string, string>();

                foreach (var item in _allItems)
                {
                    if (!string.IsNullOrEmpty(item.CachedIconPath) && File.Exists(item.CachedIconPath))
                    {
                        imagesToCache[item.AppId] = item.CachedIconPath;
                    }
                }

                _logger.Info($"Found {imagesToCache.Count} images to cache");

                // Pre-load all images into cache
                await _imageCacheService.PreloadImagesAsync(imagesToCache);

                // Update LibraryItem.CachedBitmapImage properties on UI thread
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    foreach (var item in _allItems)
                    {
                        if (imagesToCache.ContainsKey(item.AppId))
                        {
                            // Get cached image
                            var bitmap = _imageCacheService.GetCachedImage(item.AppId);
                            if (bitmap != null)
                            {
                                item.CachedBitmapImage = bitmap;
                            }
                        }
                    }

                    _logger.Info($"✓ Image caching complete! Cache size: {_imageCacheService.GetCacheSize()} images ({_imageCacheService.GetEstimatedMemoryUsageMB():F1} MB)");
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error caching images: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _refreshService.GameInstalled -= OnGameInstalled;
                _imageCacheService?.ClearCache();
            }

            _disposed = true;
        }
    }
}
