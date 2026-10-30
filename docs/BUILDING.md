# Build and run SAMY on Windows 11

## Requirements

- Windows 11 (build 22000 or later)
- .NET 10 SDK
- Visual Studio 2026 with the WinUI application development workload, or a compatible command-line setup
- Windows App SDK restore access through NuGet

Microsoft's maintained setup guide: https://learn.microsoft.com/windows/apps/get-started/start-here

## Build

From the repository root in PowerShell:

```powershell
dotnet --info
dotnet restore .\src\Samy\Samy.csproj
dotnet build .\src\Samy\Samy.csproj -c Debug
```

Open `src/Samy/Samy.csproj` in Visual Studio and run the project to launch the desktop app.

## Create the Windows installer

See [INSTALLER.md](INSTALLER.md). The Windows Actions workflow publishes a self-contained x64 build and packages it as `SAMY-Setup.exe` using Inno Setup.

## Current implemented behavior

- SAMY-branded WinUI 3 dashboard shell
- Live local clock and date
- Process CPU estimate and SAMY process working-set display
- 25-minute focus timer with start, pause, resume, and reset
- Quick note saved under the current user's Local AppData folder at `%LOCALAPPDATA%\SAMY\quick-note.txt`

## Still in development

The initial app shell is not yet the finished productivity companion. The compact always-on-top floating notch, global clipboard history, file shelf, media-session controls, calendar integration, settings, tray integration, and automated UI tests still require implementation and Windows-device validation.

## Privacy notes

SAMY does not upload the quick note. The first metrics are limited to the SAMY process; they are not whole-system CPU or RAM readings. Clipboard collection is not enabled in this build.
