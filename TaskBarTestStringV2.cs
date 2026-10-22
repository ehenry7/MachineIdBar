// ============================================================================
// TaskBarTestStringV2.cs
//
// A simple C# app that writes a test string into the Windows task bar,
// faithfully reproducing TrafficMonitor's "task area window mode"
// (taskbar window embedding).
//
// This is V2 of TaskBarTestString.cs. It fixes three issues found while
// reviewing the first draft against the TrafficMonitor sources:
//   1. Window is now created with WS_CLIPSIBLINGS | WS_CLIPCHILDREN
//      (MFC dialogs always carry these; without them siblings inside the
//      taskbar paint over our window -> invisible).
//   2. DPI: font, window height and paddings are scaled with the taskbar's
//      actual DPI (GetDpiForWindow), like TrafficMonitor's DPI() helper.
//      Critical on 200% scaled displays.
//   3. COLORREF byte order: the background color was assembled as
//      0x00RRGGBB but COLORREF is 0x00BBGGRR, producing orange instead of
//      Windows blue #0078D4.
//
// Reproduced from:  trafficmonitor-ref/TrafficMonitor/
//   - TaskBarDlg.cpp / TaskBarDlg.h        (embedding sequence, AdjustWindowPos
//                                            orchestrator, fallback, timers)
//   - Win11TaskbarDlg.cpp                  (Win11: parent = Shell_TrayWnd,
//                                            anchors TrayNotifyWnd + Start)
//   - ClassicalTaskbarDlg.cpp              (Win7/10: parent = ReBarWindow32,
//                                            shrink/restore MSTaskSwWClass)
//   - TrafficMonitor.cpp (CheckWindows11Taskbar, lines 1303-1313)
//   - WindowsSettingHelper.cpp (IsTaskbarCenterAlign, registry TaskbarAl)
//
// Fidelity points (per the "TrafficMonitor Code Analyst" analysis):
//   1. Window is created as WS_POPUP (NOT WS_CHILD) and reparented later
//      with a single SetParent call -- the style is never changed.
//   2. ExStyle is WS_EX_TOOLWINDOW only. WS_EX_NOACTIVATE is NOT used.
//   3. All MoveWindow coordinates for our window are parent-relative.
//   4. Win7/10: the sibling MSTaskSwWClass is shrunk by our width to make
//      room; its original rect is saved and restored on every forced
//      re-layout and on exit (ResetTaskbarPos).
//   5. Win11: nothing is resized; position is computed from TrayNotifyWnd
//      (right side) and Start button rects; "on left" only applies when the
//      taskbar is center-aligned (registry TaskbarAl != 0).
//   6. Steady-state repositioning runs from a 100 ms timer (TrafficMonitorDlg
//      calls AdjustWindowPos + Invalidate every 100 ms) and only does a full
//      re-layout when the host geometry changed.
//   7. Win11 detection: a "Windows.UI.Composition.DesktopWindowContentBridge"
//      child exists inside Shell_TrayWnd.
//   8. Fallback: if SetParent fails (returns NULL), position with
//      screen-absolute coordinates and stay topmost (TaskBarDlg.cpp:484-497).
//
// Usage:
//   TaskBarTestStringV2.exe [--label TEXT] [--left] [--snap]
//   Left click  : copy the shown text to the clipboard
//   Ctrl+click  : exit (restores the task list on Win7/10)
//
// Build (.NET Framework, no Visual Studio needed):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
//     /nologo /target:exe /out:TaskBarTestStringV2.exe ^
//     /r:System.Drawing.dll TaskBarTestStringV2.cs
// ============================================================================

using System;
using System.Drawing;
using System.Runtime.InteropServices;

static class TaskBarTestStringV2
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
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(int exStyle, string cls, string wnd, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern short GetKeyState(int vkey);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr timerProc);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] static extern bool PostQuitMessage(int exitCode);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
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
    [DllImport("advapi32.dll")] static extern int RegCloseKey(IntPtr hKey);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved,
        out uint type, byte[] data, ref int dataSize);

    // ------------------------------------------------------------------------
    // Win32 constants
    // ------------------------------------------------------------------------
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_VISIBLE = 0x10000000;
    const int WS_SYSMENU = 0x00080000;
    const int WS_CLIPSIBLINGS = 0x04000000;   // V2: MFC dialogs always carry these;
    const int WS_CLIPCHILDREN = 0x02000000;   //     without them we get painted over
    const int WS_EX_TOOLWINDOW = 0x80;
    const int SW_SHOWNOACTIVATE = 4;          // visible, but never steals focus
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const int WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_TIMER = 0x0113;
    const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
    const int VK_CONTROL = 0x11;
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;
    const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
    const int ERROR_ACCESS_DENIED = 5;        // SetProcessDpiAwareness already set
    static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
    const string WINDOW_TITLE = "TaskBarTestStringV2";
    const string CLASS_NAME = "TaskBarTestStringV2Class";
    static readonly IntPtr TIMER_ID = new IntPtr(1);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; public int W { get { return Right - Left; } } public int H { get { return Bottom - Top; } } }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate; public int r1, r2, r3, r4, r5, r6, r7, r8; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

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
    static uint bgBgr;                 // COLORREF (0x00BBGGRR) of #0078D4
    static IntPtr hbrBg;
    static Font font;
    static int winW, winH;
    static uint dpi = 96;

    static IntPtr taskbar;      // Shell_TrayWnd (or secondary)
    static bool isWin11Taskbar; // DesktopWindowContentBridge present
    static bool useFallback;    // SetParent failed -> absolute + topmost

    // Win11 anchors (Win11TaskbarDlg::InitTaskbarWnd)
    static IntPtr hNotify, hStart;

    // Classical anchors (ClassicalTaskbarDlg::InitTaskbarWnd)
    static IntPtr hBar, hMin;
    static int lastMinW = -1, lastMinH = -1, lastBarW = -1;
    static int savedMinW, savedMinH;   // m_rcMinOri
    static bool minShrunk;

    // Win11 layout cache (m_last_notify_width / m_last_start_pos)
    static int lastNotifyW = -1, lastStartLeft = -1;

    static bool wndOnLeft, wndSnap;
    static IntPtr hwnd;
    static WndProc proc;         // keep the delegate alive (GC!)

    // TrafficMonitor: DPI() helpers (TaskBarDlg.cpp:575-588)
    static int DPI(int pixel) { return (int)(dpi * (uint)pixel / 96); }

    // TrafficMonitor: CheckWindows11Taskbar (TrafficMonitor.cpp:1303-1313)
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
        else if (!LayoutClassical(ref rcTask, force)) return;

        // TrafficMonitorDlg.cpp:2022-2023 -- invalidate every tick
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    // ---- CWin11TaskbarDlg::AdjustTaskbarWndPos (Win11TaskbarDlg.cpp:5-87) --
    static void LayoutWin11(ref RECT rcTask, bool force)
    {
        RECT rcNotify = new RECT(), rcStart = new RECT();
        if (hNotify != IntPtr.Zero) GetWindowRect(hNotify, out rcNotify);
        if (hStart != IntPtr.Zero) GetWindowRect(hStart, out rcStart);

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
            {
                // Win11 secondary display has no TrayNotifyWnd; use the
                // configured right-space reserve (default 280, DPI-scaled)
                notifyX = taskbarW - DPI(280);
            }
            else
            {
                notifyX = notifyScreenLeft - rcTask.Left;
            }
            x = notifyX - winW + 2; // Win11TaskbarDlg.cpp:46
        }
        else
        {
            // snap to Start button, or hug the far-left edge (Win11TaskbarDlg.cpp:53-63)
            x = wndSnap ? startRelLeft - winW - 2 : 2;
        }
        int y = (startH - winH) / 2 + (taskbarH - startH); // Win11TaskbarDlg.cpp:78

        bool changed = force || lastNotifyW != rcNotify.W || lastStartLeft != rcStart.Left;
        lastNotifyW = rcNotify.W;
        lastStartLeft = rcStart.Left;

        if (changed)
        {
            if (useFallback)
            {
                // screen-absolute fallback (TaskBarDlg.cpp:484-497)
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

    // ---- CClassicalTaskbarDlg::AdjustTaskbarWndPos (ClassicalTaskbarDlg.cpp:4-68)
    static bool LayoutClassical(ref RECT rcTask, bool force)
    {
        if (hBar == IntPtr.Zero || hMin == IntPtr.Zero)
        {
            if (force) UseFallbackMode();
            return false;
        }

        RECT rcBar, rcMin;
        GetWindowRect(hBar, out rcBar);
        GetWindowRect(hMin, out rcMin);

        int leftSpace = rcMin.Left - rcBar.Left;
        int topSpace = rcMin.Top - rcBar.Top;
        int minW = rcMin.W, minH = rcMin.H;

        bool changed = force || minW != lastMinW || minH != lastMinH || rcBar.W != lastBarW;
        lastMinW = minW; lastMinH = minH; lastBarW = rcBar.W;

        if (!changed) return true;

        int x, y;
        if (rcBar.W >= rcBar.H) // horizontal taskbar (top/bottom)
        {
            // AdjustWindowPos(true): ResetTaskbarPos() first, then re-shrink
            if (minShrunk)
            {
                MoveWindow(hMin, leftSpace, 0, savedMinW, savedMinH, true); // restore original
                minShrunk = false;
            }
            if (savedMinW == 0) { savedMinW = minW; savedMinH = minH; } // remember original

            int newMinW = minW - winW;
            if (newMinW < 20) newMinW = 20; // never shrink below usability

            // right side (default): keep left edge, shrink width, place us after it
            MoveWindow(hMin, leftSpace, 0, newMinW, minH, true);
            minShrunk = true;
            x = leftSpace + newMinW + 2;
            y = (rcBar.H - winH) / 2;
        }
        else // vertical taskbar (left/right)
        {
            if (minShrunk)
            {
                MoveWindow(hMin, 0, topSpace, savedMinW, savedMinH, true);
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
        return true;
    }

    // m_connot_insert_to_task_bar fallback flag (TaskBarDlg.cpp:1017)
    static void UseFallbackMode()
    {
        if (!useFallback)
        {
            useFallback = true;
            Console.Error.WriteLine("SetParent failed - using topmost screen-absolute fallback.");
        }
    }

    // ---- CTaskBarDlg::ResetTaskbarPos (ClassicalTaskbarDlg.cpp:86-97) ------
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
            case WM_PAINT:
                {
                    PAINTSTRUCT ps = new PAINTSTRUCT();
                    IntPtr hdc = BeginPaint(h, ref ps);
                    try
                    {
                        using (Graphics g = Graphics.FromHdc(hdc))
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                            using (var bg = new SolidBrush(ColorTranslator.FromWin32((int)bgBgr)))
                            {
                                g.FillRectangle(bg, 0, 0, winW, winH);
                            }
                            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                            using (var br = new SolidBrush(Color.White))
                            {
                                g.DrawString(displayText, font, br, new RectangleF(0, 0, winW, winH), sf);
                            }
                        }
                    }
                    finally { EndPaint(h, ref ps); }
                    return IntPtr.Zero;
                }
            case WM_TIMER:
                // TrafficMonitorDlg.cpp:2022 -- every 100 ms:
                // AdjustWindowPos(); Invalidate(FALSE);
                Layout(false);
                return IntPtr.Zero;
            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
                if ((GetKeyState(VK_CONTROL) & 0x8000) != 0)
                {
                    DestroyWindow(h); // Ctrl+click -> exit
                }
                else
                {
                    CopyToClipboard(displayText); // click -> copy test string
                }
                return IntPtr.Zero;
            case WM_DESTROY:
                ResetTaskbarPos();   // give the task list its space back
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);

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
    // Main
    // ------------------------------------------------------------------------
    static int Main(string[] args)
    {
        // Per-monitor DPI awareness (TrafficMonitor: SetDPI / DPI(), Win8.1+).
        // All geometry below is therefore in physical pixels.
        try
        {
            int hr = SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            if (hr == ERROR_ACCESS_DENIED) { /* already set by manifest or host */ }
        }
        catch { /* pre-Win8.1: fall back to system default */ }

        string label = "TEST";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--label" && i + 1 < args.Length) { label = args[++i]; continue; }
            if (args[i] == "--left") wndOnLeft = true;
            if (args[i] == "--snap") wndSnap = true;
        }

        // FindTaskbarHandle (primary display only, like CTaskBarDlg default)
        taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            Console.Error.WriteLine("Shell_TrayWnd not found.");
            return 1;
        }

        isWin11Taskbar = DetectWin11Taskbar();
        InitTaskbarAnchors();

        // DPI of the monitor that hosts the taskbar
        // (CTaskBarDlg::SetDPI via DPIFromRect, TaskBarDlg.cpp:1020-1031)
        try { dpi = GetDpiForWindow(taskbar); } catch { dpi = 96; }
        if (dpi == 0) dpi = 96;

        displayText = label + "  |  " + System.Net.Dns.GetHostName();

        // COLORREF is 0x00BBGGRR: Windows blue #0078D4 (R=00 G=78 B=D4)
        bgBgr = 0x00u | (0x78u << 8) | (0xD4u << 16); // = 0xD47800
        hbrBg = CreateSolidBrush((int)bgBgr);

        // Font and window size, DPI-scaled like TrafficMonitor
        // (font.Create(m_font, GetDPI()) + TASKBAR_WND_HEIGHT = DPI(32))
        float fontPt = 9f * dpi / 96f;
        font = new Font("Segoe UI", fontPt, FontStyle.Bold);
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            SizeF sz = g.MeasureString(displayText, font);
            winW = (int)sz.Width + DPI(16);
        }
        winH = DPI(32); // TASKBAR_WND_HEIGHT (TaskBarDlg.h:14)

        // Register window class (Unicode-marshaled WNDCLASS)
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
            lpszClassName = CLASS_NAME
        };
        if (RegisterClassW(ref wc) == 0)
        {
            Console.Error.WriteLine("RegisterClassW failed, err=" + Marshal.GetLastWin32Error());
            return 1;
        }

        // Create the popup dialog exactly like TrafficMonitor's
        // IDD_TASK_BAR_DIALOG: WS_POPUP (+WS_SYSMENU) + MFC's clip styles.
        hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, CLASS_NAME, WINDOW_TITLE,
            WS_POPUP | WS_VISIBLE | WS_SYSMENU | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, winW, winH, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("CreateWindowExW failed, err=" + Marshal.GetLastWin32Error());
            return 1;
        }

        // OnInitDialog sequence:
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        UpdateWindow(hwnd);

        // THE key embedding call (TaskBarDlg.cpp:1017).
        // Win11 -> parent Shell_TrayWnd; classical -> parent ReBarWindow32.
        IntPtr parentHwnd = isWin11Taskbar ? taskbar : hBar;
        if (parentHwnd == IntPtr.Zero || SetParent(hwnd, parentHwnd) == IntPtr.Zero)
            UseFallbackMode();
        else
            BringWindowToTop(hwnd); // we are the newest child -> top of the sibling z-order

        Layout(true); // first full layout (force_adjust = true)

        // Main-window timer: every 100 ms, reposition + repaint
        // (TrafficMonitorDlg.cpp:1180 / 2022)
        SetTimer(hwnd, TIMER_ID, 100, IntPtr.Zero);

        Console.WriteLine("DPI=" + dpi + "  taskbar mode: " +
            (isWin11Taskbar ? "Windows 11 (parent = Shell_TrayWnd)" : "Classical (parent = ReBarWindow32)") +
            (useFallback ? " [fallback: topmost overlay]" : string.Empty));
        Console.WriteLine("Embedded. hwnd=" + hwnd + " size=" + winW + "x" + winH);
        Console.WriteLine("Left-click copies the text, Ctrl+click exits.");

        // Raw Win32 message loop
        MSG msgData;
        while (GetMessage(out msgData, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msgData);
            DispatchMessage(ref msgData);
        }
        return 0;
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int color);
}
