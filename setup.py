"""
Machine ID Bar — Setup & Install as Startup App

Asks for a custom label and background color (with color preview),
then installs the exe as a startup app with those params.

Run:  python setup.py
"""

import ctypes
import os
import re
import shutil
import sys
import subprocess

# Enable ANSI 24-bit color on Windows 10+
kernel32 = ctypes.windll.kernel32
kernel32.SetConsoleMode(kernel32.GetStdHandle(-11), 7)


COLORS = [
    ("1",  "Blue",   "#0078d4"),
    ("2",  "Green",  "#107c10"),
    ("3",  "Red",    "#d13438"),
    ("4",  "Orange", "#ca5010"),
    ("5",  "Purple", "#5c2d91"),
    ("6",  "Dark",   "#202020"),
    ("7",  "Teal",   "#008575"),
    ("8",  "Pink",   "#e3008c"),
    ("9",  "Brown",  "#8e562e"),
    ("10", "Navy",   "#003a70"),
    ("11", "Olive",  "#6b6917"),
    ("12", "Slate",  "#4a5759"),
]


def parse_run_bat(script_dir):
    """Read current label and bg from run.bat if it exists."""
    default_label = "Machine"
    default_bg = "#0078d4"
    run_bat = os.path.join(script_dir, "run.bat")
    if os.path.exists(run_bat):
        with open(run_bat, "r") as f:
            content = f.read()
        m_label = re.search(r'--label="([^"]*)"', content)
        m_bg = re.search(r'--bg="([^"]*)"', content)
        if m_label:
            default_label = m_label.group(1)
        if m_bg:
            default_bg = m_bg.group(1)
    return default_label, default_bg


def find_color_num(hex_code):
    """Find the color number matching a hex code, or '1' if not found."""
    for num, name, hc in COLORS:
        if hc.lower() == hex_code.lower():
            return num
    return "1"


def hex_to_rgb(h):
    h = h.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def color_sample(name, hex_code):
    """Return an ANSI-colored sample string showing the actual color."""
    r, g, b = hex_to_rgb(hex_code)
    esc = "\033"
    return f"{esc}[48;2;{r};{g};{b}m{esc}[38;2;255;255;255m  {name:<8}  {esc}[0m"


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    exe_src = os.path.join(script_dir, "dist", "machine_id_bar.exe")

    # Read current defaults from run.bat
    default_label, default_bg = parse_run_bat(script_dir)
    default_color_num = find_color_num(default_bg)

    print()
    print("=" * 44)
    print("  Machine ID Bar - Setup")
    print("=" * 44)
    print()

    # --- Label ---
    label = input(f"Enter a label for this machine [{default_label}]: ").strip()
    if not label:
        label = default_label

    # --- Color ---
    print()
    print("Choose a background color:")
    print()
    for num, name, hex_code in COLORS:
        sample = color_sample(name, hex_code)
        marker = " <-- current" if num == default_color_num else ""
        print(f"  {num:>2}. {sample}  {hex_code}{marker}")
    print()
    choice = input(f"Enter number (1-12) [{default_color_num}]: ").strip()

    bg = default_bg
    for num, name, hex_code in COLORS:
        if choice == num:
            bg = hex_code
            break
    if not choice:
        choice = default_color_num

    print()
    print(f"  Selected: label=\"{label}\", background={bg}")
    print()

    # --- Check exe exists ---
    if not os.path.exists(exe_src):
        print(f"ERROR: Could not find {exe_src}")
        print("Make sure you run setup.py from the project folder where dist\\machine_id_bar.exe exists.")
        input("Press Enter to exit...")
        sys.exit(1)

    # --- Kill any running instance before copying ---
    subprocess.run(["taskkill", "/F", "/IM", "machine_id_bar.exe"],
                   capture_output=True)

    # --- Install ---
    install_dir = os.path.join(os.environ.get("LOCALAPPDATA", ""), "MachineIdBar")
    print(f"  Installing to: {install_dir}")

    os.makedirs(install_dir, exist_ok=True)

    exe_dst = os.path.join(install_dir, "machine_id_bar.exe")
    # Retry copy in case the process hasn't fully released the file yet
    import time
    for attempt in range(3):
        try:
            shutil.copy2(exe_src, exe_dst)
            break
        except PermissionError:
            time.sleep(1)
    if not os.path.exists(exe_dst):
        print("ERROR: Could not copy machine_id_bar.exe")
        input("Press Enter to exit...")
        sys.exit(1)

    # --- Create launcher batch ---
    launcher = os.path.join(install_dir, "run_machine_id_bar.bat")
    with open(launcher, "w") as f:
        f.write('@echo off\n')
        f.write('taskkill /F /IM machine_id_bar.exe >nul 2>&1\n')
        f.write(f'start "" "{exe_dst}" --label="{label}" --bg="{bg}"\n')

    # --- Update local run.bat ---
    local_run = os.path.join(script_dir, "run.bat")
    with open(local_run, "w") as f:
        f.write('@echo off\n')
        f.write(':: Machine ID Bar launcher\n')
        f.write(':: Left or right-click the label to copy info. Ctrl+click to exit.\n')
        f.write(':: Change the label and color below for each machine.\n')
        f.write('cd /d "%~dp0"\n')
        f.write('taskkill /F /IM machine_id_bar.exe >nul 2>&1\n')
        f.write(f'dist\\machine_id_bar.exe --label="{label}" --bg="{bg}"\n')

    # --- Create startup shortcut ---
    startup_dir = os.path.join(
        os.environ.get("APPDATA", ""),
        "Microsoft", "Windows", "Start Menu", "Programs", "Startup"
    )
    shortcut_path = os.path.join(startup_dir, "MachineIdBar.lnk")

    ps_script = (
        f'$ws = New-Object -ComObject WScript.Shell; '
        f'$sc = $ws.CreateShortcut(\'{shortcut_path}\'); '
        f'$sc.TargetPath = \'{launcher}\'; '
        f'$sc.WorkingDirectory = \'{install_dir}\'; '
        f'$sc.WindowStyle = 7; '
        f'$sc.Description = \'Machine ID Bar - {label}\'; '
        f'$sc.Save()'
    )
    result = subprocess.run(
        ["powershell", "-NoProfile", "-Command", ps_script],
        capture_output=True
    )
    if result.returncode != 0:
        print("ERROR: Could not create startup shortcut.")
        print(result.stderr.decode("utf-8", errors="replace"))
        input("Press Enter to exit...")
        sys.exit(1)

    # --- Summary ---
    print()
    print("=" * 44)
    print("  Installation complete!")
    print("=" * 44)
    print()
    print(f"  Label:      {label}")
    print(f"  Background: {bg}")
    print(f"  Installed:  {exe_dst}")
    print(f"  Startup:    {shortcut_path}")
    print()
    print("  The app will auto-launch on every login.")
    print()
    print("  Launching now...")

    # --- Launch ---
    subprocess.Popen([launcher], creationflags=0x08000000)  # CREATE_NO_WINDOW

    print()
    print("  Done! You should see the bar on your taskbar.")
    print(f"  To uninstall: delete {shortcut_path} and {install_dir}")
    print()
    input("Press Enter to exit...")


if __name__ == "__main__":
    main()
