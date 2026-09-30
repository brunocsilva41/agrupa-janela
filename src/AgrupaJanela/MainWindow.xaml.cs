using AgrupaJanela.Hosting;
using AgrupaJanela.Native;
using AgrupaJanela.Persistence;
using AgrupaJanela.Shell;
using AgrupaJanela.Updates;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace AgrupaJanela;

public partial class MainWindow : Window
{
    private readonly AppController _app;

    public MainWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        Icon = AppIcon.Image;
        Logo.Source = AppIcon.Image;
        SourceInitialized += (_, _) => Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        Activated += (_, _) => RefreshWindows();
        SavedList.ItemsSource = _app.Store.Groups;
        HotkeysList.ItemsSource = _app.Hotkeys.Registered.Select(h => new { h.Shortcut, h.Description }).ToList();
        _app.GroupsChanged += RefreshGroups;
        _app.GroupsChanged += RefreshWindows; // janelas devolvidas/agrupadas mudam a lista
        RefreshGroups();
        // Enquanto visível, acompanha janelas que abrem/fecham (atualização no lugar, barata); escondida, não gasta nada.
        var listTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        listTimer.Tick += (_, _) => RefreshWindows();
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshWindows(); listTimer.Start(); } else listTimer.Stop(); };
        StartupCheck.IsChecked = AppSettings.StartWithWindows;
        ReopenCheck.IsChecked = _app.Settings.ReopenSavedOnStart;
        SysMenuCheck.IsChecked = _app.Settings.SystemMenuEnabled;
        HideMainCheck.IsChecked = _app.Settings.HideMainAfterGrouping;
        UpdatesCheck.IsChecked = _app.Settings.AutoCheckUpdates;
        AboutText.Text = $"SplitDeck {UpdateService.CurrentVersionText} · © 2026 Bruno Silva · licença MIT";
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e) => _ = _app.CheckForUpdatesAsync(manual: true);
    private void Report_Click(object sender, RoutedEventArgs e) => UpdateService.OpenUrl(UpdateService.IssuesUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => UpdateService.OpenUrl(UpdateService.RepoUrl);

    private void Prefs_Click(object sender, RoutedEventArgs e)
    {
        try { AppSettings.StartWithWindows = StartupCheck.IsChecked == true; }
        catch (Exception ex) { StatusText.Text = "Não foi possível alterar o início com o Windows: " + ex.Message; }
        _app.Settings.ReopenSavedOnStart = ReopenCheck.IsChecked == true;
        _app.Settings.SystemMenuEnabled = SysMenuCheck.IsChecked == true;
        _app.Settings.HideMainAfterGrouping = HideMainCheck.IsChecked == true;
        _app.Settings.AutoCheckUpdates = UpdatesCheck.IsChecked == true;
        _app.Settings.Save();
        _app.ApplySystemMenuSetting();
    }

    private readonly ObservableCollection<WindowCandidate> _windows = new();

    /// <summary>
    /// Atualiza a lista NO LUGAR: entram as novas, saem as que fecharam, as demais continuam sendo os mesmos itens.
    /// (Reconstruir tudo ao ativar a janela fazia o primeiro clique cair num item que acabara de ser trocado.)
    /// </summary>
    private void RefreshWindows()
    {
        WindowsList.ItemsSource ??= _windows;
        var fresh = WindowCatalog.Enumerate();
        StatusText.Text = $"{fresh.Count(c => c.CanEmbed)} janela(s) disponível(is). Ctrl+clique seleciona várias; duplo clique agrupa na hora.";

        for (var i = 0; i < fresh.Count; i++)
        {
            var item = fresh[i];
            var at = -1;
            for (var j = i; j < _windows.Count; j++)
                if (_windows[j].Handle == item.Handle) { at = j; break; }

            if (at < 0) { _windows.Insert(i, item); continue; }
            if (at != i) _windows.Move(at, i);
            // Título animado não troca o item (manteria o clique/seleção instáveis); só mudanças que importam.
            if (_windows[i].CanEmbed != item.CanEmbed || _windows[i].BlockReason != item.BlockReason)
            {
                var wasSelected = WindowsList.SelectedItems.Contains(_windows[i]);
                _windows[i] = item;
                if (wasSelected) WindowsList.SelectedItems.Add(item);
            }
        }
        while (_windows.Count > fresh.Count) _windows.RemoveAt(_windows.Count - 1);
    }

    private void RefreshGroups()
    {
        OpenGroupsList.ItemsSource = _app.Groups
            .Select(g => new OpenGroupItem(g, g.GroupName, $"{g.Count} janela(s){(g.IsSaved ? " · salvo" : "")}"))
            .ToList();
        SavedList.Items.Refresh();
        OpenGroupsHint.Visibility = _app.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedHint.Visibility = _app.Store.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<WindowCandidate> Selected()
    {
        var selected = WindowsList.SelectedItems.Cast<WindowCandidate>().ToList();
        if (selected.Count == 0) StatusText.Text = "Selecione ao menos uma janela na lista.";
        return selected;
    }

    private void Report(List<string> errors)
    {
        RefreshWindows();
        if (errors.Count > 0) StatusText.Text = "Algumas janelas não foram agrupadas:\n• " + string.Join("\n• ", errors);
        // Agrupou tudo: sai da frente e fica na bandeja (o grupo já está aberto).
        else if (_app.Settings.HideMainAfterGrouping) { Hide(); _app.MainHidden(); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count > 0) Report(_app.AddTo(_app.CreateGroup(), selected));
    }

    private void AddTo_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) return;
        var menu = new ContextMenu { PlacementTarget = AddToButton, Placement = PlacementMode.Top };
        foreach (var group in _app.Groups)
        {
            var item = new MenuItem { Header = $"{group.GroupName}  ({group.Count})" };
            item.Click += (_, _) => Report(_app.AddTo(group, selected));
            menu.Items.Add(item);
        }
        var fresh = new MenuItem { Header = "＋ Novo grupo" };
        fresh.Click += (_, _) => Report(_app.AddTo(_app.CreateGroup(), selected));
        menu.Items.Add(fresh);
        menu.IsOpen = true;
    }

    private void WindowsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not WindowCandidate candidate) return;
        Report(_app.AddTo(_app.LastActive ?? _app.CreateGroup(), new[] { candidate }));
    }

    private void OpenGroups_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OpenGroupsList.SelectedItem is not OpenGroupItem item) return;
        item.Window.Show();
        if (item.Window.WindowState == WindowState.Minimized) item.Window.WindowState = WindowState.Normal;
        item.Window.Activate();
    }

    private void OpenSaved_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SavedGroup saved) _app.OpenSaved(saved);
    }

    private void DeleteSaved_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SavedGroup saved) return;
        var answer = ChoiceDialog.Ask(this, "SplitDeck", $"Esquecer o grupo salvo \"{saved.Name}\"?",
            ("Esquecer", "Os apps abertos não são afetados.", true), ("Cancelar", "", false));
        if (answer == 0) _app.DeleteSaved(saved);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => _app.Exit();

    /// <summary>Fechar a janela principal só esconde: o app continua na bandeja com os atalhos.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _app.MainHidden();
        }
        base.OnClosing(e);
    }

    private sealed record OpenGroupItem(GroupWindow Window, string Name, string Summary);
}
