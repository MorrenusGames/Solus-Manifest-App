using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SolusManifestApp.ViewModels
{
    public partial class StoreViewModel : ObservableObject
    {
        private readonly ManifestApiService _manifestApiService;
        private readonly DownloadService _downloadService;
        private readonly SettingsService _settingsService;
        private readonly CacheService _cacheService;
        private readonly NotificationService _notificationService;
        private readonly SemaphoreSlim _iconLoadSemaphore = new SemaphoreSlim(10, 10); // Max 10 concurrent downloads

        [ObservableProperty]
        private ObservableCollection<LibraryGame> _games = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "Browse available games from the library";

        [ObservableProperty]
        private string _sortBy = "updated"; // "updated" or "name"

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _currentOffset;

        [ObservableProperty]
        private bool _hasMore;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _totalPages;

        [ObservableProperty]
        private bool _canGoNext;

        [ObservableProperty]
        private bool _canGoPrevious;

        [ObservableProperty]
        private bool _isListView;

        [ObservableProperty]
        private string _goToPageText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<int> _pageNumbers = new();

        private int PageSize => _settingsService.LoadSettings().StorePageSize;

        public Action? ScrollToTopAction { get; set; }

        public StoreViewModel(
            ManifestApiService manifestApiService,
            DownloadService downloadService,
            SettingsService settingsService,
            CacheService cacheService,
            NotificationService notificationService)
        {
            _manifestApiService = manifestApiService;
            _downloadService = downloadService;
            _settingsService = settingsService;
            _cacheService = cacheService;
            _notificationService = notificationService;

            // Auto-load games on startup
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            var settings = _settingsService.LoadSettings();
            IsListView = settings.StoreListView;
            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                await LoadGamesAsync();
            }
            else
            {
                StatusMessage = "API key required - Please configure in Settings";
            }
        }

        public void OnNavigatedTo()
        {
            var settings = _settingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                // Show warning popup when user navigates to Store without API key
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBoxHelper.Show(
                        "An API key is required to use the Store.\n\nPlease go to Settings and enter your API key to browse and download games from the library.",
                        "API Key Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }

        [RelayCommand]
        private void ToggleView()
        {
            IsListView = !IsListView;
            var settings = _settingsService.LoadSettings();
            settings.StoreListView = IsListView;
            _settingsService.SaveSettings(settings);
        }

        partial void OnSearchQueryChanged(string value)
        {
            // Auto-search when query is cleared
            if (string.IsNullOrWhiteSpace(value) && Games.Count > 0)
            {
                _ = LoadGamesAsync();
            }
        }

        [RelayCommand]
        private async Task LoadGames()
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                StatusMessage = "Please enter API key in settings";
                MessageBoxHelper.Show(
                    "An API key is required to use the Store.\n\nPlease go to Settings and enter your API key to browse and download games from the library.",
                    "API Key Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Reset to first page
            CurrentPage = 1;
            CurrentOffset = 0;
            Games.Clear();

            await LoadGamesAsync();
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (!CanGoNext || IsLoading) return;

            CurrentPage++;
            CurrentOffset = (CurrentPage - 1) * PageSize;
            Games.Clear();
            await LoadGamesAsync();
            ScrollToTopAction?.Invoke();
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            if (!CanGoPrevious || IsLoading) return;

            CurrentPage--;
            CurrentOffset = (CurrentPage - 1) * PageSize;
            Games.Clear();
            await LoadGamesAsync();
            ScrollToTopAction?.Invoke();
        }

        [RelayCommand]
        private async Task GoToPage(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > TotalPages || pageNumber == CurrentPage || IsLoading) return;

            CurrentPage = pageNumber;
            CurrentOffset = (CurrentPage - 1) * PageSize;
            Games.Clear();
            await LoadGamesAsync();
            ScrollToTopAction?.Invoke();
        }

        [RelayCommand]
        private async Task GoToPageFromText()
        {
            if (string.IsNullOrWhiteSpace(GoToPageText) || IsLoading) return;

            if (int.TryParse(GoToPageText, out int pageNumber))
            {
                if (pageNumber >= 1 && pageNumber <= TotalPages && pageNumber != CurrentPage)
                {
                    CurrentPage = pageNumber;
                    CurrentOffset = (CurrentPage - 1) * PageSize;
                    Games.Clear();
                    await LoadGamesAsync();
                    ScrollToTopAction?.Invoke();
                }
            }
            GoToPageText = string.Empty;
        }

        private void UpdatePageNumbers()
        {
            PageNumbers.Clear();
            if (TotalPages <= 0) return;

            int maxVisiblePages = 7;
            int startPage = 1;
            int endPage = TotalPages;

            if (TotalPages > maxVisiblePages)
            {
                int halfVisible = maxVisiblePages / 2;
                startPage = System.Math.Max(1, CurrentPage - halfVisible);
                endPage = System.Math.Min(TotalPages, startPage + maxVisiblePages - 1);

                if (endPage - startPage < maxVisiblePages - 1)
                {
                    startPage = System.Math.Max(1, endPage - maxVisiblePages + 1);
                }
            }

            for (int i = startPage; i <= endPage; i++)
            {
                PageNumbers.Add(i);
            }
        }

        [RelayCommand]
        private async Task SearchGames()
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                StatusMessage = "Please enter API key in settings";
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                // If search is empty, load library normally
                await LoadGames();
                return;
            }

            if (SearchQuery.Length < 2)
            {
                StatusMessage = "Enter at least 2 characters to search";
                return;
            }

            IsLoading = true;
            StatusMessage = "Searching...";
            Games.Clear();

            try
            {
                var result = await _manifestApiService.SearchLibraryAsync(SearchQuery, settings.ApiKey, 100);

                if (result != null && result.Results.Count > 0)
                {
                    foreach (var game in result.Results)
                    {
                        Games.Add(game);
                    }

                    TotalCount = result.TotalMatches;
                    CurrentPage = 1;
                    TotalPages = 1;
                    CanGoPrevious = false;
                    CanGoNext = false;
                    StatusMessage = $"Found {result.ReturnedCount} of {result.TotalMatches} matching games";

                    // Load all icons in parallel
                    _ = LoadAllGameIconsAsync(result.Results);
                }
                else
                {
                    StatusMessage = "No games found";
                    TotalCount = 0;
                    CurrentPage = 1;
                    TotalPages = 0;
                    CanGoPrevious = false;
                    CanGoNext = false;
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
                MessageBoxHelper.Show(
                    $"Failed to search: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ChangeSortBy(string sortBy)
        {
            if (SortBy == sortBy) return;

            SortBy = sortBy;
            CurrentOffset = 0;
            Games.Clear();
            await LoadGamesAsync();
        }

        private async Task LoadGamesAsync()
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                StatusMessage = "Please enter API key in settings";
                return;
            }

            IsLoading = true;
            StatusMessage = "Loading games...";

            try
            {
                var result = await _manifestApiService.GetLibraryAsync(
                    settings.ApiKey,
                    limit: PageSize,
                    offset: CurrentOffset,
                    sortBy: SortBy);

                if (result != null && result.Games.Count > 0)
                {
                    Games.Clear();

                    foreach (var game in result.Games)
                    {
                        Games.Add(game);
                    }

                    TotalCount = result.TotalCount;
                    TotalPages = (int)System.Math.Ceiling((double)TotalCount / PageSize);

                    CanGoPrevious = CurrentPage > 1;
                    CanGoNext = CurrentPage < TotalPages;
                    UpdatePageNumbers();

                    var startIndex = CurrentOffset + 1;
                    var endIndex = System.Math.Min(CurrentOffset + result.Games.Count, TotalCount);
                    StatusMessage = $"Showing {startIndex}-{endIndex} of {TotalCount} games (Page {CurrentPage} of {TotalPages})";

                    // Load all icons in parallel
                    _ = LoadAllGameIconsAsync(result.Games);
                }
                else
                {
                    StatusMessage = "No games available";
                    TotalCount = 0;
                    TotalPages = 0;
                    CanGoPrevious = false;
                    CanGoNext = false;
                    UpdatePageNumbers();
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBoxHelper.Show(
                    $"Failed to load games: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadAllGameIconsAsync(List<LibraryGame> games)
        {
            // Create tasks for all games
            var iconTasks = games
                .Where(g => !string.IsNullOrEmpty(g.HeaderImage))
                .Select(game => LoadGameIconAsync(game))
                .ToList();

            // Wait for all to complete (with semaphore limiting concurrency)
            await Task.WhenAll(iconTasks);
        }

        private async Task LoadGameIconAsync(LibraryGame game)
        {
            await _iconLoadSemaphore.WaitAsync();
            try
            {
                var iconPath = await _cacheService.GetIconAsync(game.GameId, game.HeaderImage);

                // Update on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    game.CachedIconPath = iconPath;
                });
            }
            catch
            {
                // Silently fail for individual icons
            }
            finally
            {
                _iconLoadSemaphore.Release();
            }
        }

        [RelayCommand]
        private async Task DownloadGame(LibraryGame game)
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                MessageBoxHelper.Show(
                    "Please enter API key in settings",
                    "API Key Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!game.ManifestAvailable)
            {
                MessageBoxHelper.Show(
                    $"Manifest for '{game.GameName}' is not available yet.",
                    "Not Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                // Create a manifest object for download
                var manifest = new Manifest
                {
                    AppId = game.GameId,
                    Name = game.GameName,
                    IconUrl = game.HeaderImage,
                    Size = game.ManifestSize ?? 0,
                    DownloadUrl = $"https://manifest.morrenus.xyz/api/v1/manifest/{game.GameId}"
                };

                StatusMessage = $"Downloading: {game.GameName}";
                var zipFilePath = await _downloadService.DownloadGameFileOnlyAsync(manifest, settings.DownloadsPath, settings.ApiKey);

                StatusMessage = $"{game.GameName} downloaded successfully";

                MessageBoxHelper.Show(
                    $"{game.GameName} has been downloaded!\n\nGo to the Downloads page to install it.",
                    "Download Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
                MessageBoxHelper.Show(
                    $"Failed to download {game.GameName}: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
