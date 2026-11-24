using System.Drawing;
using System.Drawing.Drawing2D;

namespace mk.dev.runner.desktop
{
    internal static class Icons
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr h);

        public static Icon Make(Color? status, int size = 32)
        {
            using var stream = typeof(Icons).Assembly.GetManifestResourceStream("app.ico")!;
            using var source = new Icon(stream, size, size);
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var art = source.ToBitmap())
                    g.DrawImage(art, 0, 0, size, size);
                float s = size;
                if (status is { } dotColor)
                {
                    var d = s * 0.36f;
                    using (var ring = new SolidBrush(Color.FromArgb(0x18, 0x18, 0x1b)))
                        g.FillEllipse(ring, s - d - 0.5f, s - d - 0.5f, d, d);
                    using (var dot = new SolidBrush(dotColor))
                        g.FillEllipse(dot, s - d + s * 0.05f, s - d + s * 0.05f, d - s * 0.12f, d - s * 0.12f);
                }
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
            using (var bg = new SolidBrush(Color.FromArgb(0xf9, 0x73, 0x16)))
                g.FillRectangle(bg, band);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI", s * 0.3f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("DEV", font, Brushes.White, band, format);
        }

        public static System.Windows.Media.ImageSource ToImageSource(Icon icon)
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
    }
}
