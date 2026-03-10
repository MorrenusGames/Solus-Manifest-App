using System;
using System.Windows.Controls;
using System.Windows.Threading;
using SolusManifestApp.Services;

namespace SolusManifestApp.Tools.CloudFix
{
    public partial class CloudFixControl : UserControl
    {
        private readonly CloudFixViewModel _viewModel;
        private bool _initialized;

        public CloudFixControl()
        {
            InitializeComponent();

            var settingsService = new SettingsService();
            var steamService = new SteamService(settingsService);
            var gamesService = new SteamGamesService(steamService);

            _viewModel = new CloudFixViewModel(gamesService, steamService);
            DataContext = _viewModel;

            Loaded += OnLoaded;
            Dispatcher.ShutdownStarted += OnShutdown;
        }

        private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                _viewModel.AddLog($"Initialization failed: {ex.Message}");
            }
        }

        private void OnShutdown(object? sender, EventArgs e)
        {
            _viewModel.Cleanup();
        }
    }
}
