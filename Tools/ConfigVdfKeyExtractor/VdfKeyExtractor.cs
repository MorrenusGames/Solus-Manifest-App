using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SolusManifestApp.Tools.ConfigVdfKeyExtractor
{
    public class VdfKeyExtractor
    {
        public class ExtractionResult
        {
            public Dictionary<string, string> Keys { get; set; } = new Dictionary<string, string>();
            public int ValidKeysCount { get; set; }
            public int InvalidKeysCount { get; set; }
            public int SkippedKeysCount { get; set; }
            public int TotalExtractedCount { get; set; }
            public string? ErrorMessage { get; set; }
            public bool Success { get; set; }
        }

        public static string GetCombinedKeysPath()
        {
            // No default path - users must specify their own path
            return string.Empty;
        }

        public static HashSet<string> LoadExistingDepotIds(string combinedKeysPath)
        {
            var existingDepotIds = new HashSet<string>();

            if (!File.Exists(combinedKeysPath))
            {
                return existingDepotIds;
            }

            try
            {
                foreach (string line in File.ReadLines(combinedKeysPath))
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    if (trimmedLine.Contains(";"))
                    {
                        string depotId = trimmedLine.Split(';')[0].Trim();
                        if (!string.IsNullOrEmpty(depotId))
                        {
                            existingDepotIds.Add(depotId);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If we can't read the file, just return empty set
            }

            return existingDepotIds;
        }

        public static ExtractionResult ExtractKeysFromVdf(string vdfPath, string? combinedKeysPath = null)
        {
            var result = new ExtractionResult { Success = false };

            if (!File.Exists(vdfPath))
            {
                result.ErrorMessage = $"VDF file not found: {vdfPath}";
                return result;
            }

            try
            {
                // Load existing depot IDs from combinedkeys.key
                if (string.IsNullOrEmpty(combinedKeysPath))
                {
                    combinedKeysPath = GetCombinedKeysPath();
                }
                var existingDepotIds = LoadExistingDepotIds(combinedKeysPath);

                // Extract all keys from VDF
                string content = File.ReadAllText(vdfPath);
                var allExtractedKeys = ParseVdfContent(content, out int validCount, out int invalidCount);

                result.TotalExtractedCount = validCount;
                result.InvalidKeysCount = invalidCount;

                // Filter out keys that already exist in combinedkeys.key
                var newKeys = new Dictionary<string, string>();
                int skippedCount = 0;

                foreach (var kvp in allExtractedKeys)
                {
                    if (existingDepotIds.Contains(kvp.Key))
                    {
                        skippedCount++;
                    }
                    else
                    {
                        newKeys[kvp.Key] = kvp.Value;
                    }
                }

                result.Keys = newKeys;
                result.ValidKeysCount = newKeys.Count;
                result.SkippedKeysCount = skippedCount;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error reading VDF file: {ex.Message}";
            }

            return result;
        }

        private static Dictionary<string, string> ParseVdfContent(string content, out int validCount, out int invalidCount)
        {
            var depotKeys = new Dictionary<string, string>();
            validCount = 0;
            invalidCount = 0;

            // Primary pattern: "depot_id" { "DecryptionKey" "key_value" }
            string depotPattern = @"""(\d+)""\s*\{\s*""DecryptionKey""\s*""([^""]+)""\s*\}";
            var matches = Regex.Matches(content, depotPattern);

            if (matches.Count == 0)
            {
                // Try alternate pattern for depots section
                string depotsSectionPattern = @"""depots""\s*\{(.*?)\}";
                var depotSection = Regex.Match(content, depotsSectionPattern, RegexOptions.Singleline);

                if (depotSection.Success)
                {
                    string depotContent = depotSection.Groups[1].Value;
                    string depotEntriesPattern = @"""(\d+)""\s*\{[^\}]*""DecryptionKey""\s*""([^""]+)""[^\}]*\}";
                    matches = Regex.Matches(depotContent, depotEntriesPattern);
                }
            }

            foreach (Match match in matches)
            {
                string depotId = match.Groups[1].Value;
                string key = match.Groups[2].Value.Trim();

                if (ValidateDepotKey(key))
                {
                    depotKeys[depotId] = key;
                    validCount++;
                }
                else
                {
                    invalidCount++;
                }
            }

            return depotKeys;
        }

        public static bool ValidateDepotKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length != 64)
                return false;

            // Check if it's a valid hexadecimal string
            foreach (char c in key)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }

        public static string GetDefaultSteamConfigPath()
        {
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (string.IsNullOrEmpty(programFilesX86))
            {
                programFilesX86 = @"C:\Program Files (x86)";
            }

            return Path.Combine(programFilesX86, "Steam", "config", "config.vdf");
        }

        public static string FormatKeysAsText(Dictionary<string, string> keys)
        {
            var sortedKeys = new SortedDictionary<int, string>();

            foreach (var kvp in keys)
            {
                if (int.TryParse(kvp.Key, out int depotId))
                {
                    sortedKeys[depotId] = kvp.Value;
                }
            }

            var lines = new List<string>();
            foreach (var kvp in sortedKeys)
            {
                lines.Add($"{kvp.Key};{kvp.Value}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
