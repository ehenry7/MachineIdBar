# Auxiliary Dialogs

> **Generated:** 2026-10-22

## 1. About (`AboutDlg`)
Version, links (GitHub/Gitee, FAQ), license notes. Static info; OK closes.

## 2. Message (`MessageDlg`)
Generic info/warning box (e.g., "cannot insert into taskbar" first-time warning, update-available notice). Buttons: OK / (optional) Don't show again.

## 3. App Already Running (`AppAlreadyRuningDlg`)
Shown when a second instance starts while one is running (single-instance guard). Buttons: go to existing window / exit new instance.

## 4. Select Connections (`SelectConnectionsDlg`)
Adapter list with checkboxes; "auto select" and "select all" modes; hidden-connection list persisted (`connections_hide`). Used to choose which adapters feed the speed display.

## 5. Set Item Order (`SetItemOrderDlg`)
Up/down reorder of taskbar display items (persisted order used by layout).

## 6. Icon Select (`IconSelectDlg`)
Choose system-tray icon variant (white/blue/etc.); index persisted; auto-adapt option follows Win10 theme.

## 7. Taskbar Color / Main Wnd Color (`TaskbarColorDlg`, `MainWndColorDlg`, `CMFCColorDialogEx`, `ColorSettingListCtrl`)
Full color pickers; per-item color list (row per item: enable checkbox + color button) used by both settings tabs when "specify each item color" is on.

## 8. Win11 Taskbar Settings (`Win11TaskbarSettingDlg`)
Win11-only sub-dialog (see page 05): widgets-overlap avoidance, left/right spacing constants, snap behavior.

## 9. Skin Preview (`CSkinPreviewView`)
Thumbnail renderer for the skin picker.

## Business Rules (shared)
- All dialogs parented to the active window; modal.
- Language applied from string table at startup; dialogs pick up translations on next run.
- DPI: per-monitor aware; control metrics scale.
