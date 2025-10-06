using SolusManifestApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class FileInstallService
    {
        private readonly SteamService _steamService;

        public FileInstallService(SteamService steamService)
        {
            _steamService = steamService;
        }

        public async Task<Dictionary<string, string>> InstallFromZipAsync(string zipPath, bool isGreenLumaMode, Action<string>? progressCallback = null, List<string>? selectedDepotIds = null)
        {
            var depotKeys = new Dictionary<string, string>();

            try
            {
                progressCallback?.Invoke("Extracting ZIP file...");

                // Create temp directory
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Extract ZIP
                    await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir));

                    progressCallback?.Invoke("Installing files...");

                    // Find all .lua and .manifest files
                    var luaFiles = Directory.GetFiles(tempDir, "*.lua", SearchOption.AllDirectories);
                    var manifestFiles = Directory.GetFiles(tempDir, "*.manifest", SearchOption.AllDirectories);

                    if (luaFiles.Length == 0)
                    {
                        throw new Exception("No .lua files found in ZIP");
                    }

                    // If GreenLuma mode, extract depot keys from .lua file in temp (don't install it)
                    if (isGreenLumaMode && luaFiles.Length > 0)
                    {
                        progressCallback?.Invoke("Extracting depot keys from .lua file...");
                        depotKeys = ExtractDepotKeysFromLua(luaFiles[0]);
                    }
                    else
                    {
                        // SteamTools mode: Install .lua files to stplug-in
                        var stpluginPath = _steamService.GetStPluginPath();
                        if (string.IsNullOrEmpty(stpluginPath))
                        {
                            throw new Exception("Steam installation not found");
                        }

                        _steamService.EnsureStPluginDirectory();

                        foreach (var luaFile in luaFiles)
                        {
                            var fileName = Path.GetFileName(luaFile);
                            var destPath = Path.Combine(stpluginPath, fileName);

                            progressCallback?.Invoke($"Installing {fileName}...");

                            // Remove existing file
                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }

                            // Remove .disabled version
                            var disabledPath = destPath + ".disabled";
                            if (File.Exists(disabledPath))
                            {
                                File.Delete(disabledPath);
                            }

                            // Copy file
                            File.Copy(luaFile, destPath, true);
                        }
                    }

                    // Install .manifest files to depotcache
                    var steamPath = _steamService.GetSteamPath();
                    if (!string.IsNullOrEmpty(steamPath) && manifestFiles.Length > 0)
                    {
                        var depotCachePath = Path.Combine(steamPath, "depotcache");
                        Directory.CreateDirectory(depotCachePath);

                        foreach (var manifestFile in manifestFiles)
                        {
                            var fileName = Path.GetFileName(manifestFile);

                            // If selectedDepotIds provided, only extract manifests for selected depots
                            if (selectedDepotIds != null && selectedDepotIds.Count > 0)
                            {
                                // Check if filename contains any of the selected depot IDs
                                var shouldExtract = selectedDepotIds.Any(depotId => fileName.Contains(depotId));
                                if (!shouldExtract)
                                {
                                    continue; // Skip this manifest
                                }
                            }

                            var destPath = Path.Combine(depotCachePath, fileName);

                            progressCallback?.Invoke($"Installing {fileName}...");

                            // Remove existing file
                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }

                            // Copy file
                            File.Copy(manifestFile, destPath, true);
                        }
                    }

                    progressCallback?.Invoke("Installation complete!");

                    return depotKeys;
                }
                finally
                {
                    // Cleanup temp directory
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
                var stpluginPath = _steamService.GetStPluginPath();
                if (string.IsNullOrEmpty(stpluginPath))
                {
                    throw new Exception("Steam installation not found");
                }

                _steamService.EnsureStPluginDirectory();

                var fileName = Path.GetFileName(luaPath);
                var destPath = Path.Combine(stpluginPath, fileName);

                // Remove existing file
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                // Remove .disabled version
                var disabledPath = destPath + ".disabled";
                if (File.Exists(disabledPath))
                {
                    File.Delete(disabledPath);
                }

                // Copy file
                await Task.Run(() => File.Copy(luaPath, destPath, true));

                return true;
            }
            catch (Exception ex)
            {
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
            catch { }

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
                    catch
                    {
                        // Continue even if Steam uninstall fails
                    }

                    // Delete the lua file
                    File.Delete(luaPath);
                    return true;
                }

                return false;
            }
            catch
            {
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
	""StateFlags""		""4""
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
            catch
            {
                return false;
            }
        }

        public bool IsAppIdInAppList(string appId, string? customAppListPath = null)
        {
            try
            {
                string appListPath;

                if (!string.IsNullOrEmpty(customAppListPath))
                {
                    appListPath = customAppListPath;
                }
                else
                {
                    var steamPath = _steamService.GetSteamPath();
                    if (string.IsNullOrEmpty(steamPath))
                    {
                        return false;
                    }
                    appListPath = Path.Combine(steamPath, "AppList");
                }

                if (!Directory.Exists(appListPath))
                {
                    return false;
                }

                var existingFiles = Directory.GetFiles(appListPath, "*.txt");
                foreach (var file in existingFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(file).Trim();
                        if (content == appId)
                        {
                            return true;
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool GenerateAppList(List<string> appIds, string? customAppListPath = null)
        {
            try
            {
                string appListPath;

                if (!string.IsNullOrEmpty(customAppListPath))
                {
                    // Use custom path for GreenLuma Stealth mode
                    appListPath = customAppListPath;
                }
                else
                {
                    // Use default path for GreenLuma mode
                    var steamPath = _steamService.GetSteamPath();
                    if (string.IsNullOrEmpty(steamPath))
                    {
                        return false;
                    }
                    appListPath = Path.Combine(steamPath, "AppList");
                }

                if (!Directory.Exists(appListPath))
                {
                    Directory.CreateDirectory(appListPath);
                }

                // Get all existing appIds to avoid duplicates
                var existingAppIds = new HashSet<string>();
                var existingFiles = Directory.GetFiles(appListPath, "*.txt");

                foreach (var file in existingFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(file).Trim();
                        if (!string.IsNullOrEmpty(content))
                        {
                            existingAppIds.Add(content);
                        }
                    }
                    catch { }
                }

                // Filter out appIds that already exist
                var newAppIds = appIds.Where(id => !existingAppIds.Contains(id)).ToList();

                if (newAppIds.Count == 0)
                {
                    // All appIds already exist, nothing to add
                    return true;
                }

                // Check if we would exceed 128 files (GreenLuma limit)
                if (existingFiles.Length + newAppIds.Count > 128)
                {
                    throw new Exception($"Cannot add {newAppIds.Count} apps. Would exceed 128 file limit (currently {existingFiles.Length} files).");
                }

                // Find the next available file number
                var usedNumbers = existingFiles
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(n => int.TryParse(n, out _))
                    .Select(int.Parse)
                    .ToHashSet();

                int nextNumber = 0;
                foreach (var appId in newAppIds)
                {
                    // Find next unused number
                    while (usedNumbers.Contains(nextNumber))
                    {
                        nextNumber++;
                    }

                    var filePath = Path.Combine(appListPath, $"{nextNumber}.txt");
                    File.WriteAllText(filePath, appId);
                    usedNumbers.Add(nextNumber);
                    nextNumber++;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RemoveAppListEntry(string appId)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                var appListPath = Path.Combine(steamPath, "AppList");
                if (!Directory.Exists(appListPath))
                {
                    return false;
                }

                // Find and delete files containing this appId
                foreach (var file in Directory.GetFiles(appListPath, "*.txt"))
                {
                    var content = File.ReadAllText(file).Trim();
                    if (content == appId)
                    {
                        File.Delete(file);
                    }
                }

                return true;
            }
            catch
            {
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
            catch
            {
                return false;
            }
        }

        public bool UpdateConfigVdfWithDepotKeys(Dictionary<string, string> depotKeys)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                var configPath = Path.Combine(steamPath, "config");
                if (!Directory.Exists(configPath))
                {
                    Directory.CreateDirectory(configPath);
                }

                var configVdfPath = Path.Combine(configPath, "config.vdf");

                // Read existing config or create new structure
                var configContent = new System.Text.StringBuilder();
                bool hasDepotsSection = false;

                if (File.Exists(configVdfPath))
                {
                    var existingContent = File.ReadAllText(configVdfPath);

                    // Check if depots section exists
                    if (existingContent.Contains("\"depots\""))
                    {
                        hasDepotsSection = true;
                        // Parse and update existing content
                        configContent.Append(existingContent);

                        // Insert depot keys before the closing brace of depots section
                        var depotsIndex = existingContent.IndexOf("\"depots\"");
                        var depotsEnd = FindClosingBrace(existingContent, depotsIndex);

                        if (depotsEnd > 0)
                        {
                            var beforeDepots = existingContent.Substring(0, depotsEnd);
                            var afterDepots = existingContent.Substring(depotsEnd);

                            configContent.Clear();
                            configContent.Append(beforeDepots);

                            // Add depot keys
                            foreach (var kvp in depotKeys)
                            {
                                configContent.AppendLine($"\t\"{kvp.Key}\"");
                                configContent.AppendLine("\t{");
                                configContent.AppendLine($"\t\t\"DecryptionKey\"\t\t\"{kvp.Value}\"");
                                configContent.AppendLine("\t}");
                            }

                            configContent.Append(afterDepots);
                        }
                    }
                }

                // If no depots section exists, create new config structure
                if (!hasDepotsSection)
                {
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

                File.WriteAllText(configVdfPath, configContent.ToString());
                return true;
            }
            catch
            {
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

        public Dictionary<string, string> ExtractDepotKeysFromLua(string luaFilePath)
        {
            var depotKeys = new Dictionary<string, string>();

            try
            {
                if (!File.Exists(luaFilePath))
                {
                    return depotKeys;
                }

                var lines = File.ReadAllLines(luaFilePath);

                foreach (var line in lines)
                {
                    // Look for lines like: addappid(285311, 1, "1e5f4762efe80ce881ab1267f4aef3bd6dcb98bac938ff35d4eb0ce470d597f7")
                    if (line.Contains("addappid") && line.Contains("\""))
                    {
                        // Extract depot ID and key using regex or string parsing
                        var trimmed = line.Trim();

                        // Find the opening parenthesis
                        var openParenIndex = trimmed.IndexOf('(');
                        if (openParenIndex < 0) continue;

                        // Find the closing parenthesis
                        var closeParenIndex = trimmed.IndexOf(')');
                        if (closeParenIndex < 0) continue;

                        // Extract the parameters
                        var paramsStr = trimmed.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1);
                        var parts = paramsStr.Split(',');

                        if (parts.Length >= 3)
                        {
                            // First param is depot ID
                            var depotId = parts[0].Trim();

                            // Third param is the key (in quotes)
                            var keyPart = parts[2].Trim();
                            var key = keyPart.Trim('"', ' ');

                            if (!string.IsNullOrEmpty(depotId) && !string.IsNullOrEmpty(key) && key.Length > 10)
                            {
                                depotKeys[depotId] = key;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return empty dictionary on error
            }

            return depotKeys;
        }

        /// <summary>
        /// Scans AppList folder and ACF files to find GreenLuma installed games
        /// </summary>
        public List<GreenLumaGame> GetGreenLumaGames(string? customAppListPath = null)
        {
            var greenLumaGames = new List<GreenLumaGame>();

            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return greenLumaGames;
                }

                // Determine AppList path
                string appListPath;
                if (!string.IsNullOrEmpty(customAppListPath))
                {
                    appListPath = customAppListPath;
                }
                else
                {
                    appListPath = Path.Combine(steamPath, "AppList");
                }

                if (!Directory.Exists(appListPath))
                {
                    return greenLumaGames;
                }

                // Read all AppList entries
                var appListEntries = new Dictionary<string, List<string>>(); // AppID -> List of file paths
                var appListFiles = Directory.GetFiles(appListPath, "*.txt");

                foreach (var file in appListFiles)
                {
                    try
                    {
                        var appId = File.ReadAllText(file).Trim();
                        if (!string.IsNullOrEmpty(appId))
                        {
                            if (!appListEntries.ContainsKey(appId))
                            {
                                appListEntries[appId] = new List<string>();
                            }
                            appListEntries[appId].Add(file);
                        }
                    }
                    catch { }
                }

                // Scan ACF files to find games
                var steamAppsPath = Path.Combine(steamPath, "steamapps");
                if (!Directory.Exists(steamAppsPath))
                {
                    return greenLumaGames;
                }

                var acfFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");

                foreach (var acfFile in acfFiles)
                {
                    try
                    {
                        var acfContent = File.ReadAllText(acfFile);

                        // Parse AppID from ACF
                        var appIdMatch = System.Text.RegularExpressions.Regex.Match(acfContent, @"""appid""\s+""(\d+)""");
                        if (!appIdMatch.Success)
                        {
                            continue;
                        }

                        var appId = appIdMatch.Groups[1].Value;

                        // Check if this game has AppList entries
                        if (!appListEntries.ContainsKey(appId))
                        {
                            continue; // Not a GreenLuma game
                        }

                        // Parse game name
                        var nameMatch = System.Text.RegularExpressions.Regex.Match(acfContent, @"""name""\s+""([^""]+)""");
                        var gameName = nameMatch.Success ? nameMatch.Groups[1].Value : $"App {appId}";

                        // Parse install date
                        DateTime? installDate = null;
                        var installDateMatch = System.Text.RegularExpressions.Regex.Match(acfContent, @"""LastUpdated""\s+""(\d+)""");
                        if (installDateMatch.Success && long.TryParse(installDateMatch.Groups[1].Value, out long timestamp))
                        {
                            installDate = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                        }

                        // Parse size
                        long sizeBytes = 0;
                        var sizeMatch = System.Text.RegularExpressions.Regex.Match(acfContent, @"""SizeOnDisk""\s+""(\d+)""");
                        if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out long size))
                        {
                            sizeBytes = size;
                        }

                        // Parse depot IDs from ACF
                        var depotIds = new List<string>();
                        var allAppListFiles = new List<string>();

                        // Add the main app ID
                        depotIds.Add(appId);
                        if (appListEntries.ContainsKey(appId))
                        {
                            allAppListFiles.AddRange(appListEntries[appId]);
                        }

                        // Find all depot IDs in the "InstalledDepots" section
                        var installedDepotsMatch = System.Text.RegularExpressions.Regex.Match(
                            acfContent,
                            @"""InstalledDepots""[^{]*\{([^}]+)\}",
                            System.Text.RegularExpressions.RegexOptions.Singleline
                        );

                        if (installedDepotsMatch.Success)
                        {
                            var depotsSection = installedDepotsMatch.Groups[1].Value;
                            var depotMatches = System.Text.RegularExpressions.Regex.Matches(depotsSection, @"""(\d+)""");

                            foreach (System.Text.RegularExpressions.Match match in depotMatches)
                            {
                                var depotId = match.Groups[1].Value;
                                if (!depotIds.Contains(depotId))
                                {
                                    depotIds.Add(depotId);
                                    if (appListEntries.ContainsKey(depotId))
                                    {
                                        allAppListFiles.AddRange(appListEntries[depotId]);
                                    }
                                }
                            }
                        }

                        // Also check for DLC app IDs in the ACF
                        var dlcMatches = System.Text.RegularExpressions.Regex.Matches(acfContent, @"""(\d{4,})""");
                        foreach (System.Text.RegularExpressions.Match match in dlcMatches)
                        {
                            var dlcId = match.Groups[1].Value;
                            // Add any app ID found in AppList that's also in the ACF
                            if (appListEntries.ContainsKey(dlcId) && !depotIds.Contains(dlcId))
                            {
                                depotIds.Add(dlcId);
                                allAppListFiles.AddRange(appListEntries[dlcId]);
                            }
                        }

                        // Check if game has a corresponding .lua file
                        var stpluginPath = _steamService.GetStPluginPath();
                        bool hasLuaFile = false;
                        string? luaFilePath = null;

                        if (!string.IsNullOrEmpty(stpluginPath))
                        {
                            var luaFile = Path.Combine(stpluginPath, $"{appId}.lua");
                            var luaFileDisabled = Path.Combine(stpluginPath, $"{appId}.lua.disabled");

                            if (File.Exists(luaFile))
                            {
                                hasLuaFile = true;
                                luaFilePath = luaFile;
                            }
                            else if (File.Exists(luaFileDisabled))
                            {
                                hasLuaFile = true;
                                luaFilePath = luaFileDisabled;
                            }
                        }

                        var greenLumaGame = new GreenLumaGame
                        {
                            AppId = appId,
                            Name = gameName,
                            SizeBytes = sizeBytes,
                            InstallDate = installDate,
                            LastUpdated = installDate,
                            AppListFilePaths = allAppListFiles,
                            DepotIds = depotIds,
                            AcfPath = acfFile,
                            HasLuaFile = hasLuaFile,
                            LuaFilePath = luaFilePath
                        };

                        greenLumaGames.Add(greenLumaGame);
                    }
                    catch
                    {
                        // Skip problematic ACF files
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }

            return greenLumaGames;
        }

        /// <summary>
        /// Uninstalls a GreenLuma game by querying SteamCMD API for complete depot list and removing all related files
        /// </summary>
        public async Task<bool> UninstallGreenLumaGameAsync(string appId, string? customAppListPath = null)
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                // Get all depot IDs from SteamCMD API for complete cleanup
                var depotIds = await GetAllDepotIdsFromApiAsync(appId);

                // Fallback: if API fails, get depot IDs from local ACF file
                if (depotIds.Count == 0)
                {
                    var greenLumaGames = GetGreenLumaGames(customAppListPath);
                    var game = greenLumaGames.FirstOrDefault(g => g.AppId == appId);
                    if (game != null)
                    {
                        depotIds = game.DepotIds;
                    }
                    else
                    {
                        return false;
                    }
                }

                // 1. Remove all AppList .txt files for ALL depot IDs
                string appListPath = !string.IsNullOrEmpty(customAppListPath)
                    ? customAppListPath
                    : Path.Combine(steamPath, "AppList");

                if (Directory.Exists(appListPath))
                {
                    var allAppListFiles = Directory.GetFiles(appListPath, "*.txt");
                    foreach (var file in allAppListFiles)
                    {
                        try
                        {
                            var fileContent = File.ReadAllText(file).Trim();
                            if (depotIds.Contains(fileContent))
                            {
                                File.Delete(file);
                            }
                        }
                        catch { }
                    }
                }

                // 2. Remove ACF manifest file
                var acfPath = Path.Combine(steamPath, "steamapps", $"appmanifest_{appId}.acf");
                try
                {
                    if (File.Exists(acfPath))
                    {
                        File.Delete(acfPath);
                    }
                }
                catch { }

                // 3. Remove depot manifest files ({depotId}_*.manifest)
                var steamAppsPath = Path.Combine(steamPath, "steamapps");
                if (Directory.Exists(steamAppsPath))
                {
                    foreach (var depotId in depotIds)
                    {
                        try
                        {
                            var manifestFiles = Directory.GetFiles(steamAppsPath, $"{depotId}_*.manifest");
                            foreach (var manifestFile in manifestFiles)
                            {
                                File.Delete(manifestFile);
                            }
                        }
                        catch { }
                    }
                }

                // 4. Remove depot keys from Config.VDF
                try
                {
                    var configPath = Path.Combine(steamPath, "config", "config.vdf");
                    if (File.Exists(configPath))
                    {
                        var content = File.ReadAllText(configPath);

                        foreach (var depotId in depotIds)
                        {
                            var pattern = $@"""{depotId}""\s*\{{\s*""DecryptionKey""\s+""[^""]*""\s*\}}";
                            content = System.Text.RegularExpressions.Regex.Replace(content, pattern, "", System.Text.RegularExpressions.RegexOptions.Multiline);
                        }

                        File.WriteAllText(configPath, content);
                    }
                }
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets all depot IDs for an app from SteamCMD API
        /// </summary>
        private async Task<List<string>> GetAllDepotIdsFromApiAsync(string appId)
        {
            var depotIds = new List<string>();

            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.GetAsync($"https://api.steamcmd.net/v1/info/{appId}");
                if (!response.IsSuccessStatusCode)
                {
                    return depotIds;
                }

                var json = await response.Content.ReadAsStringAsync();
                var jsonDoc = System.Text.Json.JsonDocument.Parse(json);

                // Navigate to data -> {appId} -> depots
                if (jsonDoc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty(appId, out var appData) &&
                    appData.TryGetProperty("depots", out var depots))
                {
                    foreach (var depot in depots.EnumerateObject())
                    {
                        // Only include numeric depot IDs (exclude "branches" and other non-depot keys)
                        if (uint.TryParse(depot.Name, out _))
                        {
                            depotIds.Add(depot.Name);
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on error - will fallback to local data
            }

            return depotIds;
        }
    }
}
