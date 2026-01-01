using Microsoft.Win32;
using SolusManifestApp.Interfaces;
using SolusManifestApp.Models;
using System;
using System.IO;

namespace SolusManifestApp.Services
{
    public class SteamService : ISteamService
    {
        private string? _cachedSteamPath;
        private readonly SettingsService _settingsService;
        private readonly LoggerService? _logger;

        public SteamService(SettingsService settingsService, LoggerService? logger = null)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        public string? GetSteamPath()
        {
            if (!string.IsNullOrEmpty(_cachedSteamPath))
                return _cachedSteamPath;

            // Try registry first (64-bit)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (key != null)
                {
                    var installPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        _cachedSteamPath = installPath;
                        return installPath;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to read 64-bit Steam registry: {ex.Message}");
            }

            // Try registry (32-bit)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key != null)
                {
                    var installPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        _cachedSteamPath = installPath;
                        return installPath;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to read 32-bit Steam registry: {ex.Message}");
            }

            // Fallback to common locations
            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                {
                    _cachedSteamPath = path;
                    return path;
                }
            }

            return null;
        }

        public string? GetStPluginPath()
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
                return null;

            var stpluginPath = Path.Combine(steamPath, "config", "stplug-in");
            return stpluginPath;
        }

        public bool EnsureStPluginDirectory()
        {
            var stpluginPath = GetStPluginPath();
            if (string.IsNullOrEmpty(stpluginPath))
                return false;

            try
            {
                if (!Directory.Exists(stpluginPath))
                {
                    Directory.CreateDirectory(stpluginPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool ValidateSteamPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            return File.Exists(Path.Combine(path, "steam.exe"));
        }

        public void SetCustomSteamPath(string path)
        {
            if (ValidateSteamPath(path))
            {
                _cachedSteamPath = path;
            }
        }

        public bool IsSteamRunning()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("steam");
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public void RestartSteam()
        {
            try
            {
                // Kill Steam
                var processes = System.Diagnostics.Process.GetProcessesByName("steam");
                foreach (var process in processes)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }

                System.Threading.Thread.Sleep(2000);

                // Get settings
                var settings = _settingsService.LoadSettings();
                var steamPath = GetSteamPath();

                if (string.IsNullOrEmpty(steamPath))
                {
                    throw new Exception("Steam path not found");
                }

                var steamExe = Path.Combine(steamPath, "steam.exe");
                if (!File.Exists(steamExe))
                {
                    throw new Exception("steam.exe not found");
                }

                System.Diagnostics.Process.Start(steamExe);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to restart Steam: {ex.Message}", ex);
            }
        }
    }
}
