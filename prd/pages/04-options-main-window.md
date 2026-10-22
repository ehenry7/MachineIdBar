# Options — Main Window Settings

> **Route:** Options dialog → tab "Main window" (`IDD_MAINWND_SETTINGS_DIALOG`)
> **Generated:** 2026-10-22

## Overview

Appearance and behavior of the main floating window only (taskbar window has its own tab). Many appearance values are stored **per skin** rather than globally, so changing a skin changes fonts/colors.

## Fields

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Window transparency | Slider/Spin 0–100 | 80 | |
| Always on top | Checkbox | on | |
| Lock window position | Checkbox | off | |
| Mouse penetrate | Checkbox | off | Click-through |
| Hide main window on start | Checkbox | off | Tray-only start |
| Hide when fullscreen | Checkbox | on | |
| Swap upload/download | Checkbox | off | |
| Background color | Color static | skin default | Click opens color dialog |
| Specify each item color | Checkbox | off | Enables per-item color statics (CMFCColorDialogEx / ColorSettingListCtrl) |
| Font… | Button | default font 10 pt | Standard font dialog |
| Display text settings… | Button | built-in prefixes | Page 06 |
| Show tooltip | Checkbox | on | |
| Speed unit | Combo (AUTO/KB/s/MB/s) | AUTO | |
| Unit: bytes / bits | Radio | bytes | |
| Speed short mode | Checkbox | off | |
| Digits number | Combo | 4 | |
| Hide unit | Checkbox | off | |
| Hide percent | Checkbox | off | |
| Separate value/unit with space | Checkbox | on | |
| Memory display | Combo (%/used/available) | % used | |
| CPU/Memory graph type | Radio bar / line | bar | |
| Show graph dashed box | Checkbox | off | |
| Double-click action | Combo (`DoubleClickAction`) | Connection info | |
| Double-click app (if SEPCIFIC_APP) | Path + Browse | `%system%\Taskmgr.exe` | |
| Allow out of border | Checkbox | off | |

## Interactions
- Color statics repaint live with the selected color; per-item colors list dialog shows one row per display item (enable + color).
- Font change re-measures the window (width/height recompute on next tick).
- OK/Apply writes to `config` section + current skin's settings block; per-skin values stored separately so other skins keep their own look.
- Changes take effect immediately on the live window (no restart).

## Business Rules
- Light/dark auto-adapt (Win10+) stores the *current* color set into the matching preset before switching (`SaveColorSettingToDefaultStyle` pattern also used in taskbar tab).
- If per-item color is enabled but some items unset, they inherit the main text color.
