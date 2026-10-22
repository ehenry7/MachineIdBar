# Plugin Manager

> **Route:** General settings tab → "Plugin manager" button (`PluginManagerDlg`); also "Other functions" menu
> **Implementation:** `PluginManager` (DLL host) + `PluginInfoDlg` + `PluginUpdateHelper`
> **Generated:** 2026-10-22

## Overview

TrafficMonitor is extensible via **DLL plugins** (github.com/zhongyang219/TrafficMonitorPlugins). Plugins add extra display items (e.g., the official hardware-temperature plugin) that appear in the main window and taskbar window item pickers.

## Manager UI
| Control | Behavior |
|---------|----------|
| Plugin list (name, version, status) | Shows discovered DLLs in `plugins` dir |
| Enable / Disable | Toggles plugin items across both windows (disabled list persisted as `plugin_disabled` string set) |
| Update plugin | `PluginUpdateHelper` fetches latest from GitHub/Gitee (per update source) |
| Plugin info | `PluginInfoDlg`: description, author, homepage |
| Display text per plugin item | Via Display Text Settings (`plugin_display_str_taskbar_window` / main equivalent) |

## Plugin Contract (reverse-engineered)
- Plugin DLL exports a registration entry point; host queries items (`IPluginItem`), each item provides: ID (string), display label, current value string (polled each monitor cycle), and optional tooltip.
- `CommonDisplayItem` unifies built-in `DisplayItem` and plugin items; `IsPlugin()` distinguishes.
- Disabled plugins keep their config but hide their items.

## Business Rules
- Plugin load failure → item hidden, no crash; error surfaced in manager.
- Plugin items are excluded from taskbar item set when the plugin is disabled (bitmask cleanup on toggle).
- Update checks respect the same GitHub/Gitee source selection as app updates.
