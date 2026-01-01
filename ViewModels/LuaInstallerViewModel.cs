using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private List<string> _selectedFiles = new();

        public LuaInstallerViewModel(
            FileInstallService fileInstallService,
            NotificationService notificationService,
            SettingsService settingsService,
            DepotDownloadService depotDownloadService,
            SteamService steamService,
            DownloadService downloadService,
            LibraryRefreshService libraryRefreshService,
            LoggerService logger)
        {
            _fileInstallService = fileInstallService;
            _notificationService = notificationService;
            _settingsService = settingsService;
            _depotDownloadService = depotDownloadService;
            _steamService = steamService;
            _downloadService = downloadService;
            _libraryRefreshService = libraryRefreshService;
            _logger = logger;
        }

        public void RefreshMode()
        {
        }

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
                    var appId = Path.GetFileNameWithoutExtension(SelectedFilePath);

                    if (settings.Mode == ToolMode.DepotDownloader)
                    {
                        StatusMessage = "Extracting depot information from lua file...";
                        var luaContent = _downloadService.ExtractLuaContentFromZip(SelectedFilePath, appId);

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

                        var steamKitService = new SteamKitAppInfoService();
                        StatusMessage = "Connecting to Steam...";
                        var initResult = await steamKitService.InitializeAsync();
                        if (!initResult)
                        {
                            _notificationService.ShowError("Failed to connect to Steam. Please check your internet connection.");
                            StatusMessage = "Installation cancelled - Steam connection failed";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = "Fetching depot metadata from Steam...";
                        var steamCmdData = await steamKitService.GetDepotInfoAsync(appId);

                        if (steamCmdData == null)
                        {
                            _notificationService.ShowError($"Failed to fetch depot information for app {appId} from Steam.");
                            StatusMessage = "Installation cancelled - App info fetch failed";
                            IsInstalling = false;
                            steamKitService.Disconnect();
                            return;
                        }

                        steamKitService.Disconnect();

                        var availableLanguages = depotFilterService.GetAvailableLanguages(steamCmdData, appId, parsedDepotKeys);
                        if (availableLanguages.Count == 0)
                        {
                            _notificationService.ShowWarning("No languages found in depot metadata. Using all depots.");
                            availableLanguages = new List<string> { "all" };
                        }

                        StatusMessage = "Waiting for language selection...";
                        var languageDialog = new LanguageSelectionDialog(availableLanguages);
                        var languageResult = languageDialog.ShowDialog();

                        if (languageResult != true || string.IsNullOrEmpty(languageDialog.SelectedLanguage))
                        {
                            StatusMessage = "Installation cancelled";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = $"Filtering depots for language: {languageDialog.SelectedLanguage}...";
                        var filteredDepotIds = depotFilterService.GetDepotsForLanguage(
                            steamCmdData,
                            parsedDepotKeys,
                            languageDialog.SelectedLanguage,
                            appId);

                        if (filteredDepotIds.Count == 0)
                        {
                            _notificationService.ShowError("No depots matched the selected language.");
                            StatusMessage = "Installation cancelled - No matching depots";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = $"Found {filteredDepotIds.Count} depots. Preparing depot selection...";

                        var luaParser = new LuaParser();
                        var luaDepots = luaParser.ParseDepotsFromLua(luaContent, appId);
                        var depotNameMap = luaDepots.ToDictionary(d => d.DepotId, d => d.Name);

                        var depotsForSelection = new List<DepotInfo>();
                        foreach (var depotIdStr in filteredDepotIds)
                        {
                            if (uint.TryParse(depotIdStr, out var depotId) && parsedDepotKeys.ContainsKey(depotIdStr))
                            {
                                string depotName = depotNameMap.TryGetValue(depotIdStr, out var name) ? name : $"Depot {depotIdStr}";
                                string depotLanguage = "";
                                long depotSize = 0;

                                if (steamCmdData.Data.TryGetValue(appId, out var appData) &&
                                    appData.Depots?.TryGetValue(depotIdStr, out var depotData) == true)
                                {
                                    depotLanguage = depotData.Config?.Language ?? "";
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

                        StatusMessage = "Waiting for depot selection...";
                        var depotDialog = new DepotSelectionDialog(depotsForSelection);
                        var depotResult = depotDialog.ShowDialog();

                        if (depotResult != true || depotDialog.SelectedDepotIds.Count == 0)
                        {
                            StatusMessage = "Installation cancelled";
                            IsInstalling = false;
                            return;
                        }

                        var outputPath = settings.DepotDownloaderOutputPath;
                        if (string.IsNullOrEmpty(outputPath))
                        {
                            _notificationService.ShowError("DepotDownloader output path not configured. Please set it in Settings.");
                            StatusMessage = "Installation cancelled - Output path not set";
                            IsInstalling = false;
                            return;
                        }

                        StatusMessage = "Extracting manifest files...";
                        var manifestFiles = _downloadService.ExtractManifestFilesFromZip(SelectedFilePath, appId);

                        var depotsToDownload = new List<(uint depotId, string depotKey, string? manifestFile)>();
                        foreach (var selectedDepotId in depotDialog.SelectedDepotIds)
                        {
                            if (uint.TryParse(selectedDepotId, out var depotId) && parsedDepotKeys.TryGetValue(selectedDepotId, out var depotKey))
                            {
                                string? manifestFilePath = manifestFiles.TryGetValue(selectedDepotId, out var manifestPath) ? manifestPath : null;
                                depotsToDownload.Add((depotId, depotKey, manifestFilePath));
                            }
                        }

                        string gameName = appId;
                        if (steamCmdData.Data.TryGetValue(appId, out var gameData))
                        {
                            gameName = gameData.Common?.Name ?? appId;
                        }

                        StatusMessage = "Starting download in Downloads tab...";
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
                        _notificationService.ShowSuccess($"Download started for {gameName}!\n\nCheck the Downloads tab to monitor progress.");
                        StatusMessage = "Download started - check Downloads tab";

                        SelectedFilePath = string.Empty;
                        SelectedFileName = "No file selected";
                        HasFileSelected = false;
                        IsInstalling = false;
                        return;
                    }

                    await _fileInstallService.InstallFromZipAsync(SelectedFilePath, message => StatusMessage = message);
                    _libraryRefreshService.NotifyGameInstalled(appId, false);
                }
                else if (SelectedFilePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    await _fileInstallService.InstallLuaFileAsync(SelectedFilePath);
                    var appId = Path.GetFileNameWithoutExtension(SelectedFilePath);
                    _libraryRefreshService.NotifyGameInstalled(appId, false);
                }
                else if (SelectedFilePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    await _fileInstallService.InstallManifestFileAsync(SelectedFilePath);
                }
                else
                {
                    throw new Exception("Unsupported file type");
                }

                _notificationService.ShowSuccess($"{SelectedFileName} installed successfully!\n\nRestart Steam for changes to take effect.");
                StatusMessage = "Installation complete! Restart Steam for changes to take effect.";

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
                            await _fileInstallService.InstallFromZipAsync(file, msg => StatusMessage = msg);
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
