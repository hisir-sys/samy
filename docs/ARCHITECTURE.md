# SAMY architecture notes

## Application shell

The desktop shell owns the compact top-edge window, expanded dashboard window state, tray integration, and app lifecycle. UI components should not directly call Windows APIs.

## Suggested layers

- Presentation — WinUI 3 pages, controls, visual states, touch targets, accessibility labels.
- Application — view models and feature orchestration.
- Domain — small models for notes, clipboard entries, shelf items, timer sessions, and preferences.
- Infrastructure — local persistence and Windows platform adapters.

## Feature boundaries

| Feature | Boundary | Privacy / behavior |
|---|---|---|
| System overview | ISystemMetricsService | Read-only; show sampling status |
| Focus timer | IFocusTimerService | Local timer state |
| Notes | INotesRepository | Local-first storage |
| Clipboard | IClipboardHistoryService | Explicit opt-in, pause and clear controls |
| File shelf | IFileShelfService | User-selected paths; no background scanning |
| Media | IMediaSessionService | Use supported Windows media-session APIs |
| Calendar | ICalendarService | Start with local/manual events; external account integration is optional |
| Notifications | INotificationService | User-configurable categories |

## Window interaction requirements

- Compact bar must be keyboard- and touch-operable.
- Expanded panel must not steal focus unexpectedly.
- Provide a visible close/collapse action and an Escape behavior.
- Support display scaling and multiple monitors.
- Avoid covering system-reserved work areas where possible.
- Persist position only after validating it against currently connected displays.

## Quality gates

- Unit-test feature services independently from WinUI.
- Keep platform adapters behind interfaces.
- Test touch target sizing, keyboard navigation, high-DPI scaling, and multi-monitor behavior on Windows.
- Do not mark features complete based on a source-only check; require a successful Windows build and a manual interaction pass.
