using SolusManifestApp.ViewModels;
using SolusManifestApp.Services;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SolusManifestApp.Views
{
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;

        public MainWindow(MainViewModel viewModel, SettingsService settingsService)
        {
            InitializeComponent();
            DataContext = viewModel;
            _settingsService = settingsService;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            SourceInitialized += MainWindow_SourceInitialized;

            // Restore window size
            var settings = _settingsService.LoadSettings();
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Update check is now handled by App.xaml.cs based on AutoUpdate settings
            // No need to check here on every startup
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save window size
            var settings = _settingsService.LoadSettings();
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            _settingsService.SaveSettings(settings);

            // Check if we should minimize to tray instead of closing
            if (settings.MinimizeToTray)
            {
                e.Cancel = true;
                var app = Application.Current as App;
                var trayService = app?.GetTrayIconService();
                trayService?.ShowInTray();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if we should minimize to tray instead of closing
            var settings = _settingsService.LoadSettings();
            if (settings.MinimizeToTray)
            {
                var app = Application.Current as App;
                var trayService = app?.GetTrayIconService();
                trayService?.ShowInTray();
            }
            else
            {
                Close();
            }
        }

        private void MainWindow_SourceInitialized(object? sender, System.EventArgs e)
        {
            // Fix for maximize issue - adjust max size to work area
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            }
        }

        private void MainWindow_StateChanged(object? sender, System.EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Adjust for taskbar when maximized
                var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                MaxHeight = screen.WorkingArea.Height + 16; // +16 accounts for border
                MaxWidth = screen.WorkingArea.Width + 16;
            }
            else
            {
                MaxHeight = double.PositiveInfinity;
                MaxWidth = double.PositiveInfinity;
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;

            if (msg == WM_GETMINMAXINFO)
            {
                // Handle window maximize to respect work area
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (screen != null)
                {
                    var workArea = screen.WorkingArea;
                    var monitorArea = screen.Bounds;

                    unsafe
                    {
                        var mmi = (MINMAXINFO*)lParam;
                        mmi->ptMaxPosition.x = workArea.Left - monitorArea.Left;
                        mmi->ptMaxPosition.y = workArea.Top - monitorArea.Top;
                        mmi->ptMaxSize.x = workArea.Width;
                        mmi->ptMaxSize.y = workArea.Height;
                    }
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private unsafe struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }
    }
}
