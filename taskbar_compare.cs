// taskbar_compare.cs
// Side-by-side comparison of the C# TaskBarTestString window and the C++
// TaskbarOverlayClass window: geometry, styles, DWM cloak state, and a
// PrintWindow capture of each. Both run while the other is embedded, so
// we can diff what a visible (C++) window looks like vs the invisible one.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

static class TaskBarCompare
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string wnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll", EntryPoint = "GetParent")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const int DWMWA_CLOAKED = 14, DWMWA_CLOAKED_REASONS = 15;
    const uint PW_CLIENTONLY = 1;

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static void Describe(string label, IntPtr h, string outPng)
    {
        if (h == IntPtr.Zero) { Console.WriteLine(label + ": NOT FOUND"); return; }
        RECT r; GetWindowRect(h, out r);
        int style = GetWindowLong(h, GWL_STYLE);
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        int cloaked = 0, reasons = 0;
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int));
        DwmGetWindowAttribute(h, DWMWA_CLOAKED_REASONS, out reasons, sizeof(int));
        IntPtr parent = GetParent(h);
        Console.WriteLine(label + ": hwnd=" + h
            + " rect=" + r.L + "," + r.T + " " + r.W + "x" + r.H
            + " style=0x" + style.ToString("X8") + " ex=0x" + ex.ToString("X8")
            + " parent=" + parent + " vis=" + IsWindowVisible(h)
            + " cloaked=" + cloaked + " cloakReasons=0x" + reasons.ToString("X"));
        // PrintWindow capture
        using (var bmp = new Bitmap(r.W, r.H, PixelFormat.Format32bppArgb))
        {
            bool ok;
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try { ok = PrintWindow(h, hdc, PW_CLIENTONLY); }
                finally { g.ReleaseHdc(hdc); }
            }
            int c1 = bmp.GetPixel(1, 1).ToArgb();
            int c2 = bmp.GetPixel(r.W / 2, r.H / 2).ToArgb();
            Console.WriteLine(label + ": PrintWindow=" + ok
                + " corner=" + (c1 & 0xFF) + "," + ((c1 >> 8) & 0xFF) + "," + ((c1 >> 16) & 0xFF)
                + " center=" + (c2 & 0xFF) + "," + ((c2 >> 8) & 0xFF) + "," + ((c2 >> 16) & 0xFF));
            try { bmp.Save(outPng, ImageFormat.Png); Console.WriteLine(label + ": saved " + outPng); } catch (Exception e) { Console.WriteLine(label + ": save failed " + e.Message); }
        }
    }

    static IntPtr FindChildByClassPrefix(IntPtr parent, string prefix)
    {
        IntPtr h = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (h != IntPtr.Zero)
        {
            IntPtr next = FindWindowEx(parent, h, null, null);
            int len = 512;
            var sb = new System.Text.StringBuilder(len);
            // GetClassName via user32
            int n = GetClassName(h, sb, len);
            if (n > 0 && sb.ToString().StartsWith(prefix)) return h;
            h = next;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

    static void Main()
    {
        string root = @"C:\Users\h00569653\Desktop\MachineIdBar\";
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Console.WriteLine("taskbar=" + taskbar);
        IntPtr mine = FindChildByClassPrefix(taskbar, "TaskBarTest_");
        IntPtr cpp = FindWindowEx(taskbar, IntPtr.Zero, "TaskbarOverlayClass", null);
        Describe("C# mine   ", mine, root + "compare_mine.png");
        Describe("C++ theirs", cpp, root + "compare_cpp.png");
    }
}
