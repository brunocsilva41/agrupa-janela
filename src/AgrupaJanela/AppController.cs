using AgrupaJanela.Hosting;
using AgrupaJanela.Layout;
using AgrupaJanela.Native;
using AgrupaJanela.Persistence;
using AgrupaJanela.Shell;
using AgrupaJanela.Updates;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;

namespace AgrupaJanela;

/// <summary>Coordena grupos, grupos salvos, atalhos globais, arrastar janelas e a bandeja.</summary>
public sealed class AppController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly WindowDragWatcher _drag = new();
    private readonly DropOverlay _overlay = new();
    private SystemMenuIntegration? _systemMenu;
    private MainWindow? _main;
    private bool _exiting;

    // Arraste de uma janela sobre OUTRA janela (não grupo): "foto" das janelas embaixo, tirada no início do arraste.
    private List<(nint Handle, Win32.RECT Rect)> _dragTargets = new();
    private (nint Target, DropZone Zone) _windowDrop;

    /// <summary>O controlador do app (um por processo).</summary>
    public static AppController Instance { get; private set; } = null!;

    public ObservableCollection<GroupWindow> Groups { get; } = new();
    /// <summary>Vigia compartilhado das janelas acopladas (ganchos só dos processos acoplados, nunca globais).</summary>
    public DockWatcher DockWatcher { get; } = new();
    public GroupStore Store { get; } = new();
    public HotkeyService Hotkeys { get; } = new();
    public AppSettings Settings { get; } = AppSettings.Load();
    public GroupWindow? LastActive { get; private set; }
    public bool IsExiting => _exiting;

    /// <summary>Algum grupo abriu, fechou ou mudou.</summary>
    public event Action? GroupsChanged;

    public AppController()
    {
        Instance = this;
        Store.SaveFailed += message => _tray?.ShowMessage("Não foi possível salvar os grupos", message + " Tentaremos de novo na próxima mudança.");
        Hotkeys.Pressed += OnHotkey;
        _drag.DragStarted += OnDragStarted;
        _drag.Dragging += OnDragging;
        _drag.Dropped += OnDropped;
        _tray = new TrayIcon(ShowMain, () => NewEmptyGroup(null),
            () => Store.Groups.Select(g => (g.Name, (Action)(() => OpenSaved(g)))).ToList(),
            () => _ = CheckForUpdatesAsync(manual: true), Exit);
        ApplySystemMenuSetting();
    }

    /// <summary>Começo do app: com --tray fica só na bandeja; opcionalmente reabre os grupos salvos.</summary>
    public void Start(bool startInTray)
    {
        if (AppPaths.MigratedFromLegacy)
        {
            AppSettings.MigrateLegacyStartup();
            _tray.ShowMessage("Agrupa-Janela agora é SplitDeck", "Seus grupos salvos e preferências foram trazidos para o novo nome.");
        }
        // Encerramento forçado anterior pode ter deixado janelas acopladas escondidas: mostra de volta.
        var recovered = DockRecovery.RecoverOrphans();
        if (recovered > 0) _tray.ShowMessage("Janelas recuperadas", $"{recovered} janela(s) que estavam agrupadas foram devolvidas à área de trabalho.");
        if (!startInTray) ShowMain();
        else TrimMemory();
        if (Settings.ReopenSavedOnStart)
            foreach (var saved in Store.Groups.ToList()) OpenSaved(saved);

        // Checagem de atualização: um pouco depois de abrir (não atrasa o início) e no máximo 1x por dia.
        if (UpdateService.IsDue(Settings))
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); _ = CheckForUpdatesAsync(manual: false); };
            timer.Start();
        }
    }

    private bool _checkingUpdates;

    /// <summary>Consulta o GitHub; se houver versão nova, pergunta, baixa, confere o SHA-256 e atualiza.</summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdates || _exiting) return;
        _checkingUpdates = true;
        try
        {
            UpdateInfo? update;
            try { update = await UpdateService.CheckAsync(); }
            catch (Exception ex)
            {
                if (manual) ChoiceDialog.Ask(_main, "Atualizações", "Não foi possível consultar o GitHub agora: " + ex.GetBaseException().Message, ("OK", "", true));
                return;
            }
            Settings.LastUpdateCheck = DateTime.UtcNow;
            Settings.Save();

            if (update is null)
            {
                if (manual) ChoiceDialog.Ask(_main, "Atualizações", $"Você já está na versão mais recente ({UpdateService.CurrentVersionText}).", ("OK", "", true));
                return;
            }
            if (!manual && Settings.SkippedVersion == update.Tag) return;

            var notes = update.Notes.Length > 700 ? update.Notes[..700] + "…" : update.Notes;
            var message = $"Nova versão {update.Version.ToString(3)} disponível (você tem a {UpdateService.CurrentVersionText}).\n\n{notes}".Trim();
            if (!UpdateService.IsInstalled)
            {
                // Versão portátil: não há instalador ao lado para trocar os arquivos.
                var portable = ChoiceDialog.Ask(_main, "Atualização disponível", message,
                    ("Baixar no GitHub", "Abre a página do release.", true), ("Pular esta versão", "", false), ("Depois", "", false));
                if (portable == 0) UpdateService.OpenUrl(update.PageUrl);
                if (portable == 1) { Settings.SkippedVersion = update.Tag; Settings.Save(); }
                return;
            }

            var answer = ChoiceDialog.Ask(_main, "Atualização disponível", message,
                ("Atualizar agora", "Baixa do GitHub, confere a integridade (SHA-256), devolve as janelas agrupadas e reabre o app atualizado.", true),
                ("Ver novidades", "Abre o release no GitHub.", false),
                ("Pular esta versão", "", false),
                ("Depois", "", false));
            if (answer == 1) { UpdateService.OpenUrl(update.PageUrl); return; }
            if (answer == 2) { Settings.SkippedVersion = update.Tag; Settings.Save(); return; }
            if (answer != 0) return;

            _tray.ShowMessage("Baixando atualização", $"SplitDeck {update.Version.ToString(3)}…");
            string setup;
            try { setup = await UpdateService.DownloadVerifiedAsync(update); }
            catch (Exception ex)
            {
                ChoiceDialog.Ask(_main, "Atualização", "A atualização não foi aplicada: " + ex.GetBaseException().Message, ("OK", "", true));
                return;
            }
            // Salvos continuam salvos; as janelas voltam para a área de trabalho antes de trocar os arquivos.
            UpdateService.LaunchInstaller(setup);
            ExitSilently();
            Application.Current.Shutdown();
        }
        finally { _checkingUpdates = false; }
    }

    public void ApplySystemMenuSetting()
    {
        if (Settings.SystemMenuEnabled && _systemMenu is null)
        {
            _systemMenu = new SystemMenuIntegration(() => LastActive?.GroupName);
            _systemMenu.GroupRequested += (hwnd, newGroup) =>
                Application.Current.Dispatcher.BeginInvoke(() => GroupWindowFromMenu(hwnd, newGroup));
        }
        else if (!Settings.SystemMenuEnabled && _systemMenu is not null)
        {
            _systemMenu.Dispose();
            _systemMenu = null;
        }
    }

    private void GroupWindowFromMenu(nint hwnd, bool newGroup)
    {
        if (WindowCatalog.Describe(hwnd) is not { } candidate) return;
        var group = newGroup ? null : LastActive;
        List<string> errors;
        if (group is null)
        {
            // Grupo novo nasce onde a janela estava.
            Win32.GetWindowRect(hwnd, out var rect);
            group = CreateGroup();
            errors = AddTo(group, new[] { candidate });
            if (errors.Count == 0) group.PlaceAt(rect);
        }
        else errors = AddTo(group, new[] { candidate });
        if (errors.Count > 0) ChoiceDialog.Ask(null, "SplitDeck", errors[0], ("OK", "", true));
    }

    /// <summary>Abre um grupo vazio (botão "Novo grupo" ou bandeja), um pouco deslocado do grupo de origem.</summary>
    public GroupWindow NewEmptyGroup(GroupWindow? from)
    {
        var group = CreateGroup();
        if (from is not null && from.WindowState == WindowState.Normal)
        {
            group.WindowStartupLocation = WindowStartupLocation.Manual;
            group.Left = from.Left + 32;
            group.Top = from.Top + 32;
            group.Width = from.Width;
            group.Height = from.Height;
        }
        group.Show();
        group.Activate();
        return group;
    }

    public void ShowMain()
    {
        _main ??= new MainWindow(this);
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void MainHidden()
    {
        _tray.ShowStillRunningHint();
        TrimMemory();
    }

    /// <summary>Na bandeja o app só espera eventos: devolve ao Windows a memória que não está em uso.</summary>
    private static void TrimMemory() =>
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Win32.TrimWorkingSet();
        });

    public GroupWindow CreateGroup()
    {
        var used = Groups.Select(g => g.GroupName).Concat(Store.Groups.Select(g => g.Name)).ToHashSet();
        var n = 1;
        while (used.Contains($"Grupo {n}")) n++;
        var group = new GroupWindow(Guid.NewGuid(), $"Grupo {n}");
        Wire(group);
        return group;
    }

    public void OpenSaved(SavedGroup saved)
    {
        if (Groups.FirstOrDefault(g => g.Id == saved.Id) is { } open)
        {
            open.Show();
            open.Activate();
            return;
        }
        var group = new GroupWindow(saved.Id, saved.Name);
        Wire(group);
        group.LoadAsync(saved).ContinueWith(t =>
        {
            if (t.Exception is { } ex)
                _tray.ShowMessage("Não foi possível reabrir o grupo", ex.GetBaseException().Message);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void DeleteSaved(SavedGroup saved)
    {
        // Se estiver aberto, deixa de ser salvo (senão a próxima mudança gravaria de novo).
        Groups.FirstOrDefault(g => g.Id == saved.Id)?.Unsave();
        Store.Delete(saved.Id);
        GroupsChanged?.Invoke();
    }

    /// <summary>Adiciona janelas a um grupo; mostra o grupo se algo entrou. Retorna os erros.</summary>
    public List<string> AddTo(GroupWindow group, IEnumerable<WindowCandidate> candidates)
    {
        var errors = new List<string>();
        var startedEmpty = group.Count == 0;
        var list = new List<WindowCandidate>();
        foreach (var candidate in candidates)
        {
            if (OwnerOf(candidate.Handle) is { } owner) errors.Add($"\"{candidate.Title}\" já está em \"{owner.GroupName}\".");
            else list.Add(candidate);
        }
        foreach (var candidate in list)
            if (!group.TryAdd(candidate, out var error)) errors.Add(error);
        if (startedEmpty && list.Count > 1 && group.Count > 1) group.ApplyAutoLayout(); // lote: começa dividido por igual
        if (group.Count == 0) group.CloseWithDecision(false);
        else { group.Show(); group.Activate(); }
        return errors;
    }

    private void Wire(GroupWindow group)
    {
        Groups.Add(group);
        LastActive = group;
        group.Activated += (_, _) => LastActive = group;
        group.NewGroupRequested += g => NewEmptyGroup(g);
        group.Changed += (g, persist) =>
        {
            if (g.IsSaved)
            {
                var snapshot = g.ToSaved();
                if (!persist && Store.Groups.FirstOrDefault(x => x.Id == g.Id) is { } previous) snapshot.Layout = previous.Layout;
                Store.Save(snapshot);
            }
            GroupsChanged?.Invoke();
        };
        group.SaveToggled += g =>
        {
            if (g.IsSaved) Store.Save(g.ToSaved());
            else Store.Delete(g.Id);
            GroupsChanged?.Invoke();
        };
        group.Closed += (_, _) =>
        {
            Groups.Remove(group);
            if (LastActive == group) LastActive = Groups.LastOrDefault();
            GroupsChanged?.Invoke();
        };
        GroupsChanged?.Invoke();
    }

    // ---------- Atalhos globais ----------

    private void OnHotkey(HotkeyAction action)
    {
        var foreground = Win32.GetAncestor(Win32.GetForegroundWindow(), Win32.GA_ROOT);
        var focusedGroup = Groups.FirstOrDefault(g => new WindowInteropHelper(g).Handle == foreground) ?? OwnerOf(foreground);
        var target = focusedGroup ?? LastActive;

        switch (action)
        {
            case HotkeyAction.GroupForeground:
                Win32.GetWindowThreadProcessId(foreground, out var pid);
                if (pid == (uint)Environment.ProcessId) return; // já é nossa
                if (WindowCatalog.Describe(foreground) is not { } candidate) return;
                var group = LastActive ?? CreateGroup();
                var errors = AddTo(group, new[] { candidate });
                if (errors.Count > 0) ChoiceDialog.Ask(null, "SplitDeck", errors[0], ("OK", "", true));
                break;
            case HotkeyAction.ToggleMode: target?.ToggleMode(); break;
            case HotkeyAction.NextPane: target?.FocusPane(+1); break;
            case HotkeyAction.PreviousPane: target?.FocusPane(-1); break;
            case HotkeyAction.MaximizePane: target?.ToggleMaximize(); break;
        }
    }

    // ---------- Arrastar janela de outro app até um grupo (com Shift) ----------

    /// <summary>O grupo que contém esta janela (incorporada ou acoplada), se houver.</summary>
    public GroupWindow? OwnerOf(nint hwnd) => Groups.FirstOrDefault(g => g.FindHost(hwnd) is not null);

    /// <summary>Todas as janelas já agrupadas (em qualquer grupo, inclusive as que estão sendo reabertas).</summary>
    public HashSet<nint> HostedHandles() => Groups.SelectMany(g => g.HostedHandles).ToHashSet();

    /// <summary>O grupo mais acima na ordem Z sob o cursor.</summary>
    private GroupWindow? GroupAt(int x, int y)
    {
        var candidates = Groups.Where(g => g.ContainsScreenPoint(x, y)).ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();
        var byHandle = candidates.ToDictionary(g => new WindowInteropHelper(g).Handle);
        GroupWindow? top = null;
        Win32.EnumWindows((h, _) => { if (byHandle.TryGetValue(h, out var g)) { top = g; return false; } return true; }, 0);
        return top ?? candidates[0];
    }

    private bool? _dragEligible;
    private bool _dragSnapshotTaken;

    private void OnDragStarted(nint dragged)
    {
        // Barato: todo arraste de qualquer janela passa aqui. A análise só acontece se o Shift aparecer.
        _dragTargets = new();
        _windowDrop = default;
        _dragEligible = null;
        _dragSnapshotTaken = false;
    }

    /// <summary>Na primeira vez que o Shift aparece no arraste: a janela serve? e quem está embaixo (uma vez só).</summary>
    private bool EnsureDragSnapshot(nint dragged)
    {
        _dragEligible ??= WindowCatalog.DescribeFast(dragged) is { CanEmbed: true };
        if (_dragEligible != true || _dragSnapshotTaken) return _dragEligible == true;
        _dragSnapshotTaken = true;
        Win32.EnumWindows((h, _) =>
        {
            if (h != dragged && !Win32.IsIconic(h) && WindowCatalog.DescribeFast(h) is { CanEmbed: true } && Win32.GetWindowRect(h, out var r))
                _dragTargets.Add((h, r));
            return true;
        }, 0);
        return true;
    }

    private void OnDragging(nint hwnd, int x, int y, bool shift)
    {
        var active = shift && EnsureDragSnapshot(hwnd);
        var overGroup = active ? GroupAt(x, y) : null;
        foreach (var group in Groups)
        {
            if (group == overGroup) group.ShowExternalDrop(x, y);
            else group.HideDrop();
        }

        // Sobre outra janela comum (fora de grupos): propõe criar um grupo com as duas.
        _windowDrop = default;
        if (active && overGroup is null && WindowAt(x, y) is { } target)
        {
            var rect = new Int32Rect(target.Rect.Left, target.Rect.Top, target.Rect.Right - target.Rect.Left, target.Rect.Bottom - target.Rect.Top);
            var zone = DropOverlay.ZoneFor(rect, x, y);
            if (zone == DropZone.Center) zone = DropZone.Right;
            _windowDrop = (target.Handle, zone);
            _overlay.ShowZone(rect, zone, "Soltar para agrupar as duas janelas");
        }
        else _overlay.HideZone();
    }

    private (nint Handle, Win32.RECT Rect)? WindowAt(int x, int y)
    {
        foreach (var item in _dragTargets)
            if (x >= item.Rect.Left && x < item.Rect.Right && y >= item.Rect.Top && y < item.Rect.Bottom && Win32.IsWindow(item.Handle))
                return item;
        return null;
    }

    private void OnDropped(nint hwnd, int x, int y, bool shift)
    {
        var active = shift && EnsureDragSnapshot(hwnd);
        var over = active ? GroupAt(x, y) : null;
        foreach (var group in Groups.Where(g => g != over)) group.HideDrop();
        _overlay.HideZone();
        var windowDrop = _windowDrop;
        _windowDrop = default;
        _dragTargets = new();

        var source = OwnerOf(hwnd);
        if (over is not null)
        {
            if (source is not null && source != over) source.ReleaseForMove(hwnd); // muda de grupo
            over.DropExternal(hwnd);
            return;
        }
        if (!active || windowDrop.Target == 0)
        {
            source?.ResyncHost(hwnd); // soltou em lugar nenhum: volta para o painel
            return;
        }
        source?.ReleaseForMove(hwnd);

        // Janela solta sobre outra: novo grupo no lugar da janela de baixo.
        if (WindowCatalog.Describe(windowDrop.Target) is not { } below || WindowCatalog.Describe(hwnd) is not { } dragged) return;
        Win32.GetWindowRect(windowDrop.Target, out var rect);
        var created = CreateGroup();
        if (!created.TryAdd(below, out var error))
        {
            created.CloseWithDecision(false);
            ChoiceDialog.Ask(null, "SplitDeck", error, ("OK", "", true));
            return;
        }
        if (!created.TryAdd(dragged, out error, created.FindHost(below.Handle), windowDrop.Zone))
            ChoiceDialog.Ask(null, "SplitDeck", error, ("OK", "", true));
        created.Show();
        created.PlaceAt(rect);
        created.Activate();
    }

    // ---------- Encerrar ----------

    public void Exit()
    {
        var open = Groups.Where(g => g.Count > 0).ToList();
        var closeApps = false;
        if (open.Count > 0)
        {
            var answer = ChoiceDialog.AskCloseGroups(_main, open.Count == 1 ? $"\"{open[0].GroupName}\" e sair" : $"os {open.Count} grupos e sair");
            if (answer is null) return;
            closeApps = answer.Value;
        }
        _exiting = true;
        foreach (var group in Groups.ToList()) group.CloseWithDecision(closeApps);
        _main?.Close();
        Dispose();
        Application.Current.Shutdown();
    }

    /// <summary>Windows está desligando/saindo: devolve tudo sem perguntar nada.</summary>
    public void ExitSilently()
    {
        if (_exiting) return;
        _exiting = true;
        foreach (var group in Groups.ToList()) group.CloseWithDecision(false);
        Dispose();
    }

    public void Dispose()
    {
        HostRegistry.ReleaseAll();
        _systemMenu?.Dispose();
        _systemMenu = null;
        _overlay.Close();
        DockWatcher.Dispose();
        _drag.Dispose();
        Hotkeys.Dispose();
        _tray.Dispose();
    }
}
