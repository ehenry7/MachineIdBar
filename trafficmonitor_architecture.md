# TrafficMonitor Taskbar Embedding Architecture

## Window Hierarchy (Windows 10 Classical Taskbar)

```
Desktop
  └── Shell_TrayWnd (taskbar)
        ├── Button (Start button)
        ├── TrayNotifyWnd (system tray)
        └── ReBarWindow32 (second-level container)
              └── MSTaskSwWClass (task list / minimized windows area)
```

## TrafficMonitor's Embedding Flow

```
┌──────────────────────────────────────────────────────────────────┐
│ 1. INIT: OnInitDialog()                                          │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ a) FindWindow("Shell_TrayWnd") → m_hTaskbar             │   │
│    │ b) InitTaskbarWnd():                                     │   │
│    │    - FindWindowEx(taskbar, "ReBarWindow32") → m_hBar    │   │
│    │    - FindWindowEx(m_hBar, "MSTaskSwWClass") → m_hMin    │   │
│    │    - Save original m_hMin rect → m_rcMinOri             │   │
│    │    - Calculate leftSpace = m_hMin.left - m_hBar.left    │   │
│    │ c) SetParent(my_hwnd, m_hBar)  ← KEY STEP               │   │
│    │    - Does NOT change WS_POPUP to WS_CHILD!              │   │
│    │    - Just reparents the popup into ReBarWindow32        │   │
│    │ d) ModifyStyleEx(0, WS_EX_TOOLWINDOW)                   │   │
│    │    - Hide from taskbar/Alt+Tab                          │   │
│    │ e) AdjustWindowPos(true):                               │   │
│    │    - Shrink MSTaskSwWClass: MoveWindow(hMin, offset+W,  │   │
│    │      0, origW-W, origH, TRUE)                           │   │
│    │    - Position our window: MoveWindow(me, leftSpace,     │   │
│    │      (barH-myH)/2, myW, myH, TRUE)                     │   │
│    └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│ 2. TIMER (every 1 second):                                       │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ AdjustWindowPos(false):                                  │   │
│    │    - Re-measure m_hBar and m_hMin rects                 │   │
│    │    - If width changed: re-shrink m_hMin                 │   │
│    │    - Re-position our window                             │   │
│    │    - If embedding failed (m_connot_insert_to_task_bar):  │   │
│    │      use absolute screen coords + topmost fallback      │   │
│    └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│ 3. EXIT: OnCancel() / OnClose()                                  │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ ResetTaskbarPos():                                       │   │
│    │    - Restore MSTaskSwWClass to original size            │   │
│    │    - MoveWindow(hMin, leftSpace, 0, origW, origH, TRUE) │   │
│    └─────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

## Key Insights

1. **SetParent WITHOUT WS_CHILD**: TrafficMonitor does NOT change the window
   style from WS_POPUP to WS_CHILD. It just reparents. The window keeps its
   WS_POPUP style but is now a child of ReBarWindow32 in the parent hierarchy.
   This is critical — WS_CHILD caused rendering issues in our attempts.

2. **Shrink the task list (MSTaskSwWClass)**: Instead of overlapping, it
   physically shrinks the task list window to make room for our window.
   The task list gets smaller, and our window fills the gap.

3. **WS_EX_TOOLWINDOW**: Applied to hide from Alt+Tab and taskbar.

4. **Coordinates are parent-relative**: MoveWindow uses coordinates relative
   to the parent (ReBarWindow32), NOT screen coordinates.

5. **leftSpace**: The gap between the left edge of MSTaskSwWClass and the
   left edge of ReBarWindow32. Our window goes at x=leftSpace.

6. **DPI**: TrafficMonitor uses per-monitor DPI awareness. All GetWindowRect
   calls return physical pixels when DPI-aware.

7. **Fallback**: If SetParent fails (returns NULL), the window uses absolute
   screen positioning with topmost as a fallback.
