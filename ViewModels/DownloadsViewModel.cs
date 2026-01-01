using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SolusManifestApp.ViewModels
{
    public partial class DownloadsViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly DownloadService _downloadService;
        private readonly FileInstallService _fileInstallService;
        private readonly SettingsService _settingsService;
        private readonly DepotDownloadService _depotDownloadService;
        private readonly SteamService _steamService;
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
            NotificationService notificationService,
            LibraryRefreshService libraryRefreshService)
        {
            _downloadService = downloadService;
            _fileInstallService = fileInstallService;
            _settingsService = settingsService;
            _depotDownloadService = depotDownloadService;
            _steamService = steamService;
            _notificationService = notificationService;
            _libraryRefreshService = libraryRefreshService;
            _logger = new LoggerService("DownloadsView");

            ActiveDownloads = _downloadService.ActiveDownloads;

            RefreshDownloadedFiles();

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

                if (settings.Mode == ToolMode.DepotDownloader)
                {
                    _logger.Info("=== Starting DepotDownloader Info Gathering Phase ===");
                    _logger.Info($"App ID: {appId}");
                    _logger.Info($"Zip file: {fileName}");

                    StatusMessage = "Extracting depot information from lua file...";
                    var luaContent = _downloadService.ExtractLuaContentFromZip(filePath, appId);
                    _logger.Info($"Lua content extracted successfully ({luaContent.Length} characters)");

                    var luaParserForTokens = new LuaParser();
                    var picsTokens = luaParserForTokens.ParseTokens(luaContent);
                    if (picsTokens.Count > 0)
                    {
                        var mainToken = picsTokens.FirstOrDefault(t => t.AppId == appId);
                        if (mainToken != default && ulong.TryParse(mainToken.Token, out ulong tokenValue))
                        {
                            DepotDownloader.TokenCFG.useAppToken = true;
                            DepotDownloader.TokenCFG.appToken = tokenValue;
                            _logger.Info($"Set PICS token for app {appId}: {tokenValue}");
                        }
                    }

                    var depotFilterService = new DepotFilterService(new LoggerService("DepotFilter"));
                    var parsedDepotKeys = depotFilterService.ExtractDepotKeysFromLua(luaContent);

                    if (parsedDepotKeys.Count == 0)
                    {
                        MessageBoxHelper.Show(
                            "No depot keys found in the lua file. Cannot proceed with download.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
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
                        MessageBoxHelper.Show(
                            "Failed to connect to Steam. Please check your internet connection and try again.",
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
                        MessageBoxHelper.Show(
                            $"Failed to fetch depot information for app {appId} from Steam.",
                            "Failed to Fetch App Info",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
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

                    List<string> filteredDepotIds;
                    if (languageDialog.SelectedLanguage == "All (Skip Filter)")
                    {
                        filteredDepotIds = parsedDepotKeys.Keys.ToList();
                        StatusMessage = $"Using all {filteredDepotIds.Count} depots from Lua file...";
                    }
                    else
                    {
                        StatusMessage = $"Filtering depots for language: {languageDialog.SelectedLanguage}...";
                        filteredDepotIds = depotFilterService.GetDepotsForLanguage(
                            steamCmdData,
                            parsedDepotKeys,
                            languageDialog.SelectedLanguage,
                            appId);

                        if (filteredDepotIds.Count == 0)
                        {
                            _notificationService.ShowWarning("Language filter returned no depots. Showing all available depots.");
                            filteredDepotIds = parsedDepotKeys.Keys.ToList();
                        }
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
                        MessageBoxHelper.Show(
                            "DepotDownloader output path not configured. Please set it in Settings.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Output path not set";
                        IsInstalling = false;
                        return;
                    }

                    StatusMessage = "Extracting manifest files...";
                    var manifestFiles = _downloadService.ExtractManifestFilesFromZip(filePath, appId);

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

                    StatusMessage = "Starting download...";
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
                    _notificationService.ShowSuccess($"Download started for {gameName}!\n\nCheck the Downloads tab to monitor progress.", "Download Started");

                    StatusMessage = "Download started - check progress below";

                    if (settings.DeleteZipAfterInstall)
                    {
                        File.Delete(filePath);
                        RefreshDownloadedFiles();
                    }

                    IsInstalling = false;
                    return;
                }

                StatusMessage = $"Installing files...";
                await _fileInstallService.InstallFromZipAsync(filePath, message => StatusMessage = message);

                _notificationService.ShowSuccess($"{fileName} has been installed successfully! Restart Steam for changes to take effect.", "Installation Complete");
                StatusMessage = $"{fileName} installed successfully";

                _libraryRefreshService.NotifyGameInstalled(appId, false);

                if (settings.DeleteZipAfterInstall)
                {
                    File.Delete(filePath);
                    RefreshDownloadedFiles();
                }
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
                _downloadService.DownloadCompleted -= OnDownloadCompleted;
            }

            _disposed = true;
        }
    }
}
