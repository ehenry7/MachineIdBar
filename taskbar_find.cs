// taskbar_find.cs -- reliable full listing of Shell_TrayWnd children via
// EnumChildWindows (z-order, top first): class, title, rect, visible.
using System;
using System.Text;
using System.Runtime.InteropServices;

static class TaskBarFind
{
    delegate bool EnumChildProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static int idx = 0;
    static string filter = "";

    static void Main(string[] args)
    {
        if (args.Length > 0) filter = args[0];
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) { Console.WriteLine("no taskbar"); return; }
        RECT tr; GetWindowRect(taskbar, out tr);
        Console.WriteLine("taskbar rect=" + tr.L + "," + tr.T + " " + tr.W + "x" + tr.H);

        EnumChildWindows(taskbar, (h, l) =>
        {
            var cls = new StringBuilder(256); GetClassName(h, cls, 256);
            var tit = new StringBuilder(128); GetWindowText(h, tit, 128);
            if (filter != "" && !cls.ToString().Contains(filter) && !tit.ToString().Contains(filter))
                return true;
            RECT r; GetWindowRect(h, out r);
            int style = GetWindowLong(h, -16), ex = GetWindowLong(h, -20);
            Console.WriteLine(string.Format("[{0:D2}] hwnd={1} cls='{2}' title='{3}' style=0x{4:X8} ex=0x{5:X8} rect={6},{7} {8}x{9} vis={10}",
                idx++, h, cls, tit, style, ex, r.L, r.T, r.W, r.H, IsWindowVisible(h)));
            return true;
        }, IntPtr.Zero);
        Console.WriteLine("matched: " + idx);
    }
}
