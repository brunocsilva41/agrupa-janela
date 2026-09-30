using AgrupaJanela.Native;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace AgrupaJanela.Shell;

/// <summary>Diálogo escuro com opções descritas. Retorna o índice escolhido, ou -1 se cancelado (Esc/fechar).</summary>
public sealed class ChoiceDialog : Window
{
    private int _result = -1;

    private ChoiceDialog(string title, string message, IReadOnlyList<(string Label, string Hint, bool Primary)> options)
    {
        Title = title;
        Icon = AppIcon.Image;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Surface"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];
        SourceInitialized += (_, _) => Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var panel = new StackPanel { Margin = new Thickness(20), MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 0, 0, 16) });
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var (label, hint, primary) = options[i];
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            if (hint.Length > 0)
                content.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            var button = new Button
            {
                Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6), IsDefault = primary,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            if (primary) button.Style = (Style)Application.Current.Resources["PrimaryButton"];
            button.Click += (_, _) => { _result = index; Close(); };
            panel.Children.Add(button);
        }
        Content = panel;
    }

    public static int Ask(Window? owner, string title, string message, params (string Label, string Hint, bool Primary)[] options)
    {
        var dialog = new ChoiceDialog(title, message, options);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>Pergunta padrão ao fechar grupo(s). Retorna null se cancelou, senão se deve fechar os apps.</summary>
    public static bool? AskCloseGroups(Window? owner, string what) =>
        Ask(owner, "SplitDeck", $"Fechar {what}?",
            ("Devolver as janelas", "Fecha só o agrupador; os apps voltam para a área de trabalho.", true),
            ("Fechar os apps também", "Fecha o agrupador e todos os aplicativos agrupados.", false),
            ("Cancelar", "", false)) switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
}
