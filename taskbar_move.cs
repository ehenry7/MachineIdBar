// taskbar_move.cs -- verification utility:
//   "peek"    : temporarily moves the embedded TaskBarTest_ window to the middle
//               of the screen (parent-relative, like the app's fallback mode),
//               screenshots the real screen pixels, then restores it.
// Works even when the taskbar region is occluded by other windows.
using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;

static class TaskBarMove
{
    delegate bool EnumChildProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }

    static IntPtr found;

    static IntPtr FindTestWindow()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        found = IntPtr.Zero;
        EnumChildWindows(taskbar, (h, l) =>
        {
            var sb = new StringBuilder(256); GetClassName(h, sb, 256);
            if (sb.ToString().StartsWith("TaskBarTest_")) found = h;
            return true;
        }, IntPtr.Zero);
        return taskbar;
    }

    static void Main(string[] args)
    {
        IntPtr taskbar = FindTestWindow();
        if (taskbar == IntPtr.Zero) { Console.WriteLine("no taskbar"); return; }
        if (found == IntPtr.Zero) { Console.WriteLine("TaskBarTest_ window not found (app not running?)"); return; }

        RECT tr, wr;
        GetWindowRect(taskbar, out tr);
        GetWindowRect(found, out wr);
        int w = wr.W, h = wr.H;

        if (args.Length > 0 && args[0] == "peek")
        {
            // report foreground window for diagnosis
            IntPtr fg = GetForegroundWindow();
            var fgsb = new StringBuilder(128); GetClassName(fg, fgsb, 128);
            RECT fgr; GetWindowRect(fg, out fgr);
            Console.WriteLine("foreground=" + fg + " (" + fgsb + ") rect=" + fgr.L + "," + fgr.T + "," + fgr.R + "," + fgr.B);

            // move to parent-relative (300, -880) => screen (300, tr.T - 880),
            // and force topmost so it renders above any covering window
            int screenX = 300, screenY = Math.Max(100, tr.T - 880);
            bool moved = MoveWindow(found, screenX - tr.L, screenY - tr.T, w, h, true);
            SetWindowPos(found, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002); // HWND_TOPMOST, NOSIZE|NOMOVE
            System.Threading.Thread.Sleep(600);
            RECT wr2; GetWindowRect(found, out wr2);
            Console.WriteLine("MoveWindow=" + moved + " actual rect now=" + wr2.L + "," + wr2.T + " " + (wr2.R - wr2.L) + "x" + (wr2.B - wr2.T));
            using (var bmp = new Bitmap(w + 20, h + 20, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(screenX - 10, screenY - 10, 0, 0, new Size(w + 20, h + 20));
                bmp.Save(@"C:\Users\h00569653\Desktop\MachineIdBar\taskbar_peek.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("peek saved at screen (" + screenX + "," + screenY + ") size " + w + "x" + h);
            // restore to original parent-relative position and drop topmost
            MoveWindow(found, wr.L - tr.L, wr.T - tr.T, w, h, true);
            SetWindowPos(found, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002); // HWND_NOTOPMOST
            Console.WriteLine("restored to " + wr.L + "," + wr.T);
        }
        else
        {
            Console.WriteLine("window hwnd=" + found + " rect=" + wr.L + "," + wr.T + " " + w + "x" + h + " taskbar=" + tr.L + "," + tr.T);
        }
    }
}
