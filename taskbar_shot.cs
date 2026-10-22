// taskbar_shot.cs -- utility: find the embedded TaskBarTest_ window (child of
// Shell_TrayWnd) and PrintWindow-capture it to a PNG for visual verification.
// Works even when the taskbar region is occluded by other windows.
using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;

static class TaskBarShot
{
    delegate bool EnumChildProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static IntPtr found;

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : @"C:\Users\h00569653\Desktop\MachineIdBar\taskbar_shot.png";
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) { Console.WriteLine("no taskbar"); return; }
        RECT tr; GetWindowRect(taskbar, out tr);

        EnumChildWindows(taskbar, (h, l) =>
        {
            var sb = new StringBuilder(256); GetClassName(h, sb, 256);
            if (sb.ToString().StartsWith("TaskBarTest_")) found = h;
            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero) { Console.WriteLine("TaskBarTest_ window NOT found among Shell_TrayWnd children"); return; }

        RECT r; GetWindowRect(found, out r);
        Console.WriteLine("window hwnd=" + found + " visible=" + IsWindowVisible(found)
            + " rect=" + r.L + "," + r.T + " " + r.W + "x" + r.H
            + " taskbar=" + tr.L + "," + tr.T + " " + tr.W + "x" + tr.H
            + (r.T >= tr.T && r.B <= tr.B && r.L >= tr.L && r.R <= tr.R ? "  [inside taskbar bounds]" : "  [OUTSIDE taskbar bounds]"));

        int w = Math.Max(r.W, 8), hh = Math.Max(r.H, 8);
        using (var bmp = new Bitmap(w, hh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                bool ok = PrintWindow(found, g.GetHdc(), 2 /* PW_RENDERFULLCONTENT */);
                Console.WriteLine("PrintWindow result=" + ok);
            }
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("saved " + outPath);
    }
}
