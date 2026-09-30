using System.Windows;
using System.Windows.Media;

namespace AgrupaJanela.Setup;

/// <summary>
/// O mesmo desenho do AppIcon do app, em vetor: quatro quadrados arredondados (gap = tamanho/16,
/// raio = tamanho/10), o superior esquerdo em #4C8DFF e os demais em #9A9EA6.
/// </summary>
internal static class SetupLogo
{
    public static DrawingImage Create()
    {
        const double size = 32;
        const double gap = size / 16;
        const double cell = (size - gap * 3) / 2;
        const double radius = size / 10;
        var accent = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
        var dim = new SolidColorBrush(Color.FromRgb(0x9A, 0x9E, 0xA6));
        accent.Freeze();
        dim.Freeze();

        var group = new DrawingGroup();
        // Fundo transparente do tamanho total, para manter as margens do desenho original.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
        for (var r = 0; r < 2; r++)
        for (var c = 0; c < 2; c++)
        {
            var rect = new Rect(gap + c * (cell + gap), gap + r * (cell + gap), cell, cell);
            group.Children.Add(new GeometryDrawing(r == 0 && c == 0 ? accent : dim, null, new RectangleGeometry(rect, radius, radius)));
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
