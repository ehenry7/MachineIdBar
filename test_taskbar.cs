using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

class MachineIdBar
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string wnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string wnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, ref RECT rect);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern short GetKeyState(int vkey);
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(int exStyle, string cls, string wnd, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
    [DllImport("kernel32.dll")] static extern int GetLastError();

    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int SW_SHOWNOACTIVATE = 4;
    const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_FRAMECHANGED = 0x0020, SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    const int WM_PAINT = 0x000F, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_DESTROY = 0x0002;
    const int VK_CONTROL = 0x11;
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; public int W() { return Right - Left; } public int H() { return Bottom - Top; } }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate; public int r1, r2, r3, r4, r5, r6, r7, r8; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS { public int style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    static IntPtr hwnd, taskbar, hNotify, hStart;
    static string displayText, bgHex;
    static uint bgColor;
    static WndProc proc;
    static System.Drawing.Font font;
    static int winW, winH;
    static bool embedded = false;

    static string GetLocalIP()
    {
        try { using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) { s.Connect("8.8.8.8", 80); return (s.LocalEndPoint as IPEndPoint).Address.ToString(); } }
        catch { return "N/A"; }
    }

    static void Log(string msg) { System.IO.File.AppendAllText("debug_native.txt", msg + "\r\n"); }

    static IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_PAINT)
        {
            var ps = new PAINTSTRUCT();
            IntPtr hdc = BeginPaint(h, ref ps);
            using (var g = System.Drawing.Graphics.FromHdc(hdc))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromWin32((int)bgColor)), 0, 0, winW, winH);
                var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
                g.DrawString(displayText, font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, winW, winH), sf);
            }
            EndPaint(h, ref ps);
            return IntPtr.Zero;
        }
        if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
        {
            if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) { DestroyWindow(h); }
            else CopyToClipboard(displayText);
            return IntPtr.Zero;
        }
        if (msg == WM_DESTROY) { System.Windows.Forms.Application.Exit(); return IntPtr.Zero; }
        return DefWindowProcW(h, msg, w, l);
    }

    static void CopyToClipboard(string text)
    {
        try
        {
            OpenClipboard(hwnd); EmptyClipboard();
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, bytes.Length);
            IntPtr ptr = GlobalLock(mem);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            GlobalUnlock(mem);
            SetClipboardData(CF_UNICODETEXT, mem); CloseClipboard();
        }
        catch { }
    }

    // Win11 approach (TrafficMonitor Win11TaskbarDlg):
    // Parent = Shell_TrayWnd, find TrayNotifyWnd + Start for positioning
    static void Embed()
    {
        hNotify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        hStart = FindWindowEx(taskbar, IntPtr.Zero, "Start", null);
        Log("hNotify=" + hNotify + " hStart=" + hStart);

        IntPtr result = SetParent(hwnd, taskbar);
        Log("SetParent=" + result + " err=" + GetLastError());
        if (result == IntPtr.Zero) { FallbackPosition(); return; }
        embedded = true;

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        PositionInTaskbar();
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        UpdateWindow(hwnd);
    }

    static void PositionInTaskbar()
    {
        RECT rcTask = new RECT();
        GetWindowRect(taskbar, ref rcTask);
        int taskbarH = rcTask.H();
        int taskbarW = rcTask.W();

        int notifyLeft = 0;
        if (hNotify != IntPtr.Zero)
        {
            RECT rcNotify = new RECT();
            GetWindowRect(hNotify, ref rcNotify);
            notifyLeft = rcNotify.Left;
        }

        int startHeight = taskbarH;
        if (hStart != IntPtr.Zero)
        {
            RECT rcStart = new RECT();
            GetWindowRect(hStart, ref rcStart);
            startHeight = rcStart.H();
        }

        int x;
        if (notifyLeft > 0)
            x = notifyLeft - rcTask.Left - winW - 2;
        else
            x = taskbarW - winW - 90;
        if (x < 2) x = 2;

        int y = (startHeight - winH) / 2 + (taskbarH - startHeight);
        if (y < 0) y = 0;

        Log("Pos: x=" + x + " y=" + y + " tw=" + taskbarW + " th=" + taskbarH + " notify=" + notifyLeft + " sh=" + startHeight);
        MoveWindow(hwnd, x, y, winW, winH, true);

        RECT rcUs = new RECT();
        GetWindowRect(hwnd, ref rcUs);
        Log("Rect: L=" + rcUs.Left + " T=" + rcUs.Top + " R=" + rcUs.Right + " B=" + rcUs.Bottom);
    }

    static void MaintainPosition()
    {
        if (!embedded) { FallbackPosition(); return; }
        PositionInTaskbar();
        BringWindowToTop(hwnd);
        if (!IsWindowVisible(hwnd)) ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    static void FallbackPosition()
    {
        RECT rcTask = new RECT();
        GetWindowRect(taskbar, ref rcTask);
        int taskbarH = rcTask.H();
        int y = rcTask.Bottom - taskbarH + (taskbarH - winH) / 2;
        SetWindowPos(hwnd, HWND_NOTOPMOST, 8, y, winW, winH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(hwnd, HWND_TOPMOST, 8, y, winW, winH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    static void Main(string[] args)
    {
        try { SetProcessDpiAwareness(2); } catch { }

        string label = "Secure PC";
        bgHex = "#0078d4";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--label") label = args[i + 1];
            if (args[i] == "--bg") bgHex = args[i + 1];
        }

        displayText = label + "  |  " + System.Net.Dns.GetHostName() + "  |  " + GetLocalIP();
        var c = System.Drawing.ColorTranslator.FromHtml(bgHex);
        bgColor = (uint)((c.B << 16) | (c.G << 8) | c.R);
        font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);

        using (var bmp = new System.Drawing.Bitmap(1, 1))
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        { var sz = g.MeasureString(displayText, font); winW = (int)sz.Width + 20; winH = (int)sz.Height + 4; }

        System.IO.File.Delete("debug_native.txt");

        taskbar = FindWindow("Shell_TrayWnd", null);
        Log("taskbar=" + taskbar);
        if (taskbar == IntPtr.Zero) return;

        string className = "MIdBar_" + Guid.NewGuid().ToString("N");
        proc = new WndProc(WndProcImpl);
        var wc = new WNDCLASS { lpfnWndProc = proc, lpszClassName = className, hbrBackground = CreateSolidBrush(bgColor) };
        if (RegisterClassW(ref wc) == 0) return;

        hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, className, className,
            WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, winW, winH, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Log("hwnd=" + hwnd + " err=" + GetLastError());
        if (hwnd == IntPtr.Zero) return;

        Embed();

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (s, e) => { try { MaintainPosition(); } catch { } };
        timer.Start();

        System.Windows.Forms.Application.Run();
    }
}
