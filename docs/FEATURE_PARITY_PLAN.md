# SAMY Feature-Parity Expansion

## Product direction

SAMY is an independently implemented Windows 11 productivity workspace. This plan uses the publicly described Vayli feature set as a product-research checklist; it does not copy Vayli source code, binaries, artwork, or proprietary implementation.

## Existing foundation

The current app already has a WinUI 3 floating window, home view, a basic focus timer, quick note, opt-in local text clipboard history, path-based file shelf, Windows media-session controls, local reminders, and basic settings.

## Feature parity checklist

| Module | Current SAMY state | Expansion target |
|---|---|---|
| Home | Basic clock, focus, process metrics, quick note | Configurable cards, recent activity, next reminder, compact/expanded layouts |
| Clipboard | Opt-in text-only history | Search, pin, delete individual items, bounded retention and sensitive-content exclusions implemented; richer previews remain |
| File shelf | Stores selected file paths | Drag-and-drop in/out, multiple shelves, copy/open/remove actions, clear storage controls |
| Timers & Focus | 25-minute focus countdown | Pomodoro cycles, configurable focus/break lengths, standalone countdown, stopwatch, water reminders |
| Notes | One quick note | Multiple notes, autosave, pinning, checklists |
| Links | Not implemented | Saved bookmarks, custom search shortcuts, quick open |
| Weather | Not implemented | Optional location/city weather with clear network/privacy controls |
| Units | Not implemented | Length, weight, temperature, data, volume, speed, time and currency conversion; currency requires current rates |
| File Converter | Image conversion (PNG/JPG/WEBP/BMP), image-to-PDF and PDF-page-to-PNG export | Add ICO export, batch conversion and large-file stress tests |
| Calendar | Local reminders with daily/weekly/monthly recurrence and persisted HTTPS .ics feed subscriptions refreshed at launch and every 12 hours | Add full timezone/RDATE/EXDATE recurrence coverage and feed deletion/management UI |
| Clock | Local clock only | Multiple configurable world clocks |
| Screen Time | Opt-in local foreground-app duration summaries with daily JSON persistence and 90-day retention | Add report history UI and export; no keystroke, screenshot, or content capture |
| System | Whole-device CPU/RAM plus disk free space, network totals, battery and default audio endpoint volume/mute | Add live network throughput graphs and additional disk/adapter selection |
| Message | Not implemented | User-authored status banner with colour and duration controls |
| AI Coding | Local Git activity report: last-30-day commits, active days, changed-file entries and file extensions; no source upload | Add opt-in IDE activity adapters only with explicit consent |
| Analytics | User-configured HTTPS JSON endpoint, bearer-token test, Windows DPAPI CurrentUser token protection; actual HTTP status/JSON response verified | Add named provider adapters/OAuth and provider-specific metric schemas; no fabricated metrics |
| Revenue | Not implemented | Optional user-connected sales providers; no credentials stored in plain text |

## Implementation order

### Batch 1 — Core daily tools
1. Multiple notes and checklists with autosave and pinning.
2. Saved links and quick-search shortcuts.
3. Pomodoro, countdown, stopwatch, and configurable reminders.
4. Search, pin, and delete for clipboard entries.
5. File-shelf drag/drop and multiple named shelves.

### Batch 2 — Desktop utilities
1. Unit conversion with recent conversions.
2. World clock cards.
3. System dashboard using supported Windows performance APIs.
4. Custom message banner and event-driven compact notifications.
5. Local image conversion and PDF export.

### Batch 3 — Context and integrations
1. Optional weather provider.
2. Calendar feed subscription with explicit URL consent and safe parsing.
3. Opt-in screen-time summaries with transparent process-level collection.
4. Local coding-usage summaries from supported local logs.
5. User-configured analytics and revenue integrations.

## Product and privacy requirements

- Windows 11, C# and WinUI 3 remain the target.
- Keep the floating notch responsive, keyboard accessible, touch friendly, and usable across multiple displays.
- Modules can be enabled, disabled, and reordered.
- Local-first storage under the user's SAMY data directory.
- Clipboard capture remains off by default; provide individual-item deletion, pinning, retention limits, and sensitive-content exclusions before broadening capture.
- No cloud account is required for core modules.
- Network-connected modules are opt-in, disclose what they send, and never silently upload clipboard, notes, files, screen contents, or usage logs.
- Never store provider credentials as plain text; use Windows Credential Manager or DPAPI-backed storage.
- Add unit tests for pure logic and Windows CI checks for each batch.
- Do not describe a feature as complete until it builds and its behavior is tested on Windows 11.

## Release gates

1. Clean Windows x64 build.
2. Installer generation and artifact verification.
3. Automated tests for persistence, retention, conversion logic, timer state, and module settings.
4. Manual Windows 11 checks for touch, keyboard, multi-monitor positioning, drag/drop, startup, and suspend/resume.
5. Privacy review for every module that reads clipboard, process metadata, calendar feeds, or connected-service data.


## Implementation status (2026-10-30)

The current feature branch adds a working Tools page with:
- Pomodoro preset, custom countdown and stopwatch timer controls.
- Unit conversions for metres/feet, kilograms/pounds, Celsius/Fahrenheit and MB/GB.
- Saved HTTP/HTTPS links with local JSON persistence and open/remove actions.
- Local task checklist with persistence and completion toggling.
- World clocks for Kolkata, London and New York.
- Custom in-app status message preview.
- Optional live weather lookup using Open-Meteo geocoding and forecast endpoints.
- Optional currency conversion using Frankfurter's latest available rates.
- Foreground application name/time sampling for the current SAMY session only.

Previously implemented modules remain available: floating workspace, local notes, clipboard history, file shelf, Windows media transport controls, local calendar reminders, settings/startup and basic SAMY process metrics.

Implemented in the current main branch: multiple named file shelves with drag-in, image format conversion, image-to-PDF and PDF-page-to-PNG export, recurring local reminders, persisted HTTPS .ics feed subscriptions, whole-device CPU/RAM/disk/network/battery/audio status, local Git coding activity summaries, and a generic authenticated HTTPS JSON API connection using a Windows DPAPI-protected bearer token. Reminder toasts use the Windows App SDK notification manager. The floating window positions itself relative to the monitor it currently occupies.

Still requiring release validation or further work: drag files out of shelves; full iCalendar timezone and exception-rule support; provider-specific OAuth and analytics schemas; richer system graphs; notification activation/quiet-hours behavior; and physical Windows 11 tests for the notch, multi-monitor DPI/scaling, drag/drop, conversions, audio, and clean-install startup. CI success alone does not verify these desktop interactions.

The current Windows build is under CI validation. No installer is considered verified until the latest commit's installer workflow succeeds and its artifact is inspected.
