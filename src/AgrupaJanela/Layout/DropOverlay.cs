using AgrupaJanela.Native;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AgrupaJanela.Layout;

/// <summary>
/// Janela transparente, sem foco e que deixa o clique passar, desenhada por cima de um painel
/// para indicar onde algo vai cair. Precisa ser uma janela própria porque o WPF não
/// consegue desenhar por cima dos HWNDs incorporados ("airspace").
/// </summary>
public sealed class DropOverlay : Window
{
    private readonly Border _zone;
    private readonly TextBlock _label;
    private nint _hwnd;

    public DropOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;

        var accent = (SolidColorBrush)Application.Current.Resources["Accent"];
        _label = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _zone = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderBrush = accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Margin = new Thickness(4),
            Child = new Border { Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1B, 0x1C, 0x1F)), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _label },
        };
        Content = _zone;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (nint)(ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
        };
    }

    /// <summary>Mostra a zona sobre um retângulo de tela em pixels físicos.</summary>
    public void ShowZone(Int32Rect pane, DropZone zone, string label)
    {
        if (zone == DropZone.None) { HideZone(); return; }
        var (x, y, w, h) = zone switch
        {
            DropZone.Left => (pane.X, pane.Y, pane.Width / 2, pane.Height),
            DropZone.Right => (pane.X + pane.Width / 2, pane.Y, pane.Width - pane.Width / 2, pane.Height),
            DropZone.Top => (pane.X, pane.Y, pane.Width, pane.Height / 2),
            DropZone.Bottom => (pane.X, pane.Y + pane.Height / 2, pane.Width, pane.Height - pane.Height / 2),
            _ => (pane.X, pane.Y, pane.Width, pane.Height),
        };
        _label.Text = label;
        if (!IsVisible) Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, Math.Max(1, w), Math.Max(1, h), Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    public void HideZone()
    {
        if (IsVisible) Hide();
    }

    /// <summary>Zona conforme a posição relativa do cursor dentro do painel.</summary>
    public static DropZone ZoneFor(Int32Rect pane, int cursorX, int cursorY)
    {
        if (pane.Width <= 0 || pane.Height <= 0) return DropZone.None;
        var fx = (cursorX - pane.X) / (double)pane.Width;
        var fy = (cursorY - pane.Y) / (double)pane.Height;
        if (fx < 0 || fx > 1 || fy < 0 || fy > 1) return DropZone.None;
        if (fx is > 0.3 and < 0.7 && fy is > 0.3 and < 0.7) return DropZone.Center;
        var distances = new[] { (fx, DropZone.Left), (1 - fx, DropZone.Right), (fy, DropZone.Top), (1 - fy, DropZone.Bottom) };
        return distances.MinBy(d => d.Item1).Item2;
    }

    public static string Describe(DropZone zone, bool swap) => zone switch
    {
        DropZone.Left => "Soltar à esquerda",
        DropZone.Right => "Soltar à direita",
        DropZone.Top => "Soltar acima",
        DropZone.Bottom => "Soltar abaixo",
        DropZone.Center => swap ? "Trocar de lugar" : "Colocar aqui",
        _ => "",
    };
}
