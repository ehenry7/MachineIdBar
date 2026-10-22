# TrafficMonitor — Product Requirements Document

> **Generated:** 2026-10-22
> **Source code:** `C:\Users\h00569653\Desktop\MachineIdBar\trafficmonitor-ref` (github.com/zhongyang219/TrafficMonitor)
> **Method:** code-to-prd skill (3-phase: global scan → surface-by-surface analysis → structured documentation)
> **Tech stack (reverse-engineered):** C++17 + MFC (CDialog/CDialogEx) desktop application, Windows-only. Rendering: GDI / Direct2D1 / D2D1+DirectComposition (auto-selected). Data: PDH performance counters, IP Helper API (`GetIfTable`), Win32 taskbar window embedding.

---

## System Overview

TrafficMonitor is a Windows network-speed monitor that shows live upload/download speed, CPU and memory usage (and, in the standard build, GPU/disk usage and hardware temperatures) on a floating window, and optionally **embedded into the Windows task bar** as a small always-visible strip. It targets personal users running one or more network adapters who want at-a-glance bandwidth usage without opening Task Manager.

The application runs a background monitoring thread that samples network byte counters (via `GetIfTable`), CPU usage (PDH or CPU-time method), memory, and hardware sensors at a configurable interval (default 1000 ms, range 100–30000 ms). The UI layers (main floating window and taskbar window) render the sampled data at up to 10 Hz via a timer-driven repaint loop.

All settings are persisted in a plain INI file (`TrafficMonitor.ini` next to the executable); history traffic is persisted in a custom binary file. The app supports multiple languages (via a string-table mechanism), custom skin themes (BMP/PNG background + `skin.ini`/`skin.xml` per skin folder), and a DLL-based plugin system that can add extra display items to both the main window and the taskbar window.

Two product editions exist: **Standard** (admin privileges, includes temperature monitoring) and **Lite** (no admin, no temperature). From 1.86 onwards Lite also includes GPU/disk usage; temperature moved to an external hardware-monitor plugin.

## Module Overview

| Module | UI Surfaces | Core Functionality |
|--------|-------------|--------------------|
| Main floating window | Main window (page 01) | Live speed/CPU/memory display, skins, drag-move, right-click menu, system tray icon |
| Task bar window | Taskbar strip (page 02) | Embedded taskbar display of selected items (Win7/10 classic, Win11 XAML taskbar, Wine fallback) |
| Options — General | Options tab (page 03) | Auto-run, update checks, monitoring interval, hardware monitoring enable/disable, notification tips, language |
| Options — Main window | Options tab (page 04) | Main window transparency, topmost, colors, fonts, units, double-click action |
| Options — Taskbar window | Options tab (page 05) | Taskbar window items, colors, fonts, position (left/right), offsets, Win11 settings |
| Display text settings | Per-window dialog (page 06) | Edit the prefix text shown before each display item's value |
| Network info | Dialog (page 07) | Detailed per-connection stats: IP addresses, DHCP, total bytes, connection details |
| History traffic | 3 dialogs (page 08) | Daily/weekly/monthly/quarterly/yearly traffic statistics with calendar and detail views |
| Skins | Skin picker + preview (page 09) | Built-in + custom skins, light/dark auto-adapt, skin settings (colors/fonts/positions) |
| Plugins | Plugin manager (page 10) | Install/enable/disable DLL plugins that add display items |
| Auxiliary dialogs | About, message, connection selector, item order, icons (page 11) | Supporting UI |

## Page Inventory

| # | Page Name | Route / Entry Point | Module | Doc Link |
|---|-----------|---------------------|--------|----------|
| 1 | Main Floating Window | App start (main dialog `IDD_TRAFFICMONITOR_DIALOG`) | Main window | [→](./pages/01-main-floating-window.md) |
| 2 | Task Bar Window (Embedded) | Right-click menu → "Show taskbar window" (`OpenTaskBarWnd`) | Task bar | [→](./pages/02-taskbar-window.md) |
| 3 | Options — General Settings | Right-click menu → "Options…" tab 1 (`IDD_GENERAL_SETTINGS_DIALOG`) | Options | [→](./pages/03-options-general.md) |
| 4 | Options — Main Window Settings | Options dialog tab 2 (`IDD_MAINWND_SETTINGS_DIALOG`) | Options | [→](./pages/04-options-main-window.md) |
| 5 | Options — Taskbar Window Settings | Options dialog tab 3 (`IDD_TASKBAR_SETTINGS_DIALOG`) | Options | [→](./pages/05-options-taskbar-window.md) |
| 6 | Display Text Settings | From Main/Taskbar settings tabs | Options | [→](./pages/06-display-text-settings.md) |
| 7 | Network / Connection Info | Right-click menu → "Connection details" | Network | [→](./pages/07-network-connection-info.md) |
| 8 | History Traffic Statistics | Right-click menu → "History traffic statistics" | History | [→](./pages/08-history-traffic.md) |
| 9 | Skin Selection & Preview | Right-click menu → "Other functions" → "Change skin" | Skins | [→](./pages/09-skins.md) |
| 10 | Plugin Manager | General settings tab → "Plugin manager" | Plugins | [→](./pages/10-plugin-manager.md) |
| 11 | Auxiliary Dialogs (About / Message / Connections / Item Order / Icons) | Various menu entries | Auxiliary | [→](./pages/11-auxiliary-dialogs.md) |

## Global Notes

### Window Hierarchy (Windows 11)

```
Desktop
  └── Shell_TrayWnd (taskbar)
        ├── Start (Start button, XAML host)
        ├── Windows.UI.Composition.DesktopWindowContentBridge (XAML surface — Win11 marker)
        ├── TrayNotifyWnd (system tray icon area)
        └── [TrafficMonitor taskbar window — SetParent'd here, WS_POPUP kept]
```

### Window Hierarchy (Windows 7/10 classical)

```
Desktop
  └── Shell_TrayWnd
        └── ReBarWindow32 (fallback: WorkerW)
              ├── MSTaskSwWClass (Win10: MSTaskListWClass) — shrunk to make room
              └── [TrafficMonitor taskbar window — SetParent'd here]
```

### Configuration Persistence

- Settings: `TrafficMonitor.ini` (INI format, sections `general`, `config`, `connection`, `task_bar`, `skins`, `notify_tip`).
- History: binary file (path configurable, default next to exe).
- Auto-run: registry (`HKCU\...\Run`) or Task Scheduler (user-selectable).
- Per-skin settings: stored inside each skin's settings (colors/fonts/display text per skin, including light/dark variants).

### Permission Model

- Standard build: requires **administrator** (hardware sensors via WMI/PDH require it).
- Lite build: normal user.
- Windows 11 taskbar detection: presence of `Windows.UI.Composition.DesktopWindowContentBridge` window inside `Shell_TrayWnd`.

### Common Interaction Patterns

- Right-click menu on the main window or tray icon is the primary navigation.
- Double-click action on either window is user-configurable (8 options, default opens connection details).
- All timers: monitoring interval (user-set, default 1 s), taskbar reposition at 100 ms (from main window), taskbar dialog internal timer 1000 ms (width-change detection + tooltip).
- DPI: per-monitor DPI awareness (Win8.1+); all coordinates in physical pixels; font sizes scaled by `MulDiv(size, dpi, 72)`.
- Fallback behavior: if taskbar embedding fails (`SetParent` returns NULL), the taskbar window becomes a topmost popup at screen-absolute coordinates.
- Multi-monitor: taskbar window can be shown on a secondary display (`Shell_SecondaryTrayWnd`) with an index selector.
