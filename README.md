# Solus Manifest App

<div align="center">

**A comprehensive Steam depot and manifest management tool**

[![Latest Release](https://img.shields.io/github/v/release/MorrenusGames/Solus-Manifest-App?include_prereleases)](https://github.com/MorrenusGames/Solus-Manifest-App/releases/latest)

</div>

---

## Description

Solus Manifest App is a powerful Windows desktop application for managing Steam game depots and advanced Steam library management. Built with .NET 8 and WPF, it features a modern Steam-inspired interface with three distinct operation modes: SteamTools (Lua scripts), GreenLuma, and DepotDownloader.

## Key Features

### Three Operation Modes
- **SteamTools Mode**: Install and manage Lua scripts for Steam games
- **GreenLuma Mode**: Full GreenLuma 2024 integration with profile management (Normal, Stealth AnyFolder, Stealth User32)
- **DepotDownloader Mode**: Download actual game files from Steam CDN with language/depot selection

### Store & Downloads
- **Manifest Library**: Browse and search games from manifest.morrenus.xyz with pagination
- **One-Click Downloads**: Download game manifests with automatic depot key lookup
- **Language Selection**: Choose specific languages when downloading (DepotDownloader mode)
- **Depot Selection**: Fine-grained control over which depots to install, with main game toggle for DLC-only installs
- **Progress Tracking**: Real-time download progress with speed display
- **Auto-Installation**: Automatically install downloads upon completion

### Library Management
- **Multi-Source Library**: View Lua scripts, GreenLuma games, and Steam games in one place
- **Mode Filtering**: Automatically filters library by current mode (hides Lua in GreenLuma mode, vice versa)
- **Pagination System**: Display library in pages (10, 20, 50, 100, or show all)
- **Image Caching**: SQLite database caching with in-memory bitmap caching (~7MB for 100 games)
- **List/Grid View Toggle**: Switch between compact list and detailed grid views
- **Search & Sort**: Filter by name with multiple sorting options
- **Batch Operations**: Bulk enable/disable auto-updates with dedicated dialogs

### GreenLuma Profile System
- **Multiple Profiles**: Create, rename, and delete GreenLuma profiles
- **Profile Assignment**: Choose which profiles to install games to during download
- **DLC Tracking**: Automatically tracks DLC and groups depots under parent AppId
- **Smart Uninstall**: Games only removed when not in any other profile
- **Export/Import**: Export profiles with manifest files as ZIP for backup/sharing

### Integrated Tools
- **DepotDumper**: Extract depot information with 2FA QR code support
- **DepotDownloader**: Download files from Steam CDN with progress tracking
- **SteamAuth Pro**: Generate encrypted Steam authentication tickets
- **Config VDF Extractor**: Extract depot keys from Steam's config.vdf
- **GBE Token Generator**: Generate Goldberg emulator tokens

### User Experience
- **8 Themes**: Default, Dark, Light, Cherry, Sunset, Forest, Grape, Cyberpunk
- **DPI Scaling**: PerMonitorV2 support for high-DPI displays
- **Responsive UI**: Adapts to window sizes down to 800x600
- **Auto-Updates**: Three modes - Disabled, Check Only, Auto Download & Install
- **System Tray**: Minimize to tray with quick access menu
- **Toast Notifications**: Native Windows 10+ notifications (can be disabled)
- **Protocol Handler**: `solus://` URLs for quick downloads
- **Single Instance**: Prevents multiple app instances
- **Settings Backup**: Export and import settings and mod lists

## Installation

### Quick Start

1. Download the latest release from [Releases](https://github.com/MorrenusGames/Solus-Manifest-App/releases)
2. Run `SolusManifestApp.exe`

**That's it!** No installation required. Self-contained single-file executable with all dependencies embedded.

### Requirements

- Windows 10 version 1903 or later
- ~200MB disk space
- Internet connection for downloading depots

### First Launch

On first launch, the app will:
- Create settings in `%AppData%\SolusManifestApp`
- Detect your Steam installation automatically
- Create local SQLite database for library caching

## Configuration

Settings are stored in `%AppData%\SolusManifestApp` and include:

| Category | Options |
|----------|---------|
| Mode | SteamTools, GreenLuma, DepotDownloader |
| GreenLuma | Normal, Stealth AnyFolder, Stealth User32 |
| Downloads | Auto-install, delete ZIP after install, output path |
| Display | Theme selection, window size/position, list/grid view |
| Notifications | Enable/disable toasts and popups |
| Auto-Update | Disabled, Check Only, Auto Download & Install |
| Keys | Auto-upload config keys to community database (hourly) |

## Technology

- .NET 8.0 with WPF
- Self-contained single-file executable
- SteamKit2 for Steam server queries
- SQLite for local caching
- Windows Toast Notifications

## Credits

### Integrated Tools
- [DepotDumper](https://github.com/NicknineTheEagle/DepotDumper) by NicknineTheEagle
- [DepotDownloader](https://github.com/SteamRE/DepotDownloader) by SteamRE

### Community
Thanks to Melly from [Lua Tools](https://discord.gg/Qxeq7RmhXw) and the Morrenus Games community for inspiration, testing, and feedback.

---

<div align="center">

[Discord](https://discord.gg/morrenusgames) | [Website](https://manifest.morrenus.xyz) | [GitHub](https://github.com/MorrenusGames/Solus-Manifest-App)

</div>
