"""
Machine ID Bar — a small overlay pinned on the Windows taskbar.
Displays: custom label | hostname | IP address

Techniques adapted from the C++ taskbar_overlay implementation:
  - Taskbar type detection (Classic Win7/8/10 vs Win11)
  - Correct parenting: ReBarWindow32 for Classic, Shell_TrayWnd for Win11
  - Minimized-window area resizing to make room (Classic only)
  - ScreenToClient coordinate conversion for proper positioning
  - Color-key transparency (transparent background, visible text only)
  - Original minimized-area rect saved and restored on exit
  - Multi-monitor: one overlay per taskbar (Shell_TrayWnd + all Shell_SecondaryTrayWnd)

Usage:
    python machine_id_bar.py [--label "My Custom Label"] [--bg #0078d4] [--fg white]
    python machine_id_bar.py --multi-monitor        # force all monitors (default)
    python machine_id_bar.py --primary-only         # only the primary taskbar

- Left or right-click the label to copy all info to clipboard.
- Ctrl+click (left or right) to exit.
"""

import argparse
import atexit
import ctypes
import ctypes.wintypes
import socket
import tkinter as tk

# ── Win32 constants ──────────────────────────────────────────────
GWL_EXSTYLE = -20
GWL_STYLE = -16
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_NOACTIVATE = 0x08000000
WS_CHILD = 0x40000000
WS_POPUP = 0x80000000
SWP_NOMOVE = 0x0002
SWP_NOSIZE = 0x0001
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040
SWP_FRAMECHANGED = 0x0020
SW_SHOWNOACTIVATE = 4
HWND_TOPMOST = -1

# Taskbar types
TASKBAR_CLASSIC = 0  # Win7/8/10 — ReBarWindow32 container
TASKBAR_WIN11 = 1    # Win11 — Shell_TrayWnd directly
TASKBAR_FLOATING = 2  # No taskbar found — topmost floating fallback

# Win32 function prototype for EnumWindows callback
WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.wintypes.BOOL, ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)


def get_work_area():
    """Return (left, top, right, bottom) of the Windows work area (excludes taskbar)."""
    rect = ctypes.wintypes.RECT()
    ctypes.windll.user32.SystemParametersInfoW(0x0030, 0, ctypes.byref(rect), 0)  # SPI_GETWORKAREA
    return rect.left, rect.top, rect.right, rect.bottom


def get_local_ip() -> str:
    """Return the primary local IPv4 address, or 'N/A' if it can't be found."""
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "N/A"


def build_text(label: str, hostname: str, ip: str) -> str:
    return f"{label}  |  {hostname}  |  {ip}"


# ── Taskbar detection helpers (adapted from C++ taskbar_overlay.cpp) ──

def find_all_taskbars():
    """Find all taskbar windows: Shell_TrayWnd (primary) + every Shell_SecondaryTrayWnd.

    Returns a list of HWNDs with Shell_TrayWnd first (primary monitor), then
    secondary taskbars in enumeration order.
    """
    primary_hwnd = ctypes.windll.user32.FindWindowW("Shell_TrayWnd", None)
    secondary_hwnds = []

    def enum_callback(hwnd, lparam):
        class_name = ctypes.create_unicode_buffer(256)
        ctypes.windll.user32.GetClassNameW(hwnd, class_name, 256)
        if class_name.value == "Shell_SecondaryTrayWnd":
            secondary_hwnds.append(hwnd)
        return True

    ctypes.windll.user32.EnumWindows(WNDENUMPROC(enum_callback), 0)

    taskbars = []
    if primary_hwnd:
        taskbars.append(primary_hwnd)
    taskbars.extend(secondary_hwnds)
    return taskbars


def find_taskbar():
    """Find the primary Shell_TrayWnd, or a Shell_SecondaryTrayWnd as fallback (legacy single-bar helper)."""
    hwnd = ctypes.windll.user32.FindWindowW("Shell_TrayWnd", None)
    if not hwnd:
        hwnd = ctypes.windll.user32.FindWindowW("Shell_SecondaryTrayWnd", None)
    return hwnd


def detect_taskbar_type(taskbar_hwnd):
    """Detect Win11 vs Classic taskbar.

    Win11: Shell_TrayWnd has a child 'Windows.UI.Composition.DesktopWindowContentBridge'.
    Otherwise: Classic (Win7/8/10).
    """
    hwnd_child = ctypes.windll.user32.FindWindowExW(
        taskbar_hwnd, 0, "Windows.UI.Composition.DesktopWindowContentBridge", None
    )
    if hwnd_child:
        return TASKBAR_WIN11
    return TASKBAR_CLASSIC


def init_taskbar_wnd(taskbar_hwnd, taskbar_type):
    """Find child container windows for the taskbar.

    Returns (h_bar, h_min, h_notify):
      Classic: h_bar = ReBarWindow32/WorkerW, h_min = MSTaskSwWClass/MSTaskListWClass
      Win11:   h_notify = TrayNotifyWnd
    """
    h_bar, h_min, h_notify = 0, 0, 0

    if taskbar_type == TASKBAR_CLASSIC:
        h_bar = ctypes.windll.user32.FindWindowExW(taskbar_hwnd, 0, "ReBarWindow32", None)
        if not h_bar:
            h_bar = ctypes.windll.user32.FindWindowExW(taskbar_hwnd, 0, "WorkerW", None)
        if h_bar:
            h_min = ctypes.windll.user32.FindWindowExW(h_bar, 0, "MSTaskSwWClass", None)
            if not h_min:
                h_min = ctypes.windll.user32.FindWindowExW(h_bar, 0, "MSTaskListWClass", None)
    elif taskbar_type == TASKBAR_WIN11:
        h_notify = ctypes.windll.user32.FindWindowExW(taskbar_hwnd, 0, "TrayNotifyWnd", None)

    return h_bar, h_min, h_notify


def get_parent_hwnd(taskbar_type, taskbar_hwnd, h_bar):
    """Return the appropriate parent window for SetParent."""
    if taskbar_type == TASKBAR_CLASSIC:
        return h_bar       # ReBarWindow32 / WorkerW
    if taskbar_type == TASKBAR_WIN11:
        return taskbar_hwnd  # Shell_TrayWnd directly
    return 0               # floating — no parent


def get_window_rect(hwnd):
    """Return (left, top, right, bottom) of a window in screen coordinates."""
    rect = ctypes.wintypes.RECT()
    ctypes.windll.user32.GetWindowRect(hwnd, ctypes.byref(rect))
    return rect.left, rect.top, rect.right, rect.bottom


def screen_to_client(hwnd_parent, x, y):
    """Convert screen coordinates to client coordinates of hwnd_parent."""
    pt = ctypes.wintypes.POINT(x, y)
    ctypes.windll.user32.ScreenToClient(hwnd_parent, ctypes.byref(pt))
    return pt.x, pt.y


def check_taskbar_horizontal(taskbar_hwnd):
    """Return True if taskbar is horizontal (top or bottom of screen)."""
    left, top, right, bottom = get_window_rect(taskbar_hwnd)
    return (right - left) >= (bottom - top)


def main():
    parser = argparse.ArgumentParser(description="Machine ID Bar — taskbar label for RDP sessions.")
    parser.add_argument("--label", default="Secure PC", help="Custom label string (default: 'Secure PC')")
    parser.add_argument("--bg", default="#0078d4", help="Background / transparency-key color (default: #0078d4)")
    parser.add_argument("--fg", default="white", help="Foreground/text color (default: white)")
    parser.add_argument("--solid", action="store_true", help="Disable transparent background (show solid color bar)")
    parser.add_argument("--primary-only", action="store_true",
                        help="Show the bar on the primary monitor only (default: all monitors)")
    args = parser.parse_args()

    hostname = socket.gethostname()
    ip = get_local_ip()
    display_text = build_text(args.label, hostname, ip)

    # ── Find all taskbars (multi-monitor support) ──
    if args.primary_only:
        primary = ctypes.windll.user32.FindWindowW("Shell_TrayWnd", None)
        taskbar_hwnds = [primary] if primary else []
    else:
        taskbar_hwnds = find_all_taskbars()

    if not taskbar_hwnds:
        taskbar_hwnds = [0]  # floating fallback — no taskbar found

    # ── Build one borderless, topmost window per taskbar ──
    root = tk.Tk()
    root.overrideredirect(True)           # no window border / title bar
    root.attributes("-topmost", True)     # always on top
    root.configure(bg=args.bg)

    # Color-key transparency: the bg color becomes transparent so only text
    # is visible on the taskbar (mirrors C++ SetLayeredWindowAttributes + LWA_COLORKEY).
    if not args.solid:
        try:
            root.attributes("-transparentcolor", args.bg)
        except Exception:
            pass  # not supported on this platform/version

    windows = [root]
    for _ in range(1, len(taskbar_hwnds)):
        top = tk.Toplevel(root)
        top.overrideredirect(True)
        top.configure(bg=args.bg)
        if not args.solid:
            try:
                top.attributes("-transparentcolor", args.bg)
            except Exception:
                pass
        windows.append(top)

    # ── Create and embed one overlay per taskbar ──
    overlays = []

    for win, tb_hwnd in zip(windows, taskbar_hwnds):
        label_widget = tk.Label(
            win,
            text=display_text,
            fg=args.fg,
            bg=args.bg,
            font=("Segoe UI", 9, "bold"),
            padx=10,
            pady=2,
        )
        label_widget.pack()

        win.update_idletasks()
        wnd_w = win.winfo_reqwidth()
        wnd_h = win.winfo_reqheight()
        hwnd = win.winfo_id()

        if tb_hwnd:
            taskbar_type = detect_taskbar_type(tb_hwnd)
        else:
            taskbar_type = TASKBAR_FLOATING

        h_bar, h_min, h_notify = init_taskbar_wnd(tb_hwnd, taskbar_type)
        min_original_rect = get_window_rect(h_min) if h_min else None

        # ── Apply extended styles (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) ──
        ex_style = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        ex_style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        ctypes.windll.user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex_style)

        # ── Embed onto taskbar (mirrors C++ EmbedOntoTaskbar / GetParentHwnd) ──
        parent_hwnd = get_parent_hwnd(taskbar_type, tb_hwnd, h_bar)

        if parent_hwnd:
            # Change window style from WS_POPUP to WS_CHILD
            style = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_STYLE)
            style = (style & ~WS_POPUP) | WS_CHILD
            ctypes.windll.user32.SetWindowLongW(hwnd, GWL_STYLE, style)
            ctypes.windll.user32.SetParent(hwnd, parent_hwnd)

        ctypes.windll.user32.SetWindowPos(
            hwnd, 0, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED
        )

        overlays.append({
            "win": win,
            "hwnd": hwnd,
            "wnd_w": wnd_w,
            "wnd_h": wnd_h,
            "taskbar_hwnd": tb_hwnd,
            "taskbar_type": taskbar_type,
            "h_bar": h_bar,
            "h_min": h_min,
            "h_notify": h_notify,
            "parent_hwnd": parent_hwnd,
            "min_original_rect": min_original_rect,
            "label_widget": label_widget,
        })

    def restore_all_taskbars():
        """Restore all minimized-window areas to their original sizes (Classic only)."""
        for ov in overlays:
            if ov["taskbar_type"] == TASKBAR_CLASSIC and ov["h_min"] and ov["min_original_rect"]:
                r = ov["min_original_rect"]
                w = r[2] - r[0]
                h = r[3] - r[1]
                ctypes.windll.user32.MoveWindow(
                    ov["h_min"], r[0], r[1], w, h, True
                )

    atexit.register(restore_all_taskbars)

    def adjust_window_pos(ov):
        """Position the overlay on its taskbar (mirrors C++ AdjustWindowPos)."""
        taskbar_hwnd = ov["taskbar_hwnd"]
        hwnd = ov["hwnd"]
        wnd_w = ov["wnd_w"]
        wnd_h = ov["wnd_h"]
        taskbar_type = ov["taskbar_type"]
        h_bar = ov["h_bar"]
        h_min = ov["h_min"]
        h_notify = ov["h_notify"]

        if not taskbar_hwnd:
            return

        tb_left, tb_top, tb_right, tb_bottom = get_window_rect(taskbar_hwnd)
        taskbar_h = tb_bottom - tb_top
        horizontal = check_taskbar_horizontal(taskbar_hwnd)

        if taskbar_type == TASKBAR_CLASSIC:
            if horizontal:
                # Position to the right of the minimized-window area
                x = 0
                y = (taskbar_h - wnd_h) // 2

                if h_min:
                    # Shrink the minimized-window area to make room
                    min_left, min_top, min_right, min_bottom = get_window_rect(h_min)
                    mx, my = screen_to_client(h_bar, min_left, min_top)
                    min_w = (min_right - min_left) - wnd_w - 4
                    if min_w < 10:
                        min_w = 10
                    min_h = min_bottom - min_top
                    ctypes.windll.user32.MoveWindow(h_min, mx, my, min_w, min_h, True)
                    x = mx + min_w + 2

                ctypes.windll.user32.MoveWindow(hwnd, x, y, wnd_w, wnd_h, True)
            else:
                # Vertical taskbar (left or right side)
                x = 2
                y = 0
                if h_min:
                    min_left, min_top, min_right, min_bottom = get_window_rect(h_min)
                    mx, my = screen_to_client(h_bar, min_left, min_top)
                    min_h_val = (min_bottom - min_top) - wnd_h - 4
                    if min_h_val < 10:
                        min_h_val = 10
                    min_w_val = min_right - min_left
                    ctypes.windll.user32.MoveWindow(h_min, mx, my, min_w_val, min_h_val, True)
                    y = my + min_h_val + 2

                ctypes.windll.user32.MoveWindow(hwnd, x, y, wnd_w, wnd_h, True)

        elif taskbar_type == TASKBAR_WIN11:
            # Position to the left of the notification area
            x = tb_left + 8
            if h_notify:
                n_left, n_top, n_right, n_bottom = get_window_rect(h_notify)
                if n_left > 0:
                    x = n_left - wnd_w - 8

            y = tb_top + (taskbar_h - wnd_h) // 2
            # Convert screen coords to taskbar-client coords
            cx, cy = screen_to_client(taskbar_hwnd, x, y)
            ctypes.windll.user32.MoveWindow(hwnd, cx, cy, wnd_w, wnd_h, True)

        else:  # Floating — topmost at bottom-left of the taskbar's screen
            tb_left, tb_top, tb_right, tb_bottom = get_window_rect(taskbar_hwnd) if taskbar_hwnd else (0, 0, 0, 0)
            screen_h = tb_bottom if tb_bottom else root.winfo_screenheight()
            ctypes.windll.user32.SetWindowPos(
                hwnd, HWND_TOPMOST, 0, screen_h - wnd_h - 4, wnd_w, wnd_h, SWP_NOACTIVATE
            )

    def force_visible(ov):
        """Re-assert visibility (mirrors C++ ForceVisible)."""
        hwnd = ov["hwnd"]
        win = ov["win"]
        if not ctypes.windll.user32.IsWindowVisible(hwnd):
            ctypes.windll.user32.ShowWindow(hwnd, SW_SHOWNOACTIVATE)
        ctypes.windll.user32.SetWindowPos(
            hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW
        )
        win.lift()

    # ── Initial positioning and visibility for all overlays ──
    for ov in overlays:
        adjust_window_pos(ov)
        force_visible(ov)

    # ── Periodic timer (mirrors C++ OnTimer: re-assert visibility + reposition) ──
    last_taskbar_rects = [
        get_window_rect(ov["taskbar_hwnd"]) if ov["taskbar_hwnd"] else None
        for ov in overlays
    ]

    def check_resolution():
        try:
            changed = False
            for i, ov in enumerate(overlays):
                current_tb = get_window_rect(ov["taskbar_hwnd"]) if ov["taskbar_hwnd"] else None
                if current_tb != last_taskbar_rects[i]:
                    last_taskbar_rects[i] = current_tb
                    changed = True
                force_visible(ov)
            if changed:
                for ov in overlays:
                    adjust_window_pos(ov)
        except Exception:
            pass  # never let an exception kill the polling loop
        root.after(1000, check_resolution)

    root.after(1000, check_resolution)

    # ── Drag & click handling (mirrors C++ OnLButtonDown/OnMouseMove/OnLButtonUp) ──
    def make_drag_handlers(ov):
        hwnd = ov["hwnd"]
        parent_hwnd = ov["parent_hwnd"]
        drag_data = {"x": 0, "y": 0, "dragging": False, "win_x": 0, "win_y": 0}

        def on_press(event):
            """Record start position; Ctrl+click exits immediately."""
            if event.state & 0x4:  # Ctrl key held
                restore_all_taskbars()
                root.destroy()
                return
            drag_data["x"] = event.x
            drag_data["y"] = event.y
            drag_data["dragging"] = False
            rect = get_window_rect(hwnd)
            if parent_hwnd:
                drag_data["win_x"], drag_data["win_y"] = screen_to_client(parent_hwnd, rect[0], rect[1])
            else:
                drag_data["win_x"] = rect[0]
                drag_data["win_y"] = rect[1]

        def on_drag(event):
            """Move window while dragging using MoveWindow (parent-relative coords)."""
            if abs(event.x - drag_data["x"]) > 3 or abs(event.y - drag_data["y"]) > 3:
                drag_data["dragging"] = True
            if drag_data["dragging"]:
                new_x = drag_data["win_x"] + (event.x - drag_data["x"])
                new_y = drag_data["win_y"] + (event.y - drag_data["y"])
                ctypes.windll.user32.MoveWindow(hwnd, new_x, new_y, ov["wnd_w"], ov["wnd_h"], True)

        def on_release(event):
            """If it was a click (not a drag), copy to clipboard."""
            if not drag_data["dragging"]:
                root.clipboard_clear()
                root.clipboard_append(display_text)
            drag_data["dragging"] = False

        return on_press, on_drag, on_release

    for ov in overlays:
        on_press, on_drag, on_release = make_drag_handlers(ov)
        for widget in (ov["win"], ov["label_widget"]):
            widget.bind("<ButtonPress-1>", on_press)
            widget.bind("<B1-Motion>", on_drag)
            widget.bind("<ButtonRelease-1>", on_release)
            # Right-click: copy only (no drag); Ctrl+right-click exits
            widget.bind("<Button-3>", lambda e: (
                (restore_all_taskbars(), root.destroy()) if e.state & 0x4 else
                (root.clipboard_clear(), root.clipboard_append(display_text))
            ))

    root.mainloop()
    restore_all_taskbars()


if __name__ == "__main__":
    main()
