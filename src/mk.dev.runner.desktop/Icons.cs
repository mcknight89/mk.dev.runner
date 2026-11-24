using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using mk.dev.runner.desktop.ViewModels;
using Application = System.Windows.Application;

namespace mk.dev.runner.desktop
{
    internal static class Icons
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr h);

        public static Icon Make(Color status, int size = 32)
        {
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size;
                using var tile = RoundRect(new RectangleF(1, 1, s - 2, s - 2), s * 0.22f);
                using (var bg = new SolidBrush(Color.FromArgb(0x1e, 0x3a, 0x5f))) g.FillPath(bg, tile);
                using (var edge = new Pen(Color.FromArgb(0x60, 0xa5, 0xfa), s / 16)) g.DrawPath(edge, tile);
                var tri = new[] { new PointF(s * 0.32f, s * 0.24f), new PointF(s * 0.32f, s * 0.76f), new PointF(s * 0.74f, s * 0.5f) };
                using (var fg = new SolidBrush(Color.FromArgb(0xe4, 0xe4, 0xe7))) g.FillPolygon(fg, tri);
                float d = s * 0.36f;
                using (var ring = new SolidBrush(Color.FromArgb(0x18, 0x18, 0x1b))) g.FillEllipse(ring, s - d - 0.5f, s - d - 0.5f, d, d);
                using (var dot = new SolidBrush(status)) g.FillEllipse(dot, s - d + s * 0.05f, s - d + s * 0.05f, d - s * 0.12f, d - s * 0.12f);
                if (App.IsDev)
                    StampDev(g, s);
            }
            var h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }

        private static void StampDev(Graphics g, float s)
        {
            var band = new RectangleF(0, 0, s, s * 0.42f);
            using (var bg = new SolidBrush(Color.FromArgb(0xf9, 0x73, 0x16))) g.FillRectangle(bg, band);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI", s * 0.3f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("DEV", font, Brushes.White, band, format);
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static System.Windows.Media.ImageSource ToImageSource(Icon icon)
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
    }
}
