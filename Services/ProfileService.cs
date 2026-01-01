using Newtonsoft.Json;
using SolusManifestApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class ProfileService
    {
        private readonly string _profilesPath;
        private readonly SettingsService _settingsService;
        private readonly SteamService _steamService;
        private readonly FileInstallService _fileInstallService;
        private readonly LibraryRefreshService _libraryRefreshService;
        private readonly LoggerService _logger;
        private ProfileData? _profileData;

        public ProfileService(SettingsService settingsService, SteamService steamService, FileInstallService fileInstallService, LibraryRefreshService libraryRefreshService, LoggerService logger)
        {
            _settingsService = settingsService;
            _steamService = steamService;
            _fileInstallService = fileInstallService;
            _libraryRefreshService = libraryRefreshService;
            _logger = logger;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "SolusManifestApp");
            Directory.CreateDirectory(appFolder);
            _profilesPath = Path.Combine(appFolder, "greenluma_profiles.json");
        }

        public ProfileData LoadProfiles()
        {
            if (_profileData != null)
                return _profileData;

            try
            {
                if (File.Exists(_profilesPath))
                {
                    var json = File.ReadAllText(_profilesPath);
                    _profileData = JsonConvert.DeserializeObject<ProfileData>(json) ?? new ProfileData();
                }
                else
                {
                    _profileData = new ProfileData();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load profiles: {ex.Message}");
                _profileData = new ProfileData();
            }

            if (_profileData.Profiles.Count == 0)
            {
                var defaultProfile = new GreenLumaProfile { Name = "Default" };
                _profileData.Profiles.Add(defaultProfile);
                _profileData.ActiveProfileId = defaultProfile.Id;
                SaveProfiles();
            }

            if (string.IsNullOrEmpty(_profileData.ActiveProfileId) && _profileData.Profiles.Count > 0)
            {
                _profileData.ActiveProfileId = _profileData.Profiles[0].Id;
                SaveProfiles();
            }

            return _profileData;
        }

        public void SaveProfiles()
        {
            try
            {
                if (_profileData == null)
                    return;

                var json = JsonConvert.SerializeObject(_profileData, Formatting.Indented);

                using (var fileStream = new FileStream(_profilesPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(fileStream))
                {
                    writer.Write(json);
                    writer.Flush();
                    fileStream.Flush(flushToDisk: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save profiles: {ex.Message}");
            }
        }

        public GreenLumaProfile CreateProfile(string name)
        {
            var data = LoadProfiles();
            var profile = new GreenLumaProfile { Name = name };
            data.Profiles.Add(profile);
            SaveProfiles();
            _logger.Info($"Created profile: {name}");
            return profile;
        }

        public bool RenameProfile(string profileId, string newName)
        {
            var data = LoadProfiles();
            var profile = data.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            profile.Name = newName;
            profile.ModifiedAt = DateTime.UtcNow;
            SaveProfiles();
            _logger.Info($"Renamed profile to: {newName}");
            return true;
        }

        public async Task<bool> DeleteProfileAsync(string profileId, bool uninstallGames = true)
        {
            var data = LoadProfiles();
            var profile = data.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            var uninstalledAppIds = new List<string>();

            if (uninstallGames && profile.Games.Count > 0)
            {
                var appListPath = GetAppListPath();
                foreach (var game in profile.Games)
                {
                    var existsInOtherProfile = data.Profiles.Any(p => p.Id != profileId && p.Games.Any(g => g.AppId == game.AppId));
                    if (!existsInOtherProfile)
                    {
                        uninstalledAppIds.Add(game.AppId);
                    }
                }
                _logger.Info($"Uninstalled {uninstalledAppIds.Count} games from profile {profile.Name} (skipped {profile.Games.Count - uninstalledAppIds.Count} that exist in other profiles)");
            }

            data.Profiles.Remove(profile);

            if (data.ActiveProfileId == profileId)
            {
                if (data.Profiles.Count > 0)
                {
                    data.ActiveProfileId = data.Profiles[0].Id;
                }
                else
                {
                    var newDefault = new GreenLumaProfile { Name = "Default" };
                    data.Profiles.Add(newDefault);
                    data.ActiveProfileId = newDefault.Id;
                }
            }

            SaveProfiles();

            foreach (var appId in uninstalledAppIds)
            {
                _libraryRefreshService.NotifyGameUninstalled(appId);
            }

            _logger.Info($"Deleted profile: {profile.Name}");
            return true;
        }

        public GreenLumaProfile? GetActiveProfile()
        {
            var data = LoadProfiles();
            return data.Profiles.FirstOrDefault(p => p.Id == data.ActiveProfileId);
        }

        public bool SetActiveProfile(string profileId)
        {
            var data = LoadProfiles();
            var profile = data.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            data.ActiveProfileId = profileId;
            SaveProfiles();
            _logger.Info($"Set active profile: {profile.Name}");
            return true;
        }

        public List<GreenLumaProfile> GetAllProfiles()
        {
            return LoadProfiles().Profiles;
        }

        public GreenLumaProfile? GetProfileById(string profileId)
        {
            return LoadProfiles().Profiles.FirstOrDefault(p => p.Id == profileId);
        }

        public bool AddGameToProfile(string profileId, ProfileGame game)
        {
            var data = LoadProfiles();
            var profile = data.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            var existing = profile.Games.FirstOrDefault(g => g.AppId == game.AppId);
            if (existing != null)
            {
                existing.Name = game.Name;
                existing.Depots = game.Depots;
            }
            else
            {
                profile.Games.Add(game);
            }

            profile.ModifiedAt = DateTime.UtcNow;
            SaveProfiles();
            _logger.Info($"Added game {game.AppId} to profile {profile.Name}");
            return true;
        }

        public async Task<bool> RemoveGameFromProfileAsync(string profileId, string appId, bool uninstallGame = true)
        {
            var data = LoadProfiles();
            var profile = data.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            var game = profile.Games.FirstOrDefault(g => g.AppId == appId);
            if (game == null)
                return false;

            profile.Games.Remove(game);
            profile.ModifiedAt = DateTime.UtcNow;
            SaveProfiles();

            bool wasUninstalled = false;
            if (uninstallGame)
            {
                var existsInOtherProfile = data.Profiles.Any(p => p.Id != profileId && p.Games.Any(g => g.AppId == appId));
                if (!existsInOtherProfile)
                {
                    _libraryRefreshService.NotifyGameUninstalled(appId);
                    wasUninstalled = true;
                    _logger.Info($"Uninstalled game {appId} (not in any other profile)");
                }
                else
                {
                    _logger.Info($"Game {appId} still exists in another profile, skipping uninstall");
                }
            }

            _logger.Info($"Removed game {appId} from profile {profile.Name}");
            return true;
        }

        public bool IsGameInProfile(string profileId, string appId)
        {
            var profile = GetProfileById(profileId);
            return profile?.Games.Any(g => g.AppId == appId) ?? false;
        }

        private string GetAppListPath()
        {
            var steamPath = _steamService.GetSteamPath();
            if (!string.IsNullOrEmpty(steamPath))
            {
                return Path.Combine(steamPath, "AppList");
            }

            return string.Empty;
        }

        public (bool success, string message) ApplyProfile(string profileId)
        {
            try
            {
                var profile = GetProfileById(profileId);
                if (profile == null)
                    return (false, "Profile not found");

                var appListPath = GetAppListPath();
                if (string.IsNullOrEmpty(appListPath))
                    return (false, "Could not determine AppList path");

                var allAppIds = new List<string>();
                foreach (var game in profile.Games)
                {
                    allAppIds.Add(game.AppId);
                    foreach (var depot in game.Depots)
                    {
                        if (!allAppIds.Contains(depot.DepotId))
                        {
                            allAppIds.Add(depot.DepotId);
                        }
                    }
                }

                if (allAppIds.Count > 128)
                    return (false, $"Profile has {allAppIds.Count} entries but GreenLuma limit is 128");

                if (Directory.Exists(appListPath))
                {
                    var existingFiles = Directory.GetFiles(appListPath, "*.txt");
                    foreach (var file in existingFiles)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Could not delete AppList file {file}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(appListPath);
                }

                for (int i = 0; i < allAppIds.Count; i++)
                {
                    var filePath = Path.Combine(appListPath, $"{i}.txt");
                    File.WriteAllText(filePath, allAppIds[i]);
                }

                _logger.Info($"Applied profile '{profile.Name}' with {allAppIds.Count} entries to AppList");
                return (true, $"Profile applied successfully. {allAppIds.Count} entries written to AppList.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply profile: {ex.Message}");
                return (false, $"Failed to apply profile: {ex.Message}");
            }
        }

        public bool IsProfileApplied(string profileId)
        {
            try
            {
                var profile = GetProfileById(profileId);
                if (profile == null)
                    return false;

                var appListPath = GetAppListPath();
                if (string.IsNullOrEmpty(appListPath) || !Directory.Exists(appListPath))
                    return profile.Games.Count == 0;

                var profileAppIds = new HashSet<string>();
                foreach (var game in profile.Games)
                {
                    profileAppIds.Add(game.AppId);
                    foreach (var depot in game.Depots)
                    {
                        profileAppIds.Add(depot.DepotId);
                    }
                }

                var appListIds = new HashSet<string>();
                var files = Directory.GetFiles(appListPath, "*.txt");
                foreach (var file in files)
                {
                    try
                    {
                        var content = File.ReadAllText(file).Trim();
                        if (!string.IsNullOrEmpty(content))
                        {
                            appListIds.Add(content);
                        }
                    }
                    catch { }
                }

                return profileAppIds.SetEquals(appListIds);
            }
            catch
            {
                return false;
            }
        }

        public GreenLumaProfile? ImportCurrentAppList(string profileName)
        {
            try
            {
                var appListPath = GetAppListPath();
                if (string.IsNullOrEmpty(appListPath) || !Directory.Exists(appListPath))
                    return null;

                var files = Directory.GetFiles(appListPath, "*.txt");
                var appIds = new HashSet<string>();

                foreach (var file in files)
                {
                    try
                    {
                        var content = File.ReadAllText(file).Trim();
                        if (!string.IsNullOrEmpty(content))
                        {
                            appIds.Add(content);
                        }
                    }
                    catch { }
                }

                if (appIds.Count == 0)
                    return null;

                var profile = CreateProfile(profileName);
                foreach (var appId in appIds)
                {
                    var game = new ProfileGame
                    {
                        AppId = appId,
                        Name = $"App {appId}",
                        Depots = new List<ProfileDepot>()
                    };
                    profile.Games.Add(game);
                }

                profile.ModifiedAt = DateTime.UtcNow;
                SaveProfiles();

                _logger.Info($"Imported {appIds.Count} apps from AppList to profile '{profileName}'");
                return profile;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to import AppList: {ex.Message}");
                return null;
            }
        }

        public (bool success, string message, int manifestCount) ExportProfileAsZip(string profileId, string filePath)
        {
            try
            {
                var profile = GetProfileById(profileId);
                if (profile == null)
                    return (false, "Profile not found", 0);

                var export = new ProfileExport
                {
                    ExportedAt = DateTime.UtcNow,
                    Profile = profile
                };

                var steamPath = _steamService.GetSteamPath();
                var depotCachePath = Path.Combine(steamPath, "depotcache");
                int manifestCount = 0;

                if (File.Exists(filePath))
                    File.Delete(filePath);

                using (var zipArchive = ZipFile.Open(filePath, ZipArchiveMode.Create))
                {
                    var json = JsonConvert.SerializeObject(export, Formatting.Indented);
                    var profileEntry = zipArchive.CreateEntry("profile.json");
                    using (var writer = new StreamWriter(profileEntry.Open()))
                    {
                        writer.Write(json);
                    }

                    foreach (var game in profile.Games)
                    {
                        foreach (var depot in game.Depots)
                        {
                            if (!string.IsNullOrEmpty(depot.ManifestId))
                            {
                                var manifestFileName = $"{depot.DepotId}_{depot.ManifestId}.manifest";
                                var manifestPath = Path.Combine(depotCachePath, manifestFileName);

                                if (File.Exists(manifestPath))
                                {
                                    zipArchive.CreateEntryFromFile(manifestPath, $"manifests/{manifestFileName}");
                                    manifestCount++;
                                }
                            }
                        }

                        foreach (var dlc in game.DLCs)
                        {
                            foreach (var depot in dlc.Depots)
                            {
                                if (!string.IsNullOrEmpty(depot.ManifestId))
                                {
                                    var manifestFileName = $"{depot.DepotId}_{depot.ManifestId}.manifest";
                                    var manifestPath = Path.Combine(depotCachePath, manifestFileName);

                                    if (File.Exists(manifestPath))
                                    {
                                        zipArchive.CreateEntryFromFile(manifestPath, $"manifests/{manifestFileName}");
                                        manifestCount++;
                                    }
                                }
                            }
                        }
                    }
                }

                _logger.Info($"Exported profile '{profile.Name}' to {filePath} with {manifestCount} manifest files");
                return (true, $"Exported profile with {profile.Games.Count} games and {manifestCount} manifest files", manifestCount);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to export profile: {ex.Message}");
                return (false, $"Failed to export profile: {ex.Message}", 0);
            }
        }

        public bool ExportProfile(string profileId, string filePath)
        {
            var result = ExportProfileAsZip(profileId, filePath);
            return result.success;
        }

        public (bool success, string message, GreenLumaProfile? profile) ImportProfile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "File not found", null);

                ProfileExport? export = null;
                int manifestCount = 0;

                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var steamPath = _steamService.GetSteamPath();
                    var depotCachePath = Path.Combine(steamPath, "depotcache");

                    using (var zipArchive = ZipFile.OpenRead(filePath))
                    {
                        var profileEntry = zipArchive.GetEntry("profile.json");
                        if (profileEntry == null)
                            return (false, "Invalid profile zip - missing profile.json", null);

                        using (var reader = new StreamReader(profileEntry.Open()))
                        {
                            var json = reader.ReadToEnd();
                            export = JsonConvert.DeserializeObject<ProfileExport>(json);
                        }

                        foreach (var entry in zipArchive.Entries)
                        {
                            if (entry.FullName.StartsWith("manifests/") && entry.Name.EndsWith(".manifest"))
                            {
                                var destPath = Path.Combine(depotCachePath, entry.Name);
                                if (!File.Exists(destPath))
                                {
                                    entry.ExtractToFile(destPath);
                                    manifestCount++;
                                }
                            }
                        }
                    }
                }
                else
                {
                    var json = File.ReadAllText(filePath);
                    export = JsonConvert.DeserializeObject<ProfileExport>(json);
                }

                if (export?.Profile == null)
                    return (false, "Invalid profile file format", null);

                export.Profile.Id = Guid.NewGuid().ToString();
                export.Profile.CreatedAt = DateTime.UtcNow;
                export.Profile.ModifiedAt = DateTime.UtcNow;

                var data = LoadProfiles();
                var existingNames = data.Profiles.Select(p => p.Name).ToHashSet();
                var baseName = export.Profile.Name;
                var newName = baseName;
                int counter = 1;
                while (existingNames.Contains(newName))
                {
                    newName = $"{baseName} ({counter++})";
                }
                export.Profile.Name = newName;

                data.Profiles.Add(export.Profile);
                SaveProfiles();

                int acfCount = 0;

                foreach (var game in export.Profile.Games)
                {
                    _fileInstallService.GenerateACF(game.AppId, game.AppId, game.Name, null);
                    acfCount++;
                }

                var message = $"Imported profile '{export.Profile.Name}' with {export.Profile.Games.Count} games";
                if (manifestCount > 0)
                    message += $", {manifestCount} manifest files";
                if (acfCount > 0)
                    message += $", {acfCount} ACF files generated";

                _logger.Info(message);
                return (true, message, export.Profile);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to import profile: {ex.Message}");
                return (false, $"Failed to import profile: {ex.Message}", null);
            }
        }

        public void InvalidateCache()
        {
            _profileData = null;
        }
    }
}
