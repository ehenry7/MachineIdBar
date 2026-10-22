// taskbar_check.cs -- utility: verifies the TaskBarTestString window geometry
// (is it a child of Shell_TrayWnd, is its screen rect inside the taskbar?)
using System;
using System.Text;
using System.Runtime.InteropServices;

static class TaskBarCheck
{
    delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static void Main()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        RECT tr; GetWindowRect(taskbar, out tr);
        Console.WriteLine("taskbar=" + taskbar + " rect=" + tr.L + "," + tr.T + " " + tr.W + "x" + tr.H);

        IntPtr notify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        IntPtr start = FindWindowEx(taskbar, IntPtr.Zero, "Start", null);
        RECT nr = new RECT(), sr = new RECT();
        if (notify != IntPtr.Zero) GetWindowRect(notify, out nr);
        if (start != IntPtr.Zero) GetWindowRect(start, out sr);
        Console.WriteLine("notify=" + notify + (notify != IntPtr.Zero ? " screenLeft=" + nr.L : ""));
        Console.WriteLine("start=" + start + (start != IntPtr.Zero ? " screenLeft=" + sr.L + " H=" + sr.H : ""));

        IntPtr bridge = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
        Console.WriteLine("win11bridge=" + bridge);

        EnumWindows((h, l) =>
        {
            var sb = new StringBuilder(256); GetClassName(h, sb, 256);
            if (sb.ToString().StartsWith("TaskBarTest_"))
            {
                RECT r; GetWindowRect(h, out r);
                IntPtr p = GetParent(h);
                var pcls = new StringBuilder(64); GetClassName(p, pcls, 64);
                Console.WriteLine("FOUND hwnd=" + h + " visible=" + IsWindowVisible(h)
                    + " rect=" + r.L + "," + r.T + " " + r.W + "x" + r.H
                    + " parent=" + p + " (" + pcls + ")"
                    + (p == taskbar ? "  [parent IS Shell_TrayWnd]" : ""));
                if (p == taskbar && r.L >= tr.L && r.R <= tr.R && r.T >= tr.T && r.B <= tr.B)
                    Console.WriteLine("RESULT: inside taskbar bounds - embedded correctly");
                else if (p == taskbar)
                    Console.WriteLine("RESULT: reparented OK but rect outside taskbar bounds");
                else
                    Console.WriteLine("RESULT: not reparented (fallback mode)");
            }
            return true;
        }, IntPtr.Zero);
    }
}
