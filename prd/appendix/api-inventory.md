# API & Data-Source Inventory

> **Note:** TrafficMonitor is a closed-UI desktop app — there are no HTTP APIs; "endpoints" are Win32/system data sources and the settings file. This inventory lists every external data source the product depends on.
> **Generated:** 2026-10-22

## Network counters
| Source | API | Trigger | Notes |
|--------|-----|---------|-------|
| Adapter list + per-adapter in/out octets | `GetIfTable` (iphlpapi) | Every monitor tick (default 1 s) | Drives connection selector and speed deltas |
| Adapter IP details (IPv4/IPv6, gateway, mask, DHCP) | `GetAdaptersAddresses` / `GetIfTable` + IP helper family | Connection info dialog open + refresh | Page 07 |
| Connection name | `MIB_IFTABLE.bIfDesc` | Selection | Displayed in UI |

## CPU / memory / hardware
| Source | API | Trigger |
|--------|-----|---------|
| CPU usage % | PDH `\Processor Information(_Total)\% Processor Time` (or `GetSystemTimes` delta) | Every monitor tick |
| CPU frequency | PDH `\Processor Information(_Total)\Processor Frequency` | Every monitor tick |
| Memory | `GlobalMemoryStatusEx` | Every monitor tick |
| GPU usage % | PDH GPU engine counters (standard ≥1.86 / Lite ≥1.86) | Every monitor tick |
| Disk usage % | PDH physical disk counters (selected disk) | Every monitor tick |
| Temperatures (CPU/GPU/HDD/MBD) | Hardware query (`PdhHardwareQuery/` + WMI) — **standard build only, admin required** | Every monitor tick |

## Taskbar geometry (embedding)
| Source | API | Trigger |
|--------|-----|---------|
| Primary taskbar | `FindWindow("Shell_TrayWnd")` | Embed + every 100 ms tick |
| Secondary taskbars | `EnumWindows` filtering class `Shell_SecondaryTrayWnd`, ordered by `EnumDisplayMonitors` | Embed (secondary option) + periodic re-check |
| Win11 detection | `FindWindowEx(Shell_TrayWnd, "Windows.UI.Composition.DesktopWindowContentBridge")` | App init + on demand |
| Win11 anchors | `FindWindowEx(Shell_TrayWnd, "TrayNotifyWnd")`, `FindWindowEx(Shell_TrayWnd, "Start")` | Embed + 100 ms re-layout check |
| Classical anchors | `FindWindowEx(Shell_TrayWnd, "ReBarWindow32")` (fallback `WorkerW`) → `FindWindowEx(bar, "MSTaskSwWClass")` (fallback `MSTaskListWClass`) | Embed + 100 ms re-layout check |
| Embed | `SetParent(our_hwnd, container)` | Once at open (or re-embed after taskbar restart) |
| Make room / restore | `MoveWindow(MSTaskSwWClass, …)` | Full re-layout events + close |
| Position self | `MoveWindow(our_hwnd, x, y, w, h, TRUE)` (parent-relative) | Full re-layout events |
| Fallback keep-visible | `SetWindowPos(&wndTopMost, SWP_NOSIZE|SWP_NOMOVE)` | When taskbar foreground in fallback mode |
| Styling | `SetWindowLong(GWL_EXSTYLE, |WS_EX_TOOLWINDOW [|WS_EX_LAYERED])`, `SetLayeredWindowAttributes(LWA_COLORKEY)` | Init |
| Taskbar restart | `RegisterWindowMessage("TaskbarCreated")` → `WM_TASKBARCREATED` handler | Event-driven re-embed |
| Center-alignment | Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` DWORD `TaskbarAl` (absent ⇒ centered) | Win11 layout decisions |

## System / misc
| Source | API | Purpose |
|--------|-----|---------|
| Per-monitor DPI | `SetProcessDpiAwarenessContext`/`SetProcessDpiAwareness`, `GetDeviceCaps(LOGPIXELSY)`, `GetMonitorInfo` | All geometry + font scaling |
| Screen rects | `EnumDisplayMonitors` (work area + monitor rects) | Off-screen window recovery |
| Fullscreen detection | Foreground window rect vs work area | Hide-when-fullscreen |
| System theme | `IsWindows10LightTheme` (registry/theme API) | ColorMode + notify icon + auto-adapt |
| Auto-run (registry method) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Start with Windows |
| Auto-run (task-scheduler method) | Task Scheduler (schtasks API) | Start with Windows (alternative) |
| Single instance | Named event / window search | AppAlreadyRuningDlg |
| Crash reporting | `crashtool` (minidump) | Diagnostics |
| Update checking | HTTP (GitHub / Gitee release API per `update_source`) | "Check update now" / on start |
| Settings | `TrafficMonitor.ini` (INI) | All persisted configuration |
| History | Binary history file (`CHistoryTrafficFile`) | Daily traffic records |
| Skins | `skins/<name>/{background*.bmp|png, skin.ini, skin.xml}` | Theming |
| Plugins | `plugins/*.dll` + plugin settings keys | Extensibility |

## INI section map (settings inventory)
| Section | Keys (abridged) |
|---------|-----------------|
| `general` | language, check_update_when_start, show_all_interface, update_source, cpu_usage_acquire_method, monitor_time_span, hard_disk_name, cpu_core_name, hardware_monitor_item, connections_hide |
| `config` | transparency, always_on_top, lock_window_pos, show_notify_icon, show_cpu_memory, mouse_penetrate, show_task_bar_wnd, position_x/y, auto_select, select_all, hide_main_window, connection_name, skin_selected, notify_icon_selected, notify_icon_auto_adapt, swap_up_down, hide_main_wnd_when_fullscreen, speed_short_mode, separate_value_unit_with_space, show_tool_tip, memory_display, unit_byte, speed_unit, hide_unit, hide_percent, double_click_action, double_click_exe, alow_out_of_border |
| `connection` | auto_select, select_all, connection_name |
| `skins` | skin_auto_adapt, skin_name_dark_mode, skin_name_light_mode (+ per-skin setting blocks) |
| `task_bar` | task_bar_back_color, transparent_color, status_bar_color, task_bar_text_color (per-item), specify_each_item_color, tbar_display_item, show_taskbar_wnd_in_secondary_display, secondary_display_index, font, disp_str (per-item), tbar_wnd_on_left, speed_short_mode, tbar_wnd_snap, unit_byte, speed_unit, hide_unit, hide_percent, value_right_align, horizontal_arrange, show_status_bar, separate_value_unit_with_space, show_tool_tip, digits_number, memory_display, double_click_action, double_click_exe, cm_graph_type, show_graph_dashed_box, item_space, vertical_margin, window_offset_top/left, avoid_overlap_with_widgets, taskbar_left_space_win11, taskbar_right_space_win11, auto_adapt_light_theme, dark_default_style |
| `notify_tip` | traffic_tip_enable/value/unit, memory_tip_enable/value, cpu/gpu/hdd/mainboard temperature tip enable+value |
