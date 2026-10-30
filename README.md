# SAMY for Windows

![SAMY logo](src/Samy/Assets/SamyLogo.svg)

**SAMY** is an independent, local-first Windows productivity companion built with C# and WinUI 3. It brings quick tools into a compact floating workspace with an expandable dashboard.

> SAMY is an independent implementation inspired by publicly visible productivity patterns. It does not include Vayli source code, proprietary assets, extracted binaries, or copied implementation details.

## Current release

- **Version:** 0.2.1
- **Platform:** Windows 11 x64
- **Installer:** [SAMY-Setup.exe — latest release](https://github.com/hisir-sys/samy/releases/latest)
- **Build:** Windows CI builds the app, publishes a self-contained x64 package, and creates the installer.

## Screenshots

Real screenshots of the running WinUI application are **not available in this repository yet**. This environment can inspect and build the source through Windows CI, but it cannot launch an interactive Windows desktop and capture genuine app screens. We will add screenshots from a real Windows 11 session rather than presenting mockups as product screenshots.

Planned captures:
- Floating notch, collapsed and expanded
- Home dashboard
- Tools page
- Calendar and reminders
- Settings and privacy controls

## Features

The current feature set includes:

- Floating productivity workspace and expandable dashboard
- Focus timer with presets, custom countdown, and stopwatch
- Unit converters for length, weight, temperature, and data
- Saved links with local persistence and URL validation
- Persistent local checklist
- World clocks for Kolkata, London, and New York
- In-app message preview
- Optional weather lookup using Open-Meteo
- Optional currency conversion using Frankfurter
- Opt-in persistent daily screen-time reports (process names and foreground duration only; stored locally)
- Whole-device CPU and RAM monitoring (not just SAMY's own process)
- Local clipboard, file shelf, media, notes, calendar/reminder, and settings surfaces

Some integrations and deeper workflows remain in development. In particular, this is not a claim that every planned parity feature is complete: file conversion, multi-shelf drag and drop, recurring calendar feeds, extended system metrics (disk, network, battery and audio), coding analytics, native Windows notification delivery, and authenticated revenue/analytics provider integrations remain future work. SAMY does not fabricate analytics or revenue data.

## Technology

- C# / WinUI 3
- .NET 10
- Windows App SDK 1.8
- Self-contained Windows x64 installer built with Inno Setup
- Local-first storage for core features; no account or cloud sync is required

## Install

1. Open the [latest SAMY release](https://github.com/hisir-sys/samy/releases/latest).
2. Download **SAMY-Setup.exe**.
3. Run the installer. It installs SAMY and the required Microsoft Visual C++ runtime, then adds a Start Menu shortcut. Administrator approval is required for the runtime prerequisite.
4. Optional Desktop and sign-in shortcuts can be selected during setup.

The installer is currently unsigned. Windows may show an unknown-publisher / SmartScreen warning. Only download it from this repository's official Releases page.

## Build from source

See [docs/BUILDING.md](docs/BUILDING.md) and [docs/INSTALLER.md](docs/INSTALLER.md).

Quick build on Windows 11 with the .NET 10 SDK:

```powershell
dotnet restore .\src\Samy\Samy.csproj
dotnet build .\src\Samy\Samy.csproj -c Release
```

## Development principles

1. Keep the implementation original.
2. Request only the Windows permissions needed for each feature.
3. Keep personal notes and clipboard content local by default.
4. Make monitoring transparent and user-configurable.
5. Separate features behind testable service boundaries.
6. Verify builds and real Windows behavior before calling a feature complete.

## License

Project license to be selected before public distribution.
