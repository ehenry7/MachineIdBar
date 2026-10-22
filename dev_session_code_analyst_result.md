I now have a complete picture of TrafficMonitor's taskbar embedding mechanism. Here is the detailed report.

---

# TrafficMonitor Taskbar Embedding — Complete Mechanism

## 1. Architecture Overview

There are three concrete taskbar implementations, all deriving from the abstract base `CTaskBarDlg` (which itself derives from MFC's `CDialogEx`). The right one is chosen in `CTrafficMonitorDlg::OpenTaskBarWnd()` (`TrafficMonitorDlg.cpp:576`):

```cpp
if (theApp.m_win_version.IsWine())
    m_tBarDlg = new CWineTaskbarDlg();      // fallback: desktop window as parent
else if (theApp.IsWindows11Taskbar())
    m_tBarDlg = new CWin11TaskbarDlg();     // Win11: parent = Shell_TrayWnd itself
else
    m_tBarDlg = new CClassicalTaskbarDlg(); // Win7/10: parent = ReBarWindow32
```

The base class `CTaskBarDlg` declares four **pure virtual** hooks (`TaskBarDlg.h:84-88`) that each subclass must implement:

```cpp
virtual void InitTaskbarWnd() = 0;             // locate child HWNDs of the taskbar
virtual void AdjustTaskbarWndPos(bool force_adjust) = 0;  // position our window + resize the host
virtual void ResetTaskbarPos() = 0;            // restore the host's original size on close
virtual void CheckTaskbarOnTopOrBottom() = 0;  // is taskbar horizontal?
virtual HWND GetParentHwnd() = 0;              // which HWND do we SetParent into?
```

Shared base members (`TaskBarDlg.h:92-101`):
- `HWND m_hTaskbar` — the top-level `Shell_TrayWnd` (or secondary `Shell_SecondaryTrayWnd`)
- `CRect m_rcTaskbar` — its screen rect
- `CRect m_rect` — our own window rect (coordinates are **client-relative to the parent**, not screen)
- `int m_window_width, m_window_height` — computed desired size
- `bool m_connot_insert_to_task_bar` — set true if `SetParent` fails
- `bool m_taskbar_on_top_or_bottom` — orientation flag

`ClassicalTaskbarDlg` adds (`ClassicalTaskbarDlg.h:16-25`): `m_hBar` (ReBarWindow32), `m_hMin` (MSTaskSwWClass), `m_rcBar`, `m_rcMin`, `m_rcMinOri`, `m_left_space`, `m_top_space`, `m_last_width`, `m_last_height`.

`Win11TaskbarDlg` adds: `m_hNotify` (TrayNotifyWnd), `m_hStart` (Start button), `m_rcNotify`, `m_rcStart`, `m_last_notify_width`, `m_last_start_pos`.

---

## 2. Dialog Template (Initial Window Styles)

From `TrafficMonitor.rc` (UTF-16):

```
IDD_TASK_BAR_DIALOG DIALOGEX 0, 0, 72, 15
STYLE DS_SETFONT | WS_POPUP | WS_SYSMENU
FONT 9, "微软雅黑", 400, 0, 0x86
BEGIN
END

IDD_TASK_BAR_DIALOG_NOREDIRECTIONBITMAP DIALOGEX 0, 0, 72, 15
STYLE DS_SETFONT | WS_POPUP | WS_SYSMENU
EXSTYLE 0x200000L          ; WS_EX_NOREDIRECTIONBITMAP
FONT 9, "微软雅黑", 400, 0, 0x86
BEGIN
END
```

**Key point:** The dialog template creates the window with **`WS_POPUP | WS_SYSMENU`** — i.e. a *popup* window, **NOT** a child window, and **NOT** a child of the taskbar. The reparenting happens later. The dialog is empty (no child controls); all content is owner-drawn in `OnPaint`.

The two template IDs are selected in `OpenTaskBarWnd()` based on the render backend:
```cpp
case RenderType::D2D1_WITH_DCOMPOSITION:
    m_tBarDlg->Create(IDD_TASK_BAR_DIALOG_NOREDIRECTIONBITMAP, this);   // WS_EX_NOREDIRECTIONBITMAP
    break;
default:
    m_tBarDlg->Create(IDD_TASK_BAR_DIALOG, this);                       // plain
```
Note the `Create` parent argument is `this` (the main `CTrafficMonitorDlg`) — a **temporary** parent used only during `Create`; it is replaced in `OnInitDialog` via `SetParent`.

---

## 3. `OnInitDialog()` — Exact Sequence (`TaskBarDlg.cpp:994-1059`)

This is the canonical embedding sequence. In order:

1. **`CDialogEx::OnInitDialog()`** — MFC creates the actual HWND from the template (popup, WS_SYSMENU).

2. **`SetWindowText(TASKBAR_WINDOW_NAME)`** — sets title to `"TrafficMonitorTaskbarWindow"` (`stdafx.h:99`). Used to find the window later.

3. **`DisableRenderFeatureIfNecessary(m_supported_render_enums)`** — decides GDI vs D2D1 vs D2D1+DComposition.

4. **`ModifyStyleEx(0, WS_EX_TOOLWINDOW)`** — adds `WS_EX_TOOLWINDOW` so the window does **not** appear in the Alt+Tab list / taskbar. This is the only style modification in OnInitDialog.

5. **`m_pDC = GetDC()`** — cache a DC for text-extent measuring.

6. **`m_hTaskbar = FindTaskbarHandle(m_is_secondary_display)`** — locates the taskbar HWND (see §4).

7. **`::GetWindowRect(m_hTaskbar, m_rcTaskbar)`** — cache the taskbar's screen rect.

8. **`ApplyWindowTransparentColor()`** — conditionally adds `WS_EX_LAYERED` (and `SetLayeredWindowAttributes` with `LWA_COLORKEY` for GDI color-key transparency). This is where `SetWindowLong(GWL_EXSTYLE, ... | WS_EX_LAYERED)` is called. For D2D1+DComposition it sets nothing. Note: `WS_EX_NOACTIVATE` is **never** used anywhere in the codebase.

9. **`InitTaskbarWnd()`** — virtual; subclass locates the host child windows (see §5).

10. **`m_connot_insert_to_task_bar = !(::SetParent(this->m_hWnd, GetParentHwnd()))`** — **THE key embedding call.** The popup dialog is reparented into the taskbar container. `SetParent` returns 0 (NULL) on failure, which sets the fallback flag. **Note this is the only `SetParent` call in the entire embedding code.** The window is created as a popup first and then reparented — it is *not* created as a child of ReBarWindow32 from the start.

11. **DPI detection** — `SetDPI(...)` based on the monitor containing `m_rcTaskbar` (Win8.1+).

12. **`SetTextFont()`** then `m_pDC->SelectObject(&m_font)`.

13. **`CheckTaskbarOnTopOrBottom()`** — sets `m_taskbar_on_top_or_bottom`.

14. **`CalculateWindowSize()`** — computes `m_window_width`, `m_window_height`, and per-item rects.

15. **`m_rect.SetRectEmpty(); m_rect.bottom = m_window_height; m_rect.right = m_rect.left + m_window_width;`** — initializes `m_rect` to a zero-origin rect of the desired size.

16. **`m_error_code = GetLastError();`**

17. **`AdjustWindowPos(true)`** — first real positioning pass (see §6). With `force_adjust=true` it first calls `ResetTaskbarPos()` (a no-op on first call since `m_rcMinOri` is empty), then `AdjustTaskbarWndPos(true)`.

18. **`SetBackgroundColor(theApp.m_taskbar_data.back_color)`** — MFC background color (also used as the color-key transparent color).

19. **Tooltip creation** — `m_tool_tips.Create(this, TTS_ALWAYSTIP)`, `SetMaxTipWidth(600)`, `AddTool`, `SetToolTipsTopMost()` (sets the tooltip `wndTopMost`).

20. **`SetTimer(TASKBAR_TIMER, 1000, NULL)`** — `TASKBAR_TIMER == 1236` (`stdafx.h:87`). A 1-second timer inside the taskbar dialog itself that calls `CalculateWindowSize()` every tick (and pops the tooltip if a menu is open) — see `OnTimer` (`TaskBarDlg.cpp:1248`). Note this timer does **not** call `AdjustWindowPos`; that is driven by the *main* window's timer (see §7).

`OnInitDialog` returns `TRUE`.

**Summary of Win32 API calls in OnInitDialog, in order:**
`CreateDialogIndirect` (via MFC) → `SetWindowText` → `ModifyStyleEx(WS_EX_TOOLWINDOW)` → `GetDC` → `FindWindow(Shell_TrayWnd)` (or secondary) → `GetWindowRect` → [optional `SetWindowLong(GWL_EXSTYLE, |WS_EX_LAYERED)` + `SetLayeredWindowAttributes`] → `FindWindowEx` chain (in `InitTaskbarWnd`) → `SetParent(m_hWnd, parentHwnd)` → `GetWindowRect` (DPI) → `CheckTaskbarOnTopOrBottom` → `AdjustWindowPos(true)` (which itself calls `ResetTaskbarPos`, `GetWindowRect`, `CalculateWindowSize`, `AdjustTaskbarWndPos`, `MoveWindow`) → `SetBackgroundColor` → `CreateWindowEx` (tooltip) → `SetWindowPos` (tooltip topmost) → `SetTimer`.

---

## 4. `FindTaskbarHandle()` (`TaskBarDlg.cpp:605-631`)

```cpp
HWND CTaskBarDlg::FindTaskbarHandle(bool& is_scendary_display)
{
    is_scendary_display = false;
    HWND hTaskbar = nullptr;
    if (theApp.m_taskbar_data.show_taskbar_wnd_in_secondary_display
        && CWindowsSettingHelper::IsTaskbarShowingInAllDisplays())
    {
        std::vector<HWND> secondary_taskbars;
        CTaskbarHelper::GetAllSecondaryDisplayTaskbar(secondary_taskbars);
        // pick one by secondary_display_index
        hTaskbar = secondary_taskbars[index];
        if (hTaskbar) is_scendary_display = true;
    }
    if (hTaskbar == nullptr)
        hTaskbar = ::FindWindow(_T("Shell_TrayWnd"), NULL);   // primary taskbar
    return hTaskbar;
}
```

`CTaskbarHelper::GetAllSecondaryDisplayTaskbar` (`TaskbarHelper.cpp`) enumerates monitors via `EnumDisplayMonitors` and windows via `EnumWindows`, collecting every top-level window whose class name is **`Shell_SecondaryTrayWnd`**, sorted by monitor order. So on multi-monitor setups the parent may be `Shell_SecondaryTrayWnd` instead of `Shell_TrayWnd`.

---

## 5. `InitTaskbarWnd()` per subclass — locating host windows

### Classical (Win7/10) — `ClassicalTaskbarDlg.cpp:70-84`
```cpp
m_hBar = ::FindWindowEx(m_hTaskbar, 0, L"ReBarWindow32", NULL);
if (m_hBar == NULL)
    m_hBar = ::FindWindowEx(m_hTaskbar, nullptr, L"WorkerW", NULL);   // fallback
m_hMin = ::FindWindowEx(m_hBar, 0, L"MSTaskSwWClass", NULL);
if (m_hMin == NULL)
    m_hMin = ::FindWindowEx(m_hBar, 0, L"MSTaskListWClass", NULL);    // Win10 fallback
::GetWindowRect(m_hMin, m_rcMin);
::GetWindowRect(m_hBar, m_rcBar);
m_left_space = m_rcMin.left - m_rcBar.left;
m_top_space  = m_rcMin.top  - m_rcBar.top;
```
Window hierarchy discovered: `Shell_TrayWnd` → `ReBarWindow32` (or `WorkerW`) → `MSTaskSwWClass` (or `MSTaskListWClass`). The **minimized-windows area** (`MSTaskSwWClass`) is the window that gets *shrunk* to make room. `GetParentHwnd()` returns **`m_hBar`** (the ReBarWindow32), so TrafficMonitor becomes a sibling of `MSTaskSwWClass` inside the rebar.

### Win11 — `Win11TaskbarDlg.cpp:89-94`
```cpp
m_hNotify = ::FindWindowEx(m_hTaskbar, 0, L"TrayNotifyWnd", NULL);
m_hStart  = ::FindWindowEx(m_hTaskbar, nullptr, L"Start", NULL);
::GetWindowRect(m_hNotify, m_rcNotify);
```
`GetParentHwnd()` returns **`m_hTaskbar`** itself (Shell_TrayWnd) — Win11's taskbar has no rebar; the window is reparented directly into the tray. Nothing is resized; positioning is computed from the notification area and Start button rects. `ResetTaskbarPos()` is empty.

### Wine — `WineTaskbarDlg.cpp`
```cpp
SetWindowPos(&wndTopMost, 0,0,0,0, SWP_NOSIZE|SWP_NOMOVE);   // just topmost
```
`GetParentHwnd()` returns `GetDesktopWindow()`. No embedding at all — the window floats topmost at the bottom-right/left of the screen. `AdjustTaskbarWndPos` positions it via `GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)`.

---

## 6. `AdjustWindowPos(bool force_adjust)` (`TaskBarDlg.cpp:460-501`) and `AdjustTaskbarWndPos`

`AdjustWindowPos` is the shared orchestrator, called once during init and then every 100 ms by the *main* window's timer (`TrafficMonitorDlg.cpp:2022`: `m_tBarDlg->AdjustWindowPos();` followed by `m_tBarDlg->Invalidate(FALSE);`).

```cpp
bool CTaskBarDlg::AdjustWindowPos(bool force_adjust)
{
    if (m_is_width_changed) force_adjust = true;
    if (force_adjust) ResetTaskbarPos();                 // restore host's original size first

    ::GetWindowRect(m_hTaskbar, m_rcTaskbar);            // refresh taskbar rect

    static bool last_taskbar_on_top_or_bottom;
    CheckTaskbarOnTopOrBottom();
    if (force_adjust || m_taskbar_on_top_or_bottom != last_taskbar_on_top_or_bottom) {
        CalculateWindowSize();
        last_taskbar_on_top_or_bottom = m_taskbar_on_top_or_bottom;
        force_adjust = true;
    }

    AdjustTaskbarWndPos(force_adjust);                   // virtual: resize host + place self

    if (m_connot_insert_to_task_bar) {                   // FALLBACK
        CRect rc_parent;
        ::GetWindowRect(GetParentHwnd(), rc_parent);
        CRect rect{ m_rect };
        rect.MoveToXY(rect.left + rc_parent.left, rect.top + rc_parent.top);
        this->MoveWindow(rect);
        if (::GetForegroundWindow() == m_hTaskbar)
            SetWindowPos(&wndTopMost, 0,0,0,0, SWP_NOSIZE|SWP_NOMOVE);
    }
    m_is_width_changed = false;
    return true;
}
```

### Classical `AdjustTaskbarWndPos` (`ClassicalTaskbarDlg.cpp:4-68`) — the resize-and-place logic

For **horizontal taskbar** (top/bottom):
```cpp
::GetWindowRect(m_hMin, m_rcMin);   // current minimized-area rect
::GetWindowRect(m_hBar, m_rcBar);
m_rect.right  = m_rect.left + m_window_width;
m_rect.bottom = m_rect.top  + m_window_height;

if (force_adjust || m_rcMin.Width() != m_last_width) {
    m_rcMinOri = m_rcMin;                                  // remember original
    m_left_space = m_rcMin.left - m_rcBar.left;
    m_last_width = m_rcMin.Width() - m_rect.Width();
    if (!tbar_wnd_on_left) {           // window on RIGHT
        ::MoveWindow(m_hMin, m_left_space, 0,
                     m_rcMin.Width() - m_rect.Width(), m_rcMin.Height(), TRUE);
        m_rect.MoveToX(m_left_space + m_rcMin.Width() - m_rect.Width() + 2);
    } else {                           // window on LEFT
        ::MoveWindow(m_hMin, m_left_space + m_rect.Width(), 0,
                     m_rcMin.Width() - m_rect.Width(), m_rcMin.Height(), TRUE);
        m_rect.MoveToX(m_left_space);
    }
    m_rect.MoveToY((m_rcBar.Height() - m_rect.Height()) / 2);  // vertical center
    if (horizontal_arrange && IsWindows7()) m_rect.MoveToY(m_rect.top + DPI(1));
    MoveWindow(m_rect);                                       // CTaskBarDlg::MoveWindow → ::MoveWindow(m_hWnd,...)
}
```

**How it makes room:** `MoveWindow(m_hMin, ...)` shrinks the `MSTaskSwWClass` window's width by exactly `m_rect.Width()` (our window's width), shifting its left edge right by our width if we sit on the left, or keeping its left edge and shrinking its right if we sit on the right. Our window is then placed in the freed gap. Coordinates are **client-relative to `m_hBar`** (the rebar, our parent), since `MoveWindow` on a child uses parent-client coords. The `+2` is a small spacer.

For **vertical taskbar** (left/right side): the same logic but on the Y axis — `m_hMin`'s height is reduced by `m_rect.Height()`, and our window is centered horizontally with a `DPI(2)` left margin.

`MoveWindow(m_rect)` is the `CTaskBarDlg::MoveWindow` wrapper (`TaskBarDlg.cpp:422`): `::MoveWindow(GetSafeHwnd(), rect.left, rect.top, rect.Width(), rect.Height(), TRUE)`.

### Win11 `AdjustTaskbarWndPos` (`Win11TaskbarDlg.cpp:5-87`)
No host resizing. It reads `m_hNotify` (TrayNotifyWnd) and `m_hStart` rects, then computes our X position relative to the notification area (right side) or Start button (left side), adds `window_offset_left/top` DPI adjustments, vertically centers using `m_rcStart.Height()`, and `MoveWindow(m_rect)`. Coordinates are client-relative to `Shell_TrayWnd` (our parent).

---

## 7. `ResetTaskbarPos()` — restoring the host on close

Classical (`ClassicalTaskbarDlg.cpp:86-97`):
```cpp
if (!m_rcMinOri.IsRectEmpty()) {
    if (m_taskbar_on_top_or_bottom)
        ::MoveWindow(m_hMin, m_left_space, 0, m_rcMinOri.Width(), m_rcMinOri.Height(), TRUE);
    else
        ::MoveWindow(m_hMin, 0, m_top_space,  m_rcMinOri.Width(), m_rcMinOri.Height(), TRUE);
}
```
Restores `MSTaskSwWClass` to its saved original size. Called from `AdjustWindowPos(true)` at the *start* of every forced re-adjust (so the recompute always starts from the true original size, not an already-shrunk one) and from `OnCancel()` (`TaskBarDlg.cpp:1077`) when the dialog is destroyed.

Win11 and Wine: empty (nothing to restore).

---

## 8. Fallback case (`m_connot_insert_to_task_bar`)

`m_connot_insert_to_task_bar` is set to `true` iff `::SetParent(this->m_hWnd, GetParentHwnd())` returned NULL (failure). In `AdjustWindowPos`, when this flag is set, the dialog positions itself using **screen-absolute** coordinates instead of parent-client coordinates: it reads the parent's screen rect via `::GetWindowRect(GetParentHwnd(), rc_parent)` and offsets `m_rect` by `rc_parent.left/top`, then `MoveWindow`. Additionally, if the taskbar currently has the foreground, it calls `SetWindowPos(&wndTopMost, SWP_NOSIZE|SWP_NOMOVE)` to keep the window visible above the taskbar. So the window still appears in the right place, just not truly embedded (it's a topmost popup overlapping the taskbar).

---

## 9. Timer / position maintenance

Two timers cooperate:

1. **`TASKBAR_TIMER` (1236), 1000 ms, inside the taskbar dialog** (`TaskBarDlg.cpp:1055`, handler `:1248`). On each tick it calls `CalculateWindowSize()` (which sets `m_is_width_changed = true` if the computed width/height changed — see `TaskBarDlg.cpp:947`). It does *not* itself move the window.

2. **`TASKBAR_TIMER` (1236), 100 ms, inside the main `CTrafficMonitorDlg`** (`TrafficMonitorDlg.cpp:1180`, handler `:1984`). Every 100 ms (if the taskbar window is valid) it:
   - every 5 ticks: checks secondary-taskbar count via `CTaskbarHelper::GetSecondaryTaskbarNum()`; if changed, schedules a delayed reopen via `RESTART_TASKBAR_TIMER` (sends `WM_REOPEN_TASKBAR_WND`).
   - every 10 ticks (Win8.1+): runs `CheckWindowMonitorDPIAndHandle` to detect per-monitor DPI changes and updates font/size.
   - **always**: `m_tBarDlg->AdjustWindowPos();` then `m_tBarDlg->Invalidate(FALSE);`

So the actual repositioning happens 10×/second from the main window. Because `AdjustWindowPos` only does the expensive `ResetTaskbarPos` + host resize when `force_adjust` is true (or width/orientation changed), the steady-state cost is just two `GetWindowRect` calls plus a comparison. When `CalculateWindowSize` detects a content-width change it sets `m_is_width_changed`, which forces the next `AdjustWindowPos` to do a full re-layout.

---

## 10. Complete Win32 API call ordering summary (classical, normal path)

**Creation:**
1. `CDialogEx::OnInitDialog` → internally `CreateDialogIndirectParam` (popup, `WS_POPUP|WS_SYSMENU`, parent = main window temporarily)
2. `SetWindowText("TrafficMonitorTaskbarWindow")`
3. `ModifyStyleEx(0, WS_EX_TOOLWINDOW)` → `SetWindowLong(GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW)`
4. `GetDC()`
5. `FindWindow("Shell_TrayWnd", NULL)` (or `EnumWindows` → `Shell_SecondaryTrayWnd`)
6. `GetWindowRect(hTaskbar, &m_rcTaskbar)`
7. *(transparency)* optional `SetWindowLong(GWL_EXSTYLE, ex | WS_EX_LAYERED)` + `SetLayeredWindowAttributes(color, 0, LWA_COLORKEY)`
8. `FindWindowEx(hTaskbar, NULL, "ReBarWindow32", NULL)` (fallback `"WorkerW"`)
9. `FindWindowEx(hBar, NULL, "MSTaskSwWClass", NULL)` (fallback `"MSTaskListWClass"`)
10. `GetWindowRect(hMin, &m_rcMin)`; `GetWindowRect(hBar, &m_rcBar)`
11. **`SetParent(m_hWnd, hBar)`** — reparent popup → child of ReBarWindow32
12. *(DPI)* `GetMonitorInfo`-style rect DPI
13. `CheckTaskbarOnTopOrBottom` → `GetWindowRect(hTaskbar)` compare W vs H
14. `CalculateWindowSize` (CPU text measuring, no Win32 placement)
15. `AdjustWindowPos(true)`:
    - `ResetTaskbarPos()` (no-op first time)
    - `GetWindowRect(hTaskbar)`
    - `CheckTaskbarOnTopOrBottom`
    - `CalculateWindowSize`
    - `AdjustTaskbarWndPos(true)`:
      - `GetWindowRect(hMin)`; `GetWindowRect(hBar)`
      - **`MoveWindow(hMin, …, shrunkWidth, …, TRUE)`** — shrink the minimized-windows area
      - `MoveWindow(ourHwnd, …, TRUE)` — place us in the gap
16. `SetBackgroundColor` (MFC, sets `CTLCOLOR` handling)
17. Tooltip: `CreateWindowEx(TTS_ALWAYSTIP)` + `SetWindowPos(wndTopMost)`
18. `SetTimer(1236, 1000)`

**Steady-state (every 100 ms, from main window timer):**
- `GetWindowRect(hTaskbar)` (refresh)
- `GetWindowRect(hMin)`; `GetWindowRect(hBar)`
- if width/orientation changed: `ResetTaskbarPos()` → `MoveWindow(hMin, …originalSize…)`; then `MoveWindow(hMin, …shrunkSize…)`; then `MoveWindow(ourHwnd, …)`
- `InvalidateRect(ourHwnd, FALSE)` → triggers `OnPaint` → `ShowInfo` (GDI/D2D draw)

**Shutdown (`OnCancel`):**
- `DestroyWindow()`
- `ResetTaskbarPos()` → `MoveWindow(hMin, …originalSize…, TRUE)` — give the space back

---

## 11. Key takeaways for a C# replication

1. **Create the form as a popup** (`WS_POPUP`), then **`SetParent`** it into `ReBarWindow32` (found via `FindWindowEx(FindWindow("Shell_TrayWnd"), null, "ReBarWindow32", null)`). Do not try to create it as a child directly.
2. Add **`WS_EX_TOOLWINDOW`** (so it doesn't show in Alt+Tab). `WS_EX_NOACTIVATE` is **not** used by TrafficMonitor.
3. Coordinates passed to `MoveWindow` for your window are **client-relative to the new parent** (ReBarWindow32), not screen coordinates.
4. To make room, **shrink `MSTaskSwWClass`** (the minimized-windows area, a sibling of your window inside the rebar) by your window's width using `MoveWindow` on *that* HWND. Save its original rect first so you can restore it on close.
5. Reposition reactively: poll every ~100 ms; on every width/orientation change, first restore the sibling's original size, then re-shrink and re-place yourself.
6. Handle failure: if `SetParent` returns IntPtr.Zero, fall back to topmost screen-absolute positioning.
7. On Windows 11 the rebar/`MSTaskSwWClass` approach does not apply — the parent is `Shell_TrayWnd` itself and nothing is resized; positioning is computed from `TrayNotifyWnd` and the Start button rects. You'll need a separate code path.
8. `WS_EX_LAYERED` + `SetLayeredWindowAttributes(LWA_COLORKEY)` is used only for the GDI color-key transparency path; for opaque rendering no layered style is set.

The grep tool could not match the search terms due to the source files being saved in mixed encodings (UTF-8 with BOM, GB18030, and UTF-16), which is why I switched to `iconv`-based reads for `WineTaskbarDlg.cpp` and `TrafficMonitor.rc`. All the relevant code was successfully located and verified above.