@echo off
echo ============================================
echo Building Solus Manifest App
echo ============================================
echo.

REM Get version from git tag
for /f "tokens=*" %%i in ('git describe --tags --long --always') do set GIT_VERSION=%%i
echo Git version: %GIT_VERSION%

REM Parse version (e.g., v2025.11.24.01-0-g622999d -> 2025.11.24.1)
for /f "tokens=1 delims=-" %%a in ("%GIT_VERSION%") do set TAG_VERSION=%%a
REM Remove 'v' prefix if present
set TAG_VERSION=%TAG_VERSION:v=%
REM Convert last .0X to .X (e.g., 2025.11.24.01 -> 2025.11.24.1)
set VERSION=%TAG_VERSION:.01=.1%
set VERSION=%VERSION:.02=.2%
set VERSION=%VERSION:.03=.3%
set VERSION=%VERSION:.04=.4%
set VERSION=%VERSION:.05=.5%
set VERSION=%VERSION:.06=.6%
set VERSION=%VERSION:.07=.7%
set VERSION=%VERSION:.08=.8%
set VERSION=%VERSION:.09=.9%
echo Build version: %VERSION%
echo.

REM Clean previous build
echo [1/3] Cleaning previous build...
dotnet clean
if %errorlevel% neq 0 (
    echo ERROR: Clean failed!
    pause
    exit /b %errorlevel%
)
echo.

REM Build Release
echo [2/3] Building Release configuration...
dotnet publish -c Release -r win-x64 /p:Version=%VERSION%
if %errorlevel% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b %errorlevel%
)
echo.

REM Show results
echo [3/3] Build complete!
echo.
echo Output location:
echo %~dp0bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
echo.
echo Executable:
dir /b "%~dp0bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\SolusManifestApp.exe"
echo.

REM Show file size
for %%I in ("%~dp0bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\SolusManifestApp.exe") do echo Size: %%~zI bytes (%%~zI / 1048576 = %%~zI MB approx)
echo.

echo Build successful! Press any key to exit.
pause
