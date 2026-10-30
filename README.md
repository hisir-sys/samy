# SAMY for Windows

A touch-friendly Windows productivity companion with a compact top-edge bar and an expandable dashboard.

> SAMY is an independent implementation inspired by publicly visible productivity patterns. It does not include Vayli source code, proprietary assets, extracted binaries, or copied implementation details.

## Product direction

- Compact, draggable top bar with collapsed and expanded states
- Touch-friendly dashboard with larger hit targets and responsive layouts
- Home overview: screen-time placeholder, system resource readings, focus timer, notes
- Local clipboard history with explicit privacy controls
- File shelf for user-selected files and folders
- Media controls through supported Windows media-session APIs
- Calendar view and configurable notifications
- Settings for appearance, position, startup behavior, and privacy

## Proposed technology

- C# and WinUI 3 with the Windows App SDK
- .NET 10 (subject to installed SDK availability)
- MVVM-oriented separation between UI, services, and platform integrations
- Local-first storage; no account or cloud sync required for core features

WinUI 3 is selected as the starting point because it provides native Windows controls and supports touch-oriented layouts, windowing, and Windows integration.

## Development principles

1. Build an original interface and original code.
2. Request only the Windows permissions needed for each feature.
3. Keep clipboard and notes data local by default.
4. Make monitoring features transparent and user-configurable.
5. Add each feature behind a small service boundary so it can be tested independently.
6. Verify builds and tests before describing a feature as working.

## Planned milestones

- [x] M0 — Repository foundation, WinUI 3 app shell, build instructions
- [ ] M1 — SAMY visual identity, compact always-on-top bar, expand/collapse interaction
- [x] M2 — First touch-friendly dashboard layout and navigation shell
- [x] M3 — Focus timer and local quick notes (settings are still pending)
- [ ] M4 — Clipboard history with privacy controls
- [ ] M5 — File shelf and Windows media-session integration
- [ ] M6 — Calendar view, notifications, and optional startup launch
- [ ] M7 — Accessibility, performance, packaging, and test pass

## Build status

The first C# / WinUI 3 application shell is committed on `feat/winui3-foundation`. The dashboard includes a local clock, SAMY process CPU and working-set readings, a 25-minute focus timer, and a quick note stored under `%LOCALAPPDATA%\\SAMY`. The Windows build workflow is configured; the first Windows CI result is still pending. The compact always-on-top notch and remaining integrations are not implemented yet.

## License

Project license to be selected before public distribution. Third-party dependencies will retain their own licenses.
