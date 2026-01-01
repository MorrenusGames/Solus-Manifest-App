# Solus Manifest App

<div align="center">

**A comprehensive Steam depot and manifest management tool**

[![Latest Release](https://img.shields.io/github/v/release/MorrenusGames/Solus-Manifest-App?include_prereleases)](https://github.com/MorrenusGames/Solus-Manifest-App/releases/latest)

</div>

---

## Description

Solus Manifest App is a powerful Windows desktop application for managing Steam game depots and advanced Steam library management. Built with .NET 8 and WPF, it features a modern Steam-inspired interface with two operation modes: SteamTools (Lua scripts) and DepotDownloader.

## Key Features

### Two Operation Modes
- **SteamTools Mode**: Install and manage Lua scripts for Steam games
- **DepotDownloader Mode**: Download actual game files from Steam CDN with language/depot selection

### Store & Downloads
- **Manifest Library**: Browse and search games from manifest.morrenus.xyz with pagination
- **One-Click Downloads**: Download game manifests with automatic depot key lookup
- **Language Selection**: Choose specific languages when downloading (DepotDownloader mode)
- **Depot Selection**: Fine-grained control over which depots to install
- **Progress Tracking**: Real-time download progress with speed display
- **Auto-Installation**: Automatically install downloads upon completion

### Library Management
- **Multi-Source Library**: View Lua scripts and Steam games in one place
- **Pagination System**: Display library in pages (10, 20, 50, 100, or show all)
- **Image Caching**: SQLite database caching with in-memory bitmap caching (~7MB for 100 games)
- **List/Grid View Toggle**: Switch between compact list and detailed grid views
- **Search & Sort**: Filter by name with multiple sorting options
- **Batch Operations**: Bulk enable/disable auto-updates with dedicated dialogs

### Integrated Tools
- **DepotDumper**: Extract depot information with 2FA QR code support
- **DepotDownloader**: Download files from Steam CDN with progress tracking
- **Config VDF Extractor**: Extract depot keys from Steam's config.vdf
- **GBE Token Generator**: Generate Goldberg emulator tokens

### User Experience
- **8 Themes**: Default, Dark, Light, Cherry, Sunset, Forest, Grape, Cyberpunk
- **DPI Scaling**: PerMonitorV2 support for high-DPI displays
- **Responsive UI**: Adapts to window sizes down to 800x600
- **Auto-Updates**: Three modes - Disabled, Check Only, Auto Download & Install
- **System Tray**: Minimize to tray with quick access menu
- **Toast Notifications**: Native Windows 10+ notifications (can be disabled)
- **Protocol Handler**: `solusapp://` URI scheme for quick actions
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
| Mode | SteamTools, DepotDownloader |
| Downloads | Auto-install, delete ZIP after install, output path |
| Display | Theme selection, window size/position, list/grid view |
| Notifications | Enable/disable toasts and popups |
| Auto-Update | Disabled, Check Only, Auto Download & Install |
| Keys | Auto-upload config keys to community database (hourly) |

## URI Scheme

The app registers a `solusapp://` protocol handler for quick actions from web browsers or other applications.

| URL Format | Action |
|------------|--------|
| `solusapp://download/{appId}` | Download manifest for the specified App ID |
| `solusapp://install/{appId}` | Install a previously downloaded game |
| `solusapp://download/install/{appId}` | Download and install in one step |

**Examples:**
- `solusapp://download/400` - Downloads Half-Life 2 manifest
- `solusapp://download/install/400` - Downloads and installs Half-Life 2

The protocol is automatically registered on first launch and updates if the app location changes.

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
