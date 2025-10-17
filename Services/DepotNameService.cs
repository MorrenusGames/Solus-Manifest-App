using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SolusManifestApp.Services
{
    /// <summary>
    /// Service for loading and looking up depot names from depots.ini file
    /// </summary>
    public class DepotNameService
    {
        private readonly Dictionary<string, string> _depotNames = new();
        private readonly LoggerService _logger;
        private bool _isLoaded = false;

        public DepotNameService()
        {
            _logger = new LoggerService();
        }

        /// <summary>
        /// Load depot names from depots.ini file
        /// </summary>
        public void LoadDepotNames(string iniFilePath)
        {
            try
            {
                if (!File.Exists(iniFilePath))
                {
                    _logger.Warning($"[DepotNameService] depots.ini not found at: {iniFilePath}");
                    return;
                }

                _logger.Info($"[DepotNameService] Loading depot names from: {iniFilePath}");
                _depotNames.Clear();

                var lines = File.ReadAllLines(iniFilePath);
                bool inDepotsSection = false;
                int loadedCount = 0;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    // Check for [depots] section
                    if (trimmed.Equals("[depots]", StringComparison.OrdinalIgnoreCase))
                    {
                        inDepotsSection = true;
                        continue;
                    }

                    // Check if we've left the [depots] section
                    if (trimmed.StartsWith("[") && !trimmed.Equals("[depots]", StringComparison.OrdinalIgnoreCase))
                    {
                        inDepotsSection = false;
                        continue;
                    }

                    // Only parse lines within [depots] section
                    if (!inDepotsSection)
                        continue;

                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                        continue;

                    // Parse: depotId = Depot Name
                    var match = Regex.Match(trimmed, @"^(\d+)\s*=\s*(.+)$");
                    if (match.Success)
                    {
                        var depotId = match.Groups[1].Value;
                        var depotName = match.Groups[2].Value.Trim();
                        _depotNames[depotId] = depotName;
                        loadedCount++;
                    }
                }

                _isLoaded = true;
                _logger.Info($"[DepotNameService] Loaded {loadedCount} depot names from depots.ini");
            }
            catch (Exception ex)
            {
                _logger.Error($"[DepotNameService] Failed to load depot names: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the friendly name for a depot ID, or return default if not found
        /// </summary>
        public string GetDepotName(string depotId, string defaultName = null)
        {
            if (_depotNames.TryGetValue(depotId, out var name))
            {
                return name;
            }

            return defaultName ?? $"Depot {depotId}";
        }

        /// <summary>
        /// Check if depot names have been loaded
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// Get total count of loaded depot names
        /// </summary>
        public int Count => _depotNames.Count;
    }
}
