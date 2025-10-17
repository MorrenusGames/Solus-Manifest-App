using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SolusManifestApp.ViewModels
{
    public partial class DownloadsViewModel : ObservableObject
    {
        private readonly DownloadService _downloadService;
        private readonly FileInstallService _fileInstallService;
        private readonly SettingsService _settingsService;
        private readonly DepotDownloadService _depotDownloadService;
        private readonly SteamService _steamService;
        private readonly SteamApiService _steamApiService;
        private readonly NotificationService _notificationService;
        private readonly LibraryRefreshService _libraryRefreshService;
        private readonly LoggerService _logger;

        [ObservableProperty]
        private ObservableCollection<DownloadItem> _activeDownloads;

        [ObservableProperty]
        private ObservableCollection<string> _downloadedFiles = new();

        [ObservableProperty]
        private string _statusMessage = "No downloads";

        [ObservableProperty]
        private bool _isInstalling;

        public DownloadsViewModel(
            DownloadService downloadService,
            FileInstallService fileInstallService,
            SettingsService settingsService,
            DepotDownloadService depotDownloadService,
            SteamService steamService,
            SteamApiService steamApiService,
            NotificationService notificationService,
            LibraryRefreshService libraryRefreshService)
        {
            _downloadService = downloadService;
            _fileInstallService = fileInstallService;
            _settingsService = settingsService;
            _depotDownloadService = depotDownloadService;
            _steamService = steamService;
            _steamApiService = steamApiService;
            _notificationService = notificationService;
            _libraryRefreshService = libraryRefreshService;
            _logger = new LoggerService();

            ActiveDownloads = _downloadService.ActiveDownloads;

            RefreshDownloadedFiles();

            // Subscribe to download completed event for auto-refresh
            _downloadService.DownloadCompleted += OnDownloadCompleted;
        }

        private async void OnDownloadCompleted(object? sender, DownloadItem downloadItem)
        {
            // Auto-refresh the downloaded files list when a download completes
            RefreshDownloadedFiles();

            // Skip auto-install for DepotDownloader mode (files are downloaded directly, not as zip)
            if (downloadItem.IsDepotDownloaderMode)
            {
                return;
            }

            // Check if auto-install is enabled
            var settings = _settingsService.LoadSettings();
            if (settings.AutoInstallAfterDownload && !string.IsNullOrEmpty(downloadItem.DestinationPath) && File.Exists(downloadItem.DestinationPath))
            {
                // Auto-install the downloaded file
                await InstallFile(downloadItem.DestinationPath);
            }
        }

        [RelayCommand]
        private void RefreshDownloadedFiles()
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrEmpty(settings.DownloadsPath) || !Directory.Exists(settings.DownloadsPath))
            {
                DownloadedFiles.Clear();
                StatusMessage = "No downloads folder configured";
                return;
            }

            try
            {
                var files = Directory.GetFiles(settings.DownloadsPath, "*.zip")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();

                DownloadedFiles = new ObservableCollection<string>(files);
                StatusMessage = files.Count > 0 ? $"{files.Count} file(s) ready to install" : "No downloaded files";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task InstallFile(string filePath)
        {
            if (IsInstalling)
            {
                MessageBoxHelper.Show(
                    "Another installation is in progress",
                    "Please Wait",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            IsInstalling = true;
            var fileName = Path.GetFileName(filePath);
            StatusMessage = $"Installing {fileName}...";

            try
            {
                var settings = _settingsService.LoadSettings();
                var appId = Path.GetFileNameWithoutExtension(filePath);

                // Handle DepotDownloader mode
                if (settings.Mode == ToolMode.DepotDownloader)
                {
                    _logger.Info("=== Starting DepotDownloader Info Gathering Phase ===");
                    _logger.Info($"App ID: {appId}");
                    _logger.Info($"Zip file: {fileName}");

                    // DepotDownloader flow: extract depot keys, filter by language, and start download
                    StatusMessage = "Extracting depot information from lua file...";
                    _logger.Info("Step 1: Extracting lua content from zip file...");
                    var luaContent = _downloadService.ExtractLuaContentFromZip(filePath, appId);
                    _logger.Info($"Lua content extracted successfully ({luaContent.Length} characters)");

                    // Parse depot keys from lua content
                    _logger.Info("Step 2: Parsing depot keys from lua content...");
                    var depotFilterService = new DepotFilterService(new LoggerService());
                    var parsedDepotKeys = depotFilterService.ExtractDepotKeysFromLua(luaContent);

                    if (parsedDepotKeys.Count == 0)
                    {
                        _logger.Error("No depot keys found in lua file!");
                        MessageBoxHelper.Show(
                            "No depot keys found in the lua file. Cannot proceed with download.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - No depot keys found";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"Found {parsedDepotKeys.Count} depot keys:");
                    foreach (var kvp in parsedDepotKeys)
                    {
                        _logger.Info($"  Depot {kvp.Key}: {kvp.Value}");
                    }

                    StatusMessage = $"Found {parsedDepotKeys.Count} depot keys. Fetching depot metadata...";

                    // Fetch depot metadata directly from Steam using SteamKit2
                    _logger.Info("Step 3: Fetching depot metadata directly from Steam...");
                    var steamKitService = new SteamKitAppInfoService();

                    StatusMessage = "Connecting to Steam...";
                    var initResult = await steamKitService.InitializeAsync();
                    if (!initResult)
                    {
                        _logger.Error("Failed to initialize Steam connection!");
                        MessageBoxHelper.Show(
                            "Failed to connect to Steam. Please check your internet connection and try again.\n\nNote: This requires a connection to Steam's servers.",
                            "Steam Connection Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Steam connection failed";
                        IsInstalling = false;
                        return;
                    }

                    StatusMessage = "Fetching depot metadata from Steam...";
                    var steamCmdData = await steamKitService.GetDepotInfoAsync(appId);

                    if (steamCmdData == null)
                    {
                        _logger.Error("Failed to fetch depot information from Steam!");
                        MessageBoxHelper.Show(
                            $"Failed to fetch depot information for app {appId} from Steam.\n\nThis could mean:\n• The app doesn't exist\n• Steam's servers are having issues\n• The app info is restricted\n\nPlease try again later.",
                            "Failed to Fetch App Info",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - App info fetch failed";
                        IsInstalling = false;
                        steamKitService.Disconnect();
                        return;
                    }

                    // Disconnect when done
                    steamKitService.Disconnect();

                    _logger.Info("Steam depot data fetched successfully");
                    if (steamCmdData.Data != null && steamCmdData.Data.ContainsKey(appId))
                    {
                        var appData = steamCmdData.Data[appId];
                        _logger.Info($"App Name: {appData.Common?.Name ?? "Unknown"}");
                        _logger.Info($"Total Depots in API data: {appData.Depots?.Count ?? 0}");
                    }

                    // Get available languages
                    _logger.Info("Step 4: Getting available languages from SteamCMD data...");
                    var availableLanguages = depotFilterService.GetAvailableLanguages(steamCmdData, appId);
                    _logger.Info($"Available languages: {string.Join(", ", availableLanguages)}");

                    if (availableLanguages.Count == 0)
                    {
                        _logger.Warning("No languages found in depot metadata. Using 'all' as fallback.");
                        _notificationService.ShowWarning("No languages found in depot metadata. Using all depots.");
                        availableLanguages = new List<string> { "all" };
                    }

                    // Show language selection dialog
                    StatusMessage = "Waiting for language selection...";
                    _logger.Info("Step 5: Showing language selection dialog to user...");
                    var languageDialog = new LanguageSelectionDialog(availableLanguages);
                    var languageResult = languageDialog.ShowDialog();

                    if (languageResult != true || string.IsNullOrEmpty(languageDialog.SelectedLanguage))
                    {
                        _logger.Info("User cancelled language selection");
                        StatusMessage = "Installation cancelled";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"User selected language: {languageDialog.SelectedLanguage}");

                    // Filter depots using Python-style logic
                    StatusMessage = $"Filtering depots for language: {languageDialog.SelectedLanguage}...";
                    _logger.Info($"Step 6: Filtering depots for language '{languageDialog.SelectedLanguage}' using Python-style logic...");
                    var filteredDepotIds = depotFilterService.GetDepotsForLanguage(
                        steamCmdData,
                        parsedDepotKeys,
                        languageDialog.SelectedLanguage,
                        appId);

                    if (filteredDepotIds.Count == 0)
                    {
                        _logger.Error("No depots matched the selected language!");
                        MessageBoxHelper.Show(
                            "No depots matched the selected language. Cannot proceed with download.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - No matching depots";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"Filtered depot list contains {filteredDepotIds.Count} depots: {string.Join(", ", filteredDepotIds)}");
                    StatusMessage = $"Found {filteredDepotIds.Count} depots for {languageDialog.SelectedLanguage}. Preparing depot selection...";

                    // Convert filtered depot IDs to depot info list for selection dialog
                    _logger.Info("Step 7: Converting filtered depot IDs to depot info for selection dialog...");
                    var depotsForSelection = new List<DepotInfo>();
                    foreach (var depotIdStr in filteredDepotIds)
                    {
                        if (uint.TryParse(depotIdStr, out var depotId) && parsedDepotKeys.ContainsKey(depotIdStr))
                        {
                            // Try to get depot name/info from SteamCMD data
                            string depotName = $"Depot {depotId}";
                            string depotLanguage = "";

                            if (steamCmdData.Data.TryGetValue(appId, out var appData) &&
                                appData.Depots?.TryGetValue(depotIdStr, out var depotData) == true)
                            {
                                depotLanguage = depotData.Config?.Language ?? "";
                            }

                            _logger.Debug($"  Added depot {depotId} - Language: {(string.IsNullOrEmpty(depotLanguage) ? "none (base depot)" : depotLanguage)}");

                            depotsForSelection.Add(new DepotInfo
                            {
                                DepotId = depotIdStr,
                                Name = depotName,
                                Size = 0, // Size unknown at this stage
                                Language = depotLanguage
                            });
                        }
                    }

                    // Show depot selection dialog
                    StatusMessage = "Waiting for depot selection...";
                    _logger.Info($"Step 8: Showing depot selection dialog ({depotsForSelection.Count} depots)...");
                    var depotDialog = new DepotSelectionDialog(depotsForSelection);
                    var depotResult = depotDialog.ShowDialog();

                    if (depotResult != true || depotDialog.SelectedDepotIds.Count == 0)
                    {
                        _logger.Info("User cancelled depot selection");
                        StatusMessage = "Installation cancelled";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"User selected {depotDialog.SelectedDepotIds.Count} depots: {string.Join(", ", depotDialog.SelectedDepotIds)}");

                    // Prepare download path
                    var outputPath = settings.DepotDownloaderOutputPath;
                    if (string.IsNullOrEmpty(outputPath))
                    {
                        _logger.Error("DepotDownloader output path not configured!");
                        MessageBoxHelper.Show(
                            "DepotDownloader output path not configured. Please set it in Settings.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Output path not set";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"Output path: {outputPath}");

                    // Extract manifest files from zip
                    StatusMessage = "Extracting manifest files...";
                    _logger.Info("Step 9: Extracting manifest files from zip...");
                    var manifestFiles = _downloadService.ExtractManifestFilesFromZip(filePath, appId);
                    _logger.Info($"Extracted {manifestFiles.Count} manifest files");

                    // Prepare depot list with keys and manifest files
                    _logger.Info("Step 10: Preparing depot download list with keys and manifest files...");
                    var depotsToDownload = new List<(uint depotId, string depotKey, string? manifestFile)>();
                    foreach (var selectedDepotId in depotDialog.SelectedDepotIds)
                    {
                        if (uint.TryParse(selectedDepotId, out var depotId) && parsedDepotKeys.TryGetValue(selectedDepotId, out var depotKey))
                        {
                            // Try to get the manifest file path for this depot
                            string? manifestFilePath = null;
                            if (manifestFiles.TryGetValue(selectedDepotId, out var manifestPath))
                            {
                                manifestFilePath = manifestPath;
                                _logger.Info($"  Depot {depotId}: Using manifest file {Path.GetFileName(manifestPath)}");
                            }
                            else
                            {
                                _logger.Info($"  Depot {depotId}: No manifest file (will download latest)");
                            }

                            depotsToDownload.Add((depotId, depotKey, manifestFilePath));
                        }
                    }

                    // Get game name from SteamCMD data
                    string gameName = appId;
                    if (steamCmdData.Data.TryGetValue(appId, out var gameData))
                    {
                        gameName = gameData.Common?.Name ?? appId;
                    }
                    _logger.Info($"Game name: {gameName}");

                    // Start download via DownloadService (shows in Downloads tab with progress)
                    StatusMessage = "Starting download...";
                    _logger.Info("=== Info Gathering Phase Complete ===");
                    _logger.Info($"Step 11: Starting download for {depotsToDownload.Count} depots...");
                    _logger.Info($"  App ID: {appId}");
                    _logger.Info($"  Game Name: {gameName}");
                    _logger.Info($"  Output Path: {outputPath}");
                    _logger.Info($"  Verify Files: {settings.VerifyFilesAfterDownload}");
                    _logger.Info($"  Max Concurrent Downloads: {settings.MaxConcurrentDownloads}");

                    // Start the download asynchronously
                    _ = _downloadService.DownloadViaDepotDownloaderAsync(
                        appId,
                        gameName,
                        depotsToDownload,
                        outputPath,
                        settings.VerifyFilesAfterDownload,
                        settings.MaxConcurrentDownloads
                    );

                    var gameFolderName = $"{gameName} ({appId})";
                    var gameDownloadPath = Path.Combine(outputPath, gameFolderName, gameName);
                    _logger.Info($"Download initiated successfully. Files will be downloaded to: {gameDownloadPath}");
                    _notificationService.ShowSuccess($"Download started for {gameName}!\n\nCheck the Downloads tab to monitor progress.\nFiles will be downloaded to: {gameDownloadPath}", "Download Started");

                    StatusMessage = "Download started - check progress below";

                    // Auto-delete the ZIP file after starting download
                    _logger.Info($"Deleting zip file: {fileName}");
                    File.Delete(filePath);
                    RefreshDownloadedFiles();

                    IsInstalling = false;
                    return; // Skip the regular file installation flow
                }

                // Validate appId for GreenLuma mode
                if (settings.Mode == ToolMode.GreenLuma)
                {
                    // Check if app already exists in AppList
                    string? customPath = null;
                    if (settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder)
                    {
                        var injectorDir = Path.GetDirectoryName(settings.DLLInjectorPath);
                        if (!string.IsNullOrEmpty(injectorDir))
                        {
                            customPath = Path.Combine(injectorDir, "AppList");
                        }
                    }

                    if (_fileInstallService.IsAppIdInAppList(appId, customPath))
                    {
                        MessageBoxHelper.Show(
                            $"App ID {appId} already exists in AppList folder. Cannot install duplicate game.",
                            "Duplicate App ID",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Duplicate App ID";
                        IsInstalling = false;
                        return;
                    }

                    // Validate app exists in Steam's official app list
                    StatusMessage = "Validating App ID...";
                    var steamAppList = await _steamApiService.GetAppListAsync();
                    var gameName = _steamApiService.GetGameName(appId, steamAppList);

                    if (gameName == "Unknown Game")
                    {
                        MessageBoxHelper.Show(
                            $"App ID {appId} not found in Steam's app list. Cannot install invalid game.",
                            "Invalid App ID",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Invalid App ID";
                        IsInstalling = false;
                        return;
                    }
                }

                List<string>? selectedDepotIds = null;

                // For GreenLuma mode, show depot selection dialog
                if (settings.Mode == ToolMode.GreenLuma)
                {
                    // Check current AppList count before proceeding
                    string? customPath = null;
                    if (settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder)
                    {
                        var injectorDir = Path.GetDirectoryName(settings.DLLInjectorPath);
                        if (!string.IsNullOrEmpty(injectorDir))
                        {
                            customPath = Path.Combine(injectorDir, "AppList");
                        }
                    }

                    var appListPath = customPath ?? Path.Combine(_steamService.GetSteamPath(), "AppList");
                    var currentCount = Directory.Exists(appListPath) ? Directory.GetFiles(appListPath, "*.txt").Length : 0;

                    if (currentCount >= 128)
                    {
                        MessageBoxHelper.Show(
                            $"AppList is full ({currentCount}/128 files). Cannot add more games. Please uninstall some games first.",
                            "AppList Full",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - AppList full";
                        IsInstalling = false;
                        return;
                    }

                    // Extract lua content from zip
                    StatusMessage = $"Analyzing depot information...";
                    var luaContent = _downloadService.ExtractLuaContentFromZip(filePath, appId);

                    // Get combined depot info (lua names/sizes + steamcmd languages)
                    var depots = await _depotDownloadService.GetCombinedDepotInfo(appId, luaContent);

                    if (depots.Count > 0)
                    {
                        // Calculate max depots that can be selected
                        var maxDepotsAllowed = 128 - currentCount - 1; // -1 for main app ID

                        if (maxDepotsAllowed <= 0)
                        {
                            MessageBoxHelper.Show(
                                $"AppList is nearly full ({currentCount}/128 files). Cannot add more games. Please uninstall some games first.",
                                "AppList Full",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            StatusMessage = "Installation cancelled - AppList full";
                            IsInstalling = false;
                            return;
                        }

                        // Show warning if space is limited
                        if (maxDepotsAllowed < depots.Count)
                        {
                            MessageBoxHelper.Show(
                                $"AppList has limited space. You can only select up to {maxDepotsAllowed} depots (currently {currentCount}/128 files).",
                                "Limited AppList Space",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        // Show depot selection dialog
                        var depotDialog = new DepotSelectionDialog(depots);
                        var dialogResult = depotDialog.ShowDialog();

                        if (dialogResult == true && depotDialog.SelectedDepotIds.Count > 0)
                        {
                            selectedDepotIds = depotDialog.SelectedDepotIds;
                        }
                        else
                        {
                            StatusMessage = "Installation cancelled";
                            IsInstalling = false;
                            return;
                        }

                        // Generate AppList with main appid + selected depot IDs
                        StatusMessage = $"Generating AppList for selected depots...";
                        var appListIds = new List<string> { appId };
                        appListIds.AddRange(selectedDepotIds);

                        // Reuse customPath from earlier check
                        _fileInstallService.GenerateAppList(appListIds, customPath);

                        // Generate ACF file for the game
                        StatusMessage = $"Generating ACF file...";
                        string? libraryFolder = settings.UseDefaultInstallLocation ? null : settings.SelectedLibraryFolder;
                        _fileInstallService.GenerateACF(appId, appId, appId, libraryFolder);
                    }
                }

                // Install files using the proper installation service
                StatusMessage = $"Installing files...";

                var depotKeys = await _fileInstallService.InstallFromZipAsync(
                    filePath,
                    settings.Mode == ToolMode.GreenLuma,
                    message => StatusMessage = message,
                    selectedDepotIds);

                // If GreenLuma mode, update Config.VDF with depot keys
                if (settings.Mode == ToolMode.GreenLuma && depotKeys.Count > 0)
                {
                    StatusMessage = $"Updating Config.VDF with {depotKeys.Count} depot keys...";
                    var success = _fileInstallService.UpdateConfigVdfWithDepotKeys(depotKeys);
                    if (!success)
                    {
                        MessageBoxHelper.Show(
                            "Failed to update config.vdf with depot keys. You may need to add them manually.",
                            "Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                _notificationService.ShowSuccess($"{fileName} has been installed successfully! Restart Steam for changes to take effect.", "Installation Complete");

                StatusMessage = $"{fileName} installed successfully";

                // Notify library to add the game instantly
                _libraryRefreshService.NotifyGameInstalled(appId, settings.Mode == ToolMode.GreenLuma);

                // Auto-delete the ZIP file
                File.Delete(filePath);
                RefreshDownloadedFiles();
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Installation failed: {ex.Message}";
                MessageBoxHelper.Show(
                    $"Failed to install {fileName}: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsInstalling = false;
            }
        }

        [RelayCommand]
        private void CancelDownload(DownloadItem item)
        {
            _downloadService.CancelDownload(item.Id);
            StatusMessage = $"Cancelled: {item.GameName}";
        }

        [RelayCommand]
        private void RemoveDownload(DownloadItem item)
        {
            _downloadService.RemoveDownload(item);
        }

        [RelayCommand]
        private void ClearCompleted()
        {
            _downloadService.ClearCompletedDownloads();
        }

        [RelayCommand]
        private void DeleteFile(string filePath)
        {
            var result = MessageBoxHelper.Show(
                $"Are you sure you want to delete {Path.GetFileName(filePath)}?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(filePath);
                    RefreshDownloadedFiles();
                    StatusMessage = "File deleted";
                }
                catch (System.Exception ex)
                {
                    MessageBoxHelper.Show(
                        $"Failed to delete file: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void OpenDownloadsFolder()
        {
            var settings = _settingsService.LoadSettings();

            if (!string.IsNullOrEmpty(settings.DownloadsPath) && Directory.Exists(settings.DownloadsPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = settings.DownloadsPath,
                    UseShellExecute = true
                });
            }
        }
    }
}
