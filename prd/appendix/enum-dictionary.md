# Enum & Constants Dictionary

> **Source:** `CommonData.h`, `DisplayItem.h`, `Common.h`, `stdafx.h`
> **Generated:** 2026-10-22

## SpeedUnit (network speed unit selector)
| Value | Meaning |
|-------|---------|
| AUTO | Auto: pick KB/s or MB/s based on magnitude |
| KBPS | Fixed KB/s |
| MBPS | Fixed MB/s |

## DoubleClickAction (double-click on main or taskbar window)
| Value | Meaning |
|-------|---------|
| CONNECTION_INFO | Open connection details (default) |
| HISTORY_TRAFFIC | Open history traffic statistics |
| SHOW_MORE_INFO | Toggle expanded main-window mode |
| OPTIONS | Open options dialog |
| TASK_MANAGER | Launch Task Manager |
| SEPCIFIC_APP | Launch a user-specified executable |
| CHANGE_SKIN | Open skin picker |
| NONE | Do nothing |

## MemoryDisplay (memory readout mode)
| Value | Meaning |
|-------|---------|
| USAGE_PERCENTAGE | % used (default) |
| MEMORY_USED | Used bytes |
| MEMORY_AVAILABLE | Available bytes |

## HistoryTrafficViewType
| Value | Meaning |
|-------|---------|
| HV_DAY | Day view |
| HV_WEEK | Week view |
| HV_MONTH | Month view |
| HV_QUARTER | Quarter view (Jan/Apr/Jul/Oct starts) |
| HV_YEAR | Year view |

## ColorMode
| Value | Meaning |
|-------|---------|
| Default | Follow current |
| Light | Force light (Win10 light theme detected at startup) |

## HardwareItem (bitmask; `general/hardware_monitor_item`)
| Value | Bit | Meaning |
|-------|-----|---------|
| HI_CPU | 1<<0 | CPU temperature monitoring |
| HI_GPU | 1<<1 | GPU temperature monitoring |
| HI_HDD | 1<<2 | HDD temperature monitoring |
| HI_MBD | 1<<3 | Mainboard temperature monitoring |

## CpuUsageAcquireMethod (GeneralSettingData)
| Value | Meaning |
|-------|---------|
| CA_PDH | PDH performance counter (default) |
| (CPU-time) | GetSystemTimes delta method |

## DisplayItem (built-in taskbar/main items; `DisplayItem.h`)
| Constant | Ordinal | Meaning |
|----------|---------|---------|
| TDI_UP | 0 | Upload speed |
| TDI_DOWN | 1 | Download speed |
| TDI_CPU | 2 | CPU usage % |
| TDI_MEMORY | 3 | Memory |
| TDI_GPU_USAGE | 4 | GPU usage % |
| TDI_CPU_TEMP | 5 | CPU temperature |
| TDI_GPU_TEMP | 6 | GPU temperature |
| TDI_HDD_TEMP | 7 | HDD temperature |
| TDI_MAIN_BOARD_TEMP | 8 | Mainboard temperature |
| TDI_HDD_USAGE | 9 | Disk usage % |
| TDI_TOTAL_SPEED | 10 | Total (up+down) speed |
| TDI_CPU_FREQ | 11 | CPU frequency |
| TDI_TODAY_TRAFFIC | 12 | Today's total traffic |

Temperature items (5–8) compiled out in `WITHOUT_TEMPERATURE` builds. Plugin items are not in this enum — addressed by plugin ID inside `CommonDisplayItem`.

## Traffic colors (status/graph palette)
| Constant | RGB |
|----------|-----|
| TRAFFIC_COLOR_GREEN | (128, 194, 105) |
| TRAFFIC_COLOR_YELLOW | (255, 216, 58) |
| TRAFFIC_COLOR_RED | (255, 95, 74) |
| TRAFFIC_COLOR_DARK_RED | (166, 19, 0) |

## Key constants
| Constant | Value | Purpose |
|----------|-------|---------|
| TASKBAR_TIMER | 1236 | Timer ID used by both the 1 s dialog timer and the 100 ms main timer |
| MONITOR_TIME_SPAN_MIN / MAX | 100 / 30000 | Monitor interval clamp (ms) |
| TASKBAR_WINDOW_NAME | "TrafficMonitorTaskbarWindow" | Window text set on the embedded strip (locate-by-name) |
| default digits number | 4 | Speed decimals (short mode) |
| taskbar_left_space_win11 (default) | 160 | Widgets-avoidance left reserve (px, DPI-scaled) |
| taskbar_right_space_win11 (default) | 280 | Right-space fallback (px, DPI-scaled) |
| secondary Win11 clock area | 88 | Px constant when TrayNotifyWnd absent |
