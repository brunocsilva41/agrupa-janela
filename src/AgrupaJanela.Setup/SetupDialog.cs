using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AgrupaJanela.Setup;

/// <summary>Diálogo escuro com opções descritas (mesmo visual do ChoiceDialog do app). Retorna o índice ou -1.</summary>
internal sealed class SetupDialog : Window
{
    private int _result = -1;

    private SetupDialog(string message, IReadOnlyList<(string Label, string Hint, bool Primary)> options)
    {
        Title = SetupContext.AppName;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Surface"];
        Foreground = (Brush)Application.Current.Resources["Text"];
        FontSize = 12.5;
        UseLayoutRounding = true;
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
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

    public static int Ask(Window? owner, string message, params (string Label, string Hint, bool Primary)[] options)
    {
        var dialog = new SetupDialog(message, options);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog._result;
    }

    public static void Info(Window? owner, string message) => Ask(owner, message, ("OK", "", true));
}
