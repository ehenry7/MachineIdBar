// taskbar_diag.cs -- dumps ALL children of Shell_TrayWnd in Z-ORDER (top->bottom),
// with class, title, styles, exstyles, rect, visibility and DWM cloak state.
// Used to find out why embedded taskbar windows are invisible on Windows 11.
using System;
using System.Text;
using System.Runtime.InteropServices;

static class TaskBarDiag
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, uint attr, out int value, int size);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);

    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const uint GW_HWNDFIRST = 0, GW_HWNDNEXT = 2, GW_HWNDTOP = 6;
    const uint DWMWA_CLOAKED = 14;

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static string Name(IntPtr h)
    {
        var sb = new StringBuilder(256); GetClassName(h, sb, 256);
        return sb.ToString();
    }
    static string Title(IntPtr h)
    {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        return sb.ToString();
    }

    static void Dump(IntPtr h, int idx)
    {
        if (h == IntPtr.Zero) return;
        var cls = Name(h);
        if (cls.StartsWith("TaskBarTest_") || cls.StartsWith("MIdBar_") || cls.Contains("MachineId") || Title(h).Contains("TrafficMonitor") || Title(h).Contains("TaskBar"))
        {
            RECT rr; GetWindowRect(h, out rr);
            int cloaked = 0; DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int));
            Console.WriteLine(string.Format("[{2:D2}] hwnd={0} class='{1}' title='{3}' style=0x{4:X8} ex=0x{5:X8} rect={6},{7} {8}x{9} vis={10} cloaked={11}",
                h, cls, idx, Title(h),
                GetWindowLong(h, GWL_STYLE), GetWindowLong(h, GWL_EXSTYLE),
                rr.L, rr.T, rr.W, rr.H, IsWindowVisible(h), cloaked));
            return;
        }
        RECT r; GetWindowRect(h, out r);
        Console.WriteLine(string.Format("[{2:D2}] hwnd={0} class='{1}' title='{3}' style=0x{4:X8} ex=0x{5:X8} rect={6},{7} {8}x{9} vis={10}",
            h, cls, idx, Title(h),
            GetWindowLong(h, GWL_STYLE), GetWindowLong(h, GWL_EXSTYLE),
            r.L, r.T, r.W, r.H, IsWindowVisible(h)));
    }

    static void Main(string[] args)
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) { Console.WriteLine("no taskbar"); return; }
        RECT tr; GetWindowRect(taskbar, out tr);
        Console.WriteLine("=== Shell_TrayWnd " + taskbar + " rect=" + tr.L + "," + tr.T + " " + tr.W + "x" + tr.H + " ===");
        Console.WriteLine("(z-order: index 0 = top-most child)");

        int idx = 0;
        IntPtr h = GetWindow(taskbar, GW_HWNDFIRST);
        while (h != IntPtr.Zero)
        {
            Dump(h, idx++);
            h = GetWindow(h, GW_HWNDNEXT);
        }
        Console.WriteLine("total children: " + idx);

        // If a --raise <hwnd> argument is given, bring that window to the top
        if (args.Length >= 2 && args[0] == "--raise")
        {
            IntPtr target = new IntPtr(long.Parse(args[1]));
            bool ok = BringWindowToTop(target);
            Console.WriteLine("BringWindowToTop(" + target + ") = " + ok);
        }
    }
}
