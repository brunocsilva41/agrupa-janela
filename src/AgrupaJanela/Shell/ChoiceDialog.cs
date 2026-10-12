using AgrupaJanela.Native;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AgrupaJanela.Shell;

/// <summary>
/// Opção de um <see cref="ChoiceDialog"/>. Primary = ação padrão (azul); Danger = ação destrutiva (vermelho).
/// Sem descrição e sem destaque, vira botão secundário pequeno no rodapé (ex.: "Cancelar").
/// </summary>
public readonly record struct ChoiceOption(string Label, string Hint = "", bool Primary = false, bool Danger = false);

/// <summary>Diálogo escuro com opções descritas. Retorna o índice escolhido, ou -1 se cancelado (Esc/fechar).</summary>
public sealed class ChoiceDialog : Window
{
    private int _result = -1;

    private ChoiceDialog(string title, string message, IReadOnlyList<ChoiceOption> options)
    {
        Title = title;
        Icon = AppIcon.Image;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Surface"];
        Foreground = (Brush)Application.Current.Resources["Text"];
        SourceInitialized += (_, _) => Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var res = Application.Current.Resources;
        var panel = new StackPanel { Margin = new Thickness(20), MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 0, 0, 16) });
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var option = options[i];
            if (option.Hint.Length == 0 && !option.Primary && !option.Danger)
            {
                var small = new Button { Content = option.Label, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
                System.Windows.Automation.AutomationProperties.SetName(small, option.Label);
                small.Click += (_, _) => { _result = index; Close(); };
                footer.Children.Add(small);
                continue;
            }

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = option.Label, FontWeight = FontWeights.SemiBold });
            if (option.Hint.Length > 0)
                content.Children.Add(new TextBlock
                {
                    Text = option.Hint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                    // Sólido (sem Opacity): sobre botão primário, TextOnAccent; sobre vermelho, o fundo escuro; em cartão neutro, TextDim.
                    Foreground = (Brush)res[option.Primary ? "TextOnAccent" : option.Danger ? "Bg" : "TextDim"],
                });
            var button = new Button
            {
                Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6), IsDefault = option.Primary,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, option.Label);
            if (option.Primary) button.Style = (Style)res["PrimaryButton"];
            else if (option.Danger) button.Style = (Style)res["DangerButton"];
            button.Click += (_, _) => { _result = index; Close(); };
            panel.Children.Add(button);
        }
        if (footer.Children.Count > 0) panel.Children.Add(footer);
        Content = panel;
    }

    public static int Ask(Window? owner, string title, string message, params (string Label, string Hint, bool Primary)[] options) =>
        Ask(owner, title, message, options.Select(o => new ChoiceOption(o.Label, o.Hint, o.Primary)).ToArray());

    public static int Ask(Window? owner, string title, string message, params ChoiceOption[] options)
    {
        var dialog = new ChoiceDialog(title, message, options);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>Pergunta ao fechar grupo(s). Retorna null se cancelou, senão se deve fechar os apps.</summary>
    public static bool? AskCloseGroups(Window? owner, string question) =>
        Ask(owner, "SplitDeck", question,
            new ChoiceOption("Devolver as janelas", "Os apps continuam abertos na área de trabalho.", Primary: true),
            new ChoiceOption("Fechar os apps também", "O que não foi salvo neles pode ser perdido."),
            new ChoiceOption("Cancelar")) switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
}
