using SolusManifestApp.Services;
using System;
using System.Windows;
using System.Windows.Controls;

namespace SolusManifestApp.Tools.CloudSaveFix
{
    public partial class CloudSaveFixControl : UserControl
    {
        private readonly CloudSaveFixService _service;
        private readonly SettingsService _settingsService;
        private string _steamPath;
        private bool _initialized;

        public CloudSaveFixControl()
        {
            InitializeComponent();

            _service = new CloudSaveFixService();
            _service.OnLog += msg => Dispatcher.Invoke(() => AppendLog(msg));
            _settingsService = new SettingsService();

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                DetectSteam();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                AppendLog($"Init error: {ex}");
            }
        }

        private void DetectSteam()
        {
            try
            {
                var steamSvc = new SteamService(_settingsService);
                _steamPath = steamSvc.GetSteamPath();
            }
            catch { }

            if (string.IsNullOrEmpty(_steamPath))
            {
                TxtSteamPath.Text = "Steam installation not found";
                TxtStatus.Text = "Steam Not Found";
                BtnApply.IsEnabled = false;
                BtnRestore.IsEnabled = false;
                return;
            }

            TxtSteamPath.Text = _steamPath;
        }

        private void RefreshStatus()
        {
            if (string.IsNullOrEmpty(_steamPath))
                return;

            try
            {
                var state = _service.GetPatchState(_steamPath);
                switch (state)
                {
                    case PatchState.NotInstalled:
                        TxtStatus.Text = "SteamTools Not Installed";
                        BtnApply.IsEnabled = false;
                        BtnRestore.IsEnabled = false;
                        break;
                    case PatchState.Unpatched:
                        TxtStatus.Text = "Not Active";
                        BtnApply.IsEnabled = true;
                        BtnRestore.IsEnabled = false;
                        break;
                    case PatchState.Patched:
                        TxtStatus.Text = "Active";
                        BtnApply.IsEnabled = false;
                        BtnRestore.IsEnabled = true;
                        break;
                    case PatchState.PartiallyPatched:
                        TxtStatus.Text = "Partially Applied";
                        BtnApply.IsEnabled = true;
                        BtnRestore.IsEnabled = true;
                        break;
                    case PatchState.UnknownVersion:
                        TxtStatus.Text = "Unknown SteamTools Version";
                        BtnApply.IsEnabled = false;
                        BtnRestore.IsEnabled = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error checking status";
                AppendLog($"Status check failed: {ex.Message}");
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_steamPath)) return;

            BtnApply.IsEnabled = false;
            BtnRestore.IsEnabled = false;
            TxtLog.Clear();

            try
            {
                var result = _service.Apply(_steamPath);
                if (!result.Succeeded && !string.IsNullOrEmpty(result.Error))
                    AppendLog($"Failed: {result.Error}");
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
            }

            RefreshStatus();
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_steamPath)) return;

            BtnApply.IsEnabled = false;
            BtnRestore.IsEnabled = false;
            TxtLog.Clear();

            try
            {
                var result = _service.Restore(_steamPath);
                if (!result.Succeeded && !string.IsNullOrEmpty(result.Error))
                    AppendLog($"Failed: {result.Error}");
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
            }

            RefreshStatus();
        }

        private void AppendLog(string message)
        {
            TxtLog.AppendText(message + Environment.NewLine);
            TxtLog.ScrollToEnd();
        }
    }
}
