"""
Machine ID Bar — a small always-on-top label pinned above the Windows taskbar.
Displays: custom label | hostname | IP address

Usage:
    python machine_id_bar.py [--label "My Custom Label"] [--bg #0078d4] [--fg white]

- Left or right-click the label to copy all info to clipboard.
- Ctrl+click (left or right) to exit.
"""

import argparse
import ctypes
import ctypes.wintypes
import socket
import tkinter as tk


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


def main():
    parser = argparse.ArgumentParser(description="Machine ID Bar — taskbar label for RDP sessions.")
    parser.add_argument("--label", default="Secure PC", help="Custom label string (default: 'Secure PC')")
    parser.add_argument("--bg", default="#0078d4", help="Background color (default: #0078d4)")
    parser.add_argument("--fg", default="white", help="Foreground/text color (default: white)")
    args = parser.parse_args()

    hostname = socket.gethostname()
    ip = get_local_ip()
    display_text = build_text(args.label, hostname, ip)

    # --- Build the borderless, topmost window ---
    root = tk.Tk()
    root.overrideredirect(True)           # no window border / title bar
    root.attributes("-topmost", True)     # always on top
    root.configure(bg=args.bg)

    # Force the window to stay above the taskbar using Windows API
    SWP_NOMOVE = 0x0002
    SWP_NOSIZE = 0x0001
    SWP_NOACTIVATE = 0x0010
    SWP_SHOWWINDOW = 0x0040
    HWND_TOPMOST = -1

    def force_topmost():
        """Re-assert HWND_TOPMOST using the Windows API."""
        # For Tkinter overrideredirect windows, winfo_id() is the HWND itself
        hwnd = root.winfo_id()
        ctypes.windll.user32.SetWindowPos(
            hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW
        )

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

    def reposition():
        """Reposition the window to bottom-left, vertically centered on the taskbar."""
        root.update_idletasks()
        w = root.winfo_reqwidth()
        h = root.winfo_reqheight()
        screen_w = root.winfo_screenwidth()
        screen_h = root.winfo_screenheight()
        # Calculate taskbar height to center the label vertically on it
        wa_left, wa_top, wa_right, wa_bottom = get_work_area()
        taskbar_h = screen_h - wa_bottom  # typically 48px
        x = 8
        # Center the small window vertically within the taskbar area
        y = screen_h - taskbar_h + (taskbar_h - h) // 2
        root.geometry(f"{w}x{h}+{x}+{y}")

    # Initial position
    reposition()
    force_topmost()

    # Reposition when screen resolution changes (RDP reconnect, monitor change, etc.)
    # Re-assert topmost frequently to stay above menus, taskbar, popups, etc.
    last_screen = (root.winfo_screenwidth(), root.winfo_screenheight())

    def check_resolution():
        nonlocal last_screen
        current = (root.winfo_screenwidth(), root.winfo_screenheight())
        if current != last_screen:
            last_screen = current
            reposition()
        # Re-assert topmost every cycle
        force_topmost()
        root.after(1000, check_resolution)

    root.after(1000, check_resolution)

    # Track drag state
    drag_data = {"x": 0, "y": 0, "dragging": False}

    def on_press(event):
        """Record start position; Ctrl+click exits immediately."""
        if event.state & 0x4:  # Ctrl key held
            root.destroy()
            return
        drag_data["x"] = event.x
        drag_data["y"] = event.y
        drag_data["dragging"] = False

    def on_drag(event):
        """Move window while dragging; mark as dragging if moved beyond a threshold."""
        if abs(event.x - drag_data["x"]) > 3 or abs(event.y - drag_data["y"]) > 3:
            drag_data["dragging"] = True
        if drag_data["dragging"]:
            x = root.winfo_x() + (event.x - drag_data["x"])
            y = root.winfo_y() + (event.y - drag_data["y"])
            root.geometry(f"+{x}+{y}")

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
        # Right-click: copy only (no drag)
        widget.bind("<Button-3>", lambda e: (
            root.destroy() if e.state & 0x4 else
            (root.clipboard_clear(), root.clipboard_append(display_text))
        ))

    root.mainloop()


if __name__ == "__main__":
    main()
