using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using System;
using System.IO;

namespace SolusManifestApp.ViewModels
{
    public partial class HomeViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly SteamService _steamService;
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private string _currentModeText = string.Empty;

        [ObservableProperty]
        private string _currentModeDescription = string.Empty;

        public HomeViewModel(
            SettingsService settingsService,
            SteamService steamService,
            NotificationService notificationService)
        {
            _settingsService = settingsService;
            _steamService = steamService;
            _notificationService = notificationService;

            RefreshMode();
        }

        public void RefreshMode()
        {
            var settings = _settingsService.LoadSettings();

            if (settings.Mode == ToolMode.SteamTools)
            {
                CurrentModeText = "Current Mode: SteamTools";
                CurrentModeDescription = "SteamTools mode: Standard download mode with .lua files installed to stplug-in folder. Use this mode for Steam game and depot management.";
            }
            else if (settings.Mode == ToolMode.DepotDownloader)
            {
                CurrentModeText = "Current Mode: DepotDownloader";
                CurrentModeDescription = "DepotDownloader mode: Download actual game files with smart language/platform filtering. Files are downloaded directly to your specified output folder.";
            }
            else
            {
                CurrentModeText = "Current Mode: Unknown";
                CurrentModeDescription = "No mode selected. Please configure your tool mode in Settings.";
            }
        }

        [RelayCommand]
        private void LaunchSteam()
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();

                if (string.IsNullOrEmpty(steamPath))
                {
                    _notificationService.ShowError("Steam path is not configured. Please set it in Settings.");
                    return;
                }

                var steamExePath = Path.Combine(steamPath, "steam.exe");

                if (!File.Exists(steamExePath))
                {
                    _notificationService.ShowError($"Steam.exe not found at: {steamExePath}\n\nPlease check your Steam path in Settings.");
                    return;
                }

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = steamExePath,
                    UseShellExecute = true,
                    WorkingDirectory = steamPath
                };

                System.Diagnostics.Process.Start(processInfo);
                _notificationService.ShowSuccess("Steam launched successfully!");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to launch Steam: {ex.Message}");
            }
        }
    }
}
