using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgrupaJanela.Shell;

/// <summary>
/// Ícone do app: o mesmo SplitDeck.ico (16/32/48/256 px) que vai embutido no executável — barra de tarefas,
/// Menu Iniciar, título das janelas e bandeja mostram exatamente o mesmo desenho, nítido em qualquer escala.
/// O .ico é gerado por build/make-icon.ps1.
/// </summary>
public static class AppIcon
{
    private static readonly Uri IconUri = new("pack://application:,,,/Assets/SplitDeck.ico", UriKind.Absolute);
    private static Icon? _trayIcon;
    private static ImageSource? _image;

    /// <summary>Ícone da bandeja, no tamanho pequeno do sistema (respeita a escala de DPI).</summary>
    public static Icon Icon => _trayIcon ??= LoadTrayIcon();

    /// <summary>Ícone das janelas (o WPF escolhe o tamanho certo dentro do .ico). Null se faltar: janela sem ícone, nunca um app que não abre.</summary>
    public static ImageSource? Image => _image ??= LoadImage();

    private static ImageSource? LoadImage()
    {
        try { return BitmapFrame.Create(IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
        catch { return null; }
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            using var stream = Application.GetResourceStream(IconUri)!.Stream;
            return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch
        {
            return SystemIcons.Application; // nunca deixa a bandeja sem ícone
        }
    }
}
