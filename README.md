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

The current main branch includes:

- Floating workspace with collapse/expand, always-on-top, title-bar dragging, and monitor-aware positioning
- Focus timer, Pomodoro, countdown, stopwatch, unit converters, saved links, checklist and world clocks
- Local notes and opt-in clipboard history with search, pinning, deletion, retention and sensitive-word exclusions
- Multiple named file shelves with drag-and-drop into and out of shelves, open, copy-path and remove actions
- Image conversion (PNG/JPG/WEBP/BMP), image-to-PDF export and PDF-page-to-PNG rendering
- Calendar reminders with daily, weekly and monthly recurrence
- Opt-in HTTPS iCalendar feed subscriptions, persisted locally and refreshed at startup / every 12 hours
- Whole-device CPU, RAM, system-disk free space, network totals, battery status and default audio endpoint status
- Persistent opt-in screen-time reports and local Git coding-activity summaries
- Authenticated generic HTTPS JSON API connection with bearer token encrypted using Windows DPAPI
- Windows App SDK notification registration and reminder toast delivery path
- Optional weather lookup and currency conversion

**Integration boundary:** the authenticated analytics feature currently supports a user-configured HTTPS JSON endpoint with a bearer token. Provider-specific OAuth flows and provider-specific analytics dashboards are not included yet. No fake metrics or revenue values are generated.

**Windows verification still required:** real notification delivery/activation, clean installation and first launch, multiple-display dragging and DPI scaling, audio monitoring, file drag/drop, and conversion of representative files. CI build success is not a substitute for this manual desktop test.

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
