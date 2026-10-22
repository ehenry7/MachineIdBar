// ============================================================================
// TaskBarTestStringV3.cs
//
// Taskbar test-string app, V3. V2 embedded into the taskbar via SetParent
// but never painted anything on screen. V3 is a faithful port of the WORKING
// C/C++ version (taskbar_overlay/MachineIdBar.cpp + winmain.cpp; the class
// was renamed TaskbarOverlay -> MachineIdBar), which itself mirrors
// TrafficMonitor's taskbar window:
//
// The critical fix (V2 had none of this):
//   1. Window is created with WS_EX_LAYERED and activated via
//      SetLayeredWindowAttributes(colorKey, 255, LWA_COLORKEY).
//      TrafficMonitor does exactly the same on its taskbar window
//      (TrafficMonitor/TaskBarDlg.cpp:524-531). On the Windows 11 taskbar
//      only a LAYERED child window gets its own composition surface; a
//      plain child of Shell_TrayWnd is never rendered above the XAML
//      content bridge.
//
// Further differences to V2, all taken from the working C++ version:
//   2. Window created with WS_POPUP | WS_CHILD (C++ style), not
//      WS_POPUP | WS_VISIBLE | WS_SYSMENU | clip styles.
//   3. ShowWindow(SW_SHOWNOACTIVATE) happens AFTER SetParent. SW_SHOW would
//      activate the top-level ancestor (Shell_TrayWnd) and steal keyboard
//      focus from the user's app.
//   4. Plain GDI drawing (FillRect + DrawText) instead of GDI+.
//   5. Position computed in screen coordinates, converted with
//      ScreenToClient, then MoveWindow.
//   6. 1 s timer: InvalidateRect + ForceVisible + AdjustWindowPos
//      (C++ MIDBAR_TIMER_INTERVAL), not a 100 ms loop.
//   7. DPI: forced PROCESS_DPI_UNAWARE, exactly like the unmanifested
//      C++ exe (no per-monitor awareness call).
//
// Updated to track the latest C++ version (MachineIdBar.cpp):
//   8. Ex-style now includes WS_EX_NOACTIVATE, and WM_MOUSEACTIVATE returns
//      MA_NOACTIVATE so clicking the overlay never activates the taskbar
//      or moves focus away from the user's app.
//   9. Drag-to-reposition support (C++ OnLButtonDown/OnMouseMove/
//      OnLButtonUp): SetCapture, 3 px drag threshold, MoveWindow by delta.
//  10. Left click copies only when it was a click, not a drag (C++
//      checks Ctrl at button-DOWN for the left button, at button-UP for
//      the right one).
//
// Display: "label  |  hostname  |  IP" like the MachineIdBar / C++ version.
//   Left click   : copy the displayed text to the clipboard (drag moves)
//   Right click  : copy (the C++ version opens its tray menu here; this
//                  test app has no menu, so it keeps copy)
//   Ctrl+click   : exit
//
// Usage:
//   TaskBarTestStringV3.exe [--label TEXT] [--left]
//
// Build (.NET Framework, no Visual Studio needed):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
//     /nologo /target:exe /out:TaskBarTestStringV3.exe TaskBarTestStringV3.cs
// ============================================================================

using System;
using System.Runtime.InteropServices;

static class TaskBarTestStringV3
{
    // ------------------------------------------------------------------------
    // Win32 interop
    // ------------------------------------------------------------------------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(int exStyle, string cls, string wnd, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr GetCapture();
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string cls, string wnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string wnd);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern short GetKeyState(int vkey);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeOut, uint charset,
        uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int color);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int color);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool DrawTextW(IntPtr hdc, string text, int length, ref RECT rect, uint flags);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetTextExtentPoint32W(IntPtr hdc, string text, int length, out SIZE size);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, uint type, out uint dpiX, out uint dpiY);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);

    // ------------------------------------------------------------------------
    // Win32 constants
    // ------------------------------------------------------------------------
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_CHILD = 0x40000000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001;
    const int IDC_ARROW = 32512;
    const int SW_SHOW = 5, SW_SHOWNOACTIVATE = 4;
    const int WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_TIMER = 0x0113;
    const int WM_MOUSEACTIVATE = 0x0021;
    const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    const int WM_MOUSEMOVE = 0x0200, WM_DISPLAYCHANGE = 0x007E;
    const int VK_CONTROL = 0x11;
    const int MA_NOACTIVATE = 3;   // don't activate, but still deliver mouse messages
    const uint LWA_COLORKEY = 0x00000001;
    const uint DT_LEFT = 0, DT_VCENTER = 4, DT_SINGLELINE = 0x20;
    const int TRANSPARENT_BK = 1;
    const int FW_BOLD = 700, FW_NORMAL = 400;
    const int DEFAULT_CHARSET = 1, OUT_DEFAULT_PRECIS = 0, CLIP_DEFAULT_PRECIS = 0;
    const int CLEARTYPE_QUALITY = 5, DEFAULT_PITCH = 0, FF_SWISS = 2;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint MONITOR_DEFAULTTONEAREST = 2, MDT_EFFECTIVE_DPI = 0;
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;
    const int LOGPIXELSX = 88;

    const string CLASS_NAME = "TaskBarTestStringV3Class";
    const string WINDOW_TITLE = "TaskBarTestStringV3";
    static readonly IntPtr TIMER_ID = new IntPtr(1236);       // C++ MIDBAR_TIMER
    const uint TIMER_INTERVAL = 1000;                         // C++ MIDBAR_TIMER_INTERVAL

    // ------------------------------------------------------------------------
    // Structs
    // ------------------------------------------------------------------------
    // NOTE: CharSet.Unicode is REQUIRED so lpszClassName marshals as UTF-16.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS
    {
        public int style;
        public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; public int W { get { return Right - Left; } } public int H { get { return Bottom - Top; } } }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int CX, CY; }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate; public int r1, r2, r3, r4, r5, r6, r7, r8; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    enum TaskbarType { Classic, Win11, Wine }

    // ------------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------------
    static IntPtr hwnd;
    static IntPtr taskbar;             // Shell_TrayWnd / Shell_SecondaryTrayWnd
    static IntPtr hBar, hMin, hNotify; // classic: ReBar + task list; Win11: tray notify
    static TaskbarType type;
    static RECT minOriginalRect;
    static bool embedded;
    static bool positionLeft;

    static string displayText = "TEST";
    static uint textColor = 0x00FFFFFF;   // COLORREF white
    static uint colorKey = 0x00000000;    // COLORREF black -> fully transparent
    static IntPtr font;
    static int dpi = 96;

    static WndProc proc;                 // keep delegate alive (GC!)
    static uint wmTaskbarCreated;

    // Drag state — C++ m_dragging / m_drag_start_* / m_win_*
    static bool dragging;
    static int dragStartX, dragStartY;
    static int winX, winY;               // window position (parent-relative)

    // ------------------------------------------------------------------------
    // DPI — C++ MachineIdBar::GetDPI
    // ------------------------------------------------------------------------
    static int GetDPI()
    {
        IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero)
        {
            try
            {
                uint dpiX, dpiY;
                if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                    return (int)dpiX;
            }
            catch { /* shcore not available */ }
        }
        IntPtr hdc = GetDC(IntPtr.Zero);
        int result = GetDeviceCaps(hdc, LOGPIXELSX);
        ReleaseDC(IntPtr.Zero, hdc);
        return result;
    }
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);

    // ------------------------------------------------------------------------
    // Taskbar detection — C++ FindTaskbarHandle / DetectTaskbarType
    // ------------------------------------------------------------------------
    static bool FindTaskbarHandle()
    {
        taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            taskbar = FindWindow("Shell_SecondaryTrayWnd", null);
        return taskbar != IntPtr.Zero;
    }

    static TaskbarType DetectTaskbarType()
    {
        if (FindWindowEx(taskbar, IntPtr.Zero,
                "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero)
            return TaskbarType.Win11;

        // Wine: ntdll exports wine_get_version
        IntPtr ntdll = GetModuleHandleW("ntdll.dll");
        if (ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero)
            return TaskbarType.Wine;

        return TaskbarType.Classic;
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr module, string proc);

    // ------------------------------------------------------------------------
    // InitTaskbarWnd — C++ MachineIdBar::InitTaskbarWnd
    // ------------------------------------------------------------------------
    static void InitTaskbarWnd()
    {
        if (type == TaskbarType.Win11)
        {
            hNotify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        }
        else if (type == TaskbarType.Classic)
        {
            hBar = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
            if (hBar == IntPtr.Zero) hBar = FindWindowEx(taskbar, IntPtr.Zero, "WorkerW", null);
            if (hBar != IntPtr.Zero)
            {
                hMin = FindWindowEx(hBar, IntPtr.Zero, "MSTaskSwWClass", null);
                if (hMin == IntPtr.Zero) hMin = FindWindowEx(hBar, IntPtr.Zero, "MSTaskListWClass", null);
                if (hMin != IntPtr.Zero)
                {
                    GetWindowRect(hMin, out minOriginalRect);
                }
            }
        }
    }

    static IntPtr GetParentHwnd()
    {
        switch (type)
        {
            case TaskbarType.Classic: return hBar;
            case TaskbarType.Win11: return taskbar;
            default: return IntPtr.Zero; // Wine handled elsewhere
        }
    }

    // ------------------------------------------------------------------------
    // Transparency — C++ MachineIdBar::ApplyTransparency
    // ------------------------------------------------------------------------
    static void ApplyTransparency()
    {
        SetLayeredWindowAttributes(hwnd, colorKey, 255, LWA_COLORKEY);
    }
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    // ------------------------------------------------------------------------
    // Size calculation — C++ MachineIdBar::CalculateWindowSize
    // ------------------------------------------------------------------------
    static void CalculateWindowSize(out int width, out int height)
    {
        dpi = GetDPI();

        if (font == IntPtr.Zero)
        {
            int fontSize = -MulDiv(9, dpi, 72);   // 9 pt, like the C++ version
            int weight = FW_BOLD;
            font = CreateFontW(fontSize, 0, 0, 0, weight, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, "Segoe UI");
        }

        IntPtr hdc = GetDC(hwnd);
        IntPtr oldFont = SelectObject(hdc, font);

        SIZE sz;
        string sample = string.IsNullOrEmpty(displayText) ? "Sample" : displayText;
        GetTextExtentPoint32W(hdc, sample, sample.Length, out sz);

        SelectObject(hdc, oldFont);
        ReleaseDC(hwnd, hdc);

        width = sz.CX + 20;   // padx=10 on each side
        height = sz.CY + 4;   // pady=2 on each side

        // Constrain to taskbar height
        if (taskbar != IntPtr.Zero)
        {
            RECT rc;
            GetWindowRect(taskbar, out rc);
            if (height > rc.H - 2)
                height = rc.H - 2;
        }
    }

    static int MulDiv(int a, int b, int c) { return a * b / c; }

    // ------------------------------------------------------------------------
    // Positioning — C++ MachineIdBar::AdjustWindowPos
    // ------------------------------------------------------------------------
    static void AdjustWindowPos()
    {
        if (taskbar == IntPtr.Zero) return;

        RECT rcTaskbar;
        GetWindowRect(taskbar, out rcTaskbar);
        int wndW, wndH;
        CalculateWindowSize(out wndW, out wndH);
        bool horizontal = rcTaskbar.W >= rcTaskbar.H;

        switch (type)
        {
            case TaskbarType.Classic:
                if (horizontal)
                {
                    int x = 0;
                    int y = (rcTaskbar.H - wndH) / 2;
                    if (hMin != IntPtr.Zero)
                    {
                        RECT rcMin;
                        GetWindowRect(hMin, out rcMin);
                        POINT pt = new POINT { X = rcMin.Left, Y = rcMin.Top };
                        ScreenToClient(hBar, ref pt);

                        int minW = rcMin.W - wndW - 4;
                        if (minW < 10) minW = 10;
                        MoveWindow(hMin, pt.X, pt.Y, minW, rcMin.H, true);
                        x = pt.X + minW + 2;
                    }
                    MoveWindow(hwnd, x, y, wndW, wndH, true);
                }
                else
                {
                    int x = 2, y = 0;
                    if (hMin != IntPtr.Zero)
                    {
                        RECT rcMin;
                        GetWindowRect(hMin, out rcMin);
                        POINT pt = new POINT { X = rcMin.Left, Y = rcMin.Top };
                        ScreenToClient(hBar, ref pt);

                        int minH = rcMin.H - wndH - 4;
                        if (minH < 10) minH = 10;
                        MoveWindow(hMin, pt.X, pt.Y, rcMin.W, minH, true);
                        y = pt.Y + minH + 2;
                    }
                    MoveWindow(hwnd, x, y, wndW, wndH, true);
                }
                break;

            case TaskbarType.Win11:
                {
                    RECT rcNotify = new RECT();
                    if (hNotify != IntPtr.Zero) GetWindowRect(hNotify, out rcNotify);

                    int y = rcTaskbar.Top + (rcTaskbar.H - wndH) / 2;
                    int x;
                    if (!positionLeft)
                    {
                        // right side: just left of the notification area
                        x = rcNotify.Left > 0
                            ? rcNotify.Left - wndW - 8
                            : rcTaskbar.Right - wndW - 8;
                    }
                    else
                    {
                        x = rcTaskbar.Left + 8;
                    }

                    POINT pt = new POINT { X = x, Y = y };
                    ScreenToClient(taskbar, ref pt);
                    MoveWindow(hwnd, pt.X, pt.Y, wndW, wndH, true);
                }
                break;

            case TaskbarType.Wine:
                {
                    IntPtr hdc = GetDC(IntPtr.Zero);
                    int screenW = GetDeviceCaps(hdc, 8);  // HORZRES
                    int screenH = GetDeviceCaps(hdc, 10); // VERTRES
                    ReleaseDC(IntPtr.Zero, hdc);
                    int wx = positionLeft ? 0 : (screenW - wndW - 4);
                    SetWindowPos(hwnd, HWND_TOPMOST, wx, screenH - wndH - 4,
                        wndW, wndH, SWP_NOACTIVATE);
                }
                break;
        }
    }

    // ------------------------------------------------------------------------
    // Restore — C++ MachineIdBar::RestoreTaskbar
    // ------------------------------------------------------------------------
    static void RestoreTaskbar()
    {
        if (type == TaskbarType.Classic && hMin != IntPtr.Zero)
        {
            int w = minOriginalRect.W, h = minOriginalRect.H;
            MoveWindow(hMin, minOriginalRect.Left, minOriginalRect.Top, w, h, true);
        }
    }

    // ------------------------------------------------------------------------
    // ForceVisible — C++ MachineIdBar::ForceVisible
    // ------------------------------------------------------------------------
    static void ForceVisible()
    {
        if (hwnd == IntPtr.Zero) return;
        if (!IsWindowVisible(hwnd))
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    // ------------------------------------------------------------------------
    // Drawing — C++ MachineIdBar::OnPaint (GDI, color-key background)
    // ------------------------------------------------------------------------
    static void OnPaint(IntPtr hdc)
    {
        if (font == IntPtr.Zero)
        {
            int wDummy, hDummy;
            CalculateWindowSize(out wDummy, out hDummy); // force font creation
        }

        RECT rc;
        GetClientRect(hwnd, out rc);

        // Fill with the transparency color key
        IntPtr brush = CreateSolidBrush((int)colorKey);
        FillRect(hdc, ref rc, brush);
        DeleteObject(brush);

        // Draw text
        IntPtr oldFont = SelectObject(hdc, font);
        SetBkMode(hdc, TRANSPARENT_BK);
        SetTextColor(hdc, (int)textColor);

        RECT rcText = rc;
        rcText.Left += 10;  // padx=10
        rcText.Top += 2;    // pady=2
        DrawTextW(hdc, displayText, displayText.Length, ref rcText,
            DT_LEFT | DT_VCENTER | DT_SINGLELINE);

        SelectObject(hdc, oldFont);
    }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    // ------------------------------------------------------------------------
    // Clipboard
    // ------------------------------------------------------------------------
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
    // Embed / re-embed (TaskbarCreated support, like the C++ version)
    // ------------------------------------------------------------------------
    static void Embed()
    {
        FindTaskbarHandle();
        type = DetectTaskbarType();
        InitTaskbarWnd();

        IntPtr parent = type == TaskbarType.Wine ? IntPtr.Zero : GetParentHwnd();
        if (parent != IntPtr.Zero)
            SetParent(hwnd, parent);

        ApplyTransparency();
        AdjustWindowPos();
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    // ------------------------------------------------------------------------
    // Window proc
    // ------------------------------------------------------------------------
    static IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == wmTaskbarCreated)
        {
            // Explorer restarted — re-embed
            Embed();
            return IntPtr.Zero;
        }

        // Never activate this window when the user clicks it — activating would
        // move keyboard focus away from the app the user is working in.
        // MA_NOACTIVATE: don't activate, but still deliver the mouse messages
        // (click-to-copy and drag keep working).
        if (msg == WM_MOUSEACTIVATE)
            return new IntPtr(MA_NOACTIVATE);

        switch (msg)
        {
            case WM_PAINT:
                {
                    PAINTSTRUCT ps = new PAINTSTRUCT();
                    IntPtr hdc = BeginPaint(h, ref ps);
                    OnPaint(hdc);
                    EndPaint(h, ref ps);
                    return IntPtr.Zero;
                }
            case WM_TIMER:
                InvalidateRect(hwnd, IntPtr.Zero, false);
                ForceVisible();
                if (embedded) AdjustWindowPos();
                return IntPtr.Zero;
            case WM_LBUTTONDOWN:
                OnLButtonDown(l);
                return IntPtr.Zero;
            case WM_MOUSEMOVE:
                OnMouseMove(l);
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                OnLButtonUp();
                return IntPtr.Zero;
            case WM_RBUTTONUP:
                if ((GetKeyState(VK_CONTROL) & 0x8000) != 0)
                    DestroyWindow(h);          // Ctrl+right-click -> exit
                else
                    CopyToClipboard(displayText); // C++ opens its tray menu here
                return IntPtr.Zero;
            case WM_DISPLAYCHANGE:
                if (embedded) AdjustWindowPos();
                return IntPtr.Zero;
            case WM_DESTROY:
                KillTimer(h, TIMER_ID);
                RestoreTaskbar();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    // ------------------------------------------------------------------------
    // Mouse interaction — C++ OnLButtonDown / OnMouseMove / OnLButtonUp
    // ------------------------------------------------------------------------
    static int GetXLParam(IntPtr l) { return (short)((long)l & 0xFFFF); }
    static int GetYLParam(IntPtr l) { return (short)(((long)l >> 16) & 0xFFFF); }

    static void OnLButtonDown(IntPtr lp)
    {
        // Check for Ctrl key
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0)
        {
            DestroyWindow(hwnd);   // Ctrl+click -> exit
            return;
        }

        dragging = false;
        dragStartX = GetXLParam(lp);
        dragStartY = GetYLParam(lp);

        // Store current window position
        RECT rc;
        GetWindowRect(hwnd, out rc);
        POINT pt = new POINT { X = rc.Left, Y = rc.Top };
        ScreenToClient(GetParent(hwnd), ref pt);
        winX = pt.X;
        winY = pt.Y;

        SetCapture(hwnd);
    }

    static void OnMouseMove(IntPtr lp)
    {
        if (GetCapture() != hwnd)
            return;

        int x = GetXLParam(lp), y = GetYLParam(lp);

        // Mark as dragging if moved beyond threshold (matching C++'s 3px)
        if (!dragging)
        {
            if (Math.Abs(x - dragStartX) > 3 || Math.Abs(y - dragStartY) > 3)
                dragging = true;
            else
                return;
        }

        // Move window by drag delta
        int wndW, wndH;
        CalculateWindowSize(out wndW, out wndH);
        MoveWindow(hwnd, winX + (x - dragStartX), winY + (y - dragStartY), wndW, wndH, true);
    }

    static void OnLButtonUp()
    {
        if (GetCapture() == hwnd)
            ReleaseCapture();

        // If it was a click (not a drag), copy to clipboard
        if (!dragging)
            CopyToClipboard(displayText);

        dragging = false;
    }

    // ------------------------------------------------------------------------
    // Network helpers (display text)
    // ------------------------------------------------------------------------
    static string GetLocalIP()
    {
        try
        {
            using (var s = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp))
            {
                s.Connect("8.8.8.8", 65530);
                var ep = s.LocalEndPoint as System.Net.IPEndPoint;
                if (ep != null) return ep.Address.ToString();
            }
        }
        catch { }
        return "N/A";
    }

    // ------------------------------------------------------------------------
    // Main
    // ------------------------------------------------------------------------
    static int Main(string[] args)
    {
        // Match the unmanifested C++ exe: force DPI-unaware. (If the host
        // already set awareness this fails harmlessly.)
        try { SetProcessDpiAwareness(0); }
        catch { }

        string label = "TEST";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--label" && i + 1 < args.Length) { label = args[++i]; continue; }
            if (args[i] == "--left") positionLeft = true;
        }

        displayText = label;
        string hostname = Environment.MachineName;
        string ip = GetLocalIP();
        if (!string.IsNullOrEmpty(hostname)) displayText += "  |  " + hostname;
        if (ip != "N/A") displayText += "  |  " + ip;

        // Register window class
        proc = new WndProc(WndProcImpl);
        var wc = new WNDCLASS
        {
            style = CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc = proc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = Marshal.GetHINSTANCE(typeof(TaskBarTestStringV3).Module),
            hIcon = IntPtr.Zero,
            hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
            hbrBackground = IntPtr.Zero,   // we paint our own background
            lpszMenuName = null,
            lpszClassName = CLASS_NAME
        };
        if (RegisterClassW(ref wc) == 0)
        {
            Console.Error.WriteLine("RegisterClassW failed, err=" + Marshal.GetLastWin32Error());
            return 1;
        }

        wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        // Create the window exactly like the C++ version:
        // WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE, WS_POPUP | WS_CHILD
        hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE, CLASS_NAME, WINDOW_TITLE,
            WS_POPUP | WS_CHILD,
            0, 0, 200, 32,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("CreateWindowExW failed, err=" + Marshal.GetLastWin32Error());
            return 1;
        }

        if (!FindTaskbarHandle())
        {
            Console.Error.WriteLine("Shell_TrayWnd not found.");
            return 1;
        }
        type = DetectTaskbarType();
        InitTaskbarWnd();

        // Embed onto the taskbar (SetParent BEFORE ShowWindow, like the C++)
        if (type == TaskbarType.Wine)
        {
            // topmost floating fallback, like the C++ Wine path
        }
        else
        {
            IntPtr parent = GetParentHwnd();
            if (parent != IntPtr.Zero)
                SetParent(hwnd, parent);
        }

        ApplyTransparency();
        AdjustWindowPos();

        SetTimer(hwnd, TIMER_ID, TIMER_INTERVAL, IntPtr.Zero);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        embedded = true;

        Console.WriteLine("DPI=" + dpi +
            "  taskbar mode: " + (type == TaskbarType.Win11 ? "Windows 11" :
                                  type == TaskbarType.Classic ? "Classic" : "Wine"));
        Console.WriteLine("Embedded. hwnd=" + hwnd + "  text=\"" + displayText + "\"");
        Console.WriteLine("Click copies the text, drag moves, Ctrl+click exits.");

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        return 0;
    }

    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr inst, int id);
}
