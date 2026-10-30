# SAMY for Windows

A touch-friendly Windows productivity companion with a compact top-edge bar and an expandable dashboard.

> SAMY is an independent implementation inspired by publicly visible productivity patterns. It does not include Vayli source code, proprietary assets, extracted binaries, or copied implementation details.

## Product direction

- Compact, draggable top bar with collapsed and expanded states
- Touch-friendly dashboard with larger hit targets and responsive layouts
- Home overview: system resource readings, focus timer, notes
- Local clipboard history with explicit privacy controls
- File shelf for user-selected files and folders
- Media controls through supported Windows media-session APIs
- Calendar view and configurable notifications
- Settings for appearance, position, startup behavior, and privacy

## Technology

- C# and WinUI 3 with the Windows App SDK
- .NET 10
- Local-first storage; no account or cloud sync required for core features

## Development principles

1. Build an original interface and original code.
2. Request only the Windows permissions needed for each feature.
3. Keep clipboard and notes data local by default.
4. Make monitoring features transparent and user-configurable.
5. Add each feature behind a small service boundary so it can be tested independently.
6. Verify builds and tests before describing a feature as working.

## Milestones

- [x] M0 — Repository foundation and WinUI 3 app shell
- [x] M2 — First touch-friendly dashboard layout
- [x] M3 — Focus timer and local quick notes
- [x] Windows x64 installer pipeline definition (build result pending)
- [ ] M1 — Compact always-on-top bar and expand/collapse
- [ ] M4 — Clipboard history with privacy controls
- [ ] M5 — File shelf and Windows media-session integration
- [ ] M6 — Calendar view, notifications, and optional startup launch
- [ ] M7 — Accessibility, performance, and Windows-device validation

## Build and installer

See [docs/BUILDING.md](docs/BUILDING.md) and [docs/INSTALLER.md](docs/INSTALLER.md).

The Windows Actions workflow is configured to produce `SAMY-Setup.exe` as the `SAMY-Windows-Installer` artifact on pushes to the feature branch or main. It installs for the current user, creates a Start Menu shortcut, and offers optional Desktop and sign-in shortcuts. No installer is claimed to exist until CI succeeds.

## License

Project license to be selected before public distribution.
