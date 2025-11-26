using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SolusManifestApp.ViewModels
{
    public partial class LuaInstallerViewModel : ObservableObject
    {
        private readonly FileInstallService _fileInstallService;
        private readonly NotificationService _notificationService;
        private readonly SettingsService _settingsService;
        private readonly DepotDownloadService _depotDownloadService;
        private readonly SteamService _steamService;
        private readonly DownloadService _downloadService;
        private readonly SteamApiService _steamApiService;
        private readonly LibraryRefreshService _libraryRefreshService;
        private readonly LoggerService _logger;

        [ObservableProperty]
        private string _selectedFilePath = string.Empty;

        [ObservableProperty]
        private string _selectedFileName = "No file selected";

        [ObservableProperty]
        private bool _hasFileSelected = false;

        [ObservableProperty]
        private bool _isInstalling = false;

        [ObservableProperty]
        private string _statusMessage = "Drop a .zip, .lua, or .manifest file here to install";

        [ObservableProperty]
        private bool _isGreenLumaMode;

        public LuaInstallerViewModel(
            FileInstallService fileInstallService,
            NotificationService notificationService,
            SettingsService settingsService,
            DepotDownloadService depotDownloadService,
            SteamService steamService,
            DownloadService downloadService,
            SteamApiService steamApiService,
            LibraryRefreshService libraryRefreshService,
            LoggerService logger)
        {
            _fileInstallService = fileInstallService;
            _notificationService = notificationService;
            _settingsService = settingsService;
            _depotDownloadService = depotDownloadService;
            _steamService = steamService;
            _downloadService = downloadService;
            _steamApiService = steamApiService;
            _libraryRefreshService = libraryRefreshService;
            _logger = logger;

            // Load initial mode
            var settings = _settingsService.LoadSettings();
            IsGreenLumaMode = settings.Mode == ToolMode.GreenLuma;
        }

        public void RefreshMode()
        {
            var settings = _settingsService.LoadSettings();
            IsGreenLumaMode = settings.Mode == ToolMode.GreenLuma;
        }

        [ObservableProperty]
        private List<string> _selectedFiles = new();

        [RelayCommand]
        private void ProcessDroppedFiles(string[] files)
        {
            if (files == null || files.Length == 0)
                return;

            var validFiles = files.Where(f =>
                f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)).ToList();

            if (validFiles.Count == 0)
            {
                _notificationService.ShowWarning("Please drop a .zip, .lua, or .manifest file");
                return;
            }

            var settings = _settingsService.LoadSettings();

            if (settings.Mode == ToolMode.SteamTools && validFiles.Count > 1)
            {
                SelectedFiles = validFiles;
                SelectedFilePath = string.Join(";", validFiles);
                SelectedFileName = $"{validFiles.Count} files selected";
                HasFileSelected = true;

                var luaCount = validFiles.Count(f => f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
                var zipCount = validFiles.Count(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                var manifestCount = validFiles.Count(f => f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase));

                var parts = new List<string>();
                if (zipCount > 0) parts.Add($"{zipCount} zip(s)");
                if (luaCount > 0) parts.Add($"{luaCount} lua(s)");
                if (manifestCount > 0) parts.Add($"{manifestCount} manifest(s)");

                StatusMessage = $"Ready to install: {string.Join(", ", parts)}";
            }
            else
            {
                var file = validFiles.First();
                SelectedFiles = new List<string> { file };
                SelectedFilePath = file;
                SelectedFileName = Path.GetFileName(file);
                HasFileSelected = true;

                if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = $"Ready to install manifest: {SelectedFileName}";
                }
                else if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = $"Ready to install Lua file: {SelectedFileName}";
                }
                else
                {
                    StatusMessage = $"Ready to install: {SelectedFileName}";
                }
            }
        }

        [RelayCommand]
        private void BrowseFile()
        {
            var settings = _settingsService.LoadSettings();
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Supported Files (*.zip;*.lua;*.manifest)|*.zip;*.lua;*.manifest|Lua Archives (*.zip)|*.zip|Lua Files (*.lua)|*.lua|Manifest Files (*.manifest)|*.manifest|All files (*.*)|*.*",
                Title = "Select File to Install",
                Multiselect = settings.Mode == ToolMode.SteamTools
            };

            if (dialog.ShowDialog() == true)
            {
                var validFiles = dialog.FileNames.Where(f =>
                    f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)).ToList();

                if (validFiles.Count == 0) return;

                if (settings.Mode == ToolMode.SteamTools && validFiles.Count > 1)
                {
                    SelectedFiles = validFiles;
                    SelectedFilePath = string.Join(";", validFiles);
                    SelectedFileName = $"{validFiles.Count} files selected";
                    HasFileSelected = true;

                    var luaCount = validFiles.Count(f => f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
                    var zipCount = validFiles.Count(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                    var manifestCount = validFiles.Count(f => f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase));

                    var parts = new List<string>();
                    if (zipCount > 0) parts.Add($"{zipCount} zip(s)");
                    if (luaCount > 0) parts.Add($"{luaCount} lua(s)");
                    if (manifestCount > 0) parts.Add($"{manifestCount} manifest(s)");

                    StatusMessage = $"Ready to install: {string.Join(", ", parts)}";
                }
                else
                {
                    var file = validFiles.First();
                    SelectedFiles = new List<string> { file };
                    SelectedFilePath = file;
                    SelectedFileName = Path.GetFileName(file);
                    HasFileSelected = true;

                    if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusMessage = $"Ready to install manifest: {SelectedFileName}";
                    }
                    else if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusMessage = $"Ready to install Lua file: {SelectedFileName}";
                    }
                    else
                    {
                        StatusMessage = $"Ready to install: {SelectedFileName}";
                    }
                }
            }
        }

        [RelayCommand]
        private async Task InstallFile()
        {
            var settings = _settingsService.LoadSettings();

            if (settings.Mode == ToolMode.SteamTools && SelectedFiles.Count > 1)
            {
                await InstallMultipleFilesAsync();
                return;
            }

            if (string.IsNullOrEmpty(SelectedFilePath) || (!File.Exists(SelectedFilePath) && !SelectedFilePath.Contains(";")))
            {
                _notificationService.ShowError("Please select a valid file first");
                return;
            }

            IsInstalling = true;
            StatusMessage = $"Installing {SelectedFileName}...";

            try
            {
                if (SelectedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract appId from filename (e.g., 224060.zip -> 224060)
                    var appId = Path.GetFileNameWithoutExtension(SelectedFilePath);

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
                            _notificationService.ShowError($"App ID {appId} already exists in AppList folder. Cannot install duplicate game.");
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
                            _notificationService.ShowError($"App ID {appId} not found in Steam's app list. Cannot install invalid game.");
                            StatusMessage = "Installation cancelled - Invalid App ID";
                            IsInstalling = false;
                            return;
                        }
                    }

                    List<string>? selectedDepotIds = null;

                    // Follow GreenLuma flow if mode is enabled
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
                            _notificationService.ShowError($"AppList is full ({currentCount}/128 files). Cannot add more games. Please uninstall some games first.");
                            StatusMessage = "Installation cancelled - AppList full";
                            IsInstalling = false;
                            return;
                        }

                        // Extract lua content from the zip
                        StatusMessage = $"Analyzing depot information...";
                        var luaContent = _downloadService.ExtractLuaContentFromZip(SelectedFilePath, appId);

                        // Get combined depot info (lua names/sizes + steamcmd languages)
                        var depots = await _depotDownloadService.GetCombinedDepotInfo(appId, luaContent);

                        if (depots.Count > 0)
                        {
                            // Calculate max depots that can be selected
                            var maxDepotsAllowed = 128 - currentCount - 1; // -1 for main app ID

                            if (maxDepotsAllowed <= 0)
                            {
                                _notificationService.ShowError($"AppList is nearly full ({currentCount}/128 files). Cannot add more games. Please uninstall some games first.");
                                StatusMessage = "Installation cancelled - AppList full";
                                IsInstalling = false;
                                return;
                            }

                            // Show warning if space is limited
                            if (maxDepotsAllowed < depots.Count)
                            {
                                _notificationService.ShowWarning($"AppList has limited space. You can only select up to {maxDepotsAllowed} depots (currently {currentCount}/128 files).");
                            }
                            // Show depot selection dialog
                            var depotDialog = new DepotSelectionDialog(depots);
                            var result = depotDialog.ShowDialog();

                            if (result == true && depotDialog.SelectedDepotIds.Count > 0)
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
                    else if (settings.Mode == ToolMode.DepotDownloader)
                    {
                        // DepotDownloader flow: extract depot keys, filter by language, and start download in Downloads tab
                        StatusMessage = "Extracting depot information from lua file...";
                        var luaContent = _downloadService.ExtractLuaContentFromZip(SelectedFilePath, appId);

                        // Parse depot keys from lua content
                        var depotFilterService = new DepotFilterService(_logger);
                        var parsedDepotKeys = depotFilterService.ExtractDepotKeysFromLua(luaContent);

                        if (parsedDepotKeys.Count == 0)
                        {
                            _notificationService.ShowError("No depot keys found in the lua file. Cannot proceed with download.");
                            StatusMessage = "Installation cancelled - No depot keys found";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = $"Found {parsedDepotKeys.Count} depot keys. Fetching depot metadata...";

                        // Fetch depot metadata directly from Steam using SteamKit2
                        var steamKitService = new SteamKitAppInfoService();

                        StatusMessage = "Connecting to Steam...";
                        var initResult = await steamKitService.InitializeAsync();
                        if (!initResult)
                        {
                            _notificationService.ShowError("Failed to connect to Steam. Please check your internet connection and try again.\n\nNote: This requires a connection to Steam's servers.");
                            StatusMessage = "Installation cancelled - Steam connection failed";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = "Fetching depot metadata from Steam...";
                        var steamCmdData = await steamKitService.GetDepotInfoAsync(appId);

                        if (steamCmdData == null)
                        {
                            _notificationService.ShowError($"Failed to fetch depot information for app {appId} from Steam.\n\nThis could mean:\n• The app doesn't exist\n• Steam's servers are having issues\n• The app info is restricted\n\nPlease try again later.");
                            StatusMessage = "Installation cancelled - App info fetch failed";
                            IsInstalling = false;
                            steamKitService.Disconnect();
                            return;
                        }

                        // Disconnect when done
                        steamKitService.Disconnect();

                        // Get available languages (only from depots with keys)
                        var availableLanguages = depotFilterService.GetAvailableLanguages(steamCmdData, appId, parsedDepotKeys);

                        if (availableLanguages.Count == 0)
                        {
                            _notificationService.ShowWarning("No languages found in depot metadata. Using all depots.");
                            availableLanguages = new List<string> { "all" };
                        }

                        // Show language selection dialog
                        StatusMessage = "Waiting for language selection...";
                        var languageDialog = new LanguageSelectionDialog(availableLanguages);
                        var languageResult = languageDialog.ShowDialog();

                        if (languageResult != true || string.IsNullOrEmpty(languageDialog.SelectedLanguage))
                        {
                            StatusMessage = "Installation cancelled";
                            IsInstalling = false;
                            return;
                        }

                        // Filter depots using Python-style logic
                        StatusMessage = $"Filtering depots for language: {languageDialog.SelectedLanguage}...";
                        var filteredDepotIds = depotFilterService.GetDepotsForLanguage(
                            steamCmdData,
                            parsedDepotKeys,
                            languageDialog.SelectedLanguage,
                            appId);

                        if (filteredDepotIds.Count == 0)
                        {
                            _notificationService.ShowError("No depots matched the selected language. Cannot proceed with download.");
                            StatusMessage = "Installation cancelled - No matching depots";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = $"Found {filteredDepotIds.Count} depots for {languageDialog.SelectedLanguage}. Preparing depot selection...";

                        // Parse depot names from lua content for friendly display
                        var luaParser = new LuaParser();
                        var luaDepots = luaParser.ParseDepotsFromLua(luaContent, appId);
                        var depotNameMap = luaDepots.ToDictionary(d => d.DepotId, d => d.Name);

                        // Convert filtered depot IDs to depot info list for selection dialog
                        var depotsForSelection = new List<DepotInfo>();
                        foreach (var depotIdStr in filteredDepotIds)
                        {
                            if (uint.TryParse(depotIdStr, out var depotId) && parsedDepotKeys.ContainsKey(depotIdStr))
                            {
                                // Get friendly depot name from lua, or fallback to generic name
                                string depotName = depotNameMap.TryGetValue(depotIdStr, out var name) ? name : $"Depot {depotIdStr}";
                                string depotLanguage = "";
                                long depotSize = 0;

                                if (steamCmdData.Data.TryGetValue(appId, out var appData) &&
                                    appData.Depots?.TryGetValue(depotIdStr, out var depotData) == true)
                                {
                                    depotLanguage = depotData.Config?.Language ?? "";

                                    // Get size from manifest if available
                                    if (depotData.Manifests?.TryGetValue("public", out var manifestData) == true)
                                    {
                                        depotSize = manifestData.Size;
                                    }
                                }

                                depotsForSelection.Add(new DepotInfo
                                {
                                    DepotId = depotIdStr,
                                    Name = depotName,
                                    Size = depotSize,
                                    Language = depotLanguage
                                });
                            }
                        }

                        // Show depot selection dialog
                        StatusMessage = "Waiting for depot selection...";
                        var depotDialog = new DepotSelectionDialog(depotsForSelection);
                        var depotResult = depotDialog.ShowDialog();

                        if (depotResult != true || depotDialog.SelectedDepotIds.Count == 0)
                        {
                            StatusMessage = "Installation cancelled";
                            IsInstalling = false;
                            return;
                        }

                        // Prepare download path
                        var outputPath = settings.DepotDownloaderOutputPath;
                        if (string.IsNullOrEmpty(outputPath))
                        {
                            _notificationService.ShowError("DepotDownloader output path not configured. Please set it in Settings.");
                            StatusMessage = "Installation cancelled - Output path not set";
                            IsInstalling = false;
                            return;
                        }

                        // Extract manifest files from zip
                        StatusMessage = "Extracting manifest files...";
                        var manifestFiles = _downloadService.ExtractManifestFilesFromZip(SelectedFilePath, appId);

                        // Prepare depot list with keys and manifest files
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

                        // Start download via DownloadService (shows in Downloads tab with progress)
                        StatusMessage = "Starting download in Downloads tab...";

                        // Start the download asynchronously - it will appear in Downloads tab
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
                        _notificationService.ShowSuccess($"Download started for {gameName}!\n\nCheck the Downloads tab to monitor progress.\nFiles will be downloaded to: {gameDownloadPath}");
                        StatusMessage = "Download started - check Downloads tab";

                        // Clear selection
                        SelectedFilePath = string.Empty;
                        SelectedFileName = "No file selected";
                        HasFileSelected = false;
                        IsInstalling = false;
                        return; // Skip the regular file installation flow
                    }

                    // Install ZIP (contains .lua and .manifest files)
                    var depotKeys = await _fileInstallService.InstallFromZipAsync(
                        SelectedFilePath,
                        settings.Mode == ToolMode.GreenLuma,
                        message =>
                        {
                            StatusMessage = message;
                        },
                        selectedDepotIds);

                    // If GreenLuma mode, update Config.VDF with depot keys
                    if (settings.Mode == ToolMode.GreenLuma)
                    {
                        StatusMessage = $"Extracted {depotKeys.Count} depot keys from package";

                        if (depotKeys.Count > 0)
                        {
                            StatusMessage = $"Updating Config.VDF with {depotKeys.Count} depot keys...";

                            var success = _fileInstallService.UpdateConfigVdfWithDepotKeys(depotKeys);
                            if (success)
                            {
                                StatusMessage = $"Successfully added {depotKeys.Count} depot keys to config.vdf";
                            }
                            else
                            {
                                _notificationService.ShowWarning("Failed to update config.vdf with depot keys. You may need to add them manually.");
                            }
                        }
                        else
                        {
                            _notificationService.ShowWarning("No depot keys found in the package. Config.vdf will not be updated.");
                        }
                    }

                    // Notify library that game was installed
                    _libraryRefreshService.NotifyGameInstalled(appId, settings.Mode == ToolMode.GreenLuma);
                }
                else if (SelectedFilePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    // Install individual .lua file
                    await _fileInstallService.InstallLuaFileAsync(SelectedFilePath);

                    // Extract appId from filename and notify library
                    var appId = Path.GetFileNameWithoutExtension(SelectedFilePath);
                    _libraryRefreshService.NotifyGameInstalled(appId, settings.Mode == ToolMode.GreenLuma);
                }
                else if (SelectedFilePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    // Install .manifest file
                    await _fileInstallService.InstallManifestFileAsync(SelectedFilePath);
                }
                else
                {
                    throw new Exception("Unsupported file type");
                }

                _notificationService.ShowSuccess($"{SelectedFileName} installed successfully!\n\nRestart Steam for changes to take effect.");
                StatusMessage = "Installation complete! Restart Steam for changes to take effect.";

                // Clear selection
                SelectedFilePath = string.Empty;
                SelectedFileName = "No file selected";
                HasFileSelected = false;
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Installation failed: {ex.Message}");
                StatusMessage = $"Installation failed: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallMultipleFilesAsync()
        {
            if (SelectedFiles.Count == 0)
            {
                _notificationService.ShowError("No files selected");
                return;
            }

            IsInstalling = true;
            int successCount = 0;
            int failCount = 0;
            var installedAppIds = new List<string>();

            try
            {
                var settings = _settingsService.LoadSettings();

                for (int i = 0; i < SelectedFiles.Count; i++)
                {
                    var file = SelectedFiles[i];
                    if (!File.Exists(file)) continue;

                    StatusMessage = $"Installing {i + 1}/{SelectedFiles.Count}: {Path.GetFileName(file)}...";

                    try
                    {
                        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            var appId = Path.GetFileNameWithoutExtension(file);
                            await _fileInstallService.InstallFromZipAsync(file, false, msg => StatusMessage = msg);
                            installedAppIds.Add(appId);
                            successCount++;
                        }
                        else if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                        {
                            await _fileInstallService.InstallLuaFileAsync(file);
                            var appId = Path.GetFileNameWithoutExtension(file);
                            installedAppIds.Add(appId);
                            successCount++;
                        }
                        else if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                        {
                            await _fileInstallService.InstallManifestFileAsync(file);
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to install {Path.GetFileName(file)}: {ex.Message}");
                        failCount++;
                    }
                }

                foreach (var appId in installedAppIds.Distinct())
                {
                    _libraryRefreshService.NotifyGameInstalled(appId, false);
                }

                if (failCount == 0)
                {
                    _notificationService.ShowSuccess($"All {successCount} files installed successfully!\n\nRestart Steam for changes to take effect.");
                    StatusMessage = "Installation complete! Restart Steam for changes to take effect.";
                }
                else
                {
                    _notificationService.ShowWarning($"Installed {successCount} files, {failCount} failed.\n\nRestart Steam for changes to take effect.");
                    StatusMessage = $"Partial installation: {successCount} succeeded, {failCount} failed";
                }

                SelectedFilePath = string.Empty;
                SelectedFileName = "No file selected";
                SelectedFiles.Clear();
                HasFileSelected = false;
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Installation failed: {ex.Message}");
                StatusMessage = $"Installation failed: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        [RelayCommand]
        private void ClearSelection()
        {
            SelectedFilePath = string.Empty;
            SelectedFileName = "No file selected";
            SelectedFiles.Clear();
            HasFileSelected = false;
            StatusMessage = "Drop a .zip, .lua, or .manifest file here to install";
        }
    }
}
