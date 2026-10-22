# Options — General Settings

> **Route:** Options dialog → tab "General" (`IDD_GENERAL_SETTINGS_DIALOG`)
> **Generated:** 2026-10-22

## Overview

Application-wide behavior: auto-run, update checking, monitoring interval, language, hardware monitoring enablement, notification tips, and connection display scope.

## Fields

### Auto-run & updates
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Auto run at startup | Checkbox | off | Two methods: **Registry** (`HKCU\...\Run`) or **Task Scheduler** (radio selectable) |
| Reset auto-run path | Button | — | Re-registers the path (fixes moved exe) |
| Check update when start | Checkbox | on | |
| Check update now | Button | — | Immediate check |
| Update source | Radio GitHub / Gitee | by language | zh→Gitee |

### Monitoring
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Monitor interval | Spin (ms) | 1000 | Clamped 100–30000; "Restore default" button |
| CPU usage acquisition | Radio PDH / CPU-time | PDH | PDH more accurate |
| Select CPU core | Combo | "Core Average" | Per-core usage display name |
| Hardware monitoring: CPU / GPU / HDD / Mainboard | Checkboxes | off | Enabling shows a one-time warning dialog (suppressible via registry `DontShowMeThisDialogAgain`) |
| Select hard disk | Combo | — | Which disk to monitor |
| Show all network interfaces | Checkbox | off | All adapters visible in selector |
| Select connections… | Button | — | Manual adapter hide-list |

### Notifications
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Show notify icon | Checkbox | on | |
| Traffic tip + value + unit | Check+Spin+Combo | off/200/KB/s | |
| Memory usage tip + value | Check+Spin | off/80 | |
| CPU/GPU/HDD/Mainboard temperature tips + values | Check+Spin | off/80 | Enabled only when corresponding hardware monitoring on |

### Language & misc
| Field | Type | Default | Notes |
|-------|------|---------|-------|
| Language | Combo | system | Restart required to apply (string table reload) |
| Open config path | Button | — | Opens INI folder in Explorer |
| Plugin manager | Button | — | Page 10 |

## Interactions
- **Apply/OK** validates: interval clamped; auto-run method applied immediately if toggled (registry write / schtasks); hardware-monitor warning shown once per opt-in.
- **Cancel** discards; changes flagged per-field (`IsAutoRunModified`, `IsMonitorTimeSpanModified`, …) to skip unchanged writes.
- Enabling hardware monitoring disables the corresponding taskbar temperature items automatically (cross-tab effect applied on Apply).

## Business Rules
- Auto-run via Task Scheduler used when registry write is not possible; method persisted.
- The hardware warning is a consent gate for admin-requiring features (standard build).
