using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SolusManifestApp.Services;
using Newtonsoft.Json.Linq;
using QRCoder;
using SteamKit2;

namespace SolusManifestApp.Tools.DepotDumper
{
    public partial class DepotDumperControl : UserControl
    {
        private Steam3Session? steam3;
        private CancellationTokenSource? cancellationTokenSource;
        private bool isDumping = false;
        private List<string> generatedFiles = new List<string>();
        private readonly SettingsService _settingsService;
        private StreamWriter? _logFile;
        private readonly string _logFilePath;

        public bool ShowLoginView { get; set; } = true;

        public DepotDumperControl()
        {
            InitializeComponent();
            DataContext = this;
            _settingsService = new SettingsService();
            string outputDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            _logFilePath = Path.Combine(outputDir, "depot_dumper.log");
        }

        #region Login Events

        private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                PasswordBox.Focus();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SignInButton_Click(sender, e);
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                MessageBox.Show("Please enter a username.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                MessageBox.Show("Please enter a password.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StartDumping(UsernameTextBox.Text, PasswordBox.Password, false);
        }

        private void QrCodeButton_Click(object sender, RoutedEventArgs e)
        {
            LoginView.Visibility = Visibility.Collapsed;
            ProgressView.Visibility = Visibility.Visible;
            ProgressTitle.Text = "QR Code Login";
            QrCodeSection.Visibility = Visibility.Visible;
            StatusText.Text = "Generating QR code...";

            StartDumping(null, null, true);
        }

        private void AnonymousButton_Click(object sender, RoutedEventArgs e)
        {
            StartDumping(null, null, false);
        }

        #endregion

        #region Progress Events

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            steam3?.Disconnect(false); // Disconnect from Steam to abort QR code login
            AppendLog("Cancellation requested...");
            UpdateStatus("Cancelled by user");
            CancelButton.IsEnabled = false;

            // Show Done button so user can go back to login
            CancelButton.Visibility = Visibility.Collapsed;
            DoneButton.Visibility = Visibility.Visible;
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            LoginView.Visibility = Visibility.Visible;
            ProgressView.Visibility = Visibility.Collapsed;
            QrCodeSection.Visibility = Visibility.Collapsed;
            isDumping = false;

            UsernameTextBox.Text = string.Empty;
            PasswordBox.Password = string.Empty;
            AppIdTextBox.Text = string.Empty;
            DumpUnreleasedCheckBox.IsChecked = false;

            StatusText.Text = "Initializing...";
            ProgressBar.Value = 0;
            ProgressText.Text = "";
            LogTextBlock.Text = string.Empty;
            CancelButton.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;
            DoneButton.Visibility = Visibility.Collapsed;
            UploadButton.Visibility = Visibility.Collapsed;
            CancelButton.IsEnabled = true;

            // Clear generated files list
            generatedFiles.Clear();
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                MessageBox.Show("Please set your API key in Settings before uploading.", "API Key Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (generatedFiles.Count == 0)
            {
                MessageBox.Show("No files to upload.", "No Files",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UploadButton.IsEnabled = false;
            UpdateStatus("Uploading files to server...");

            int uploadedCount = 0;
            int failedCount = 0;

            foreach (var filePath in generatedFiles)
            {
                if (!File.Exists(filePath))
                {
                    AppendLog($"❌ File not found: {Path.GetFileName(filePath)}");
                    failedCount++;
                    continue;
                }

                var fileName = Path.GetFileName(filePath);
                AppendLog($"Uploading {fileName}...");

                try
                {
                    var result = await UploadFileToServer(filePath, settings.ApiKey);

                    if (result.Success)
                    {
                        uploadedCount++;
                        AppendLog($"✅ {fileName}: {result.Message}");
                    }
                    else
                    {
                        failedCount++;
                        AppendLog($"❌ {fileName}: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    AppendLog($"❌ {fileName}: {ex.Message}");
                }
            }

            UpdateStatus($"Upload complete: {uploadedCount} successful, {failedCount} failed");
            UploadButton.IsEnabled = true;

            MessageBox.Show($"Upload Complete!\n\nSuccessful: {uploadedCount}\nFailed: {failedCount}",
                "Upload Results", MessageBoxButton.OK,
                uploadedCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

            // Hide upload button to prevent duplicate uploads
            UploadButton.Visibility = Visibility.Collapsed;
        }

        private async Task<(bool Success, string Message)> UploadFileToServer(string filePath, string apiKey)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                using (var form = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath));
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

                    string fileName = Path.GetFileName(filePath);
                    form.Add(fileContent, "file", fileName);

                    try
                    {
                        var response = await httpClient.PostAsync("https://manifest.morrenus.xyz/api/v1/upload", form);
                        var responseString = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonResponse = JObject.Parse(responseString);
                            int validLines = jsonResponse["valid_lines"]?.Value<int>() ?? 0;
                            int invalidLines = jsonResponse["invalid_lines_removed"]?.Value<int>() ?? 0;

                            string message = $"Uploaded ({validLines} valid lines";
                            if (invalidLines > 0)
                            {
                                message += $", {invalidLines} invalid removed";
                            }
                            message += ")";

                            return (true, message);
                        }
                        else
                        {
                            try
                            {
                                var errorResponse = JObject.Parse(responseString);
                                string errorDetail = errorResponse["detail"]?.Value<string>() ?? $"HTTP {response.StatusCode}";
                                return (false, errorDetail);
                            }
                            catch
                            {
                                return (false, $"HTTP {response.StatusCode}: {responseString}");
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        return (false, $"Network error: {ex.Message}");
                    }
                }
            }
        }

        #endregion

        #region Dumping Logic

        private async void StartDumping(string? username, string? password, bool useQr)
        {
            LoginView.Visibility = Visibility.Collapsed;
            ProgressView.Visibility = Visibility.Visible;
            isDumping = true;

            try
            {
                _logFile = new StreamWriter(_logFilePath, append: false) { AutoFlush = true };
                AppendLog($"=== Depot Dumper Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                AppendLog($"Log file: {_logFilePath}");
            }
            catch (Exception ex)
            {
                AppendLog($"Warning: Could not create log file: {ex.Message}");
            }

            bool dumpUnreleased = DumpUnreleasedCheckBox.IsChecked ?? false;
            var targetAppIds = new List<uint>();
            if (!string.IsNullOrWhiteSpace(AppIdTextBox.Text))
            {
                var parts = AppIdTextBox.Text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (!uint.TryParse(trimmed, out uint appId))
                    {
                        MessageBox.Show($"Invalid App ID: {trimmed}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        DoneButton_Click(this, new RoutedEventArgs());
                        return;
                    }
                    targetAppIds.Add(appId);
                }
            }

            var config = new DumperConfig
            {
                UseQrCode = useQr,
                RememberPassword = false,
                TargetAppIds = targetAppIds,
                DumpUnreleased = dumpUnreleased
            };

            cancellationTokenSource = new CancellationTokenSource();

            bool wasCancelled = false;
            try
            {
                await RunDumper(username, password, config);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Operation cancelled by user.");
                wasCancelled = true;
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                AppendLog($"=== Depot Dumper Finished at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                try
                {
                    _logFile?.Close();
                    _logFile?.Dispose();
                    _logFile = null;
                }
                catch { }

                steam3?.Disconnect();
                await Dispatcher.InvokeAsync(() =>
                {
                    CancelButton.Visibility = Visibility.Collapsed;

                    if (wasCancelled)
                    {
                        BackButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        if (generatedFiles.Count > 0)
                        {
                            UploadButton.Visibility = Visibility.Visible;
                        }

                        DoneButton.Visibility = Visibility.Visible;
                    }

                    isDumping = false;
                });
            }
        }

        private async Task RunDumper(string? username, string? password, DumperConfig config)
        {
            await Task.Run(async () =>
            {
                try
                {
                    UpdateStatus("Loading account settings...");
                    AccountSettingsStore.LoadFromFile("depot_dumper_settings");

                    UpdateStatus("Connecting to Steam...");
                    steam3 = new Steam3Session(
                        new SteamUser.LogOnDetails()
                        {
                            Username = username,
                            Password = password,
                            ShouldRememberPassword = false,
                            LoginID = 0x534B32,
                        },
                        config
                    );

                    steam3.OnLog += (msg) => AppendLog(msg);

                    if (config.UseQrCode)
                    {
                        steam3.OnQrCodeGenerated += (qrUrl) =>
                        {
                            DisplayQrCode(qrUrl);
                        };
                    }

                    if (!steam3.WaitForCredentials())
                    {
                        AppendLog("Unable to get Steam credentials.");
                        return;
                    }

                    _ = Task.Run(() => steam3.TickCallbacks());

                    Dispatcher.InvokeAsync(() =>
                    {
                        QrCodeSection.Visibility = Visibility.Collapsed;
                    });

                    UpdateStatus("Getting licenses...");

                    IEnumerable<uint> licenseQuery;
                    if (steam3.steamUser.SteamID.AccountType == EAccountType.AnonUser)
                    {
                        licenseQuery = new List<uint>() { 17906 };
                    }
                    else
                    {
                        steam3.WaitUntilCallback(() => { }, () => { return steam3.Licenses != null; });
                        licenseQuery = steam3.Licenses!.Select(x => x.PackageID).Distinct();
                    }

                    UpdateStatus("Requesting package info...");
                    await steam3.RequestPackageInfo(licenseQuery);

                    if (config.TargetAppIds.Count == 0)
                    {
                        AppendLog("Dumping all apps in account...");
                        await DumpAllApps(licenseQuery);
                    }
                    else
                    {
                        AppendLog($"Dumping {config.TargetAppIds.Count} app(s)...");
                        await DumpSpecificApps(licenseQuery, config.TargetAppIds);
                    }

                    UpdateStatus("Dumping complete!");
                    AppendLog("All operations completed successfully.");
                }
                catch (Exception ex)
                {
                    AppendLog($"Fatal error: {ex.Message}");
                    throw;
                }
            });
        }

        private async Task DumpAllApps(IEnumerable<uint> licenseQuery)
        {
            string filenameUser = (steam3!.steamUser.SteamID.AccountType != EAccountType.AnonUser)
                ? steam3.steamUser.SteamID.AccountID.ToString()
                : "anon";

            UpdateStatus("Creating output files...");

            // Clear previous files list
            generatedFiles.Clear();

            string outputDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            string pkgsFile = Path.Combine(outputDir, $"{filenameUser}_pkgs.txt");
            StreamWriter sw_pkgs = new StreamWriter(pkgsFile);
            sw_pkgs.AutoFlush = true;

            var apps = new List<uint>();

            UpdateStatus("Processing packages...");
            foreach (var license in licenseQuery)
            {
                cancellationTokenSource?.Token.ThrowIfCancellationRequested();

                SteamApps.PICSProductInfoCallback.PICSProductInfo? package;
                if (steam3.PackageInfo.TryGetValue(license, out package) && package != null)
                {
                    var token = steam3.PackageTokens.ContainsKey(license) ? steam3.PackageTokens[license] : 0;
                    sw_pkgs.WriteLine("{0};{1}", license, token);

                    List<KeyValue> packageApps = package.KeyValues["appids"].Children;
                    apps.AddRange(packageApps.Select(x => x.AsUnsignedInteger()).Where(x => !apps.Contains(x)));
                }
            }

            sw_pkgs.Close();

            string appsFile = Path.Combine(outputDir, $"{filenameUser}_apps.txt");
            string keysFile = Path.Combine(outputDir, $"{filenameUser}_keys.txt");
            string appnamesFile = Path.Combine(outputDir, $"{filenameUser}_appnames.txt");

            StreamWriter sw_apps = new StreamWriter(appsFile);
            sw_apps.AutoFlush = true;
            StreamWriter sw_keys = new StreamWriter(keysFile);
            sw_keys.AutoFlush = true;
            StreamWriter sw_appnames = new StreamWriter(appnamesFile);
            sw_appnames.AutoFlush = true;

            UpdateStatus("Fetching app info...");
            await steam3.RequestAppInfoList(apps);

            var depots = new List<uint>();

            UpdateStatus("Dumping depot keys...");
            int current = 0;
            int total = apps.Count;

            foreach (var appId in apps)
            {
                cancellationTokenSource?.Token.ThrowIfCancellationRequested();

                current++;
                UpdateProgress(current, total);
                AppendLog($"Processing app {appId} ({current}/{total})...");

                await DumpApp(appId, licenseQuery, sw_apps, sw_keys, sw_appnames, depots);
            }

            sw_apps.Close();
            sw_keys.Close();
            sw_appnames.Close();

            // Add generated files to list for upload
            generatedFiles.Add(Path.GetFullPath(keysFile));
            generatedFiles.Add(Path.GetFullPath(appsFile));

            AppendLog($"Files saved to: {outputDir}");
        }

        private async Task DumpSpecificApps(IEnumerable<uint> licenseQuery, List<uint> appIds)
        {
            generatedFiles.Clear();

            var depots = new List<uint>();
            var allAppIds = new List<uint>(appIds);

            UpdateStatus("Fetching app info...");
            foreach (var appId in appIds)
            {
                await steam3!.RequestAppInfo(appId);
            }

            foreach (var appId in appIds)
            {
                SteamApps.PICSProductInfoCallback.PICSProductInfo? app;
                if (steam3!.AppInfo.TryGetValue(appId, out app) && app != null)
                {
                    var dlcList = app.KeyValues["extended"]["listofdlc"];
                    if (dlcList != KeyValue.Invalid && !string.IsNullOrEmpty(dlcList.Value))
                    {
                        var dlcIds = dlcList.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var dlcIdStr in dlcIds)
                        {
                            if (uint.TryParse(dlcIdStr.Trim(), out uint dlcId) && !allAppIds.Contains(dlcId))
                            {
                                allAppIds.Add(dlcId);
                                AppendLog($"Found DLC: {dlcId}");
                            }
                        }
                    }
                }
            }

            if (allAppIds.Count > appIds.Count)
            {
                AppendLog($"Fetching info for {allAppIds.Count - appIds.Count} DLC apps...");
                foreach (var dlcId in allAppIds.Skip(appIds.Count))
                {
                    await steam3!.RequestAppInfo(dlcId);
                }
            }

            int current = 0;
            int total = allAppIds.Count;

            string outputDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            string keysFile, tokensFile, namesFile;

            if (appIds.Count == 1)
            {
                uint appId = appIds[0];
                keysFile = Path.Combine(outputDir, $"app_{appId}_keys.txt");
                tokensFile = Path.Combine(outputDir, $"app_{appId}_token.txt");
                namesFile = Path.Combine(outputDir, $"app_{appId}_names.txt");
            }
            else
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                keysFile = Path.Combine(outputDir, $"apps_{timestamp}_keys.txt");
                tokensFile = Path.Combine(outputDir, $"apps_{timestamp}_tokens.txt");
                namesFile = Path.Combine(outputDir, $"apps_{timestamp}_names.txt");
            }

            using var sw_apps = new StreamWriter(tokensFile);
            using var sw_keys = new StreamWriter(keysFile);
            using var sw_appnames = new StreamWriter(namesFile);
            sw_apps.AutoFlush = true;
            sw_keys.AutoFlush = true;
            sw_appnames.AutoFlush = true;

            foreach (var appId in allAppIds)
            {
                cancellationTokenSource?.Token.ThrowIfCancellationRequested();

                current++;
                UpdateProgress(current, total);

                if (steam3!.AppTokens.ContainsKey(appId))
                {
                    AppendLog($"Processing app {appId} ({current}/{total})...");
                    await DumpApp(appId, licenseQuery, sw_apps, sw_keys, sw_appnames, depots);
                }
                else
                {
                    AppendLog($"Unable to get token for app {appId}");
                }
            }

            generatedFiles.Add(keysFile);
            generatedFiles.Add(tokensFile);
            AppendLog($"Files saved to: {outputDir}");
        }

        private async Task<bool> DumpApp(uint appId, IEnumerable<uint> licenses,
            StreamWriter sw_apps, StreamWriter sw_keys, StreamWriter sw_appnames,
            List<uint> depots)
        {
            SteamApps.PICSProductInfoCallback.PICSProductInfo? app;
            if (!steam3!.AppInfo.TryGetValue(appId, out app) || app == null)
                return false;

            if (!steam3.AppTokens.ContainsKey(appId))
                return false;

            KeyValue appInfo = app.KeyValues;
            KeyValue depotInfo = appInfo["depots"];

            if (!steam3.Config.DumpUnreleased &&
                appInfo["common"]["ReleaseState"] != KeyValue.Invalid &&
                appInfo["common"]["ReleaseState"].AsString() != "released")
                return false;

            sw_apps.WriteLine("{0};{1}", appId, steam3.AppTokens[appId]);
            sw_appnames.WriteLine("{0} - {1}", appId, appInfo["common"]["name"].AsString());

            if (depotInfo == KeyValue.Invalid)
                return false;

            foreach (var depotSection in depotInfo.Children)
            {
                cancellationTokenSource?.Token.ThrowIfCancellationRequested();

                uint depotId = uint.MaxValue;

                if (!uint.TryParse(depotSection.Name, out depotId) || depotId == uint.MaxValue)
                    continue;

                if (depotSection["manifests"] == KeyValue.Invalid)
                {
                    if (depotSection["depotfromapp"] != KeyValue.Invalid)
                    {
                        uint otherAppId = depotSection["depotfromapp"].AsUnsignedInteger();
                        if (otherAppId == appId)
                        {
                            continue;
                        }

                        await steam3.RequestAppInfo(otherAppId);

                        SteamApps.PICSProductInfoCallback.PICSProductInfo? otherApp;
                        if (!steam3.AppInfo.TryGetValue(otherAppId, out otherApp) || otherApp == null)
                            continue;

                        if (otherApp.KeyValues["depots"][depotId.ToString()]["manifests"] == KeyValue.Invalid)
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                bool isOwned = false;
                foreach (var license in licenses)
                {
                    SteamApps.PICSProductInfoCallback.PICSProductInfo? package;
                    if (steam3.PackageInfo.TryGetValue(license, out package) && package != null)
                    {
                        if (package.KeyValues["depotids"].Children.Any(child => child.AsUnsignedInteger() == depotId) ||
                            package.KeyValues["appids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                        {
                            isOwned = true;
                            break;
                        }
                    }
                }

                if (!isOwned)
                    continue;

                await steam3.RequestDepotKeyEx(depotId, appId);

                byte[]? depotKey;
                if (steam3.DepotKeys.TryGetValue(depotId, out depotKey))
                {
                    if (!depots.Contains(depotId))
                    {
                        sw_keys.WriteLine("{0};{1}", depotId, string.Concat(depotKey.Select(b => b.ToString("X2")).ToArray()));
                        depots.Add(depotId);
                    }

                    sw_appnames.WriteLine("\t{0}", depotId);

                    if (depotSection["manifests"] != KeyValue.Invalid)
                    {
                        foreach (var branch in depotSection["manifests"].Children)
                        {
                            sw_appnames.WriteLine("\t\t{0} - {1}", branch.Name, branch["gid"].AsUnsignedLong());
                        }
                    }
                }
            }

            if (depotInfo["workshopdepot"] != KeyValue.Invalid)
            {
                uint workshopDepotId = depotInfo["workshopdepot"].AsUnsignedInteger();
                if (workshopDepotId != 0 && !depots.Contains(workshopDepotId))
                {
                    await steam3.RequestDepotKeyEx(workshopDepotId, appId);

                    byte[]? workshopKey;
                    if (steam3.DepotKeys.TryGetValue(workshopDepotId, out workshopKey))
                    {
                        sw_keys.WriteLine("{0};{1}", workshopDepotId, string.Concat(workshopKey.Select(b => b.ToString("X2")).ToArray()));
                        depots.Add(workshopDepotId);
                        sw_appnames.WriteLine("\t{0} (workshop)", workshopDepotId);
                    }
                }
            }

            return true;
        }

        private void DisplayQrCode(string qrUrl)
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);
                byte[] qrCodeBytes = qrCode.GetGraphic(20);

                var bitmap = new BitmapImage();
                using (var stream = new MemoryStream(qrCodeBytes))
                {
                    stream.Position = 0;
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                Dispatcher.InvokeAsync(() =>
                {
                    QrCodeImage.Source = bitmap;
                    QrCodeSection.Visibility = Visibility.Visible;
                });

                AppendLog("QR code generated. Please scan with Steam Mobile app.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to display QR code: {ex.Message}");
            }
        }

        #endregion

        #region UI Helpers

        private void UpdateStatus(string status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = status;
            });
            AppendLog(status);
        }

        private void UpdateProgress(int current, int total)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ProgressBar.Value = (double)current / total * 100;
                ProgressText.Text = $"{current} / {total}";
            });
        }

        private void AppendLog(string message)
        {
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            try
            {
                _logFile?.WriteLine(logLine);
                _logFile?.Flush();
            }
            catch { }

            Dispatcher.InvokeAsync(() =>
            {
                LogTextBlock.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                LogScrollViewer.ScrollToEnd();
            });
        }

        #endregion
    }
}
