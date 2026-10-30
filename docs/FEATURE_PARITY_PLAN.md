# SAMY Feature-Parity Expansion

## Product direction

SAMY is an independently implemented Windows 11 productivity workspace. This plan uses the publicly described Vayli feature set as a product-research checklist; it does not copy Vayli source code, binaries, artwork, or proprietary implementation.

## Existing foundation

The current app already has a WinUI 3 floating window, home view, a basic focus timer, quick note, opt-in local text clipboard history, path-based file shelf, Windows media-session controls, local reminders, and basic settings.

## Feature parity checklist

| Module | Current SAMY state | Expansion target |
|---|---|---|
| Home | Basic clock, focus, process metrics, quick note | Configurable cards, recent activity, next reminder, compact/expanded layouts |
| Clipboard | Opt-in text-only history | Search, pin, delete individual items, richer previews, bounded retention, sensitive-content exclusions |
| File shelf | Stores selected file paths | Drag-and-drop in/out, multiple shelves, copy/open/remove actions, clear storage controls |
| Timers & Focus | 25-minute focus countdown | Pomodoro cycles, configurable focus/break lengths, standalone countdown, stopwatch, water reminders |
| Notes | One quick note | Multiple notes, autosave, pinning, checklists |
| Links | Not implemented | Saved bookmarks, custom search shortcuts, quick open |
| Weather | Not implemented | Optional location/city weather with clear network/privacy controls |
| Units | Not implemented | Length, weight, temperature, data, volume, speed, time and currency conversion; currency requires current rates |
| File Converter | Not implemented | Local image conversion (JPG/PNG/ICO) and PDF export where supported |
| Calendar | Date picker and local reminders | Multiple local events, recurring reminders, calendar-feed subscription as an explicit opt-in |
| Clock | Local clock only | Multiple configurable world clocks |
| Screen Time | Not implemented | Opt-in local foreground-app duration summaries; no keystroke, screenshot, or content capture |
| System | SAMY process CPU/RAM only | Whole-device CPU, memory, disk, network, battery and audio output status |
| Message | Not implemented | User-authored status banner with colour and duration controls |
| AI Coding | Not implemented | Optional local summaries from supported coding-tool usage logs; no API key or upload |
| Analytics | Not implemented | Optional user-connected analytics providers; credentials protected and data fetched only on request |
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
