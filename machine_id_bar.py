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

Usage:
    python machine_id_bar.py [--label "My Custom Label"] [--bg #0078d4] [--fg white]

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

def find_taskbar():
    """Find Shell_TrayWnd (primary) or Shell_SecondaryTrayWnd (multi-monitor)."""
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
    args = parser.parse_args()

    hostname = socket.gethostname()
    ip = get_local_ip()
    display_text = build_text(args.label, hostname, ip)

    # ── Build the borderless, topmost window ──
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

    label_widget = tk.Label(
        root,
        text=display_text,
        fg=args.fg,
        bg=args.bg,
        font=("Segoe UI", 9, "bold"),
        padx=10,
        pady=2,
    )
    label_widget.pack()

    root.update_idletasks()
    wnd_w = root.winfo_reqwidth()
    wnd_h = root.winfo_reqheight()
    hwnd = root.winfo_id()

    # ── Find and detect taskbar (mirrors C++ FindTaskbarHandle / DetectTaskbarType) ──
    taskbar_hwnd = find_taskbar()
    if taskbar_hwnd:
        taskbar_type = detect_taskbar_type(taskbar_hwnd)
    else:
        taskbar_type = TASKBAR_FLOATING

    h_bar, h_min, h_notify = init_taskbar_wnd(taskbar_hwnd, taskbar_type)

    # Save original rect of minimized-window area so we can restore on exit
    min_original_rect = get_window_rect(h_min) if h_min else None

    # ── Apply extended styles (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) ──
    ex_style = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
    ex_style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
    ctypes.windll.user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex_style)

    # ── Embed onto taskbar (mirrors C++ EmbedOntoTaskbar / GetParentHwnd) ──
    parent_hwnd = get_parent_hwnd(taskbar_type, taskbar_hwnd, h_bar)

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

    def restore_taskbar():
        """Restore the minimized-window area to its original size (Classic only)."""
        if taskbar_type == TASKBAR_CLASSIC and h_min and min_original_rect:
            w = min_original_rect[2] - min_original_rect[0]
            h = min_original_rect[3] - min_original_rect[1]
            ctypes.windll.user32.MoveWindow(
                h_min, min_original_rect[0], min_original_rect[1], w, h, True
            )

    atexit.register(restore_taskbar)

    def adjust_window_pos():
        """Position the overlay on the taskbar (mirrors C++ AdjustWindowPos)."""
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

        else:  # Floating — topmost at bottom-left
            screen_h = root.winfo_screenheight()
            ctypes.windll.user32.SetWindowPos(
                hwnd, HWND_TOPMOST, 0, screen_h - wnd_h - 4, wnd_w, wnd_h, SWP_NOACTIVATE
            )

    def force_visible():
        """Re-assert visibility (mirrors C++ ForceVisible)."""
        if not ctypes.windll.user32.IsWindowVisible(hwnd):
            ctypes.windll.user32.ShowWindow(hwnd, SW_SHOWNOACTIVATE)
        ctypes.windll.user32.SetWindowPos(
            hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW
        )
        root.lift()

    # ── Initial positioning and visibility ──
    adjust_window_pos()
    force_visible()

    # ── Periodic timer (mirrors C++ OnTimer: re-assert visibility + reposition) ──
    last_screen = (root.winfo_screenwidth(), root.winfo_screenheight())
    last_taskbar_rect = get_window_rect(taskbar_hwnd) if taskbar_hwnd else None

    def check_resolution():
        nonlocal last_screen, last_taskbar_rect
        try:
            current = (root.winfo_screenwidth(), root.winfo_screenheight())
            current_tb = get_window_rect(taskbar_hwnd) if taskbar_hwnd else None
            if current != last_screen or current_tb != last_taskbar_rect:
                last_screen = current
                last_taskbar_rect = current_tb
                adjust_window_pos()
            force_visible()
        except Exception:
            pass  # never let an exception kill the polling loop
        root.after(1000, check_resolution)

    root.after(1000, check_resolution)

    # ── Drag & click handling (mirrors C++ OnLButtonDown/OnMouseMove/OnLButtonUp) ──
    drag_data = {"x": 0, "y": 0, "dragging": False, "win_x": 0, "win_y": 0}

    def on_press(event):
        """Record start position; Ctrl+click exits immediately."""
        if event.state & 0x4:  # Ctrl key held
            restore_taskbar()
            root.destroy()
            return
        drag_data["x"] = event.x
        drag_data["y"] = event.y
        drag_data["dragging"] = False
        # Store current window position (parent-relative) for drag math
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
            ctypes.windll.user32.MoveWindow(hwnd, new_x, new_y, wnd_w, wnd_h, True)

    def on_release(event):
        """If it was a click (not a drag), copy to clipboard."""
        if not drag_data["dragging"]:
            root.clipboard_clear()
            root.clipboard_append(display_text)
        drag_data["dragging"] = False

    for widget in (root, label_widget):
        widget.bind("<ButtonPress-1>", on_press)
        widget.bind("<B1-Motion>", on_drag)
        widget.bind("<ButtonRelease-1>", on_release)
        # Right-click: copy only (no drag); Ctrl+right-click exits
        widget.bind("<Button-3>", lambda e: (
            (restore_taskbar(), root.destroy()) if e.state & 0x4 else
            (root.clipboard_clear(), root.clipboard_append(display_text))
        ))

    root.mainloop()
    restore_taskbar()


if __name__ == "__main__":
    main()
