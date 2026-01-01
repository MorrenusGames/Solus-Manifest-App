using System;
using System.Collections.Generic;

namespace SolusManifestApp.Models
{
    public enum ToolMode
    {
        SteamTools,
        DepotDownloader
    }

    public enum AppTheme
    {
        Default,
        Dark,
        Light,
        Cherry,
        Sunset,
        Forest,
        Grape,
        Cyberpunk
    }

    public enum AutoUpdateMode
    {
        Disabled,
        CheckOnly,
        AutoDownloadAndInstall
    }

    public class AppSettings
    {
        // API & Authentication
        public string ApiKey { get; set; } = string.Empty;
        public List<string> ApiKeyHistory { get; set; } = new List<string>();

        // Steam Configuration
        public string SteamPath { get; set; } = string.Empty;
        public ToolMode Mode { get; set; } = ToolMode.SteamTools;

        // Downloads & Installation
        public string DownloadsPath { get; set; } = string.Empty;
        public bool AutoInstallAfterDownload { get; set; } = false;
        public bool DeleteZipAfterInstall { get; set; } = true;

        // Key Upload
        public bool AutoUploadConfigKeys { get; set; } = true;
        public DateTime LastConfigKeysUpload { get; set; } = DateTime.MinValue;

        // Application Behavior
        public bool MinimizeToTray { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
        public bool ConfirmBeforeDelete { get; set; } = true;
        public bool ConfirmBeforeUninstall { get; set; } = true;
        public bool AlwaysShowTrayIcon { get; set; } = false;

        // Display & Interface
        public AppTheme Theme { get; set; } = AppTheme.Default;
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 800;
        public int StorePageSize { get; set; } = 20;
        public int LibraryPageSize { get; set; } = 20;
        public bool RememberWindowPosition { get; set; } = true;
        public double? WindowLeft { get; set; } = null;
        public double? WindowTop { get; set; } = null;

        // Auto-Update
        public bool AutoCheckUpdates { get; set; } = true; // Legacy - kept for compatibility
        public AutoUpdateMode AutoUpdate { get; set; } = AutoUpdateMode.CheckOnly;

        // Config VDF Extractor
        public string ConfigVdfPath { get; set; } = string.Empty;
        public string CombinedKeysPath { get; set; } = string.Empty;

        // DepotDownloader Configuration
        public string DepotDownloaderOutputPath { get; set; } = string.Empty;
        public string SteamUsername { get; set; } = string.Empty;
        public bool VerifyFilesAfterDownload { get; set; } = true;
        public int MaxConcurrentDownloads { get; set; } = 8;

        // GBE Token Generator Configuration
        public string GBETokenOutputPath { get; set; } = string.Empty;
        public string GBESteamWebApiKey { get; set; } = string.Empty;

        // Notification Preferences
        public bool DisableAllNotifications { get; set; } = false;
        public bool ShowGameAddedNotification { get; set; } = true;

        // View Mode Preferences
        public bool StoreListView { get; set; } = false; // false = grid, true = list
        public bool LibraryListView { get; set; } = false; // false = grid, true = list
    }
}
