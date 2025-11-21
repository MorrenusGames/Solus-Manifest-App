using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Tools.SteamAuthPro.Models;
using SolusManifestApp.Tools.SteamAuthPro.Views;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace SolusManifestApp.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SteamService _steamService;
        private readonly SettingsService _settingsService;
        private readonly ManifestApiService _manifestApiService;
        private readonly BackupService _backupService;
        private readonly CacheService _cacheService;
        private readonly NotificationService _notificationService;
        private readonly LuaInstallerViewModel _luaInstallerViewModel;
        private readonly SteamLibraryService _steamLibraryService;
        private readonly ThemeService _themeService;
        private readonly LoggerService _logger;
        private readonly UpdateService _updateService;

        [ObservableProperty]
        private AppSettings _settings;

        [ObservableProperty]
        private string _steamPath = string.Empty;

        [ObservableProperty]
        private string _apiKey = string.Empty;

        [ObservableProperty]
        private string _downloadsPath = string.Empty;

        [ObservableProperty]
        private bool _autoCheckUpdates;

        [ObservableProperty]
        private string _selectedAutoUpdateMode = "CheckOnly";

        [ObservableProperty]
        private bool _minimizeToTray;

        [ObservableProperty]
        private bool _autoInstallAfterDownload;

        [ObservableProperty]
        private bool _showNotifications;

        [ObservableProperty]
        private bool _startMinimized;

        [ObservableProperty]
        private bool _confirmBeforeDelete;

        [ObservableProperty]
        private bool _confirmBeforeUninstall;

        [ObservableProperty]
        private bool _alwaysShowTrayIcon;

        [ObservableProperty]
        private bool _autoUploadConfigKeys;

        [ObservableProperty]
        private bool _showGameAddedNotification;

        [ObservableProperty]
        private int _storePageSize;

        [ObservableProperty]
        private int _libraryPageSize;

        [ObservableProperty]
        private bool _rememberWindowPosition;

        [ObservableProperty]
        private double? _windowLeft;

        [ObservableProperty]
        private double? _windowTop;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private ObservableCollection<string> _apiKeyHistory = new();

        [ObservableProperty]
        private string? _selectedHistoryKey;

        [ObservableProperty]
        private long _cacheSize;

        [ObservableProperty]
        private bool _isSteamToolsMode;

        [ObservableProperty]
        private bool _isGreenLumaMode;

        [ObservableProperty]
        private bool _isDepotDownloaderMode;

        [ObservableProperty]
        private bool _isGreenLumaNormalMode;

        [ObservableProperty]
        private bool _isGreenLumaStealthAnyFolderMode;

        [ObservableProperty]
        private bool _isGreenLumaStealthUser32Mode;

        [ObservableProperty]
        private string _appListPath = string.Empty;

        [ObservableProperty]
        private string _dllInjectorPath = string.Empty;

        [ObservableProperty]
        private bool _useDefaultInstallLocation;

        [ObservableProperty]
        private ObservableCollection<string> _libraryFolders = new();

        [ObservableProperty]
        private string _selectedLibraryFolder = string.Empty;

        [ObservableProperty]
        private bool _isAdvancedNormalMode;

        [ObservableProperty]
        private string _selectedThemeName = "Default";

        [ObservableProperty]
        private bool _hasUnsavedChanges;

        private bool _isLoading; // Flag to prevent marking as unsaved during load

        // SteamAuth Pro properties
        [ObservableProperty]
        private ObservableCollection<string> _steamAuthProAccounts = new();

        [ObservableProperty]
        private int _steamAuthProActiveAccountIndex = -1;

        [ObservableProperty]
        private string _steamAuthProApiUrl = "https://drm.steam.run/ticket/create";

        [ObservableProperty]
        private string _steamAuthProPhpSessionId = string.Empty;

        [ObservableProperty]
        private string _steamAuthProTicketMethod = "GetETicket";

        private Config _steamAuthProConfig = null!;

        // Config VDF Extractor properties
        [ObservableProperty]
        private string _configVdfPath = string.Empty;

        [ObservableProperty]
        private string _combinedKeysPath = string.Empty;

        // DepotDownloader properties
        [ObservableProperty]
        private string _depotDownloaderOutputPath = string.Empty;

        [ObservableProperty]
        private string _steamUsername = string.Empty;

        [ObservableProperty]
        private bool _verifyFilesAfterDownload;

        [ObservableProperty]
        private int _maxConcurrentDownloads;

        public string CurrentVersion => _updateService.GetCurrentVersion();

        public bool ShowAdvancedNormalModeSettings => IsGreenLumaNormalMode && IsAdvancedNormalMode;

        // Mark as unsaved when properties change
        partial void OnSteamPathChanged(string value) => MarkAsUnsaved();
        partial void OnApiKeyChanged(string value) => MarkAsUnsaved();
        partial void OnDownloadsPathChanged(string value) => MarkAsUnsaved();
        partial void OnAutoCheckUpdatesChanged(bool value) => MarkAsUnsaved();
        partial void OnSelectedAutoUpdateModeChanged(string value) => MarkAsUnsaved();
        partial void OnMinimizeToTrayChanged(bool value) => MarkAsUnsaved();
        partial void OnAutoInstallAfterDownloadChanged(bool value) => MarkAsUnsaved();
        partial void OnShowNotificationsChanged(bool value) => MarkAsUnsaved();
        partial void OnShowGameAddedNotificationChanged(bool value) => MarkAsUnsaved();
        partial void OnStartMinimizedChanged(bool value) => MarkAsUnsaved();
        partial void OnAlwaysShowTrayIconChanged(bool value) => MarkAsUnsaved();
        partial void OnAutoUploadConfigKeysChanged(bool value) => MarkAsUnsaved();
        partial void OnConfirmBeforeDeleteChanged(bool value) => MarkAsUnsaved();
        partial void OnConfirmBeforeUninstallChanged(bool value) => MarkAsUnsaved();
        partial void OnStorePageSizeChanged(int value) => MarkAsUnsaved();
        partial void OnLibraryPageSizeChanged(int value) => MarkAsUnsaved();
        partial void OnRememberWindowPositionChanged(bool value) => MarkAsUnsaved();
        partial void OnWindowLeftChanged(double? value) => MarkAsUnsaved();
        partial void OnWindowTopChanged(double? value) => MarkAsUnsaved();
        partial void OnSelectedThemeNameChanged(string value) => MarkAsUnsaved();
        partial void OnUseDefaultInstallLocationChanged(bool value) => MarkAsUnsaved();
        partial void OnSelectedLibraryFolderChanged(string value) => MarkAsUnsaved();
        partial void OnDllInjectorPathChanged(string value) => MarkAsUnsaved();
        partial void OnSteamAuthProApiUrlChanged(string value) => MarkAsUnsaved();
        partial void OnSteamAuthProPhpSessionIdChanged(string value) => MarkAsUnsaved();
        partial void OnSteamAuthProActiveAccountIndexChanged(int value) => MarkAsUnsaved();
        partial void OnSteamAuthProTicketMethodChanged(string value) => MarkAsUnsaved();
        partial void OnConfigVdfPathChanged(string value) => MarkAsUnsaved();
        partial void OnCombinedKeysPathChanged(string value) => MarkAsUnsaved();
        partial void OnDepotDownloaderOutputPathChanged(string value) => MarkAsUnsaved();
        partial void OnSteamUsernameChanged(string value) => MarkAsUnsaved();
        partial void OnVerifyFilesAfterDownloadChanged(bool value) => MarkAsUnsaved();
        partial void OnMaxConcurrentDownloadsChanged(int value) => MarkAsUnsaved();

        private void MarkAsUnsaved()
        {
            if (!_isLoading)
            {
                HasUnsavedChanges = true;
            }
        }

        partial void OnIsSteamToolsModeChanged(bool value)
        {
            if (value)
            {
                IsGreenLumaMode = false;
                IsDepotDownloaderMode = false;
                Settings.Mode = ToolMode.SteamTools;
            }
            MarkAsUnsaved();
        }

        partial void OnIsGreenLumaModeChanged(bool value)
        {
            if (value)
            {
                IsSteamToolsMode = false;
                IsDepotDownloaderMode = false;
                Settings.Mode = ToolMode.GreenLuma;
            }
            MarkAsUnsaved();
        }

        partial void OnIsDepotDownloaderModeChanged(bool value)
        {
            if (value)
            {
                IsSteamToolsMode = false;
                IsGreenLumaMode = false;
                Settings.Mode = ToolMode.DepotDownloader;
            }
            MarkAsUnsaved();
        }

        partial void OnIsGreenLumaNormalModeChanged(bool value)
        {
            if (value)
            {
                IsGreenLumaStealthAnyFolderMode = false;
                IsGreenLumaStealthUser32Mode = false;
                Settings.GreenLumaSubMode = GreenLumaMode.Normal;

                // Auto-set DLLInjector path to {steampath}/DLLInjector.exe (unless advanced mode is enabled)
                if (!IsAdvancedNormalMode && !string.IsNullOrEmpty(Settings.SteamPath))
                {
                    DllInjectorPath = Path.Combine(Settings.SteamPath, "DLLInjector.exe");
                }
            }
            OnPropertyChanged(nameof(ShowAdvancedNormalModeSettings));
            MarkAsUnsaved();
        }

        partial void OnIsAdvancedNormalModeChanged(bool value)
        {
            if (!value && IsGreenLumaNormalMode)
            {
                // When unchecking advanced mode, reset to default path
                if (!string.IsNullOrEmpty(Settings.SteamPath))
                {
                    DllInjectorPath = Path.Combine(Settings.SteamPath, "DLLInjector.exe");
                }
            }
            OnPropertyChanged(nameof(ShowAdvancedNormalModeSettings));
            MarkAsUnsaved();
        }

        partial void OnIsGreenLumaStealthAnyFolderModeChanged(bool value)
        {
            if (value)
            {
                IsGreenLumaNormalMode = false;
                IsGreenLumaStealthUser32Mode = false;
                Settings.GreenLumaSubMode = GreenLumaMode.StealthAnyFolder;
            }
            MarkAsUnsaved();
        }

        partial void OnIsGreenLumaStealthUser32ModeChanged(bool value)
        {
            if (value)
            {
                IsGreenLumaNormalMode = false;
                IsGreenLumaStealthAnyFolderMode = false;
                Settings.GreenLumaSubMode = GreenLumaMode.StealthUser32;
            }
            MarkAsUnsaved();
        }

        public SettingsViewModel(
            SteamService steamService,
            SettingsService settingsService,
            ManifestApiService manifestApiService,
            BackupService backupService,
            CacheService cacheService,
            NotificationService notificationService,
            LuaInstallerViewModel luaInstallerViewModel,
            SteamLibraryService steamLibraryService,
            ThemeService themeService,
            LoggerService logger,
            UpdateService updateService)
        {
            _steamService = steamService;
            _settingsService = settingsService;
            _manifestApiService = manifestApiService;
            _backupService = backupService;
            _cacheService = cacheService;
            _notificationService = notificationService;
            _luaInstallerViewModel = luaInstallerViewModel;
            _steamLibraryService = steamLibraryService;
            _themeService = themeService;
            _logger = logger;
            _updateService = updateService;

            _settings = new AppSettings();
            LoadSettings();
            UpdateCacheSize();
        }

        [RelayCommand]
        private void LoadSettings()
        {
            _isLoading = true; // Prevent marking as unsaved during load

            Settings = _settingsService.LoadSettings();

            // Auto-detect Steam path if not set
            if (string.IsNullOrEmpty(Settings.SteamPath))
            {
                var detectedPath = _steamService.GetSteamPath();
                if (!string.IsNullOrEmpty(detectedPath))
                {
                    Settings.SteamPath = detectedPath;
                }
            }

            SteamPath = Settings.SteamPath;
            ApiKey = Settings.ApiKey;
            DownloadsPath = Settings.DownloadsPath;
            AutoCheckUpdates = Settings.AutoCheckUpdates;
            SelectedAutoUpdateMode = Settings.AutoUpdate.ToString();
            MinimizeToTray = Settings.MinimizeToTray;
            AutoInstallAfterDownload = Settings.AutoInstallAfterDownload;
            ShowNotifications = Settings.ShowNotifications;
            ShowGameAddedNotification = Settings.ShowGameAddedNotification;
            StartMinimized = Settings.StartMinimized;
            AlwaysShowTrayIcon = Settings.AlwaysShowTrayIcon;
            AutoUploadConfigKeys = Settings.AutoUploadConfigKeys;
            ConfirmBeforeDelete = Settings.ConfirmBeforeDelete;
            ConfirmBeforeUninstall = Settings.ConfirmBeforeUninstall;
            StorePageSize = Settings.StorePageSize;
            LibraryPageSize = Settings.LibraryPageSize;
            RememberWindowPosition = Settings.RememberWindowPosition;
            WindowLeft = Settings.WindowLeft;
            WindowTop = Settings.WindowTop;
            ApiKeyHistory = new ObservableCollection<string>(Settings.ApiKeyHistory);
            AppListPath = Settings.AppListPath;
            UseDefaultInstallLocation = Settings.UseDefaultInstallLocation;
            SelectedLibraryFolder = Settings.SelectedLibraryFolder;

            // Load library folders
            var folders = _steamLibraryService.GetLibraryFolders();
            LibraryFolders = new ObservableCollection<string>(folders);

            // Set default if none selected
            if (string.IsNullOrEmpty(SelectedLibraryFolder) && LibraryFolders.Any())
            {
                SelectedLibraryFolder = LibraryFolders.First();
            }

            // Set mode radio buttons
            IsSteamToolsMode = Settings.Mode == ToolMode.SteamTools;
            IsGreenLumaMode = Settings.Mode == ToolMode.GreenLuma;
            IsDepotDownloaderMode = Settings.Mode == ToolMode.DepotDownloader;

            // Set GreenLuma sub-mode radio buttons
            IsGreenLumaNormalMode = Settings.GreenLumaSubMode == GreenLumaMode.Normal;
            IsGreenLumaStealthAnyFolderMode = Settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder;
            IsGreenLumaStealthUser32Mode = Settings.GreenLumaSubMode == GreenLumaMode.StealthUser32;

            // Set theme
            SelectedThemeName = Settings.Theme.ToString();

            // Auto-set DLLInjector path based on mode
            if (Settings.GreenLumaSubMode == GreenLumaMode.Normal)
            {
                // Normal mode: Always use {SteamPath}/DLLInjector.exe
                if (!string.IsNullOrEmpty(Settings.SteamPath))
                {
                    DllInjectorPath = Path.Combine(Settings.SteamPath, "DLLInjector.exe");
                    Settings.DLLInjectorPath = DllInjectorPath;
                }
            }
            else if (Settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder)
            {
                // Stealth Any Folder: Use saved path
                DllInjectorPath = Settings.DLLInjectorPath;

                // Auto-set AppListPath to {DLLInjectorPath directory}/AppList
                if (!string.IsNullOrEmpty(DllInjectorPath))
                {
                    var injectorDir = Path.GetDirectoryName(DllInjectorPath);
                    if (!string.IsNullOrEmpty(injectorDir))
                    {
                        AppListPath = Path.Combine(injectorDir, "AppList");
                        Settings.AppListPath = AppListPath;
                    }
                }
            }
            else
            {
                // Stealth User32: No custom paths needed
                DllInjectorPath = Settings.DLLInjectorPath;
            }

            // Load SteamAuth Pro settings
            _steamAuthProConfig = Config.Load();
            SteamAuthProApiUrl = _steamAuthProConfig.ApiUrl;
            SteamAuthProPhpSessionId = _steamAuthProConfig.PhpSessionId;
            SteamAuthProTicketMethod = _steamAuthProConfig.TicketMethod.ToString();
            LoadSteamAuthProAccounts();

            // Load Config VDF Extractor settings
            ConfigVdfPath = Settings.ConfigVdfPath;
            CombinedKeysPath = Settings.CombinedKeysPath;

            // Load DepotDownloader settings
            DepotDownloaderOutputPath = Settings.DepotDownloaderOutputPath;
            SteamUsername = Settings.SteamUsername;
            VerifyFilesAfterDownload = Settings.VerifyFilesAfterDownload;
            MaxConcurrentDownloads = Settings.MaxConcurrentDownloads;

            _isLoading = false;
            HasUnsavedChanges = false; // Clear unsaved changes flag after load

            StatusMessage = "Settings loaded";
        }

        [RelayCommand]
        private void SaveSettings()
        {
            Settings.SteamPath = SteamPath;
            Settings.ApiKey = ApiKey;
            Settings.DownloadsPath = DownloadsPath;
            Settings.AutoCheckUpdates = AutoCheckUpdates;

            // Parse and save auto-update mode
            if (Enum.TryParse<AutoUpdateMode>(SelectedAutoUpdateMode, out var autoUpdateMode))
            {
                Settings.AutoUpdate = autoUpdateMode;
            }

            Settings.MinimizeToTray = MinimizeToTray;
            Settings.AutoInstallAfterDownload = AutoInstallAfterDownload;
            Settings.ShowNotifications = ShowNotifications;
            Settings.ShowGameAddedNotification = ShowGameAddedNotification;
            Settings.StartMinimized = StartMinimized;
            Settings.AlwaysShowTrayIcon = AlwaysShowTrayIcon;
            Settings.AutoUploadConfigKeys = AutoUploadConfigKeys;
            Settings.ConfirmBeforeDelete = ConfirmBeforeDelete;
            Settings.ConfirmBeforeUninstall = ConfirmBeforeUninstall;
            Settings.StorePageSize = StorePageSize;
            Settings.LibraryPageSize = LibraryPageSize;
            Settings.RememberWindowPosition = RememberWindowPosition;
            Settings.WindowLeft = WindowLeft;
            Settings.WindowTop = WindowTop;
            Settings.AppListPath = AppListPath;
            Settings.DLLInjectorPath = DllInjectorPath;
            Settings.UseDefaultInstallLocation = UseDefaultInstallLocation;
            Settings.SelectedLibraryFolder = SelectedLibraryFolder;

            // Parse and save theme
            if (Enum.TryParse<AppTheme>(SelectedThemeName, out var theme))
            {
                Settings.Theme = theme;
            }

            // Save SteamAuth Pro settings
            _steamAuthProConfig.ApiUrl = SteamAuthProApiUrl;
            _steamAuthProConfig.PhpSessionId = SteamAuthProPhpSessionId;
            if (Enum.TryParse<TicketDumpMethod>(SteamAuthProTicketMethod, out var ticketMethod))
            {
                _steamAuthProConfig.TicketMethod = ticketMethod;
            }
            _steamAuthProConfig.Save();

            // Save Config VDF Extractor settings
            Settings.ConfigVdfPath = ConfigVdfPath;
            Settings.CombinedKeysPath = CombinedKeysPath;

            // Save DepotDownloader settings
            Settings.DepotDownloaderOutputPath = DepotDownloaderOutputPath;
            Settings.SteamUsername = SteamUsername;
            Settings.VerifyFilesAfterDownload = VerifyFilesAfterDownload;
            Settings.MaxConcurrentDownloads = MaxConcurrentDownloads;

            try
            {
                _settingsService.SaveSettings(Settings);
                _steamService.SetCustomSteamPath(SteamPath);

                // Apply theme
                _themeService.ApplyTheme(Settings.Theme);

                // Refresh mode on Installer page
                _luaInstallerViewModel.RefreshMode();

                HasUnsavedChanges = false; // Clear unsaved changes flag after successful save
                StatusMessage = "Settings saved successfully!";
                _notificationService.ShowSuccess("Settings saved successfully!");
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _notificationService.ShowError($"Failed to save settings: {ex.Message}");
            }
        }

        [RelayCommand]
        private void BrowseSteamPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Steam.exe",
                Filter = "Steam Executable|steam.exe|All Files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                var path = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(path) && _steamService.ValidateSteamPath(path))
                {
                    SteamPath = path;
                    StatusMessage = "Steam path updated";

                    // Refresh library folders
                    var folders = _steamLibraryService.GetLibraryFolders();
                    LibraryFolders = new ObservableCollection<string>(folders);
                    if (LibraryFolders.Any())
                    {
                        SelectedLibraryFolder = LibraryFolders.First();
                    }
                }
                else
                {
                    _notificationService.ShowError("Invalid Steam installation path");
                }
            }
        }

        [RelayCommand]
        private void BrowseDownloadsPath()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Downloads Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                DownloadsPath = dialog.FolderName;
                Directory.CreateDirectory(DownloadsPath);
                StatusMessage = "Downloads path updated";
            }
        }

        [RelayCommand]
        private void BrowseAppListPath()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select AppList Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                AppListPath = dialog.FolderName;
                Directory.CreateDirectory(AppListPath);
                StatusMessage = "AppList path updated";
            }
        }

        [RelayCommand]
        private void BrowseDLLInjectorPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select DLLInjector.exe",
                Filter = "DLLInjector|DLLInjector.exe|Executable Files|*.exe|All Files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                DllInjectorPath = dialog.FileName;

                // Auto-set AppListPath to {DLLInjectorPath directory}/AppList for StealthAnyFolder mode
                if (Settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder)
                {
                    var injectorDir = Path.GetDirectoryName(DllInjectorPath);
                    if (!string.IsNullOrEmpty(injectorDir))
                    {
                        AppListPath = Path.Combine(injectorDir, "AppList");
                    }
                }

                StatusMessage = "DLLInjector path updated";
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ValidateApiKey()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                _notificationService.ShowWarning("Please enter an API key");
                return;
            }

            if (!_manifestApiService.ValidateApiKey(ApiKey))
            {
                _notificationService.ShowWarning("API key must start with 'smm'");
                return;
            }

            StatusMessage = "Testing API key...";

            try
            {
                var isValid = await _manifestApiService.TestApiKeyAsync(ApiKey);

                if (isValid)
                {
                    StatusMessage = "API key is valid";
                    _notificationService.ShowSuccess("API key is valid!");

                    // Save current API key before refreshing
                    var currentApiKey = ApiKey;
                    _settingsService.AddApiKeyToHistory(currentApiKey);

                    // Reload settings and restore the current API key
                    LoadSettings();
                    ApiKey = currentApiKey;
                }
                else
                {
                    StatusMessage = "API key is invalid";
                    _notificationService.ShowError("API key is invalid or expired");
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _notificationService.ShowError($"Failed to validate API key: {ex.Message}");
            }
        }

        [RelayCommand]
        private void DetectSteam()
        {
            var path = _steamService.GetSteamPath();

            if (!string.IsNullOrEmpty(path))
            {
                SteamPath = path;
                StatusMessage = "Steam detected successfully";
                _notificationService.ShowSuccess($"Steam found at: {path}");

                // Refresh library folders
                var folders = _steamLibraryService.GetLibraryFolders();
                LibraryFolders = new ObservableCollection<string>(folders);
                if (LibraryFolders.Any())
                {
                    SelectedLibraryFolder = LibraryFolders.First();
                }
            }
            else
            {
                StatusMessage = "Steam not found";
                _notificationService.ShowWarning("Could not detect Steam installation.\n\nPlease select Steam path manually.");
            }
        }

        [RelayCommand]
        private void UseHistoryKey()
        {
            if (!string.IsNullOrEmpty(SelectedHistoryKey))
            {
                ApiKey = SelectedHistoryKey;
                StatusMessage = "API key loaded from history";
            }
        }

        [RelayCommand]
        private void RemoveHistoryKey()
        {
            if (!string.IsNullOrEmpty(SelectedHistoryKey))
            {
                Settings.ApiKeyHistory.Remove(SelectedHistoryKey);
                _settingsService.SaveSettings(Settings);
                ApiKeyHistory.Remove(SelectedHistoryKey);
                StatusMessage = "API key removed from history";
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task CreateBackup()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Backup",
                Filter = "JSON Files|*.json",
                FileName = $"SolusBackup_{System.DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusMessage = "Creating backup...";
                    var backupPath = await _backupService.CreateBackupAsync(Path.GetDirectoryName(dialog.FileName)!);
                    StatusMessage = "Backup created successfully";
                    _notificationService.ShowSuccess($"Backup created: {Path.GetFileName(backupPath)}");
                }
                catch (System.Exception ex)
                {
                    StatusMessage = $"Backup failed: {ex.Message}";
                    _notificationService.ShowError($"Failed to create backup: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task RestoreBackup()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Backup File",
                Filter = "JSON Files|*.json|All Files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusMessage = "Loading backup...";
                    var backup = await _backupService.LoadBackupAsync(dialog.FileName);

                    var result = MessageBoxHelper.Show(
                        $"Backup Date: {backup.BackupDate}\n" +
                        $"Lua: {backup.InstalledModAppIds.Count}\n\n" +
                        $"Restore settings and lua list?",
                        "Restore Backup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        var restoreResult = await _backupService.RestoreBackupAsync(backup, true);
                        StatusMessage = restoreResult.Message;

                        if (restoreResult.Success)
                        {
                            LoadSettings();
                            _notificationService.ShowSuccess(restoreResult.Message);
                        }
                        else
                        {
                            _notificationService.ShowError(restoreResult.Message);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    StatusMessage = $"Restore failed: {ex.Message}";
                    _notificationService.ShowError($"Failed to restore backup: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void ClearCache()
        {
            var result = MessageBoxHelper.Show(
                "This will delete all cached icons and data.\n\nContinue?",
                "Clear Cache",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _cacheService.ClearAllCache();
                UpdateCacheSize();
                _notificationService.ShowSuccess("Cache cleared successfully");
                _logger.Info("User cleared cache from settings");
            }
        }

        [RelayCommand]
        private void ClearLogs()
        {
            var result = MessageBoxHelper.Show(
                "This will delete all old log files (except the current session log).\n\nContinue?",
                "Clear Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _logger.ClearOldLogs();
                _notificationService.ShowSuccess("Old logs cleared successfully");
                _logger.Info("User cleared old logs from settings");
            }
        }

        [RelayCommand]
        private void SteamAuthProAutoDetect()
        {
            var steamAccounts = SteamAccountManager.GetSteamAccounts();

            if (steamAccounts.Count == 0)
            {
                MessageBoxHelper.Show("No Steam accounts detected. Make sure Steam is installed and you have logged in accounts.",
                    "No Accounts Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int addedCount = 0;
            foreach (var kvp in steamAccounts)
            {
                var steamId = kvp.Key;
                var steamAccount = kvp.Value;

                // Check if account already exists by SteamId
                var exists = _steamAuthProConfig.Accounts.Any(a => a.SteamId == steamId);

                if (!exists)
                {
                    var displayName = string.IsNullOrEmpty(steamAccount.PersonaName)
                        ? $"{steamAccount.AccountName} → {steamId}"
                        : $"{steamAccount.PersonaName} → {steamAccount.AccountName}";

                    _steamAuthProConfig.AddAccount(displayName, steamId);
                    addedCount++;
                }
            }

            LoadSteamAuthProAccounts();

            if (addedCount > 0)
            {
                MessageBoxHelper.Show($"Added {addedCount} Steam account(s).",
                    "Auto-Detect Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBoxHelper.Show("All detected Steam accounts are already in the list.",
                    "Auto-Detect Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private void SteamAuthProAddAccount()
        {
            var dialog = new InputDialog("Add Account", "Account Name:", "")
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                var name = dialog.Result;
                if (string.IsNullOrWhiteSpace(name))
                    return;

                _steamAuthProConfig.AddAccount(name.Trim());
                LoadSteamAuthProAccounts();
            }
        }

        [RelayCommand]
        private void SteamAuthProRemoveAccount()
        {
            if (SteamAuthProActiveAccountIndex == -1)
            {
                MessageBoxHelper.Show("Please select an account to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBoxHelper.Show("Are you sure you want to remove this account?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _steamAuthProConfig.RemoveAccount(SteamAuthProActiveAccountIndex);
                LoadSteamAuthProAccounts();
            }
        }

        [RelayCommand]
        private void SteamAuthProSetActive()
        {
            if (SteamAuthProActiveAccountIndex == -1)
            {
                MessageBoxHelper.Show("Please select an account to set as active.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _steamAuthProConfig.SetActiveAccount(SteamAuthProActiveAccountIndex);
            LoadSteamAuthProAccounts();
        }

        private void LoadSteamAuthProAccounts()
        {
            SteamAuthProAccounts.Clear();
            for (int i = 0; i < _steamAuthProConfig.Accounts.Count; i++)
            {
                var account = _steamAuthProConfig.Accounts[i];
                var isActive = i == _steamAuthProConfig.ActiveAccount ? " [ACTIVE]" : "";
                SteamAuthProAccounts.Add($"{account.Name}{isActive}");
            }
        }

        [RelayCommand]
        private void BrowseConfigVdf()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "VDF files (*.vdf)|*.vdf|All files (*.*)|*.*",
                Title = "Select config.vdf file"
            };

            if (!string.IsNullOrEmpty(ConfigVdfPath) && File.Exists(ConfigVdfPath))
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(ConfigVdfPath);
                openFileDialog.FileName = Path.GetFileName(ConfigVdfPath);
            }

            if (openFileDialog.ShowDialog() == true)
            {
                ConfigVdfPath = openFileDialog.FileName;
            }
        }

        [RelayCommand]
        private void BrowseCombinedKeys()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Key files (*.key)|*.key|All files (*.*)|*.*",
                Title = "Select combinedkeys.key file"
            };

            if (!string.IsNullOrEmpty(CombinedKeysPath) && File.Exists(CombinedKeysPath))
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(CombinedKeysPath);
                openFileDialog.FileName = Path.GetFileName(CombinedKeysPath);
            }

            if (openFileDialog.ShowDialog() == true)
            {
                CombinedKeysPath = openFileDialog.FileName;
            }
        }

        [RelayCommand]
        private void BrowseDepotOutputPath()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select DepotDownloader Output Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                DepotDownloaderOutputPath = dialog.FolderName;
                Directory.CreateDirectory(DepotDownloaderOutputPath);
                StatusMessage = "DepotDownloader output path updated";
            }
        }

        private void UpdateCacheSize()
        {
            CacheSize = _cacheService.GetCacheSize();
        }

        public string GetCacheSizeFormatted()
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = CacheSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task CheckForUpdates()
        {
            try
            {
                StatusMessage = "Checking for updates...";
                var (hasUpdate, updateInfo) = await _updateService.CheckForUpdatesAsync();

                if (hasUpdate && updateInfo != null)
                {
                    var result = MessageBoxHelper.Show(
                        $"A new version ({updateInfo.TagName}) is available!\n\nWould you like to download and install it now?\n\nCurrent version: {_updateService.GetCurrentVersion()}",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        StatusMessage = "Downloading update...";
                        _notificationService.ShowNotification("Downloading Update", "Downloading the latest version...", NotificationType.Info);

                        var updatePath = await _updateService.DownloadUpdateAsync(updateInfo);

                        if (!string.IsNullOrEmpty(updatePath))
                        {
                            var installResult = MessageBoxHelper.Show(
                                "Update downloaded successfully!\n\nThe app will now restart to install the update.",
                                "Update Ready",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            _updateService.InstallUpdate(updatePath);
                        }
                        else
                        {
                            StatusMessage = "Failed to download update";
                            _notificationService.ShowError("Failed to download update. Please try again later.", "Update Failed");
                        }
                    }
                    else
                    {
                        StatusMessage = "Update cancelled";
                    }
                }
                else
                {
                    StatusMessage = "You're up to date!";
                    _notificationService.ShowSuccess($"You have the latest version ({_updateService.GetCurrentVersion()})");
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Update check failed: {ex.Message}";
                _notificationService.ShowError($"An error occurred while checking for updates: {ex.Message}", "Update Error");
            }
        }
    }
}
