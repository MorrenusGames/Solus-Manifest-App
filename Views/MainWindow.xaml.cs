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

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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
                var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                MaxHeight = (screen.WorkingArea.Height / dpiScale) + 14;
                MaxWidth = (screen.WorkingArea.Width / dpiScale) + 14;
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
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (screen != null)
                {
                    var workArea = screen.WorkingArea;
                    var monitorArea = screen.Bounds;

                    var source = PresentationSource.FromVisual(this);
                    double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                    int minWidth = (int)(MinWidth * dpiScale);
                    int minHeight = (int)(MinHeight * dpiScale);

                    unsafe
                    {
                        var mmi = (MINMAXINFO*)lParam;
                        mmi->ptMaxPosition.x = workArea.Left - monitorArea.Left;
                        mmi->ptMaxPosition.y = workArea.Top - monitorArea.Top;
                        mmi->ptMaxSize.x = workArea.Width;
                        mmi->ptMaxSize.y = workArea.Height;
                        mmi->ptMinTrackSize.x = minWidth;
                        mmi->ptMinTrackSize.y = minHeight;
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
