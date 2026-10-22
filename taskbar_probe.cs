// taskbar_probe.cs -- for a given taskbar-embedded window (by class prefix):
//   1. dumps its style/exstyle/rect + DWM cloak
//   2. walks the Z-order listing every window ABOVE it (what could cover it)
//   3. PrintWindow-captures its content to a PNG
using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;

static class TaskBarProbe
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, uint attr, out int value, int size);

    const uint GW_HWNDFIRST = 0, GW_HWNDNEXT = 2;
    const uint DWMWA_CLOAKED = 14, DWMWA_CLOAK = 13;

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }

    static void Main(string[] args)
    {
        string prefix = args.Length > 0 ? args[0] : "MIdBar_";
        string outPng = args.Length > 1 ? args[1] : @"C:\Users\h00569653\Desktop\MachineIdBar\probe_" + prefix + ".png";

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        RECT tr; GetWindowRect(taskbar, out tr);

        IntPtr target = IntPtr.Zero;
        IntPtr child = FindWindowEx(taskbar, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            if (Cls(child).StartsWith(prefix)) { target = child; break; }
            child = FindWindowEx(taskbar, child, null, null);
        }
        if (target == IntPtr.Zero) { Console.WriteLine("window with prefix '" + prefix + "' not found"); return; }

        RECT r; GetWindowRect(target, out r);
        int cloak = 0; DwmGetWindowAttribute(target, DWMWA_CLOAKED, out cloak, 4);
        Console.WriteLine("TARGET hwnd=" + target + " cls=" + Cls(target)
            + " style=0x" + GetWindowLong(target, -16).ToString("X8")
            + " ex=0x" + GetWindowLong(target, -20).ToString("X8")
            + " rect=" + r.L + "," + r.T + " " + r.W + "x" + r.H
            + " vis=" + IsWindowVisible(target) + " cloaked=" + cloak);

        // walk z-order from top until we reach the target; everything listed is ABOVE it
        int count = 0;
        IntPtr h = GetWindow(taskbar, GW_HWNDFIRST);
        Console.WriteLine("=== windows ABOVE target (top first) ===");
        while (h != IntPtr.Zero && h != target && count < 2000)
        {
            if (IsWindowVisible(h))
            {
                RECT cr; GetWindowRect(h, out cr);
                Console.WriteLine(string.Format("[{0:D3}] cls='{1}' rect={2},{3} {4}x{5}", count, Cls(h), cr.L, cr.T, cr.W, cr.H));
            }
            h = GetWindow(h, GW_HWNDNEXT);
            count++;
        }
        Console.WriteLine("walked " + count + " siblings before reaching target; above-count=" + (h == target ? count : -1));

        // PrintWindow capture
        using (var bmp = new Bitmap(Math.Max(r.W, 8), Math.Max(r.H, 8), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
                Console.WriteLine("PrintWindow=" + PrintWindow(target, g.GetHdc(), 2));
            Color c1 = bmp.GetPixel(3, 3), c2 = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            Console.WriteLine("pixels corner=" + c1.R + "," + c1.G + "," + c1.B + " center=" + c2.R + "," + c2.G + "," + c2.B);
            bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("saved " + outPng);
    }
}
