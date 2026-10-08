// Renders the tray icons to a PNG sheet (light and dark taskbar backgrounds, several sizes),
// so they can be looked at without Windows. Used by the author; not needed to build the tray.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DualConnectTray
{
    internal static class RenderIconsProgram
    {
        private static int Main(string[] args)
        {
            string[] keys = { "on", "on-not-default", "off", "busy", "problem" };
            int[] sizes = { 16, 20, 24, 32 };
            Color[] backgrounds = { Color.FromArgb(243, 243, 243), Color.FromArgb(32, 32, 32) };
            const int zoom = 4, gap = 8;
            int cell = 32 * zoom + gap;
            using (var sheet = new Bitmap(keys.Length * cell + gap, sizes.Length * backgrounds.Length * cell + gap))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                int row = 0;
                foreach (Color bg in backgrounds)
                {
                    foreach (int size in sizes)
                    {
                        for (int k = 0; k < keys.Length; k++)
                        {
                            var rect = new Rectangle(gap + k * cell, gap + row * cell, 32 * zoom, 32 * zoom);
                            using (var b = new SolidBrush(bg)) g.FillRectangle(b, rect);
                            using (Bitmap icon = TrayIcons.Render(keys[k], size))
                                g.DrawImage(icon, new Rectangle(rect.X, rect.Y, size * zoom, size * zoom));
                        }
                        row++;
                    }
                }
                sheet.Save(args[0], ImageFormat.Png);
            }
            return 0;
        }
    }
}
