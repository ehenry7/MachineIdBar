# Display Text Settings

> **Route:** From Main window or Taskbar window settings tab → "Display text settings…" (`DisplayTextSettingDlg`)
> **Generated:** 2026-10-22

## Overview

Lets the user edit the **prefix text** shown before each display item's value (e.g. change `CPU: ` to `Cpu: ` or a custom label), separately for the main window and the taskbar window.

## Fields

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| One row per display item: prefix text | Edit per item | built-in (`": "`, `"CPU: "`, `"Memory: "`, …) | Applies only to items currently shown |
| (plugin items) prefix text | Edit per item | plugin default | Loaded via `LoadPluginDisplayStr` |

## Interactions
- OK/Apply writes `task_bar` (or `config`) display-string map; live windows re-render on next repaint.
- Cancel discards.
- Strings stored per-window-surface and per-skin (main-window values live in the skin's settings block; taskbar values in `task_bar` section).

## Business Rules
- Empty prefix = value only.
- Prefix changes do not affect item width until the next width re-measure (≤ 1 s).
