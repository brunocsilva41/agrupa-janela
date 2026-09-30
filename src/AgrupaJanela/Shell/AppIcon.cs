using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgrupaJanela.Shell;

/// <summary>Ícone desenhado em código: quatro painéis, um deles em destaque.</summary>
public static class AppIcon
{
    private static Icon? _icon;
    private static ImageSource? _image;

    public static Icon Icon => _icon ??= Create(32);

    public static ImageSource Image => _image ??= Imaging.CreateBitmapSourceFromHIcon(Icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private static Icon Create(int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            var gap = size / 16f;
            var cell = (size - gap * 3) / 2;
            using var dim = new SolidBrush(System.Drawing.Color.FromArgb(0x9A, 0x9E, 0xA6));
            using var accent = new SolidBrush(System.Drawing.Color.FromArgb(0x4C, 0x8D, 0xFF));
            for (var r = 0; r < 2; r++)
            for (var c = 0; c < 2; c++)
            {
                var rect = new RectangleF(gap + c * (cell + gap), gap + r * (cell + gap), cell, cell);
                using var path = Rounded(rect, size / 10f);
                g.FillPath(r == 0 && c == 0 ? accent : dim, path);
            }
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
