# Solus Manifest App

<div align="center">

**A comprehensive Steam depot management and game downgrading tool**

[![Build](https://github.com/yourusername/solus-manifest-app/workflows/Build/badge.svg)](https://github.com/yourusername/solus-manifest-app/actions)
[![Release](https://github.com/yourusername/solus-manifest-app/workflows/Build%20and%20Release/badge.svg)](https://github.com/yourusername/solus-manifest-app/releases)

[Features](#features) • [Installation](#installation) • [Usage](#usage) • [Tools](#integrated-tools) • [Support](#support)

</div>

---

## Overview

Solus Manifest App is a powerful Windows desktop application for managing Steam game depots, downgrading games to previous versions, and advanced Steam library management. Built with .NET 8 and WPF, it features a modern Steam-inspired interface with comprehensive depot and manifest management capabilities.

## ✨ Key Features

### 📦 Depot & Manifest Management
- **Download & Install Depots** - Access and install Steam game depots and manifests
- **Game Downgrading** - Revert games to previous versions using historical manifests
- **Manifest Database** - Browse and search thousands of game manifests via integrated API
- **Depot Selection Dialog** - Choose specific depots to download (base game, DLCs, languages)
- **Version Control** - Install specific game builds by manifest ID or date

### 🔧 Integrated Tools

#### Depot Key Dumper
- Extract Steam depot decryption keys directly from your account
- Support for username/password, QR code, and anonymous login
- Dump all account depots or specific app IDs
- Unreleased apps support
- **Upload to Server** - Automatically upload dumped keys to manifest.morrenus.xyz
- Real-time progress tracking and logging

#### Config VDF Key Extractor
- Extract depot keys from Steam's `config.vdf` file
- Compare against existing keys to find new entries
- Filter duplicates automatically
- Export keys in multiple formats
- **Persistent Settings** - Remembers file paths between sessions

#### Lua Installer
- Manage and install Lua-based game modifications
- Batch installation support
- Steam integration for seamless deployment

### 🎮 GreenLuma Integration

**Full GreenLuma 2024 Support:**
- **Three Installation Modes:**
  - Normal Mode - Standard GreenLuma installation
  - Stealth AnyFolder - Install to any directory
  - Stealth User32 - System32 injection method
- **Automated Management:**
  - AppList generation and updates
  - Config.vdf depot key injection
  - DLL injector configuration
  - .lua file management
- **Smart Uninstallation:**
  - Removes ALL depot files (main app + DLC depots)
  - Cleans up config.vdf depot keys
  - Deletes associated .lua files
  - Removes ACF files from Steam library

### 📚 Steam Library Features
- **Automatic Detection** - Finds Steam installation and all library folders
- **Library Management** - View and manage installed games and mods
- **Game Metadata** - Displays game icons, names, and versions
- **Icon Caching** - Fast loading with intelligent cache system
- **Launch Games** - Start games directly from the app
- **Multi-Library Support** - Handles multiple Steam library locations

### 🎨 User Interface

**8 Premium Themes:**
- Default (Steam Dark)
- Dark Theme
- Light Theme
- Cherry Theme
- Sunset Theme
- Forest Theme
- Grape Theme
- Cyberpunk Theme

**Modern Design:**
- Steam-inspired UI with rounded corners and gradients
- Smooth animations and transitions
- Responsive layout
- Tabbed tools interface

### 📥 Download Management
- **Queue System** - Manage multiple downloads simultaneously
- **Progress Tracking** - Real-time download speed and progress
- **Auto-Installation** - Seamlessly install after download (optional)
- **Resume Support** - Continue interrupted downloads
- **Archive Extraction** - Automatic ZIP/7z extraction

### ⚙️ Additional Features
- **Settings Backup/Restore** - Export and import your configuration
- **Update Checker** - Automatic update notifications
- **Comprehensive Logging** - Detailed logs for troubleshooting
- **Cache Management** - Clear cached icons and data
- **API Key Management** - Support for multiple API keys with history

## 🚀 Installation

### Option 1: Download Release (Recommended)

**For Stable Releases:**
1. Go to [Releases](https://github.com/yourusername/solus-manifest-app/releases)
2. Download a versioned release: `SolusManifestApp-v1.0.0-win-x64.zip`
3. Extract to a folder
4. Run `SolusManifestApp.exe`

**For Latest Development Build:**
1. Go to [Releases](https://github.com/yourusername/solus-manifest-app/releases)
2. Find the "Latest Build (Development)" pre-release
3. Download `SolusManifestApp-latest-win-x64.zip`
4. Extract and run `SolusManifestApp.exe`
   - ⚠️ Warning: May be unstable, use for testing only

**No installation required!** All releases include the .NET runtime.

### Option 2: Build from Source

**Requirements:**
- Windows 10/11
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code (optional)

**Steps:**

```bash
# Clone the repository
git clone https://github.com/yourusername/solus-manifest-app.git
cd solus-manifest-app

# Restore dependencies
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run --configuration Release
```

### Option 3: Publish Standalone

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\SolusManifestApp.exe`

## 📖 Usage

### First Time Setup

1. **Launch the application**

2. **Configure Settings** (⚙️ Settings tab)
   - **API Key**: Enter your manifest API key
     - Click "Validate" to verify
     - API keys are saved to history
   - **Steam Path**: Usually auto-detected
     - Click "Auto-Detect" if needed
     - Or "Browse" to select manually
   - **Downloads Folder**: Choose where downloads are saved
     - Default: `Documents\SolusManifestApp\Downloads`

3. **Select Tool Mode**
   - **SteamTools Mode**: Standard manifest installation
   - **GreenLuma Mode**: Advanced depot management
     - Choose sub-mode: Normal, Stealth AnyFolder, or Stealth User32
     - Configure GreenLuma paths

4. **Save Settings**

### Using the Store

1. Go to **Store** tab (🛒)
2. Search for games by name or App ID
3. Click **"Download"** on any game card
4. Monitor progress in **Downloads** tab

### Managing Downloads

1. Go to **Downloads** tab (⬇️)
2. **Active Downloads** - View current downloads with progress
3. **Ready to Install** - Install downloaded manifests
   - Click "Install" to deploy to Steam
   - Choose installation location (for GreenLuma mode)

### Using Library

1. Go to **Library** tab (📚)
2. View all installed games and mods
3. **Uninstall** games (fully removes depots, keys, and files)
4. **Launch** games directly
5. Filter by name or App ID

### Lua Installer

1. Go to **Lua Installer** tab
2. Upload .lua files or paste Lua code
3. Select installation mode
4. Click "Install" to deploy to Steam

## 🔧 Integrated Tools

### Depot Dumper

**Access:** Tools → DepotDumper tab

**Features:**
- **Login Methods:**
  - Username/Password
  - QR Code (scan with Steam Mobile)
  - Anonymous
- **Dump Options:**
  - All apps in account
  - Specific App ID
  - Include unreleased apps
- **Output Files:**
  - `{steamid}_keys.txt` - Depot decryption keys
  - `{steamid}_apps.txt` - App tokens
  - `{steamid}_appnames.txt` - App/depot names
  - For single app: `app_{appid}_keys.txt`, `app_{appid}_token.txt`
- **Upload Feature:**
  - Automatically uploads keys to manifest.morrenus.xyz
  - Uses API key from settings
  - Shows upload progress and results
  - Validates and removes invalid lines

**Usage:**
1. Select login method
2. Enter credentials (or scan QR code)
3. Optional: Enter specific App ID
4. Check "Dump unreleased apps" if needed
5. Click "SIGN IN"
6. Wait for dumping to complete
7. Click "📤 UPLOAD TO SERVER" to share keys (optional)
8. Click "DONE" to return

### Config VDF Key Extractor

**Access:** Tools → Config VDF Key Extractor tab

**Features:**
- Extract keys from Steam's `config.vdf`
- Compare against existing `combinedkeys.key`
- Show only NEW keys (skip duplicates)
- Display valid/invalid/skipped counts
- Copy to clipboard or save to file
- **Persistent Paths** - Saves file locations to settings

**Usage:**
1. Click "Browse" to select `config.vdf` file
   - Default location: `C:\Program Files (x86)\Steam\config\config.vdf`
2. Optional: Select `combinedkeys.key` for comparison
3. Click "EXTRACT KEYS"
4. Review results (new keys only)
5. Click "COPY TO CLIPBOARD" or "SAVE TO FILE"

**Output Format:**
```
228980;HEXKEY1234567890ABCDEF
271590;HEXKEY0987654321FEDCBA
```

### External Resources

**Access:** Tools → External Resources tab

Quick links to:
- **SteamTools** - Download SteamTools for standard game management
- **GreenLuma** - Download latest GreenLuma version
- **Solus Manifest Website** - Browse the manifest database
- **Discord Community** - Join for support and updates

## 🎮 GreenLuma Setup

### Installation Modes

**Normal Mode:**
- Installs to Steam root directory
- Traditional GreenLuma setup
- Requires Steam restart

**Stealth AnyFolder:**
- Install GreenLuma to any directory
- Separate from Steam installation
- More flexible configuration

**Stealth User32:**
- System32 DLL injection method
- Advanced users only
- Maximum stealth

### Configuration

1. **Enable GreenLuma Mode** in Settings
2. **Select Sub-Mode**
3. **Configure Paths:**
   - AppList folder (where depot IDs are stored)
   - DLL Injector path (for Stealth modes)
4. **Save Settings**

### Installing Games

1. Download manifest from Store
2. Go to Downloads → Ready to Install
3. Click "Install"
4. For GreenLuma:
   - Automatically creates AppList .txt files
   - Injects depot keys into config.vdf
   - Generates .lua files if needed
   - Creates ACF files in Steam library

### Uninstalling Games

1. Go to Library
2. Click "Uninstall" on game card
3. Removal process:
   - ✅ Deletes ALL AppList files (main + DLC depots)
   - ✅ Removes depot keys from config.vdf
   - ✅ Deletes .lua files
   - ✅ Removes ACF files from Steam library
4. Restart Steam to apply changes

## 📁 File Locations

### Application Data
```
%AppData%\SolusManifestApp\
├── settings.json          # App configuration
├── Cache\                 # Cached icons and data
└── solus_*.log           # Application logs
```

### Downloads
```
%UserProfile%\Documents\SolusManifestApp\Downloads\
├── manifests\            # Downloaded manifest ZIPs
└── extracted\            # Extracted game files
```

### GreenLuma Files
```
Steam\
├── config\
│   └── config.vdf        # Depot keys injected here
├── AppList\              # Depot ID files (Normal mode)
└── steamapps\
    └── *.acf             # Game manifest files
```

## 🔑 API Integration

### Manifest API

**Base URL:** `https://manifest.morrenus.xyz/api/v1`

**Endpoints:**
- `GET /manifest/{appid}` - Get manifest for specific app
- `GET /manifest/search?q={query}` - Search manifests
- `GET /status/{appid}` - Check app status
- `POST /upload` - Upload depot keys (requires auth)

**Authentication:**
```http
Authorization: Bearer {api_key}
```

**Upload Format:**
```
Content-Type: multipart/form-data

file: depot_keys.txt
```

**Response:**
```json
{
  "valid_lines": 150,
  "invalid_lines_removed": 5,
  "message": "Upload successful"
}
```

### Settings Schema

```json
{
  "SteamPath": "C:\\Program Files (x86)\\Steam",
  "ApiKey": "smm_your_key_here",
  "DownloadsPath": "C:\\Users\\...\\Downloads",
  "Mode": "GreenLuma",
  "GreenLumaSubMode": "Normal",
  "AppListPath": "C:\\Steam\\AppList",
  "DLLInjectorPath": "C:\\GreenLuma\\Injector.exe",
  "UseDefaultInstallLocation": true,
  "SelectedLibraryFolder": "",
  "Theme": "Default",
  "AutoCheckUpdates": true,
  "ShowNotifications": true,
  "ConfigVdfPath": "C:\\Steam\\config\\config.vdf",
  "CombinedKeysPath": "C:\\Keys\\combinedkeys.key",
  "ApiKeyHistory": ["smm_key1", "smm_key2"]
}
```

## 🛠️ Development

### Project Structure

```
SolusManifestApp/
├── Models/                    # Data models
│   ├── AppSettings.cs
│   ├── Manifest.cs
│   ├── LibraryItem.cs
│   └── GreenLumaGame.cs
├── Services/                  # Business logic
│   ├── SteamService.cs
│   ├── ManifestApiService.cs
│   ├── FileInstallService.cs
│   ├── DepotDownloadService.cs
│   └── SettingsService.cs
├── ViewModels/                # MVVM ViewModels
│   ├── MainViewModel.cs
│   ├── LibraryViewModel.cs
│   ├── StoreViewModel.cs
│   └── ToolsViewModel.cs
├── Views/                     # XAML UI
│   ├── MainWindow.xaml
│   ├── LibraryPage.xaml
│   ├── StorePage.xaml
│   └── ToolsPage.xaml
├── Tools/                     # Integrated tools
│   ├── DepotDumper/
│   │   ├── DepotDumperControl.xaml
│   │   └── Steam3Session.cs
│   └── ConfigVdfKeyExtractor/
│       ├── ConfigVdfKeyExtractorControl.xaml
│       └── VdfKeyExtractor.cs
├── Resources/
│   ├── Styles/
│   │   └── SteamTheme.xaml
│   └── Themes/                # 8 color themes
└── Converters/                # Value converters
```

### Dependencies

**Core:**
- .NET 8.0 Windows Desktop
- WPF (Windows Presentation Foundation)

**NuGet Packages:**
- `Microsoft.Extensions.DependencyInjection` (8.0.0) - DI container
- `Microsoft.Extensions.Hosting` (8.0.0) - Application hosting
- `CommunityToolkit.Mvvm` (8.2.2) - MVVM helpers
- `Newtonsoft.Json` (13.0.3) - JSON serialization
- `System.IO.Compression` (4.3.0) - Archive handling
- `SteamKit2` (3.0.1) - Steam protocol integration
- `QRCoder` (1.6.0) - QR code generation
- `protobuf-net` (3.2.45) - Protocol buffers

### Building

**Debug Build:**
```bash
dotnet build --configuration Debug
```

**Release Build:**
```bash
dotnet build --configuration Release
```

**Run Tests:**
```bash
dotnet test
```

**Clean Build Artifacts:**
```bash
dotnet clean
```

## 🚢 Releases

### Automated Releases via GitHub Actions

**Create a Release:**
```bash
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions will automatically:
1. Build the application
2. Package as self-contained executable
3. Create a GitHub Release
4. Attach `SolusManifestApp-v1.0.0-win-x64.zip`

**Versioning:**
- `v1.0.0` - Major release
- `v1.1.0` - Minor (new features)
- `v1.0.1` - Patch (bug fixes)

See [.github/workflows/README.md](.github/workflows/README.md) for details.

## 🐛 Troubleshooting

### Common Issues

**Steam Not Detected:**
1. Settings → Click "Auto-Detect"
2. If fails, click "Browse" and select Steam folder
3. Default: `C:\Program Files (x86)\Steam`

**API Key Invalid:**
1. Ensure key starts with `smm_`
2. Click "Validate" to test
3. Check internet connection

**Depot Dumper QR Code Not Working:**
1. Ensure Steam Mobile app is installed
2. Make sure you're logged into Steam Mobile
3. Scan QR code within 60 seconds
4. Try username/password login instead

**Upload Failing:**
1. Verify API key is set in Settings
2. Check internet connection
3. Ensure files are not empty
4. Review upload logs for details

**GreenLuma Games Not Appearing:**
1. Restart Steam (use button in Library)
2. Wait 30-60 seconds
3. Check AppList folder for .txt files
4. Verify depot keys in config.vdf

**Config VDF Key Extractor Shows No Keys:**
1. Verify config.vdf path is correct
2. Check if file contains "DecryptionKey" entries
3. Try running as Administrator
4. Ensure file isn't locked by Steam

**Uninstall Doesn't Remove Everything:**
1. Manually check:
   - AppList folder for depot .txt files
   - config.vdf for depot entries
   - steamapps for .acf files
2. Restart Steam after manual cleanup
3. Report issue with log files

### Logs

**Location:** `%AppData%\SolusManifestApp\solus_{timestamp}.log`

**View Logs:**
1. Settings → Scroll to bottom
2. Click "Open Logs Folder"
3. Open latest `solus_*.log` file

**Useful for:**
- Debugging download issues
- Tracking installation failures
- Reporting bugs

## 🤝 Support

### Getting Help

1. **Check Troubleshooting** section above
2. **Review Logs** for error messages
3. **Search Issues** on GitHub
4. **Open New Issue** with:
   - Description of problem
   - Steps to reproduce
   - Log files
   - Screenshots (if applicable)

### Community

- **Discord**: [Join Server](https://discord.gg/morrenusgames)
- **Website**: [manifest.morrenus.xyz](https://manifest.morrenus.xyz)
- **GitHub**: [Report Issues](https://github.com/yourusername/solus-manifest-app/issues)

## 📜 License

This project is provided as-is. Modify and distribute as needed.

## 🙏 Credits

- **Built with**: .NET 8, WPF, SteamKit2
- **Inspired by**: Steam's modern UI design
- **API Integration**: Solus Manifest Database
- **Tools**: DepotDumper, GreenLuma 2024
- **Community**: Discord members and contributors

## 🔄 Changelog

### Version 1.0.0
- Initial release
- Full GreenLuma integration with smart uninstallation
- Integrated Depot Dumper with upload functionality
- Integrated Config VDF Key Extractor with persistent settings
- 8 premium themes
- Manifest download and installation
- Steam library management
- Automated GitHub releases

---

<div align="center">

**Made with ❤️ for the Steam community**

[⬆ Back to Top](#solus-manifest-app)

</div>
