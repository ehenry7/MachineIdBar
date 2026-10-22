# Skins (Selection & Customization)

> **Route:** Right-click → "Other functions" → "Change skin" (`SkinDlg` + `CSkinPreviewView` + `SkinManager`/`SkinFile`)
> **Storage:** `skins/<skin_name>/` folders next to exe: `background.bmp`(+`_l`), optional `background.png`(+`_l`), `skin.ini` and/or `skin.xml`
> **Generated:** 2026-10-22

## Overview

Skins re-theme the **main window only** (taskbar window keeps its own color/font settings). Each skin is a folder; the folder name is the skin name. Built-in skins ship in `res`/`skins`; user skins drop in via the `skins` directory.

## Skin File Format
| File | Purpose |
|------|---------|
| `background.bmp` / `background_l.bmp` | Normal / "larger (more info)" mode background |
| `background.png` / `background_l.png` | PNG variants (transparent backgrounds supported since 1.85) |
| `skin.ini` | Legacy config: text colors, fonts, author, per-item positions/sizes |
| `skin.xml` | Extended config (tinyxml2): additionally enables temperature/GPU-usage display support |

`SkinNameNormalize` strips `.\skins\` prefixes for backward compatibility.

## Selection UI
- Grid of skin thumbnails (preview view), click to select, OK applies.
- **Auto-adapt light/dark** option: separate skin for light mode and one for dark mode (`skin_name_light_mode` / `skin_name_dark_mode`), switched automatically with the Windows theme.

## Per-Skin Settings
Each skin carries its own `SkinSettingData`: font, display prefixes, per-item text colors (+ specify-each-item flag). Stored in the main settings file under skin-scoped blocks; switching skins swaps the active look; main-window tab edits write to the *current* skin's block.

## Interactions
- Select skin → live apply (background, colors, fonts, item layout).
- Author shown in tooltip/about.
- No in-app skin *editor* — skins are edited externally (wiki tutorial).

## Business Rules
- XML-only feature gating: temperature & GPU-usage items require `skin.xml`.
- If a skin file is deleted at runtime, app falls back to default skin on next switch.
- Light/dark auto-adapt only available on Win10+.
