# Task Bar Window (Embedded)

> **Entry point:** Right-click menu → "Show taskbar window" (`config/show_task_bar_wnd` INI key)
> **Implementation:** `CTaskBarDlg` (abstract, `TaskBarDlg.cpp`) + 3 concrete subclasses; generated via `CTrafficMonitorDlg::OpenTaskBarWnd()` (`TrafficMonitorDlg.cpp:576`)
> **Generated:** 2026-10-22

## Overview

The taskbar window is a small strip embedded **inside the Windows task bar** that displays a user-configurable set of live metrics (default: upload + download speed). It is the signature feature of TrafficMonitor. It must stay visible when the Start menu opens, survive taskbar re-renders, and give up its space cleanly on close.

## Variant Selection

```
if (IsWine())                      → CWineTaskbarDlg      (no embedding; topmost floating)
else if (IsWindows11Taskbar())     → CWin11TaskbarDlg     (parent = Shell_TrayWnd itself)
else                               → CClassicalTaskbarDlg (parent = ReBarWindow32, shrink MSTaskSwWClass)
```

- `IsWindows11Taskbar()` = `FindWindowExW(Shell_TrayWnd, 0, "Windows.UI.Composition.DesktopWindowContentBridge", NULL) != NULL` — i.e., the XAML/DirectComposition taskbar is present (Windows 11). Re-checked on demand (taskbar restart).
- Windows version detection helper: `CWinVersionHelper` (build number based).

## Layout

Single horizontal row of display items, each: `[prefix text][value]` (value right-aligned by default), optional mini bar/plot graph for CPU/memory (style selectable: bar or line plot, dashed box optional), optional status bar. Items separated by configurable spacing (default 8 px). Window height follows the taskbar height (item area vertically centered).

```
┌────────────────────────────────────────────────────────────┐
│  ↑ 1.24 MB/s   ↓ 3.10 KB/s   CPU: 45%   Memory: 62%        │  ← our window (parent = taskbar container)
└────────────────────────────────────────────────────────────┘
```

## Fields

### Display items (checkboxes in "Display settings", stored as bitmask `task_bar/tbar_display_item`)

| Item ID | Constant | Label | Data source | Notes |
|---------|----------|-------|-------------|-------|
| Upload speed | `TDI_UP` | `: ` (configurable prefix) | `m_out_bytes` delta per interval | Default ON |
| Download speed | `TDI_DOWN` | `: ` (configurable prefix) | `m_in_bytes` delta per interval | Default ON |
| CPU usage | `TDI_CPU` | `CPU: ` | PDH / CPU-time method | Optional |
| Memory | `TDI_MEMORY` | `Memory: ` | GlobalMemoryStatusEx | Mode selectable: % used / used / available |
| GPU usage | `TDI_GPU_USAGE` | — | PDH (standard build ≥1.86 Lite) | |
| CPU temperature | `TDI_CPU_TEMP` | — | Hardware query (standard build only) | Removed if hardware monitoring disabled |
| GPU temperature | `TDI_GPU_TEMP` | — | ditto | ditto |
| HDD temperature | `TDI_HDD_TEMP` | — | ditto | ditto |
| Mainboard temperature | `TDI_MAIN_BOARD_TEMP` | — | ditto | ditto |
| HDD usage | `TI_HDD_USAGE` | — | PDH disk counters | |
| Total speed | `TDI_TOTAL_SPEED` | — | in+out combined | |
| CPU frequency | `TDI_CPU_FREQ` | — | PDH `\Processor Information(_Total)\Processor Frequency` | |
| Today's traffic | `TDI_TODAY_TRAFFIC` | — | History file | |
| Plugin items | (plugin ID) | — | Plugin DLL callback | Via `CommonDisplayItem` |

Temperature items are silently removed from the set when the corresponding hardware monitoring is disabled in General settings (or in Lite builds compiled `WITHOUT_TEMPERATURE`).

### Style / appearance (from `task_bar` INI section)

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Background color | Color | `dft_back_color` | 0 = transparent (color-key) |
| Transparent color | Color | `dft_transparent_color` | Color-keyed out when background transparent |
| Status bar color | Color | `dft_status_bar_color` | Shown when "show status bar" on |
| Text colors | Per-item map | `dft_text_colors` | "Specify each item color" toggle |
| Font | Name/size/bold/italic | default font, 9 pt | Taskbar font dialog |
| Display text (prefixes) | Per-item strings | built-ins | Display Text Settings dialog |
| Speed unit | `SpeedUnit` (AUTO/KBPS/MBPS) | AUTO | |
| Unit: bytes vs bits | Radio | bytes | |
| Hide unit | Checkbox | off | |
| Hide percent | Checkbox | off | |
| Value right-align | Checkbox | on | |
| Short speed mode | Checkbox | on | e.g. `1.24 MB/s` vs `1.240 MB/s` |
| Digits number | Combo | 4 | Decimals in short mode |
| Separate value/unit with space | Checkbox | on | |
| Show tooltip | Checkbox | on | |
| Double-click action | `DoubleClickAction` | CONNECTION_INFO | |
| Horizontal arrange (Win7) | Checkbox | off | Items side by side vs stacked |
| Show status bar | Checkbox | on | |
| CPU/Memory graph type | Bar / line plot | bar | |
| Show graph dashed box | Checkbox | off | |
| Item space | Spin | 8 | px between items |
| Vertical margin | Spin | 0 | |
| Window offset top / left | Spin | 0 | Manual nudging |
| Show in secondary display | Checkbox | off | + display index combo |
| Avoid overlap with widgets (Win11) | Checkbox | off | Reserves `taskbar_left_space_win11` px (default 160) |
| Win11 left/right spaces | Int | 160 / 280 | Fallback positioning constants |
| Auto-adapt light theme (Win10+) | Checkbox | off | Light/dark default style presets |
| Dark default style | Int (preset) | 0 | Preset selector |
| Render backend | GDI / D2D1 radio | auto | D2D1 path uses `WS_EX_NOREDIRECTIONBITMAP` template |

### Position settings

| Field | Applies to | Behavior |
|-------|-----------|----------|
| `task_bar_wnd_on_left` | Both | Right = default (next to tray / system area); Left = next to Start |
| `tbar_wnd_snap` | Win11 left | "Snap" to Start button vs far-left edge |
| Center-align detection | Win11 | Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` DWORD `TaskbarAl` (missing ⇒ center-aligned). If taskbar is left-aligned, window **always stays on the right side** regardless of the on-left setting. |

## Interactions

### Page load (embed) — Windows 11 path

1. Create dialog from template `IDD_TASK_BAR_DIALOG` (`WS_POPUP | WS_SYSMENU`, empty, owner-drawn) — temporary parent = main window.
2. `SetWindowText("TrafficMonitorTaskbarWindow")` (window name used to locate it later).
3. `ModifyStyleEx(0, WS_EX_TOOLWINDOW)` — hides from Alt+Tab and taskbar button list. **No** `WS_EX_NOACTIVATE` anywhere in the codebase.
4. Optional `WS_EX_LAYERED` + `SetLayeredWindowAttributes(LWA_COLORKEY)` only for the GDI color-key transparency path.
5. `FindTaskbarHandle()`: `FindWindow("Shell_TrayWnd")` (or `Shell_SecondaryTrayWnd` picked by `secondary_display_index` when "show in secondary display" is on and taskbar-on-all-displays is enabled).
6. **`SetParent(our_hwnd, Shell_TrayWnd)`** — popup reparented into the taskbar; **window style stays WS_POPUP**. Failure (returns NULL) sets `m_connot_insert_to_task_bar` → fallback mode.
7. Per-monitor DPI set for the taskbar's monitor (Win8.1+).
8. `CalculateWindowSize()` — measures text extents per item (DC `GetTextExtentPoint32`), computes `m_window_width/height`.
9. `AdjustWindowPos(true)` → `AdjustTaskbarWndPos(true)` (see Positioning below).
10. Tooltip created (`TTS_ALWAYSTIP`, topmost, max width 600).
11. Internal 1000 ms timer: re-runs `CalculateWindowSize()` each tick (sets width-changed flag) and pops tooltip while a menu is open.

### Positioning — Windows 11 (`Win11TaskbarDlg::AdjustTaskbarWndPos`)

Coordinates are **parent-relative to Shell_TrayWnd**.

Right side (default):
- `notify_x = TrayNotifyWnd.left - taskbar.left` (parent-relative)
- if `TrayNotifyWnd` missing (secondary display): `notify_x = taskbar.width - DPI(88)`; primary fallback: `taskbar.width - DPI(taskbar_right_space_win11)` (280)
- `x = notify_x - our_width + 2`
- if "avoid overlap with widgets" && widgets button shown && not center-aligned: `x = notify_x - our_width + 2 - DPI(taskbar_left_space_win11)` (160)

Left side (only honored when `tbar_wnd_on_left` && center-aligned):
- snap: `x = Start.left - our_width - 2` (Start rect converted to parent-relative)
- else: `x = 2` (+ `DPI(160)` if widgets button shown)

Vertical (both):
- `y = (Start.Height() - our_height)/2 + (taskbar.Height() - Start.Height()) + DPI(window_offset_top)`
- (the `taskbar.H - start.H` term compensates Win11 build ≥ 22621 touch devices where the taskbar rect is taller than the content row)

Then `MoveWindow(rect)` (plain `::MoveWindow`, reparented coordinates, `repaint=TRUE`). **Nothing is resized** — no sibling is shrunk on Win11; `ResetTaskbarPos()` is empty.

### Positioning — Classical Win7/10 (`ClassicalTaskbarDlg`)

- Hosts found: `ReBarWindow32` (fallback `WorkerW`) inside `Shell_TrayWnd`; inside it `MSTaskSwWClass` (Win10 fallback `MSTaskListWClass`).
- `left_space = MSTaskSw.left - ReBar.left`.
- `SetParent(our_hwnd, ReBarWindow32)` — we become a **sibling** of the task list.
- To make room, the task list (`MSTaskSwWClass`) is **shrunk** by exactly our width with `MoveWindow` on *that* HWND:
  - right side: `MoveWindow(hMin, left_space, 0, minW - ourW, minH, TRUE)`; our `x = left_space + (minW - ourW) + 2`
  - left side: `MoveWindow(hMin, left_space + ourW, 0, minW - ourW, minH, TRUE)`; our `x = left_space`
- Vertical centering: `y = (ReBar.Height() - our_height)/2` (+ `DPI(1)` nudge on Win7 with horizontal arrange).
- Original `MSTaskSwWClass` rect saved (`m_rcMinOri`) and restored:
  - at the **start** of every forced re-adjust (so recompute always begins from the true original size), and
  - on close (`OnCancel` → `ResetTaskbarPos()`).
- Vertical (left/right side) taskbar: same logic on the Y axis, our window centered horizontally with `DPI(2)` margin.

### Positioning — Wine

No embedding: parent = desktop, topmost, positioned by `GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)` at bottom corner.

### Positioning — Fallback (any OS, when SetParent failed)

- Screen-**absolute** coordinates: `our_rect + parent's screen left/top`.
- If the taskbar has the foreground window: `SetWindowPos(&wndTopMost, SWP_NOSIZE|SWP_NOMOVE)` to keep visible above it.
- A warning dialog may be shown once ("cannot insert into task bar") — `m_cannot_insert_to_task_bar_warning`.

### Steady state (main window timer, every 100 ms)

```
m_tBarDlg->AdjustWindowPos();      // re-measure host rects; full re-layout only on force/change
m_tBarDlg->Invalidate(FALSE);      // triggers OnPaint → redraw metrics
```

Full re-layout (restore host → shrink host → place self) happens only when: first call, content width changed, or taskbar orientation changed. Steady-state cost is two `GetWindowRect` calls plus a comparison.

Taskbar-restart handling: the app watches for `WM_TASKBARCREATED` (registered `WM_` message). On taskbar recreation the embedded window is orphaned; the main timer re-detects (and re-embeds) — attempt counter `m_insert_to_taskbar_cnt`.

### User interactions on the strip

| Action | System response |
|--------|-----------------|
| Hover | Tooltip with detailed values (if "show tooltip" on) |
| Left click / click on item | `m_clicked_item` set; click-through behavior per settings |
| Double click | Configured `DoubleClickAction` (default: open Connection Info) |
| Right click | Taskbar-window context menu: display settings (items), taskbar window settings, item order, colors, show/hide, exit |
| Close (menu → exit) | `OnCancel`: `ResetTaskbarPos()` (restore shrunk task list, classical only) + `DestroyWindow` |

## API Dependencies

| API | Purpose |
|-----|---------|
| `FindWindow("Shell_TrayWnd")` / `EnumWindows` for `Shell_SecondaryTrayWnd` | Locate taskbar(s) |
| `FindWindowEx` chain: `TrayNotifyWnd`, `Start` (Win11); `ReBarWindow32`/`WorkerW`, `MSTaskSwWClass`/`MSTaskListWClass` (classical) | Locate anchor/sibling windows |
| `FindWindowExW(..., "Windows.UI.Composition.DesktopWindowContentBridge")` | Win11 taskbar detection |
| `SetParent` | Embed popup into taskbar container (single call, at init) |
| `MoveWindow` | Position self (parent-relative) and shrink/restore `MSTaskSwWClass` |
| `GetWindowRect` | All geometry (physical px when DPI-aware) |
| `SetWindowLong(GWL_EXSTYLE, \| WS_EX_TOOLWINDOW [/\| WS_EX_LAYERED])`, `SetLayeredWindowAttributes` | Styling/transparency |
| `SetWindowPos(&wndTopMost, ...)` | Fallback keep-visible when taskbar is foreground |
| `SetTimer`/`OnTimer` (1000 ms dialog, 100 ms main) | Width-change detection, reposition, repaint |
| `GetTextExtentPoint32` (DC) | Measure item text for sizing |
| Registry `HKCU\...\Explorer\Advanced\TaskbarAl` | Win11 center/left alignment |
| `RegisterWindowMessage("TaskbarCreated")` | Taskbar restart detection |

## Business Rules

1. Embedding is best-effort: failure degrades gracefully to topmost popup, never crashes, warning shown once.
2. The task list's original geometry is **always** restored before re-shrinking (idempotent re-layout) and on exit (no leftover hole in the taskbar).
3. On Win11 with left-aligned taskbar, the "show on left" setting is ignored — window stays right of the icons (documented limitation in code comments).
4. Content width changes (value length, DPI change, item toggle) force a full re-layout on the next 100 ms tick.
5. Multi-monitor: exactly one taskbar window; secondary-display target chosen by index; secondary Win11 taskbars lack `TrayNotifyWnd` (88 px clock-area constant used).
6. Render backend selection auto-falls-back (D2D1+DComposition → D2D1 → GDI) based on capability; D2D1 uses the `WS_EX_NOREDIRECTIONBITMAP` dialog template to avoid redirection-bitmap copies.
