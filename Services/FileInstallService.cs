using SolusManifestApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class FileInstallService
    {
        private readonly SteamService _steamService;
        private readonly LoggerService _logger;

        public FileInstallService(SteamService steamService, LoggerService logger)
        {
            _steamService = steamService;
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> InstallFromZipAsync(string zipPath, Action<string>? progressCallback = null)
        {
            var depotKeys = new Dictionary<string, string>();

            try
            {
                progressCallback?.Invoke("Extracting ZIP file...");

                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir));

                    progressCallback?.Invoke("Installing files...");

                    var luaFiles = Directory.GetFiles(tempDir, "*.lua", SearchOption.AllDirectories);
                    var manifestFiles = Directory.GetFiles(tempDir, "*.manifest", SearchOption.AllDirectories);

                    if (luaFiles.Length == 0)
                    {
                        throw new Exception("No .lua files found in ZIP");
                    }

                    _logger.Info("Installing .lua files to stplug-in");
                    var stpluginPath = _steamService.GetStPluginPath();
                    if (string.IsNullOrEmpty(stpluginPath))
                    {
                        _logger.Error("Steam installation not found - stpluginPath is null or empty");
                        throw new Exception("Steam installation not found");
                    }

                    _logger.Info($"stplug-in path: {stpluginPath}");
                    _steamService.EnsureStPluginDirectory();

                    foreach (var luaFile in luaFiles)
                    {
                        var fileName = Path.GetFileName(luaFile);
                        var destPath = Path.Combine(stpluginPath, fileName);

                        progressCallback?.Invoke($"Installing {fileName}...");
                        _logger.Info($"Installing {fileName} to: {destPath}");

                        if (File.Exists(destPath))
                        {
                            File.Delete(destPath);
                        }

                        var disabledPath = destPath + ".disabled";
                        if (File.Exists(disabledPath))
                        {
                            File.Delete(disabledPath);
                        }

                        File.Copy(luaFile, destPath, true);
                        _logger.Info($"Successfully installed: {fileName}");
                    }

                    var steamPath = _steamService.GetSteamPath();
                    if (!string.IsNullOrEmpty(steamPath) && manifestFiles.Length > 0)
                    {
                        var depotCachePath = Path.Combine(steamPath, "depotcache");
                        Directory.CreateDirectory(depotCachePath);

                        foreach (var manifestFile in manifestFiles)
                        {
                            var fileName = Path.GetFileName(manifestFile);
                            var destPath = Path.Combine(depotCachePath, fileName);

                            progressCallback?.Invoke($"Installing {fileName}...");

                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }

                            File.Copy(manifestFile, destPath, true);
                        }
                    }

                    progressCallback?.Invoke("Installation complete!");

                    return depotKeys;
                }
                finally
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Error: {ex.Message}");
                throw new Exception($"Installation failed: {ex.Message}", ex);
            }
        }

        public async Task<bool> InstallLuaFileAsync(string luaPath)
        {
            try
            {
                _logger.Info($"InstallLuaFileAsync called with: {luaPath}");

                var stpluginPath = _steamService.GetStPluginPath();
                if (string.IsNullOrEmpty(stpluginPath))
                {
                    _logger.Error("Steam installation not found - stpluginPath is null or empty");
                    throw new Exception("Steam installation not found");
                }

                _logger.Info($"stplug-in path: {stpluginPath}");

                _steamService.EnsureStPluginDirectory();
                _logger.Debug("Ensured stplug-in directory exists");

                var fileName = Path.GetFileName(luaPath);
                var destPath = Path.Combine(stpluginPath, fileName);
                _logger.Info($"Installing lua file to: {destPath}");

                // Remove existing file
                if (File.Exists(destPath))
                {
                    _logger.Debug($"Removing existing file: {destPath}");
                    File.Delete(destPath);
                }

                // Remove .disabled version
                var disabledPath = destPath + ".disabled";
                if (File.Exists(disabledPath))
                {
                    _logger.Debug($"Removing disabled file: {disabledPath}");
                    File.Delete(disabledPath);
                }

                // Copy file
                _logger.Debug($"Copying {luaPath} to {destPath}");
                await Task.Run(() => File.Copy(luaPath, destPath, true));
                _logger.Info($"Successfully installed lua file: {fileName}");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Installation failed: {ex.Message}");
                throw new Exception($"Installation failed: {ex.Message}", ex);
            }
        }

        public async Task<bool> InstallManifestFileAsync(string manifestPath)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    throw new Exception("Steam installation not found");
                }

                // Manifest files go to depotcache
                var depotCachePath = Path.Combine(steamPath, "depotcache");
                Directory.CreateDirectory(depotCachePath);

                var fileName = Path.GetFileName(manifestPath);
                var destPath = Path.Combine(depotCachePath, fileName);

                // Remove existing file
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                // Copy file
                await Task.Run(() => File.Copy(manifestPath, destPath, true));

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Installation failed: {ex.Message}", ex);
            }
        }

        public List<Game> GetInstalledGames()
        {
            var games = new List<Game>();

            try
            {
                var stpluginPath = _steamService.GetStPluginPath();
                if (string.IsNullOrEmpty(stpluginPath) || !Directory.Exists(stpluginPath))
                {
                    return games;
                }

                var luaFiles = Directory.GetFiles(stpluginPath, "*.lua");

                foreach (var luaFile in luaFiles)
                {
                    var fileName = Path.GetFileName(luaFile);
                    var appId = Path.GetFileNameWithoutExtension(fileName);

                    var fileInfo = new FileInfo(luaFile);

                    games.Add(new Game
                    {
                        AppId = appId,
                        Name = appId, // Will be updated from manifest if available
                        IsInstalled = true,
                        LocalPath = luaFile,
                        SizeBytes = fileInfo.Length,
                        InstallDate = fileInfo.CreationTime,
                        LastUpdated = fileInfo.LastWriteTime
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error scanning installed games: {ex.Message}");
            }

            return games;
        }

        public bool UninstallGame(string appId)
        {
            try
            {
                var stpluginPath = _steamService.GetStPluginPath();
                if (string.IsNullOrEmpty(stpluginPath))
                {
                    return false;
                }

                var luaPath = Path.Combine(stpluginPath, $"{appId}.lua");
                if (File.Exists(luaPath))
                {
                    // Call Steam's uninstall first
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = $"steam://uninstall/{appId}",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Steam uninstall command failed (continuing anyway): {ex.Message}");
                    }

                    // Delete the lua file
                    File.Delete(luaPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to uninstall game {appId}: {ex.Message}");
                return false;
            }
        }

        public bool GenerateACF(string appId, string gameName, string installDir, string? libraryFolder = null)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                // Use custom library folder if provided, otherwise use default steamapps
                string steamAppsPath;
                if (!string.IsNullOrEmpty(libraryFolder))
                {
                    steamAppsPath = libraryFolder;
                }
                else
                {
                    steamAppsPath = Path.Combine(steamPath, "steamapps");
                }

                if (!Directory.Exists(steamAppsPath))
                {
                    Directory.CreateDirectory(steamAppsPath);
                }

                var acfPath = Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");
                var steamExe = Path.Combine(steamPath, "steam.exe").Replace("\\", "\\\\");
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                // Generate ACF content matching actual Steam format
                var acfContent = $@"""AppState""
{{
	""appid""		""{appId}""
	""Universe""		""1""
	""LauncherPath""		""{steamExe}""
	""name""		""{gameName}""
	""StateFlags""		""6""
	""installdir""		""{installDir}""
	""LastUpdated""		""{timestamp}""
	""SizeOnDisk""		""0""
	""StagingSize""		""0""
	""buildid""		""0""
	""LastOwner""		""0""
	""UpdateResult""		""0""
	""BytesToDownload""		""0""
	""BytesDownloaded""		""0""
	""BytesToStage""		""0""
	""BytesStaged""		""0""
	""TargetBuildID""		""0""
	""AutoUpdateBehavior""		""0""
	""AllowOtherDownloadsWhileRunning""		""0""
	""ScheduledAutoUpdate""		""0""
	""UserConfig""
	{{
		""language""		""english""
	}}
	""MountedConfig""
	{{
		""language""		""english""
	}}
}}
";

                File.WriteAllText(acfPath, acfContent);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RemoveACF(string appId)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                var acfPath = Path.Combine(steamPath, "steamapps", $"appmanifest_{appId}.acf");
                if (File.Exists(acfPath))
                {
                    File.Delete(acfPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to delete ACF file for {appId}: {ex.Message}");
                return false;
            }
        }

        public bool MoveManifestToDepotCache(string manifestPath)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                var depotCachePath = Path.Combine(steamPath, "Depotcache");
                if (!Directory.Exists(depotCachePath))
                {
                    Directory.CreateDirectory(depotCachePath);
                }

                var fileName = Path.GetFileName(manifestPath);
                var destPath = Path.Combine(depotCachePath, fileName);

                File.Move(manifestPath, destPath, true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to move manifest to depot cache: {ex.Message}");
                return false;
            }
        }

        public bool UpdateConfigVdfWithDepotKeys(Dictionary<string, string> depotKeys)
        {
            try
            {
                _logger.Debug($"UpdateConfigVdfWithDepotKeys called with {depotKeys?.Count ?? 0} keys");

                if (depotKeys == null || depotKeys.Count == 0)
                {
                    _logger.Debug("No depot keys provided");
                    return false;
                }

                var steamPath = _steamService.GetSteamPath();
                _logger.Debug($"Steam path: {steamPath}");

                if (string.IsNullOrEmpty(steamPath))
                {
                    _logger.Error("Steam path is null or empty");
                    return false;
                }

                var configPath = Path.Combine(steamPath, "config");
                if (!Directory.Exists(configPath))
                {
                    _logger.Debug($"Creating config directory: {configPath}");
                    Directory.CreateDirectory(configPath);
                }

                var configVdfPath = Path.Combine(configPath, "config.vdf");
                _logger.Debug($"Config.vdf path: {configVdfPath}");

                // Read existing config or create new structure
                var configContent = new System.Text.StringBuilder();
                bool hasDepotsSection = false;

                if (File.Exists(configVdfPath))
                {
                    _logger.Debug("Config.vdf exists, reading...");
                    var existingContent = File.ReadAllText(configVdfPath);

                    // Check if depots section exists
                    if (existingContent.Contains("\"depots\""))
                    {
                        _logger.Debug("Found existing depots section");
                        hasDepotsSection = true;
                        // Parse and update existing content
                        configContent.Append(existingContent);

                        // Insert depot keys before the closing brace of depots section
                        var depotsIndex = existingContent.IndexOf("\"depots\"");
                        var depotsEnd = FindClosingBrace(existingContent, depotsIndex);

                        _logger.Debug($"Depots section ends at index: {depotsEnd}");

                        if (depotsEnd > 0)
                        {
                            var beforeDepots = existingContent.Substring(0, depotsEnd);
                            var afterDepots = existingContent.Substring(depotsEnd);

                            configContent.Clear();
                            configContent.Append(beforeDepots);

                            // Add depot keys with proper indentation
                            int addedCount = 0;
                            foreach (var kvp in depotKeys)
                            {
                                // Remove any existing entry for this depot ID
                                if (!beforeDepots.Contains($"\"{kvp.Key}\""))
                                {
                                    configContent.AppendLine($"\t\t\t\t\t\"{kvp.Key}\"");
                                    configContent.AppendLine("\t\t\t\t\t{");
                                    configContent.AppendLine($"\t\t\t\t\t\t\"DecryptionKey\"\t\t\"{kvp.Value}\"");
                                    configContent.AppendLine("\t\t\t\t\t}");
                                    addedCount++;
                                }
                                else
                                {
                                    _logger.Debug($"Depot {kvp.Key} already exists, skipping");
                                }
                            }

                            _logger.Info($"Added {addedCount} new depot keys to config.vdf");

                            configContent.Append(afterDepots);
                        }
                        else
                        {
                            // Failed to find closing brace
                            _logger.Error("Failed to find closing brace in depots section");
                            return false;
                        }
                    }
                    else
                    {
                        _logger.Debug("No existing depots section found");
                    }
                }
                else
                {
                    _logger.Debug("Config.vdf does not exist, will create new");
                }

                // If no depots section exists, create new config structure
                if (!hasDepotsSection)
                {
                    _logger.Info("Creating new config.vdf structure");
                    configContent.Clear();
                    configContent.AppendLine("\"InstallConfigStore\"");
                    configContent.AppendLine("{");
                    configContent.AppendLine("\t\"Software\"");
                    configContent.AppendLine("\t{");
                    configContent.AppendLine("\t\t\"Valve\"");
                    configContent.AppendLine("\t\t{");
                    configContent.AppendLine("\t\t\t\"Steam\"");
                    configContent.AppendLine("\t\t\t{");
                    configContent.AppendLine("\t\t\t\t\"depots\"");
                    configContent.AppendLine("\t\t\t\t{");

                    foreach (var kvp in depotKeys)
                    {
                        _logger.Debug($"Adding depot {kvp.Key}");
                        configContent.AppendLine($"\t\t\t\t\t\"{kvp.Key}\"");
                        configContent.AppendLine("\t\t\t\t\t{");
                        configContent.AppendLine($"\t\t\t\t\t\t\"DecryptionKey\"\t\t\"{kvp.Value}\"");
                        configContent.AppendLine("\t\t\t\t\t}");
                    }

                    configContent.AppendLine("\t\t\t\t}");
                    configContent.AppendLine("\t\t\t}");
                    configContent.AppendLine("\t\t}");
                    configContent.AppendLine("\t}");
                    configContent.AppendLine("}");
                }

                _logger.Debug($"Writing {configContent.Length} characters to config.vdf");
                File.WriteAllText(configVdfPath, configContent.ToString());
                _logger.Info("Successfully wrote config.vdf");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update config.vdf: {ex.Message}");
                return false;
            }
        }

        private int FindClosingBrace(string content, int startIndex)
        {
            int braceCount = 0;
            bool foundOpenBrace = false;

            for (int i = startIndex; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    braceCount++;
                    foundOpenBrace = true;
                }
                else if (content[i] == '}')
                {
                    braceCount--;
                    if (foundOpenBrace && braceCount == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

    }
}
