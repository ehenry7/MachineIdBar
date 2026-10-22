// test_exstyle.cs -- isolate why SetWindowLongPtr appears to be a no-op:
// create a plain popup window, try writing GWL_EXSTYLE via
// SetWindowLongPtr (64-bit) and SetWindowLong (32-bit), read back each time.
using System;
using System.Runtime.InteropServices;

static class TestExStyle
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_POPUP = unchecked((int)0x80000000);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(int exStyle, string cls, string wnd, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("kernel32.dll")] static extern uint GetLastError();
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, int colorKey, byte opacity, uint flags);
    const uint LWA_COLORKEY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS { public int style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    static WndProc proc;

    static System.IO.TextWriter outw;
    static void W(string s) { outw.WriteLine(s); outw.Flush(); }

    static void Main()
    {
        outw = new System.IO.StreamWriter(@"C:\Users\h00569653\Desktop\MachineIdBar\test_exstyle_out.txt", false);
        // NOTE: the test window is created WITHOUT WS_VISIBLE and never
        // ShowWindow'd: fully invisible, no console, no focus, no taskbar.
        proc = new WndProc((pp, mm, ww, ll) => DefWindowProcW(pp, mm, ww, ll));
        var wc = new WNDCLASS { style = 0, lpfnWndProc = proc, cbClsExtra = 0, cbWndExtra = 0, hInstance = IntPtr.Zero, hIcon = IntPtr.Zero, hCursor = IntPtr.Zero, hbrBackground = IntPtr.Zero, lpszMenuName = null, lpszClassName = "TestExStyle" + Guid.NewGuid().ToString("N").Substring(0, 8) };
        if (RegisterClassW(ref wc) == 0) { W("RegisterClassW failed " + GetLastError()); return; }
        IntPtr h = CreateWindowExW(0, wc.lpszClassName, wc.lpszClassName, WS_POPUP, 0, 0, 100, 30, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        W("hwnd=" + h);

        W("init ex (Ptr) = 0x" + ((int)GetWindowLongPtr(h, GWL_EXSTYLE)).ToString("X8"));

        IntPtr old1 = SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(WS_EX_TOOLWINDOW));
        W("SetWindowLongPtr(+TOOLWINDOW) returned old=0x" + ((int)old1).ToString("X8")
            + " now=0x" + ((int)GetWindowLongPtr(h, GWL_EXSTYLE)).ToString("X8")
            + " err=" + GetLastError());

        IntPtr old2 = SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(WS_EX_TOOLWINDOW | WS_EX_LAYERED));
        W("SetWindowLongPtr(+LAYERED)   returned old=0x" + ((int)old2).ToString("X8")
            + " now=0x" + ((int)GetWindowLongPtr(h, GWL_EXSTYLE)).ToString("X8")
            + " err=" + GetLastError());

        int old3 = SetWindowLong(h, GWL_EXSTYLE, 0);
        W("SetWindowLong(clear)        returned old=0x" + old3.ToString("X8")
            + " now=0x" + GetWindowLong(h, GWL_EXSTYLE).ToString("X8")
            + " err=" + GetLastError());

        int old4 = SetWindowLong(h, GWL_EXSTYLE, WS_EX_TOOLWINDOW | WS_EX_LAYERED);
        W("SetWindowLong(+LAYERED)     returned old=0x" + old4.ToString("X8")
            + " now=0x" + GetWindowLong(h, GWL_EXSTYLE).ToString("X8")
            + " err=" + GetLastError());

        // ---- Phase 2: mirror the APP's sequence: create WITH the layered
        // flag, reparent into the real taskbar (window stays invisible: no
        // WS_VISIBLE, never shown), then OR the flag again and call SLWA.
        W("--- phase 2: create-with-layered + reparent into taskbar ---");
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        W("taskbar=" + taskbar);
        IntPtr h2 = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_LAYERED, wc.lpszClassName, wc.lpszClassName, WS_POPUP, 0, 0, 100, 30, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        W("h2=" + h2 + " ex at create=0x" + ((int)GetWindowLongPtr(h2, GWL_EXSTYLE)).ToString("X8"));
        IntPtr oldParent = SetParent(h2, taskbar);
        W("SetParent returned old=" + oldParent);
        IntPtr ex2 = GetWindowLongPtr(h2, GWL_EXSTYLE);
        SetWindowLongPtr(h2, GWL_EXSTYLE, new IntPtr(unchecked((int)ex2 | WS_EX_LAYERED)));
        W("after runtime OR: ex=0x" + ((int)GetWindowLongPtr(h2, GWL_EXSTYLE)).ToString("X8"));
        bool slwa = SetLayeredWindowAttributes(h2, unchecked((int)0x00D47800), 255, LWA_COLORKEY);
        W("SetLayeredWindowAttributes=" + slwa + " err=" + GetLastError());
        outw.Close();
    }
}
