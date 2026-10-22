# Options — Taskbar Window Settings

> **Route:** Options dialog → tab "Taskbar window" (`IDD_TASKBAR_SETTINGS_DIALOG`)
> **Generated:** 2026-10-22

## Overview

Every appearance/position parameter of the embedded taskbar strip (page 02). All values persisted under the `task_bar` INI section.

## Fields

### Display items & text
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Display items (up/down/CPU/memory/GPU/HDD/freq/temps/today/plugin…) | Checkboxes (Display settings dialog) | up+down | Bitmask `tbar_display_item` |
| Item order | "Set order…" button (SetItemOrderDlg) | definition order | Up/down reorder |
| Display text settings… | Button | built-ins | Page 06 (taskbar variant) |
| Font… | Button + size spin | default font 9 pt | |
| Specify each item color | Checkbox | off | Per-item color list |

### Colors
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Text color | Color static | white | Per-item when specified |
| Background color | Color static | default | 0 = transparent |
| Background transparent | Checkbox | off | Color-key mode |
| Transparent color | Color static | default | |
| Status bar color | Color static | default | |
| Show status bar | Checkbox | on | |
| Auto set background color | Checkbox | off | Follow system |
| Default style… | Button + preset menu | 0 | Apply/modify light/dark presets |
| Auto-adapt light theme (Win10+) | Checkbox | off | |

### Units & formatting
| Field | Type | Default |
|-------|------|---------|
| Speed unit (AUTO/KB/s/MB/s) | Combo | AUTO |
| Unit bytes / bits | Radio | bytes |
| Hide unit | Checkbox | off |
| Hide percent | Checkbox | off |
| Speed short mode | Checkbox | on |
| Digits number | Combo | 4 |
| Separate value/unit with space | Checkbox | on |
| Value right align | Checkbox | on |
| Memory display | Combo | % used |
| Show tooltip | Checkbox | on |

### Layout
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Window on left | Checkbox | off (right) | Disabled on Win11 unless center-aligned (always right when left-aligned taskbar) |
| Horizontal arrange (Win7) | Checkbox | off | |
| Item space | Spin | 8 | px |
| Vertical margin | Spin | 0 | |
| Window offset top / left | Spins | 0 | Manual nudge (DPI-scaled) |
| CPU/Memory graph type | Radio bar / plot | bar | |
| Show graph dashed box | Checkbox | off | |
| Net speed figure: show / max value / max unit | Check + Spin + Combo | off | Speed bar scaling |
| Usage graph follow system color | Checkbox | off | |

### Win11-specific (button "Win11 settings…" → `Win11TaskbarSettingDlg`)
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Avoid overlap with widgets | Checkbox | off | Reserves left space |
| Taskbar left space (widgets) | Int | 160 | px (DPI-scaled) |
| Taskbar right space | Int | 280 | Fallback when TrayNotifyWnd missing |
| Snap to icons (left side) | Checkbox (`tbar_wnd_snap`) | off | |

### Position
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Show in secondary display | Checkbox | off | + display combo (all monitors with taskbar) |
| Double-click action | Combo | Connection info | Same enum as main window |
| Render backend | Radio GDI / D2D1 | auto | D2D1 → `WS_EX_NOREDIRECTIONBITMAP` template; enables color emoji checkbox |

## Interactions
- **Win11 settings button** visible/enabled only when `IsWindows11Taskbar()` is true; conversely the "window on left" checkbox is disabled in that case (kept for classical).
- **Default style button** → popup menu: apply preset / save current as preset (separate menus for dark/light); on save, current colors/fonts written to that preset slot.
- Color statics repaint live; changing background to 0 + transparent check enables color-key path live.
- Item order changes re-layout the strip on next tick (full re-layout forced).
- OK/Apply: values validated (`ValidItemSpace`, `ValidWindowOffsetTop/Left` clamp to sensible ranges), written to INI, live window re-created/re-styled if render backend or transparency mode changed.

## Business Rules
- Temperature items hidden unless hardware monitoring enabled in General tab (cross-tab dependency evaluated at load and on Apply).
- `WITHOUT_TEMPERATURE` builds strip the item list at compile time.
- Changing the taskbar window between left/right on Win11 with left-aligned taskbar has no effect (always right) — setting still persists.
- Secondary display option only active when Windows "taskbar on all displays" is on.
