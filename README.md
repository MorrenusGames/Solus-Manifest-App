# Solus Manifest App

<div align="center">

**A comprehensive Steam depot and manifest management tool**

[![Build and Release](https://github.com/MorrenusGames/Solus-Manifest-App/workflows/Build%20and%20Release/badge.svg)](https://github.com/MorrenusGames/Solus-Manifest-App/actions)
[![Latest Release](https://img.shields.io/github/v/release/MorrenusGames/Solus-Manifest-App)](https://github.com/MorrenusGames/Solus-Manifest-App/releases/latest)

</div>

---

## 📖 Description

Solus Manifest App is a powerful Windows desktop application for managing Steam game depots and advanced Steam library management. Built with .NET 8 and WPF, it features a modern Steam-inspired interface with comprehensive depot and manifest management capabilities.

## ✨ Key Features

### Depot & Manifest Management
- **Browse & Download Depots**: Search and download Steam depots with automatic key lookup
- **Manifest Downloads**: Download specific game manifests and depot files
- **Version Management**: Download and install specific game versions using manifests
- **DepotDownloader Integration**: Built-in DepotDownloader with progress tracking and notifications
- **Depot Name Lookup**: Built-in database of depot names from `depots.ini`

### Advanced Tools
- **DepotDumper**: Integrated DepotDumper with 2FA support and QR code authentication
- **Config VDF Key Extraction**: Extract depot keys from Steam's config.vdf
- **Auto Config Keys Upload**: Automatically upload new depot keys to Morrenus database (hourly)
- **SteamAuth Pro**: Generate encrypted tickets for Steam authentication
- **Protocol Handler**: `solus://` URL protocol for quick downloads

### Library Management
- **Library View**: Manage installed games, Lua scripts, and GreenLuma apps
- **Performance Optimized**: Fast loading with background threading
- **Steam Integration**: Automatically detect installed Steam games
- **GreenLuma 2024**: Full integration with multiple installation modes

### User Experience
- **8 Premium Themes**: Default, Dark, Light, Cherry, Sunset, Forest, Grape, Cyberpunk
- **Auto-Updates**: Automatic update checking with one-click downloads
- **System Tray**: Minimize to tray with quick access menu
- **Recent Games**: Quick access to recently played games
- **Notifications**: Toast notifications for downloads and operations

## 🚀 Installation

### Quick Start

1. Download the latest release from [Releases](https://github.com/MorrenusGames/Solus-Manifest-App/releases)
2. Download both files:
   - `SolusManifestApp.exe` (main application)
   - `depots.ini` (depot name database)
3. Place both files in the same folder
4. Run `SolusManifestApp.exe`

**That's it!** No installation, no .NET runtime required. Everything is embedded in the single executable.

### Requirements

- Windows 10 version 1903 or later
- ~200MB disk space for the application
- Internet connection for downloading depots

### First Launch

On first launch, the app will:
- Extract Resources folder to a temporary location
- Create settings in `%AppData%\SolusManifestApp`
- Detect your Steam installation automatically

For detailed documentation, see the [Wiki](https://github.com/MorrenusGames/Solus-Manifest-App/wiki).

## 🙏 Credits & Thanks

### DepotDumper
This application integrates [**DepotDumper**](https://github.com/NicknineTheEagle/DepotDumper) by **NicknineTheEagle**.

**Our Changes:**
- Added WPF UI integration with Steam-themed design
- Implemented 2FA dialog with QR code support
- Added automatic key upload to manifest.morrenus.xyz
- Integrated into tabbed interface with real-time logging
- Enhanced error handling and user feedback

### Inspiration & Community
A **huge thank you** to **Melly** from [**Lua Tools**](https://discord.gg/Qxeq7RmhXw) and their incredible Discord community. Your inspiration, guidance, and drive pushed me to create this tool for our community. The support and feedback from everyone has been invaluable.

### Our Community
**Thank you** to all the users and members of the Morrenus Games community. Your feedback, testing, and enthusiasm make this project worthwhile. This tool was built for you, by you.

Special shoutout to:
- Everyone who tested early builds
- Community members who reported bugs and suggested features
- The Discord server for constant support and encouragement

---

<div align="center">

**Made with ❤️ for the Steam community**

[Discord](https://discord.gg/morrenusgames) • [Website](https://manifest.morrenus.xyz) • [GitHub](https://github.com/MorrenusGames/Solus-Manifest-App)

</div>
