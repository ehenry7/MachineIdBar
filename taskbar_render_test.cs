// taskbar_render_test.cs -- self-test: reproduce TaskBarTestString's DrawContent
// into a Bitmap and save a PNG, to verify the render (bg color + centered text)
// independent of the taskbar host / PrintWindow quirks.
using System;
using System.Drawing;
using System.Drawing.Text;

static class RenderTest
{
    const uint bgColor = 0x00D47800; // COLORREF of #0078D4 (B=0xD4,G=0x78,R=0x00)
    static string displayText = "TEST  |  " + System.Net.Dns.GetHostName();

    static void Main()
    {
        Font font = new Font("Segoe UI", 9, FontStyle.Bold);
        int winW = 244, winH = 38; // same as the embedded window reported
        using (var bmp = new Bitmap(winW, winH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.FillRectangle(new SolidBrush(ColorTranslator.FromWin32((int)bgColor)), 0, 0, winW, winH);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(displayText, font, Brushes.White, new RectangleF(0, 0, winW, winH), sf);
            }
            Color corner = bmp.GetPixel(2, 2);
            Console.WriteLine("bg pixel(2,2) = RGB(" + corner.R + "," + corner.G + "," + corner.B + ")  (expect 0,120,212)");
            Color center = bmp.GetPixel(winW / 2, winH / 2);
            Console.WriteLine("center pixel  = RGB(" + center.R + "," + center.G + "," + center.B + ")  (expect near-white text or blue)");
            bmp.Save(@"C:\Users\h00569653\Desktop\MachineIdBar\taskbar_render_test.png", System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("saved taskbar_render_test.png  text='" + displayText + "'");
    }
}
