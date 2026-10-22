// ============================================================================
// TaskBarTestString.cs
//
// A simple C# app that writes a test string into the Windows task bar,
// reproducing TrafficMonitor's "task area window mode" (taskbar window).
//
// Reproduced from:  trafficmonitor-ref/TrafficMonitor/
//   - TaskBarDlg.cpp / TaskBarDlg.h        (base class: embedding sequence,
//                                            AdjustWindowPos orchestrator,
//                                            fallback mode, timers)
//   - Win11TaskbarDlg.cpp                  (Windows 11 path: parent =
//                                            Shell_TrayWnd, anchors =
//                                            TrayNotifyWnd + Start)
//   - ClassicalTaskbarDlg.cpp              (Win7/10 path: parent =
//                                            ReBarWindow32, shrink
//                                            MSTaskSwWClass)
//   - TrafficMonitor.cpp (CheckWindows11Taskbar, line 1303-1313)
//   - WindowsSettingHelper.cpp (IsTaskbarCenterAlign, registry TaskbarAl)
//
// Key fidelity points (from the TrafficMonitor Code Analyst analysis):
//   1. Window is created as WS_POPUP (NOT WS_CHILD) and reparented later with
//      a single SetParent call -- the style is never changed.
//   2. ExStyle is WS_EX_TOOLWINDOW only. WS_EX_NOACTIVATE is NOT used.
//   3. All MoveWindow coordinates for our window are parent-relative.
//   4. Win7/10: the sibling MSTaskSwWClass is shrunk by our width to make
//      room; its original rect is saved and restored on every forced
//      re-layout and on exit (ResetTaskbarPos).
//   5. Win11: nothing is resized; position is computed from TrayNotifyWnd
//      (right side) and Start button rects; "on left" only applies when the
//      taskbar is center-aligned (registry TaskbarAl != 0).
//   6. Steady-state repositioning runs from a 100 ms timer and only does a
//      full re-layout when the host geometry changed (two GetWindowRect +
//      a comparison otherwise).
//   7. Win11 detection: a "Windows.UI.Composition.DesktopWindowContentBridge"
//      child exists inside Shell_TrayWnd.
//   8. Fallback: if SetParent fails (returns NULL), position with
//      screen-absolute coordinates and stay topmost while the taskbar is
//      foreground.
//   9. Per-monitor DPI awareness: all geometry is in physical pixels.
//   10. WS_EX_LAYERED + LWA_COLORKEY (color key = background color): on Win11
//       a plain GDI child window composites UNDER the XAML taskbar surface
//       (visible in PrintWindow, invisible on screen); layered windows are
//       composited by DWM according to z-order and ARE visible. The strip
//       therefore looks like TrafficMonitor's default: transparent
//       background, text only. (Proven by taskbar_overlay.cpp
//       ApplyTransparency, which works on this machine.)
//
// Opt-in behavior (mirrors TrafficMonitor):
//   Showing the task bar window is OFF by default. The switch is persisted
//   in taskbar_test.ini next to the exe:
//       [config]
//       show_task_bar_wnd=0|1
//   This mirrors TrafficMonitor.ini's [config] show_task_bar_wnd
//   (CommonData.h:191 default false; TrafficMonitor.cpp:101/315; toggled by
//   the tray menu item ID_SHOW_TASK_BAR_WND -> OnShowTaskBarWnd, and reset to
//   0 when the strip is closed -> OnTaskbarWndClosed).
//
// Usage:
//   TaskBarTestString.exe              show strip only if the option is on
//   TaskBarTestString.exe --enable     turn ON (persisted) and show the strip
//   TaskBarTestString.exe --disable    turn OFF (persisted), no strip
//   TaskBarTestString.exe --label TEXT text shown on the strip
//   TaskBarTestString.exe --left       place on the left side
//   TaskBarTestString.exe --snap       snap next to the Start button
//   Left click  : copy test string to clipboard
//   Ctrl+click  : turn OFF the option (persisted, like OnTaskbarWndClosed)
//                 and exit (restores the task list on Win7/10)
//
// Build (.NET Framework):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
//     /target:winexe /out:taskbar_test_string.exe ^
//     /r:System.Drawing.dll /r:System.Windows.Forms.dll TaskBarTestString.cs
//   NOTE: /target:winexe (GUI subsystem) is REQUIRED. A console build
//   pops a terminal window that grabs focus; the app must run without a
//   console attached.
// ============================================================================

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class TaskBarTestString
{
    // ------------------------------------------------------------------------
    // Win32 interop
    // ------------------------------------------------------------------------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string cls, string wnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string wnd);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int idx, IntPtr val);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int colorKey, byte opacity, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern ushort RegisterWindowMessage(string msg);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(int exStyle, string cls, string wnd, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern short GetKeyState(int vkey);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegOpenKeyEx(IntPtr hKey, string subKey, uint options, int sam, out IntPtr phkResult);
    [DllImport("advapi32.dll")]
    static extern int RegCloseKey(IntPtr hKey);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved,
        out uint type, byte[] data, ref int dataSize);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    // ------------------------------------------------------------------------
    // Win32 constants
    // ------------------------------------------------------------------------
    const int WM_CLOSE = 0x0010;
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_VISIBLE = 0x10000000;
    const int WS_SYSMENU = 0x00080000;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int SW_SHOW = 5, SW_SHOWNOACTIVATE = 4;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_FRAMECHANGED = 0x0020;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const int WM_PAINT = 0x000F, WM_DESTROY = 0x0002, WM_PRINTCLIENT = 0x031A;
    const int WM_ERASEBKGND = 0x0014, WM_WINDOWPOSCHANGED = 0x0047, WM_DPICHANGED = 0x02E0;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x00080000;
    const uint LWA_COLORKEY = 0x00000001;

    static System.IO.TextWriter log;
    static void Log(string msg)
    {
        try { lock (typeof(TaskBarTestString)) { log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg); log.Flush(); } } catch { }
    }
    // Console.Error is null in a GUI (/winexe) build with no console attached.
    static void CErr(string msg)
    {
        try { System.IO.TextWriter e = Console.Error; if (e != null) e.WriteLine(msg); } catch { }
    }
    const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
    const int VK_CONTROL = 0x11;
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;
    const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
    static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; public int W { get { return Right - Left; } } public int H { get { return Bottom - Top; } } }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate; public int r1, r2, r3, r4, r5, r6, r7, r8; }

    // NOTE: CharSet.Unicode on the struct is REQUIRED. Without it the
    // lpszClassName string is marshaled as ANSI and CreateWindowExW fails
    // with ERROR_CANNOT_FIND_WND_CLASS (1407).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS { public int style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    // ------------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------------
    static string displayText = "TEST";
    static uint bgColor;
    static IntPtr hbrBg;
    static Font font;
    static int winW, winH;

    static IntPtr taskbar;      // Shell_TrayWnd (or secondary)
    static bool isWin11Taskbar; // DesktopWindowContentBridge present
    static bool useFallback;    // SetParent failed -> absolute + topmost

    // Win11 anchors (Win11TaskbarDlg::InitTaskbarWnd)
    static IntPtr hNotify, hStart;

    // Classical anchors (ClassicalTaskbarDlg::InitTaskbarWnd)
    static IntPtr hBar, hMin;
    static int lastMinW = -1, lastMinH = -1, lastBarW = -1;
    static int savedMinW, savedMinH, savedLeftSpace, savedTopSpace; // m_rcMinOri + m_left_space
    static bool minShrunk;

    // Win11 layout cache (m_last_notify_width / m_last_start_pos)
    static int lastNotifyW = -1, lastStartLeft = -1;

    static bool wndOnLeft, wndSnap;
    static Timer layoutTimer;    // 100 ms, like CTrafficMonitorDlg's main timer
    static IntPtr hwnd;
    static WndProc proc;         // keep the delegate alive (GC!)

    // ------------------------------------------------------------------------
    // TrafficMonitor: CheckWindows11Taskbar (TrafficMonitor.cpp:1303)
    // ------------------------------------------------------------------------
    static bool DetectWin11Taskbar()
    {
        if (taskbar == IntPtr.Zero) return false;
        return FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero;
    }

    // TrafficMonitor: CWindowsSettingHelper::IsTaskbarCenterAlign
    // (HKCU\...\Explorer\Advanced, DWORD TaskbarAl; absent => centered)
    static bool IsTaskbarCenterAlign()
    {
        IntPtr subKey;
        if (RegOpenKeyEx(HKEY_CURRENT_USER,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", 0, 0x20019 /* KEY_READ */,
            out subKey) != 0)
            return true; // cannot read => assume centered (TrafficMonitor default)
        try
        {
            byte[] data = new byte[4];
            int size = 4;
            uint regType;
            int err = RegQueryValueEx(subKey, "TaskbarAl", IntPtr.Zero, out regType, data, ref size);
            if (err != 0) return true; // value absent => centered
            return BitConverter.ToInt32(data, 0) != 0;
        }
        finally { RegCloseKey(subKey); }
    }

    // TrafficMonitor: InitTaskbarWnd per subclass
    static void InitTaskbarAnchors()
    {
        if (isWin11Taskbar)
        {
            hNotify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            hStart = FindWindowEx(taskbar, IntPtr.Zero, "Start", null);
        }
        else
        {
            hBar = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
            if (hBar == IntPtr.Zero) hBar = FindWindowEx(taskbar, IntPtr.Zero, "WorkerW", null); // Win10 fallback
            if (hBar != IntPtr.Zero)
            {
                hMin = FindWindowEx(hBar, IntPtr.Zero, "MSTaskSwWClass", null);
                if (hMin == IntPtr.Zero) hMin = FindWindowEx(hBar, IntPtr.Zero, "MSTaskListWClass", null); // Win10 fallback
            }
        }
    }

    // ------------------------------------------------------------------------
    // Layout (AdjustWindowPos + AdjustTaskbarWndPos per OS)
    // ------------------------------------------------------------------------
    static void Layout(bool force)
    {
        if (hwnd == IntPtr.Zero || winW <= 0) return;

        RECT rcTask;
        if (!GetWindowRect(taskbar, out rcTask)) return;

        if (isWin11Taskbar) LayoutWin11(ref rcTask, force);
        else                LayoutClassical(ref rcTask, force);
    }

    // ---- CWin11TaskbarDlg::AdjustTaskbarWndPos -----------------------------
    static void LayoutWin11(ref RECT rcTask, bool force)
    {
        RECT rcNotify = new RECT(), rcStart = new RECT();
        if (hNotify != IntPtr.Zero) GetWindowRect(hNotify, out rcNotify);
        if (hStart != IntPtr.Zero)  GetWindowRect(hStart, out rcStart);

        // Start button converted to parent-relative (Win11TaskbarDlg.cpp:9)
        int startRelLeft = rcStart.Left - rcTask.Left;
        int startH = Math.Max(rcStart.H, 1);
        int taskbarW = rcTask.W, taskbarH = rcTask.H;

        // "on left" is only honored when the taskbar is center-aligned;
        // with a left-aligned taskbar the window always stays on the right.
        bool effectiveLeft = wndOnLeft && IsTaskbarCenterAlign();

        int x;
        if (!effectiveLeft)
        {
            int notifyScreenLeft = (hNotify != IntPtr.Zero) ? rcNotify.Left : 0;
            int notifyX;
            if (notifyScreenLeft == 0)
                notifyX = taskbarW - 280; // taskbar_right_space_win11 default (DPI-scaled in TM)
            else
                notifyX = notifyScreenLeft - rcTask.Left;
            x = notifyX - winW + 2; // Win11TaskbarDlg.cpp:46
        }
        else
        {
            x = wndSnap ? startRelLeft - winW - 2 : 2; // snap-to-start / far-left
        }
        int y = (startH - winH) / 2 + (taskbarH - startH); // Win11TaskbarDlg.cpp:78

        bool changed = force || lastNotifyW != rcNotify.W || lastStartLeft != rcStart.Left;
        lastNotifyW = rcNotify.W;
        lastStartLeft = rcStart.Left;

        if (changed)
        {
            if (useFallback)
            {
                // screen-absolute fallback (TaskBarDlg.cpp AdjustWindowPos)
                SetWindowPos(hwnd, HWND_TOPMOST,
                    rcTask.Left + x, rcTask.Top + y, winW, winH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            else
            {
                MoveWindow(hwnd, x, y, winW, winH, true); // parent-relative
            }
            InvalidateRect(hwnd, IntPtr.Zero, true);
        }
    }

    // ---- CClassicalTaskbarDlg::AdjustTaskbarWndPos -------------------------
    static void LayoutClassical(ref RECT rcTask, bool force)
    {
        if (hBar == IntPtr.Zero || hMin == IntPtr.Zero)
        {
            if (force) UseFallbackMode();
            return;
        }

        RECT rcBar, rcMin;
        GetWindowRect(hBar, out rcBar);
        GetWindowRect(hMin, out rcMin);

        int leftSpace = rcMin.Left - rcBar.Left;
        int topSpace = rcMin.Top - rcBar.Top;
        int barW = rcBar.W, barH = rcBar.H;
        int minW = rcMin.W, minH = rcMin.H;

        bool changed = force || minW != lastMinW || minH != lastMinH || barW != lastBarW;
        lastMinW = minW; lastMinH = minH; lastBarW = barW;

        if (!changed) return;

        // m_left_space / m_top_space bookkeeping
        savedLeftSpace = leftSpace;
        savedTopSpace = topSpace;

        int x, y;
        if (barW >= barH) // horizontal taskbar (top/bottom)
        {
            // AdjustWindowPos(true): ResetTaskbarPos() first, then re-shrink
            if (minShrunk)
            {
                MoveWindow(hMin, savedLeftSpace, 0, savedMinW, savedMinH, true); // restore original
                minShrunk = false;
            }
            if (savedMinW == 0) { savedMinW = minW; savedMinH = minH; } // remember original

            int newMinW = minW - winW;
            if (newMinW < 20) newMinW = 20; // never shrink below usability

            // right side (default): keep left edge, shrink width, place us after it
            MoveWindow(hMin, leftSpace, 0, newMinW, minH, true);
            minShrunk = true;
            x = leftSpace + newMinW + 2;
            y = (barH - winH) / 2;
        }
        else // vertical taskbar (left/right)
        {
            if (minShrunk)
            {
                MoveWindow(hMin, 0, savedTopSpace, savedMinW, savedMinH, true);
                minShrunk = false;
            }
            if (savedMinH == 0) { savedMinW = minW; savedMinH = minH; }

            int newMinH = minH - winH;
            if (newMinH < 20) newMinH = 20;

            MoveWindow(hMin, 0, topSpace, minW, newMinH, true);
            minShrunk = true;
            x = 2;
            y = topSpace + newMinH + 2;
        }

        if (useFallback)
        {
            SetWindowPos(hwnd, HWND_TOPMOST,
                rcBar.Left + x, rcBar.Top + y, winW, winH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        else
        {
            MoveWindow(hwnd, x, y, winW, winH, true);
        }
        InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    // m_connot_insert_to_task_bar fallback flag
    static void UseFallbackMode()
    {
        if (!useFallback)
        {
            useFallback = true;
            CErr("SetParent failed - using topmost screen-absolute fallback.");
        }
    }

    // ---- CTaskBarDlg::ResetTaskbarPos (called on exit) ----------------------
    static void ResetTaskbarPos()
    {
        if (isWin11Taskbar || useFallback) return; // nothing to restore
        if (!minShrunk || hMin == IntPtr.Zero || savedMinW == 0) return;
        if (IsWindow(hMin))
        {
            RECT rcBar, rcMin;
            GetWindowRect(hBar, out rcBar);
            GetWindowRect(hMin, out rcMin);
            int leftSpace = rcMin.Left - rcBar.Left;
            int topSpace = rcMin.Top - rcBar.Top;
            if (rcBar.W >= rcBar.H)
                MoveWindow(hMin, leftSpace, 0, savedMinW, savedMinH, true);
            else
                MoveWindow(hMin, 0, topSpace, savedMinW, savedMinH, true);
            minShrunk = false;
        }
    }

    // ------------------------------------------------------------------------
    // Window proc
    // ------------------------------------------------------------------------
    static IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                Log("WM_ERASEBKGND");
                break;
            case WM_WINDOWPOSCHANGED:
                Log("WM_WINDOWPOSCHANGED");
                break;
            case WM_DPICHANGED:
                Log("WM_DPICHANGED w=" + w);
                break;
            case WM_PAINT:
                Log("WM_PAINT");
                {
                    PAINTSTRUCT ps = new PAINTSTRUCT();
                    IntPtr hdc = BeginPaint(h, ref ps);
                    try { DrawContent(hdc); }
                    finally { EndPaint(h, ref ps); }
                    return IntPtr.Zero;
                }
            case WM_PRINTCLIENT:
                // MFC routes WM_PRINTCLIENT to OnPaint as well; needed so
                // PrintWindow/WM_PRINT captures include the drawn content.
                {
                    DrawContent(w);
                    return IntPtr.Zero;
                }
            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
                if ((GetKeyState(VK_CONTROL) & 0x8000) != 0)
                    DestroyWindow(h);               // Ctrl+click -> exit
                else
                    CopyToClipboard(displayText);   // click -> copy test string
                return IntPtr.Zero;
            case WM_DESTROY:
                ResetTaskbarPos();                 // give the task list its space back
                WriteShowFlag(false);              // like OnTaskbarWndClosed: closing the
                                                   // strip also turns the option OFF
                Application.Exit();
                return IntPtr.Zero;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    static void DrawContent(IntPtr hdc)
    {
        using (Graphics g = Graphics.FromHdc(hdc))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.FillRectangle(new SolidBrush(ColorTranslator.FromWin32((int)bgColor)), 0, 0, winW, winH);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(displayText, font, Brushes.White, new RectangleF(0, 0, winW, winH), sf);
        }
    }

    static void CopyToClipboard(string text)
    {
        try
        {
            if (!OpenClipboard(hwnd)) return;
            try
            {
                EmptyClipboard();
                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, bytes.Length);
                IntPtr ptr = GlobalLock(mem);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                GlobalUnlock(mem);
                SetClipboardData(CF_UNICODETEXT, mem);
            }
            finally { CloseClipboard(); }
        }
        catch { }
    }

    // ------------------------------------------------------------------------
    // Task bar strip on/off state (TrafficMonitor: [config] show_task_bar_wnd)
    // ------------------------------------------------------------------------
    static string StateFile()
    {
        return System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "taskbar_test.ini");
    }

    static bool ReadShowFlag()
    {
        try
        {
            if (!System.IO.File.Exists(StateFile())) return false; // absent => OFF (default)
            foreach (string line in System.IO.File.ReadAllLines(StateFile()))
            {
                string t = line.Trim();
                const string key = "show_task_bar_wnd=";
                if (t.StartsWith(key))
                    return int.Parse(t.Substring(key.Length).Trim()) != 0;
            }
        }
        catch { }
        return false;
    }

    static void WriteShowFlag(bool show)
    {
        try
        {
            System.IO.File.WriteAllText(StateFile(),
                "[config]\r\nshow_task_bar_wnd=" + (show ? 1 : 0) + "\r\n");
        }
        catch { }
    }

    // ------------------------------------------------------------------------
    // Kill the running strip (easy remote kill switch)
    // ------------------------------------------------------------------------
    // Finds every child of Shell_TrayWnd whose class starts with "TaskBarTest_"
    // and posts WM_CLOSE to it. This is reliable regardless of whether the
    // strip is clickable/visible, and doesn't require knowing the PID. Falls
    // back to a process-name kill if no window is found.
    static void DoKill()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        int closed = 0;
        if (taskbar != IntPtr.Zero)
        {
            IntPtr child = FindWindowEx(taskbar, IntPtr.Zero, null, null);
            while (child != IntPtr.Zero)
            {
                var sb = new System.Text.StringBuilder(512);
                int n = GetClassName(child, sb, sb.Capacity);
                if (n > 0 && sb.ToString().StartsWith("TaskBarTest_"))
                {
                    PostMessage(child, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    closed++;
                }
                child = FindWindowEx(taskbar, child, null, null);
            }
        }
        if (closed > 0)
            CErr("Killed " + closed + " strip window(s) via WM_CLOSE.");
        else
            CErr("No strip window found under the taskbar.");
        // Belt-and-suspenders: also terminate by process name in case the
        // window is in fallback (topmost) mode or reparenting failed.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = "/F /IM TaskBarTestString.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = System.Diagnostics.Process.Start(psi);
            if (p != null) { p.WaitForExit(3000); CErr("taskkill exit=" + p.ExitCode); }
        }
        catch { }
    }

    // ------------------------------------------------------------------------
    // Main
    // ------------------------------------------------------------------------
    static void Main(string[] args)
    {
        // Per-monitor DPI awareness (TrafficMonitor: SetDPI / DPI(8.1+))
        try { SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE); } catch { }

        try { log = new System.IO.StreamWriter("taskbar_paint_log.txt", true); } catch { }

        string label = "TEST";
        bool explicitEnable = false, explicitDisable = false, explicitKill = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--label" && i + 1 < args.Length) label = args[i + 1];
            else if (args[i] == "--left") wndOnLeft = true;
            else if (args[i] == "--snap") wndSnap = true;
            else if (args[i] == "--enable") explicitEnable = true;
            else if (args[i] == "--disable") explicitDisable = true;
            else if (args[i] == "--kill") explicitKill = true;
        }

        // --kill: stop the running strip and exit. This is the easy/remote
        // kill switch. It does NOT change the enabled setting, so the strip
        // stays "on" and a later plain run (or --enable) will show it again.
        if (explicitKill)
        {
            DoKill();
            return;
        }

        // TrafficMonitor parity: the task bar strip is OFF by default and
        // only shown when the user turned the option on (show_task_bar_wnd).
        bool wantShow = ReadShowFlag();
        if (explicitEnable) wantShow = true;
        if (explicitDisable) wantShow = false;
        if (explicitEnable || explicitDisable)
            WriteShowFlag(wantShow); // persist explicit commands
        if (!wantShow)
        {
            Console.WriteLine("Task bar strip is OFF (default - same as TrafficMonitor's show_task_bar_wnd=false).");
            Console.WriteLine("Run with --enable to turn it on, or --disable to turn it off.");
            return;
        }

        displayText = label + "  |  " + System.Net.Dns.GetHostName();
        // COLORREF is 0x00BBGGRR (byte order), same value the earlier test app used
        Color blue = ColorTranslator.FromHtml("#0078D4");
        bgColor = (uint)((blue.B << 16) | (blue.G << 8) | blue.R);
        font = new Font("Segoe UI", 9, FontStyle.Bold);

        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            SizeF sz = g.MeasureString(displayText, font);
            winW = (int)sz.Width + 16;
            winH = (int)sz.Height + 4;
        }

        // FindTaskbarHandle (primary)
        taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            CErr("Shell_TrayWnd not found.");
            return;
        }

        isWin11Taskbar = DetectWin11Taskbar();
        Console.WriteLine("Taskbar mode: " + (isWin11Taskbar ? "Windows 11 (Shell_TrayWnd direct)" : "Classical Win7/10 (ReBarWindow32)"));
        InitTaskbarAnchors();

        if (!isWin11Taskbar && (hBar == IntPtr.Zero || hMin == IntPtr.Zero))
            Console.WriteLine("Warning: ReBar/MSTaskSw not found; expecting fallback mode.");

        // Register window class (unique name; Unicode-marshaled WNDCLASS)
        hbrBg = CreateSolidBrushFromColor(bgColor);
        string className = "TaskBarTest_" + Guid.NewGuid().ToString("N");
        proc = new WndProc(WndProcImpl);
        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = proc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = IntPtr.Zero,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = hbrBg,
            lpszMenuName = null,
            lpszClassName = className
        };
        if (RegisterClassW(ref wc) == 0)
        {
            CErr("RegisterClassW failed, err=" + Marshal.GetLastWin32Error());
            return;
        }

        // Create the popup dialog exactly like TrafficMonitor's
        // IDD_TASK_BAR_DIALOG: WS_POPUP (+WS_SYSMENU), visible; ex WS_EX_TOOLWINDOW.
        // WS_EX_LAYERED must be present FROM CREATION: proven on this machine
        // that a runtime SetWindowLongPtr write to GWL_EXSTYLE after SetParent
        // into the taskbar is silently lost, while the create-time flag
        // survives reparenting (see test_exstyle.cs / test_exstyle3.cs).
        // The C++ reference does the same (taskbar_overlay.cpp:181).
        hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_LAYERED, className, className,
            WS_POPUP | WS_VISIBLE | WS_SYSMENU,
            0, 0, winW, winH, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            CErr("CreateWindowExW failed, err=" + Marshal.GetLastWin32Error());
            return;
        }

        // OnInitDialog sequence:
        ShowWindow(hwnd, SW_SHOW);
        UpdateWindow(hwnd);

        // THE key embedding call (TaskBarDlg.cpp:994-1059 step 10).
        // Win11 -> parent Shell_TrayWnd; classical -> parent ReBarWindow32.
        // NOTE: SetParent returns the OLD parent (NULL for a top-level
        // window), so a NULL result is NOT a failure. The working C++
        // reference (taskbar_overlay.cpp EmbedOntoTaskbar) does not check
        // the return value either.
        IntPtr parentHwnd = isWin11Taskbar ? taskbar : hBar;
        if (parentHwnd == IntPtr.Zero)
            UseFallbackMode();
        else
        {
            SetParent(hwnd, parentHwnd);
            BringWindowToTop(hwnd); // newest child -> top of the sibling z-order
        }

        // DWM layered surface -- mirrors taskbar_overlay.cpp ApplyTransparency.
        // Without WS_EX_LAYERED a plain GDI window under the Win11 XAML
        // taskbar composites UNDER the XAML surface: it paints normally
        // (WM_PAINT flows, PrintWindow shows the content) yet is invisible
        // on screen. Layered windows are composited by DWM according to
        // z-order, which makes them visible. Color-keying the background
        // color makes the box transparent so only the text shows, exactly
        // like TrafficMonitor's default look.
        // WS_EX_LAYERED is already set (create-time exstyle); only the
        // color-key attribute needs to be applied here.
        IntPtr exNow = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        bool layeredOk = SetLayeredWindowAttributes(hwnd, (int)bgColor, 255, LWA_COLORKEY);
        Log("layered: ex=0x" + ((int)exNow).ToString("X8")
            + " SetLayeredWindowAttributes=" + layeredOk);

        Layout(true); // first full layout (force_adjust = true)

        // Main-window timer: every 100 ms, reposition + repaint
        // (TrafficMonitorDlg.cpp:1984 - AdjustWindowPos + Invalidate(FALSE),
        //  the invalidate is UNCONDITIONAL every tick)
        //
        // ADDITION vs TrafficMonitor: re-assert z-order every tick.
        // Measured on this machine (Win11 22631): when our child sits below
        // the XAML DesktopWindowContentBridge surface, Windows suppresses
        // WM_PAINT entirely (0 WM_PAINT vs 64 WM_ERASEBKGND observed), so the
        // strip is painted once and then blank. BringWindowToTop is a no-op
        // if we are already topmost, so doing it every tick is safe and
        // keeps the strip permanently visible.
        layoutTimer = new Timer { Interval = 100 };
        layoutTimer.Tick += (s, e) =>
        {
            try
            {
                Layout(false);
                if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                {
                    BringWindowToTop(hwnd);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            catch { }
        };
        layoutTimer.Start();

        Console.WriteLine("Embedded. hwnd=" + hwnd + " size=" + winW + "x" + winH +
                          " left=" + (wndOnLeft ? "yes" : "no (right, default)"));
        Console.WriteLine("Left-click copies the text, Ctrl+click exits.");

        Application.Run(); // message loop; exits on WM_DESTROY
        layoutTimer.Dispose();
    }

    static IntPtr CreateSolidBrushFromColor(uint bgr)
    {
        // COLORREF = 0x00BBGGRR (bgr is already in that layout)
        return Gdi.CreateSolidBrush((int)bgr);
    }

    static class Gdi
    {
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateSolidBrush(int color);
    }
}
