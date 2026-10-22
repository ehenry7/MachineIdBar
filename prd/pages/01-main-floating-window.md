# Main Floating Window

> **Route:** App start — `CTrafficMonitorDlg` (`IDD_TRAFFICMONITOR_DIALOG`), plus system-tray notification icon
> **Generated:** 2026-10-22

## Overview

The primary UI surface: a borderless floating window (default top-right area) showing live upload/download speed plus optional CPU/memory/GPU/disk/temperature items. The user drags it to any position, right-clicks for the main menu, and can minimize it to the system tray. It is the entry point for every other surface.

## Layout

```
┌──────────────────────────────┐
│  ↑ 1.24 MB/s   ↓ 3.10 KB/s   │   upload / download rows
│  CPU: 45%     Memory: 62%    │   optional rows
│  [skin background image]     │
└──────────────────────────────┘
```
- Background: skin background image (BMP/PNG) or solid color; per-skin item positions/sizes/colors from `skin.ini`/`skin.xml`.
- Row order and per-item colors configurable ("specify each item color").
- Optional "more info" mode: larger window with additional rows (CPU freq, HDD, GPU usage, temperatures).
- Optional speed graph (mini bar/plot) per CPU/memory.

## Fields

### Display data (live)

| Field | Source | Format |
|-------|--------|--------|
| Upload speed | `m_out_bytes` delta over monitor interval | unit per settings (AUTO/KB/s/MB/s, bytes/bits, short mode, digits) |
| Download speed | `m_in_bytes` delta | ditto |
| CPU usage % | PDH `\Processor Information(_Total)\% Processor Time` or CPU-time method | `n.nn%` or `nn%` (hide percent option) |
| Memory | `GlobalMemoryStatusEx`; mode: % used / used / available | per memory-display setting |
| CPU frequency | PDH | MHz/GHz |
| GPU usage | PDH GPU engine counters | % |
| Disk usage | PDH physical disk counters | % |
| Temperatures (std build) | WMI/hardware query | °C (unit configurable) |
| Plugin items | plugin DLL | per plugin |

### Settings (persisted, `config` section unless noted)

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Transparency | int 0–100 | 80 | `WS_EX_LAYERED` + `SetLayeredWindowAttributes(alpha)` |
| Always on top | bool | true | |
| Lock window position | bool | false | Disables dragging |
| Show notify icon | bool | true | System tray icon |
| Show more info | bool | false | Expanded layout |
| Mouse penetrate | bool | false | Click-through mode |
| Position x/y | int | -1 (default corner) | Restored on start |
| Auto select connection | bool | true | Pick adapter automatically |
| Select all connections | bool | false | Sum all adapters |
| Hide main window | bool | false | Start minimized to tray |
| Selected connection name | string | "" | Manual choice |
| Skin name | string | "" (default) | From `skins` dir; auto light/dark adapt optional |
| Notify icon index | int | 0 (or 2 on Win7/8) | White icon default; blue on old Windows |
| Notify icon auto adapt | bool | true | Follow Win10 light/dark theme |
| Swap up/down | bool | false | Invert upload/download rows |
| Hide when fullscreen | bool | true | Hide main window while a foreground window is fullscreen |
| Speed short mode | bool | false | |
| Separate value/unit with space | bool | true | |
| Show tooltip | bool | true | |
| Memory display | `MemoryDisplay` | % used | |
| Unit bytes | bool | true | vs bits |
| Speed unit | `SpeedUnit` | AUTO | |
| Hide unit / hide percent | bool | false | |
| Double-click action | `DoubleClickAction` | CONNECTION_INFO | Applies to both windows |
| Double-click exe | string | `%system%\Taskmgr.exe` | For SEPCIFIC_APP action |
| Allow out of border | bool | false | Window may live outside screen rect |
| Show all interface | bool | false | Show all adapters in selector (general section) |

## Interactions

### Page load
1. Load config INI → defaults.
2. Resolve connection list via `GetIfTable` (IP Helper); apply auto-select / select-all / manual selection; store `m_connection_name`.
3. Restore window position (or default corner).
4. Apply transparency, topmost, mouse-penetrate, skin, fonts, colors.
5. Start monitoring thread (default 1000 ms interval): sample bytes/CPU/memory/hardware; compute deltas.
6. Start UI timers: 100 ms taskbar reposition (if taskbar window open), 1 s main repaint/tooltip.
7. Optional: check for updates on start (`general/check_update_when_start`).
8. If `hide_main_window` → run tray-only.

### Right-click menu (main window or tray icon)

| Menu item | Opens / Does |
|-----------|--------------|
| Select connection | Adapter list (all or auto) |
| Show/hide main window | Toggle |
| Show taskbar window | Embeds taskbar window (page 02) |
| Connection details | Network info dialog (page 07) |
| History traffic statistics | History dialog (page 08) |
| Options… | Options dialog (pages 03–05) |
| Other functions ▸ Change skin | Skin picker (page 09) |
| Other functions ▸ Plugin manager | Plugin manager (page 10) |
| Other functions ▸ FAQ / Update log / Help | Web pages (Help.md / UpdateLog / GitHub) |
| Check update now | UpdateHelper (GitHub/Gitee per `update_source`) |
| Exit | Restore taskbar geometry, stop monitor thread, `DestroyWindow` |

### Drag / resize / move
- Drag with left button (unless position-locked); position saved on close.
- If window ends up off-screen after resolution/monitor change, `CheckWindowPos` moves it fully into the screen rect (unless "allow out of border").
- Fullscreen detection: if a foreground window covers the work area and "hide when fullscreen" is on, window hides; shows again when it ends.

### Notification tips (configurable, `notify_tip` section)
| Tip | Trigger | Default |
|-----|---------|---------|
| Traffic tip | speed ≥ threshold (unit selectable) | off, 200 |
| Memory usage tip | memory % ≥ threshold | off, 80 |
| CPU temperature tip | ≥ threshold °C | off, 80 |
| GPU temperature tip | ditto | off, 80 |
| HDD temperature tip | ditto | off, 80 |
| Mainboard temperature tip | ditto | off, 80 |

Fired as balloon notifications from the tray icon.

## API Dependencies

| API | Purpose |
|-----|---------|
| `GetIfTable` (iphlpapi) | Adapter list + per-adapter byte counters |
| PDH (`PdhOpenQuery` etc.) | CPU %, CPU frequency, GPU/disk usage |
| `GlobalMemoryStatusEx` | Memory |
| WMI/hardware query (`PdhHardwareQuery/`) | Temperatures (standard build) |
| `Shell_NotifyIcon` | Tray icon |
| `SetLayeredWindowAttributes` | Transparency / color-key |
| `SystemParametersInfoForDpi`, `EnumDisplayMonitors` | Screen geometry, DPI |

## Business Rules
- Zero-speed counter: if no speed detected for a sustained period (`m_zero_speed_cnt`), the app assumes the selected connection is gone and re-runs connection selection (auto mode).
- Monitor interval clamped to 100–30000 ms; out-of-range falls back to 1000 ms.
- Update source defaults by UI language: zh → Gitee, else GitHub.
- Skin text/colors/fonts are **per-skin** (stored in skin settings), not global — switching skins swaps the whole look.
- Restart-manager support is compiled in but disabled by default.
