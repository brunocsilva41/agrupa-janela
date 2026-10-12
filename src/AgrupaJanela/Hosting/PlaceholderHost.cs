using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Painel de um app de grupo salvo que não está aberto: abrindo, não abriu ou foi fechado.
/// Ocupa o lugar do app no layout (e no arquivo salvo) até a janela real entrar ou o usuário remover o painel.
/// </summary>
public sealed class PlaceholderHost : Border, IGroupedWindow
{
    private readonly TextBlock _heading = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detail = new() { Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _retry = new() { Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };

    public PlaceholderHost(AppIdentity identity, EmbedMode mode)
    {
        Identity = identity;
        Mode = mode;
        var res = Application.Current.Resources;
        Background = (Brush)res["Bg"];
        _detail.Foreground = (Brush)res["TextDim"];
        _retry.Style = (Style)res["PrimaryButton"];
        _retry.Click += (_, _) => RetryRequested?.Invoke(this);
        var remove = new Button { Content = "Remover do grupo", Style = (Style)res["GhostButton"], Padding = new Thickness(10, 3, 10, 3) };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this);
        _actions.Children.Add(_retry);
        _actions.Children.Add(remove);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420, Margin = new Thickness(16) };
        content.Children.Add(_heading);
        content.Children.Add(_detail);
        content.Children.Add(_actions);
        Child = content;
        MouseLeftButtonDown += (_, _) => TargetClicked?.Invoke(this);
        ShowOpening();
    }

    public nint Target => 0;
    public uint ProcessId => 0;
    public string CurrentTitle => Identity.Title;
    public bool IsAttached => true; // continua no grupo até entrar a janela real ou o usuário remover
    public AppIdentity Identity { get; }
    /// <summary>O modo em que o app estava, usado quando a janela real entrar.</summary>
    public EmbedMode Mode { get; }
    public FrameworkElement View => this;
    public bool IsOpening { get; private set; }

    public event Action<IGroupedWindow>? TargetClicked;
    public event Action<PlaceholderHost>? RetryRequested;
    public event Action<PlaceholderHost>? RemoveRequested;

    public void ShowOpening()
    {
        IsOpening = true;
        Show("Abrindo…", Program, null);
    }

    public void ShowFailed(string reason)
    {
        IsOpening = false;
        Show("Não abriu", reason, "Tentar de novo");
    }

    public void ShowClosed()
    {
        IsOpening = false;
        Show("Fechado", $"{Program} foi fechado. O grupo salvo guarda o lugar dele.", "Reabrir");
    }

    private string Program => System.IO.Path.GetFileNameWithoutExtension(Identity.ExePath) is { Length: > 0 } name ? name : "O app";

    private void Show(string heading, string detail, string? retry)
    {
        _heading.Text = heading;
        _detail.Text = detail;
        _retry.Content = retry;
        _retry.Visibility = retry is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, $"{CurrentTitle}: {heading}");
    }

    public bool RefreshTitle() => false;
    public void FocusTarget() { if (_retry.IsVisible) _retry.Focus(); }
    public void Release(bool keepPosition = false) { }
    public void Dispose() { }
}
