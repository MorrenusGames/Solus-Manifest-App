using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SolusManifestApp.Services
{
    /// <summary>
    /// Filters depots using the same logic as the Python bot's depot_downloader_commands.py
    /// Specifically ports the _get_depots_for_language() method
    /// </summary>
    public class DepotFilterService
    {
        private readonly LoggerService _logger;

        public DepotFilterService(LoggerService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets available languages from SteamCMD data
        /// Port of Python's _get_available_languages() method (lines 257-271)
        /// Only shows languages that have depots with keys
        /// </summary>
        public List<string> GetAvailableLanguages(SteamCmdDepotData? steamCmdData, string appId, Dictionary<string, string>? depotKeys = null)
        {
            var languages = new List<string>();
            var hasBaseDepots = false; // Tracks if there are depots with no language (treated as English/base)

            if (steamCmdData?.Data == null || !steamCmdData.Data.ContainsKey(appId))
                return new List<string> { "English" };

            var depots = steamCmdData.Data[appId].Depots;

            foreach (var kvp in depots)
            {
                var depotId = kvp.Key;
                var depotInfo = kvp.Value;

                // Skip non-numeric depot IDs
                if (!long.TryParse(depotId, out _))
                    continue;

                // If depot keys provided, only consider depots that have keys
                if (depotKeys != null && !depotKeys.ContainsKey(depotId))
                    continue;

                // Skip depots without manifests
                if (depotInfo.Manifests == null || !depotInfo.Manifests.Any())
                    continue;

                // Skip if all manifest values are null/empty
                if (depotInfo.Manifests.All(m => m.Value == null || string.IsNullOrEmpty(m.Value.Gid)))
                    continue;

                // Skip depots with steamchina realm
                if (depotInfo?.Config?.Realm != null && depotInfo.Config.Realm.Equals("steamchina", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (depotInfo?.Config != null && !string.IsNullOrEmpty(depotInfo.Config.Language))
                {
                    var lang = depotInfo.Config.Language;
                    if (!languages.Contains(lang) && !string.IsNullOrEmpty(lang))
                    {
                        languages.Add(lang);
                    }
                }
                else
                {
                    // Depot has no language specified - this counts as base/English depot
                    hasBaseDepots = true;
                }
            }

            // Only include English if there are base depots (no language) OR explicit English depots
            if ((hasBaseDepots || languages.Contains("english")) && !languages.Contains("english"))
            {
                languages.Insert(0, "english");
            }

            // If no languages found at all (shouldn't happen but safeguard)
            if (languages.Count == 0)
            {
                _logger.Warning("No languages found in depots - defaulting to 'English'");
                return new List<string> { "English" };
            }

            // Convert all languages to Title Case for professional display
            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            return languages.OrderBy(l => l).Select(l => textInfo.ToTitleCase(l.ToLower())).ToList();
        }

        /// <summary>
        /// Filters depots for a specific language using Python bot's logic
        /// Port of Python's _get_depots_for_language() method (lines 339-499)
        /// </summary>
        public List<string> GetDepotsForLanguage(
            SteamCmdDepotData? steamCmdData,
            Dictionary<string, string> depotKeys,
            string language,
            string appId,
            HashSet<string>? blacklistedApps = null,
            HashSet<string>? blockedDepots = null)
        {
            var baseDepots = new List<string>();
            var languageDepots = new List<string>();

            blacklistedApps ??= new HashSet<string>();
            blockedDepots ??= new HashSet<string>();

            if (steamCmdData?.Data == null || !steamCmdData.Data.ContainsKey(appId))
            {
                _logger.Warning($"No SteamCMD data found for app {appId}");
                return new List<string>();
            }

            var depots = steamCmdData.Data[appId].Depots;

            _logger.Info($"Looking for depots for language: {language}");
            _logger.Debug($"Available depot keys: {string.Join(", ", depotKeys.Keys)}");
            _logger.Debug($"Blacklisted apps: {string.Join(", ", blacklistedApps)}");
            _logger.Debug($"Blocked depots: {string.Join(", ", blockedDepots)}");

            // MAIN GAME DEPOTS - Python lines 354-403
            foreach (var kvp in depots)
            {
                var depotId = kvp.Key;
                var depotInfo = kvp.Value;

                // Skip non-numeric depot IDs (like "branches")
                if (!long.TryParse(depotId, out _))
                    continue;

                // Must have depot key
                if (!depotKeys.ContainsKey(depotId))
                    continue;

                // Must have manifests
                if (depotInfo.Manifests == null || !depotInfo.Manifests.Any())
                    continue;

                // Skip if all manifest values are null/empty
                if (depotInfo.Manifests.All(m => m.Value == null || string.IsNullOrEmpty(m.Value.Gid)))
                {
                    _logger.Debug($"Skipping depot {depotId} - empty manifests");
                    continue;
                }

                // Skip DLC depots (handled separately)
                if (!string.IsNullOrEmpty(depotInfo.DlcAppId))
                    continue;

                // Skip shared depots (handled separately)
                if (!string.IsNullOrEmpty(depotInfo.DepotFromApp))
                    continue;

                // Check if depot is blocked
                if (blockedDepots.Contains(depotId))
                {
                    _logger.Debug($"Skipping depot {depotId} - blocked depot");
                    continue;
                }

                _logger.Debug($"Checking depot {depotId}: {(depotInfo.Config != null ? $"oslist={depotInfo.Config.OsList}, language={depotInfo.Config.Language}, lowviolence={depotInfo.Config.LowViolence}" : "no config")}");

                if (depotInfo.Config != null)
                {
                    var config = depotInfo.Config;

                    // Filter: SteamChina realm
                    if (config.Realm != null && config.Realm.Equals("steamchina", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Debug($"Skipping depot {depotId} - steamchina realm");
                        continue;
                    }

                    // Filter: Low violence (Python line 378-380)
                    if (config.LowViolence == "1")
                    {
                        _logger.Debug($"Skipping depot {depotId} - low violence version");
                        continue;
                    }

                    // Filter: macOS only (Python line 382-384)
                    if (config.OsList == "macos")
                    {
                        _logger.Debug($"Skipping depot {depotId} - macOS only");
                        continue;
                    }

                    // Filter: Linux only (Python line 386-388)
                    if (config.OsList == "linux")
                    {
                        _logger.Debug($"Skipping depot {depotId} - Linux only");
                        continue;
                    }

                    // Language filtering (Python lines 390-400)
                    var depotLanguage = config.Language ?? "";

                    if (language.ToLower() == "english")
                    {
                        if (string.IsNullOrEmpty(depotLanguage))
                        {
                            baseDepots.Add(depotId);
                            _logger.Debug($"Added main depot {depotId} for English (no language)");
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(depotLanguage))
                        {
                            baseDepots.Add(depotId);
                            _logger.Debug($"Added base depot {depotId} (no language)");
                        }
                        else if (depotLanguage.ToLower() == language.ToLower())
                        {
                            languageDepots.Add(depotId);
                            _logger.Debug($"Added language depot {depotId} for {language}");
                        }
                    }
                }
                else
                {
                    baseDepots.Add(depotId);
                    _logger.Debug($"Added depot {depotId} (no config)");
                }
            }

            // DLC DEPOTS - Python lines 405-448
            foreach (var kvp in depots)
            {
                var depotId = kvp.Key;
                var depotInfo = kvp.Value;

                if (!long.TryParse(depotId, out _))
                    continue;

                if (!depotKeys.ContainsKey(depotId))
                    continue;

                // Only process DLC depots
                if (string.IsNullOrEmpty(depotInfo.DlcAppId))
                    continue;

                if (depotInfo.Manifests == null || !depotInfo.Manifests.Any())
                    continue;

                if (depotInfo.Manifests.All(m => m.Value == null || string.IsNullOrEmpty(m.Value.Gid)))
                {
                    _logger.Debug($"Skipping DLC depot {depotId} - empty manifests");
                    continue;
                }

                if (blockedDepots.Contains(depotId))
                {
                    _logger.Debug($"Skipping DLC depot {depotId} - blocked depot");
                    continue;
                }

                _logger.Debug($"Checking DLC depot {depotId}: {(depotInfo.Config != null ? $"oslist={depotInfo.Config.OsList}, language={depotInfo.Config.Language}" : "no config")}");

                if (depotInfo.Config != null)
                {
                    var config = depotInfo.Config;

                    // Filter: SteamChina realm
                    if (config.Realm != null && config.Realm.Equals("steamchina", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Debug($"Skipping DLC depot {depotId} - steamchina realm");
                        continue;
                    }

                    // Same filters as main depots
                    if (config.LowViolence == "1")
                    {
                        _logger.Debug($"Skipping DLC depot {depotId} - low violence version");
                        continue;
                    }

                    if (config.OsList == "macos")
                    {
                        _logger.Debug($"Skipping DLC depot {depotId} - macOS only");
                        continue;
                    }

                    if (config.OsList == "linux")
                    {
                        _logger.Debug($"Skipping DLC depot {depotId} - Linux only");
                        continue;
                    }

                    var depotLanguage = config.Language ?? "";

                    if (string.IsNullOrEmpty(depotLanguage))
                    {
                        baseDepots.Add(depotId);
                        _logger.Debug($"Added DLC depot {depotId} (no language)");
                    }
                    else if (depotLanguage.ToLower() == language.ToLower())
                    {
                        languageDepots.Add(depotId);
                        _logger.Debug($"Added DLC language depot {depotId} for {language}");
                    }
                }
                else
                {
                    baseDepots.Add(depotId);
                    _logger.Debug($"Added DLC depot {depotId} (no config)");
                }
            }

            // SHARED DEPOTS - Python lines 450-493
            foreach (var kvp in depots)
            {
                var depotId = kvp.Key;
                var depotInfo = kvp.Value;

                if (!long.TryParse(depotId, out _))
                    continue;

                if (!depotKeys.ContainsKey(depotId))
                    continue;

                // Only process shared depots
                if (string.IsNullOrEmpty(depotInfo.DepotFromApp) ||
                    string.IsNullOrEmpty(depotInfo.SharedInstall))
                    continue;

                if (blockedDepots.Contains(depotId))
                {
                    _logger.Debug($"Skipping shared depot {depotId} - blocked depot");
                    continue;
                }

                var depotFromApp = depotInfo.DepotFromApp;

                // Check if source app is blacklisted (Python line 462-464)
                if (blacklistedApps.Contains(depotFromApp))
                {
                    _logger.Debug($"Skipping shared depot {depotId} - from blacklisted app {depotFromApp}");
                    continue;
                }

                if (depotInfo.Config != null)
                {
                    var config = depotInfo.Config;

                    // Filter: SteamChina realm
                    if (config.Realm != null && config.Realm.Equals("steamchina", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Debug($"Skipping shared depot {depotId} - steamchina realm");
                        continue;
                    }

                    // Same filters
                    if (config.LowViolence == "1")
                    {
                        _logger.Debug($"Skipping shared depot {depotId} - low violence version");
                        continue;
                    }

                    if (config.OsList == "macos")
                    {
                        _logger.Debug($"Skipping shared depot {depotId} - macOS only");
                        continue;
                    }

                    if (config.OsList == "linux")
                    {
                        _logger.Debug($"Skipping shared depot {depotId} - Linux only");
                        continue;
                    }

                    var depotLanguage = config.Language ?? "";

                    // Language check for shared depots (Python line 484-486)
                    if (!string.IsNullOrEmpty(depotLanguage) && depotLanguage.ToLower() != language.ToLower())
                    {
                        _logger.Debug($"Skipping shared depot {depotId} - wrong language ({depotLanguage} != {language})");
                        continue;
                    }

                    // Add to appropriate list (Python line 488-493)
                    if (!string.IsNullOrEmpty(depotLanguage))
                    {
                        languageDepots.Add(depotId);
                        _logger.Debug($"Added shared language depot {depotId}");
                    }
                    else
                    {
                        baseDepots.Add(depotId);
                        _logger.Debug($"Added shared depot {depotId}");
                    }
                }
            }

            // Combine: base depots first, then language depots (Python line 495-498)
            var finalDepotList = baseDepots.Concat(languageDepots).ToList();

            _logger.Info($"Final depot list (base first, then language): {string.Join(", ", finalDepotList)}");
            _logger.Debug($"Base depots: {string.Join(", ", baseDepots)}");
            _logger.Debug($"Language depots: {string.Join(", ", languageDepots)}");

            return finalDepotList;
        }

        /// <summary>
        /// Extracts depot keys from lua content
        /// Port of Python's _extract_depot_keys_from_lua() method (lines 203-209)
        /// </summary>
        public Dictionary<string, string> ExtractDepotKeysFromLua(string luaContent)
        {
            var depotKeys = new Dictionary<string, string>();
            var lines = luaContent.Split('\n');

            foreach (var line in lines)
            {
                // Match: addappid(285311, 1, "hash")
                var match = System.Text.RegularExpressions.Regex.Match(
                    line.Trim(),
                    @"addappid\((\d+),\s*\d+,\s*""([a-f0-9]+)""\)");

                if (match.Success)
                {
                    var depotId = match.Groups[1].Value;
                    var key = match.Groups[2].Value;
                    depotKeys[depotId] = key;
                }
            }

            return depotKeys;
        }
    }
}
