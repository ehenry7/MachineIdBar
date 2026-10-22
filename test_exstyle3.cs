// test_exstyle3.cs -- bisect why the exstyle write no-ops in the app:
// All windows are created INVISIBLE (no WS_VISIBLE, never shown) and
// reparented into the taskbar; nothing is ever visible on screen.
//
//   A: DPI-unaware,            SetParent,          write 0x80->0x80080
//   B: DPI-aware (process),    SetParent,          write
//   C: DPI-aware (process),    SetParent + BWT,    write
// Each variant logs readback before/after the write.
using System;
using System.Runtime.InteropServices;

static class TestExStyle3
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_POPUP = unchecked((int)0x80000000);
    const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(int exStyle, string cls, string wnd, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
    [DllImport("kernel32.dll")] static extern uint GetLastError();
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS { public int style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    static WndProc proc;
    static System.IO.TextWriter o;
    static void W(string s) { o.WriteLine(s); o.Flush(); }

    static void Variant(string tag, IntPtr taskbar)
    {
        string cls = "E3_" + tag + Guid.NewGuid().ToString("N").Substring(0, 8);
        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = proc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = IntPtr.Zero,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = cls
        };
        RegisterClassW(ref wc);
        IntPtr hw = CreateWindowExW(WS_EX_TOOLWINDOW, cls, cls, WS_POPUP, 0, 0, 100, 30,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        W(tag + ": hwnd=" + hw + " err=" + GetLastError()
            + " ex(create)=0x" + ((int)GetWindowLongPtr(hw, GWL_EXSTYLE)).ToString("X8"));
        SetParent(hw, taskbar);
        if (tag == "C") BringWindowToTop(hw);
        IntPtr before = GetWindowLongPtr(hw, GWL_EXSTYLE);
        SetWindowLongPtr(hw, GWL_EXSTYLE, new IntPtr(unchecked((int)before | WS_EX_LAYERED)));
        IntPtr after = GetWindowLongPtr(hw, GWL_EXSTYLE);
        W(tag + ": write 0x" + before.ToInt32().ToString("X8") + " -> want 0x80080 : after=0x"
            + after.ToInt32().ToString("X8")
            + (after.ToInt32() == unchecked((int)before | WS_EX_LAYERED) ? "  [WRITE OK]" : "  [WRITE LOST]"));
    }

    static void Main()
    {
        o = new System.IO.StreamWriter(@"C:\Users\h00569653\Desktop\MachineIdBar\test_exstyle3_out.txt", false);
        proc = new WndProc((pp, mm, ww, ll) => DefWindowProcW(pp, mm, ww, ll));
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        W("taskbar=" + taskbar);
        Variant("A", taskbar);
        int dpi = SetProcessDpiAwareness(2);
        W("SetProcessDpiAwareness(2) returned " + dpi + " err=" + GetLastError());
        Variant("B", taskbar);
        Variant("C", taskbar);
        o.Close();
    }
}
