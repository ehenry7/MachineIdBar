# Page Relationships (Navigation Map)

> **Generated:** 2026-10-22

```
                        ┌─────────────────────────┐
                        │  01 Main Floating Wnd    │◄────────────┐
                        └────────────┬────────────┘             │
             right-click menu        │                          │ toggle
   ┌──────────┬──────────┬───────────┼───────────┬──────────┐   │
   ▼          ▼          ▼           ▼           ▼          ▼   │
 07 Conn    08 History  02 Taskbar  03-05       09 Skins  (exit) │
 Info       Stats       Wnd         Options                       │
 (modal)    (modal)     (embedded,  (modal)     (modal)          │
            │            persistent)                       04 ◄──┘
            │              │        │                       (main-wnd tab:
   ◄────────┘              │        │
      back to main         ▼        ▼
                    06 Display   11 SetItemOrder
                      Text       11 IconSelect
                       │
   ┌───────────────────┼────────────────────────┐
   │ 03 General tab    │ 05 Taskbar tab         │
   ▼                   ▼                        ▼
 10 Plugin Mgr      11 Win11 Taskbar Setting  11 Taskbar/MainWnd
 (from General)     (Win11 only)               Color dialogs
```

## Inbound / Outbound summary

| Page | Inbound | Outbound |
|------|---------|----------|
| 01 Main window | App start; tray icon | All pages via right-click menu; double-click action (configurable) |
| 02 Taskbar window | 01 menu "Show taskbar window"; auto if `show_task_bar_wnd=1` | 06 (display items), 05 (settings), 11 item order; double-click action |
| 03 General | 03-05 Options dialog tab | 10 Plugin manager; 11 Select connections |
| 04 Main-wnd tab | Options tab | 06 Display text; 11 color dialogs |
| 05 Taskbar tab | Options tab | 06 Display text; 11 color dialogs; 11 Win11 settings; 11 item order |
| 06 Display text | 04 or 05 | — (OK/Cancel back) |
| 07 Connection info | 01 menu / double-click | — |
| 08 History | 01 menu / double-click | Calendar ↔ detail (internal) |
| 09 Skins | 01 menu (double-click action CHANGE_SKIN) | Preview (internal) |
| 10 Plugins | 03 tab button | 11 plugin info |
| 11 Auxiliary | Various | — |

## Data coupling (cross-page refresh triggers)
- **General → Taskbar window:** enabling/disabling hardware monitoring adds/removes temperature items from the taskbar item set (and main window rows).
- **Options → both windows:** any display/units/font/color change re-lays-out the affected window within ≤ 1 s (width re-measure + full re-layout if size changed).
- **Skin selection → main window only:** taskbar window look is independent (its own color/font settings).
- **Connection selection (04/General/SelectConnections) → main + taskbar windows:** both read the same selected-connection delta counters.
- **Taskbar restart event → 02:** re-embed triggered from the main window's 100 ms watcher, not by the taskbar window itself.
