using AgrupaJanela.Hosting;
using AgrupaJanela.Layout;
using AgrupaJanela.Native;
using AgrupaJanela.Persistence;
using AgrupaJanela.Shell;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AgrupaJanela;

public enum LayoutMode { Grid, Tabs }

/// <summary>A janela única que contém as janelas agrupadas.</summary>
public partial class GroupWindow : Window
{
    private const double SplitterSize = 6;
    private const double DragThreshold = 6;

    private readonly LayoutTree _tree = new();
    private readonly Dictionary<IGroupedWindow, PaneView> _panes = new();
    private readonly Dictionary<IGroupedWindow, List<TextBlock>> _titles = new();
    private readonly Dictionary<SplitNode, Grid> _splitGrids = new();
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusClear = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DropOverlay _overlay = new();

    private LayoutMode _mode = LayoutMode.Grid;
    private IGroupedWindow? _active;
    private IGroupedWindow? _maximized;
    private bool _syncingSave;
    private bool _ready, _loading, _closing, _closed, _closeDecided, _closeApps, _closeStarted;

    // Arraste de painel pelo cabeçalho
    private IGroupedWindow? _dragSource;
    private Point _dragStart;
    private bool _dragging;
    private (IGroupedWindow? Target, DropZone Zone) _drop;

    public Guid Id { get; }
    public string GroupName => string.IsNullOrWhiteSpace(NameBox.Text) ? "Grupo" : NameBox.Text.Trim();
    public bool IsSaved { get; private set; }
    public int Count => _tree.Leaves().Count();
    /// <summary>Há janelas reais no grupo (painéis de apps que não abriram não contam).</summary>
    public bool HasWindows => Hosts.Any(h => h is not PlaceholderHost);
    private List<IGroupedWindow> Hosts => _tree.Leaves().Select(l => l.Host).ToList();

    /// <summary>
    /// Algo mudou (layout, nome, modo, janelas). Num grupo salvo, o layout atual é sempre o salvo: apps que não abriram
    /// ou foram fechados continuam nele como painéis de espera (<see cref="PlaceholderHost"/>).
    /// </summary>
    public event Action<GroupWindow>? Changed;
    /// <summary>O usuário pediu um grupo novo pelo botão da barra.</summary>
    public event Action<GroupWindow>? NewGroupRequested;
    /// <summary>O usuário ligou/desligou "Salvar".</summary>
    public event Action<GroupWindow>? SaveToggled;

    public GroupWindow(Guid id, string name)
    {
        InitializeComponent();
        Id = id;
        NameBox.Text = name;
        Icon = AppIcon.Image;
        SourceInitialized += (_, _) => Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        _watch.Tick += (_, _) => Watch();
        _statusClear.Tick += (_, _) => HideStatus();
        _watch.Start();
        BuildPresetButtons();
        // Janelas acopladas acompanham o grupo: mover, redimensionar, minimizar e ordem Z.
        LocationChanged += (_, _) => SyncDocked(force: true);
        SizeChanged += (_, _) => SyncDocked(force: true);
        StateChanged += (_, _) => SyncDocked(force: true);
        Activated += (_, _) => { foreach (var d in Docked) d.RaiseAboveGroup(); };
        AppController.Instance.DockWatcher.Destroyed += OnDockedDestroyed;
        Closed += (_, _) => AppController.Instance.DockWatcher.Destroyed -= OnDockedDestroyed;
        _ready = true;
        Rebuild();
    }

    private IEnumerable<DockedWindowHost> Docked => Hosts.OfType<DockedWindowHost>();

    private void OnDockedDestroyed(nint hwnd)
    {
        if (FindHost(hwnd) is DockedWindowHost) Dispatcher.BeginInvoke(Watch);
    }

    private void SyncDocked(bool force)
    {
        foreach (var docked in Docked) docked.Sync(force);
    }

    /// <summary>Uma acoplada virou primeiro plano: o grupo vai logo abaixo dela e as outras acopladas ficam acima do grupo.</summary>
    private void ArrangeUnder(DockedWindowHost top)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, top.Target, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_ASYNCWINDOWPOS);
        foreach (var other in Docked.Where(d => d != top)) other.RaiseAboveGroup();
    }

    // ================= API usada pelo controlador =================

    public bool TryAdd(WindowCandidate candidate, out string error, IGroupedWindow? target = null, DropZone zone = DropZone.None)
    {
        error = "";
        if (_loading)
        {
            error = "Aguarde: este grupo ainda está reabrindo os apps salvos.";
            StatusError(error);
            return false;
        }
        if (AppController.Instance.OwnerOf(candidate.Handle) is { } owner)
        {
            error = $"\"{candidate.Title}\" já está em \"{owner.GroupName}\".";
            StatusError(error);
            return false;
        }
        IGroupedWindow host;
        try { host = CreateHost(candidate); }
        catch (Exception ex)
        {
            error = ex.Message;
            StatusError("Não foi possível agrupar: " + ex.Message);
            return false;
        }

        var wide = true;
        if (_active is not null && _panes.TryGetValue(_active, out var pane)) wide = pane.Root.ActualWidth >= pane.Root.ActualHeight;
        var beside = _active is null ? null : _tree.Find(_active);
        Mutate(() =>
        {
            if (target is not null && zone != DropZone.None) _tree.InsertAt(host, target, zone);
            else _tree.Add(host, beside, wide);
            _maximized = null;
        });
        SetActive(host, focus: true);
        Status($"\"{host.CurrentTitle}\" agrupada.");
        return true;
    }

    /// <summary>Posiciona a janela do grupo num retângulo de tela (pixels físicos), ex.: onde estava a janela agrupada.</summary>
    internal void PlaceAt(Win32.RECT rect)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        var width = Math.Max(rect.Right - rect.Left, 640);
        var height = Math.Max(rect.Bottom - rect.Top, 420);
        Win32.SetWindowPos(hwnd, 0, rect.Left, rect.Top, width, height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public IGroupedWindow? FindHost(nint target) => target == 0 ? null : Hosts.FirstOrDefault(h => h.Target == target);

    /// <summary>Divide por igual: colunas até 3 janelas, grade a partir de 4.</summary>
    public void ApplyAutoLayout() => Mutate(() => { _tree.ApplyPreset(Count <= 3 ? Preset.Columns : Preset.Grid); _maximized = null; });

    public void ToggleMode()
    {
        if (_mode == LayoutMode.Grid) TabsModeButton.IsChecked = true;
        else GridModeButton.IsChecked = true;
    }

    public void FocusPane(int delta)
    {
        var hosts = Hosts;
        if (hosts.Count == 0) return;
        var index = _active is null ? 0 : hosts.IndexOf(_active);
        var next = hosts[((index + delta) % hosts.Count + hosts.Count) % hosts.Count];
        if (_maximized is not null) { _maximized = next; Rebuild(); }
        if (ContentArea.Content is TabControl tabs) tabs.SelectedIndex = hosts.IndexOf(next);
        SetActive(next, focus: true);
    }

    public void ToggleMaximize(IGroupedWindow? host = null)
    {
        host ??= _active;
        if (host is null) return;
        if (_mode == LayoutMode.Tabs) { Status("No modo abas cada janela já ocupa o espaço todo."); return; }
        CaptureWeights();
        _maximized = _maximized == host ? null : host;
        Rebuild();
        SetActive(host, focus: true);
        if (_maximized is not null) Status("Painel maximizado. Duplo clique no cabeçalho (ou o atalho) para voltar.");
    }

    public void CloseWithDecision(bool closeApps)
    {
        _closeDecided = true;
        _closeApps = closeApps;
        // Já fechando (o diálogo de OnClosing pode estar aberto e o Quit chegou): Close() de novo lança
        // InvalidOperationException. A decisão acima basta: OnClosing a respeita ao voltar do diálogo.
        if (_closeStarted || _closed) return;
        Close();
    }

    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return false;
        Win32.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
        return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }

    /// <summary>Mostra onde uma janela externa arrastada vai cair.</summary>
    public void ShowExternalDrop(int x, int y)
    {
        if (_mode == LayoutMode.Tabs || _maximized is not null || Count == 0)
        {
            if (ScreenRect(ContentArea) is { } area && Contains(area, x, y))
            {
                _drop = (null, DropZone.Center);
                _overlay.ShowZone(area, DropZone.Center, _mode == LayoutMode.Tabs ? "Soltar para abrir em nova aba" : "Soltar para agrupar");
                return;
            }
            HideDrop();
            return;
        }
        UpdateDropTarget(x, y, source: null);
    }

    public void HideDrop()
    {
        _drop = default;
        _overlay.HideZone();
    }

    public bool DropExternal(nint hwnd)
    {
        var drop = _drop;
        HideDrop();
        if (FindHost(hwnd) is { } existing)
        {
            // Já é deste grupo (arrastou uma acoplada com Shift): reorganiza em vez de adicionar de novo.
            if (drop.Target is not null && drop.Target != existing && drop.Zone != DropZone.None)
                Mutate(() => _tree.Move(existing, drop.Target, drop.Zone));
            SyncDocked(force: true);
            return true;
        }
        if (drop.Zone == DropZone.None) return false;
        var candidate = WindowCatalog.Describe(hwnd);
        if (candidate is null) return false;
        var ok = TryAdd(candidate, out var error, drop.Target, drop.Zone);
        if (!ok) ChoiceDialog.Ask(this, "SplitDeck", error, ("OK", "", true));
        Activate();
        return ok;
    }

    public SavedGroup ToSaved()
    {
        var bounds = WindowState == WindowState.Normal || RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var hosts = Hosts;
        return new SavedGroup
        {
            Id = Id, Name = GroupName, Tabs = _mode == LayoutMode.Tabs, Layout = _tree.ToDto(StateOf),
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
            ActiveIndex = _active is null ? -1 : hosts.IndexOf(_active),
            MaximizedIndex = _maximized is null ? -1 : hosts.IndexOf(_maximized),
        };
    }

    /// <summary>Última pasta lida de cada terminal: se o app fechar, o painel de espera ainda reabre no mesmo lugar.</summary>
    private readonly Dictionary<IGroupedWindow, string> _lastDirectory = new();

    /// <summary>O que salvar de um app agora: identidade atual, título atual e, nos terminais, a pasta atual.</summary>
    private AppIdentity StateOf(IGroupedWindow host)
    {
        if (host is PlaceholderHost) return host.Identity;
        if (host.IsAttached && TerminalDirectory.Of(host.Target) is { } dir) _lastDirectory[host] = dir;
        return host.Identity with
        {
            Title = host.CurrentTitle,
            WorkingDirectory = _lastDirectory.GetValueOrDefault(host) ?? host.Identity.WorkingDirectory,
        };
    }

    /// <summary>Reabre um grupo salvo: monta o layout salvo na hora e troca cada painel pela janela real quando ela aparece.</summary>
    public async Task LoadAsync(SavedGroup saved)
    {
        _loading = true;
        NameBox.Text = saved.Name;
        SetSaved(true);
        if (!double.IsNaN(saved.Left) && !double.IsNaN(saved.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
        }
        Width = saved.Width;
        Height = saved.Height;
        if (saved.Tabs) TabsModeButton.IsChecked = true;
        Show();
        if (saved.Maximized) WindowState = WindowState.Maximized;

        try { await LoadAppsAsync(saved); }
        finally
        {
            // Mesmo com erro, o grupo precisa voltar a vigiar e redesenhar normalmente.
            if (_loading) { _loading = false; if (!_closed) Rebuild(); }
        }
    }

    private async Task LoadAppsAsync(SavedGroup saved)
    {
        var leaves = new List<NodeDto>();
        CollectApps(saved.Layout, leaves);
        var placeholders = leaves.Select(leaf => CreatePlaceholder(leaf.App!, leaf.Mode)).ToList();
        var queue = new Queue<PlaceholderHost>(placeholders);
        _tree.Load(saved.Layout, _ => queue.Count > 0 ? queue.Dequeue() : null);

        // Painel ativo (aba selecionada) e painel maximizado como estavam.
        var hosts = Hosts;
        _active = hosts.ElementAtOrDefault(saved.ActiveIndex) ?? hosts.FirstOrDefault();
        _maximized = saved.Tabs ? null : hosts.ElementAtOrDefault(saved.MaximizedIndex);
        Rebuild();

        // Um app por vez: o "antes/depois" da lista de janelas identifica a janela nova de cada um.
        var taken = AppController.Instance.HostedHandles();
        var opened = 0;
        for (var i = 0; i < placeholders.Count && !_closed && !_closing; i++)
        {
            Status($"Abrindo apps… {i + 1}/{placeholders.Count}");
            if (await ReopenAsync(placeholders[i], taken)) opened++;
        }
        if (_closed || _closing) return;

        _loading = false;
        Rebuild();
        if (_active is not null) SetActive(_active, focus: true);
        if (opened == placeholders.Count) Status(Texto.Plural(opened, "app restaurado.", "apps restaurados."));
        else StatusError($"Restaurados {opened} de {placeholders.Count}. Os demais ficaram no lugar com \"Tentar de novo\".");
        RaiseChanged(); // grava as janelas novas (HWND, processo) para a próxima reabertura reencontrá-las
    }

    private static void CollectApps(NodeDto? node, List<NodeDto> leaves)
    {
        if (node is null) return;
        if (node.App is not null) { leaves.Add(node); return; }
        foreach (var child in node.Children) CollectApps(child, leaves);
    }

    private PlaceholderHost CreatePlaceholder(AppIdentity identity, EmbedMode mode)
    {
        var placeholder = new PlaceholderHost(identity, mode);
        placeholder.TargetClicked += h => SetActive(h, focus: false);
        placeholder.RemoveRequested += p => Remove(p, closeApp: false);
        placeholder.RetryRequested += async p =>
        {
            if (p.IsOpening) return;
            if (await ReopenAsync(p, AppController.Instance.HostedHandles()) && !_loading) RaiseChanged();
        };
        return placeholder;
    }

    /// <summary>Reencontra ou reabre o app de um painel de espera e põe a janela real no lugar dele.</summary>
    private async Task<bool> ReopenAsync(PlaceholderHost placeholder, ISet<nint> taken)
    {
        placeholder.ShowOpening();
        taken.UnionWith(AppController.Instance.HostedHandles()); // outros grupos podem estar reabrindo ao mesmo tempo
        WindowCandidate? candidate = null;
        try { candidate = await AppLauncher.ResolveAsync(placeholder.Identity, taken); }
        catch { /* um app que falha não impede os outros */ }
        if (_closed || _closing || _tree.Find(placeholder) is null) return false; // grupo fechou ou o painel foi removido
        if (candidate is not null && AppController.Instance.OwnerOf(candidate.Handle) is not null) candidate = null;
        if (candidate is null)
        {
            placeholder.ShowFailed(AppLauncher.WhyNot(placeholder.Identity));
            return false;
        }

        IGroupedWindow host;
        try { host = CreateHost(candidate, ModeFor(placeholder)); }
        catch (Exception ex)
        {
            placeholder.ShowFailed(ex.Message);
            return false;
        }
        taken.Add(candidate.Handle);
        if (placeholder.Identity.WorkingDirectory is { } dir) _lastDirectory[host] = dir;
        _tree.Replace(placeholder, host);
        if (_active == placeholder) _active = host;
        if (_maximized == placeholder) _maximized = host;
        DetachFromTree(placeholder);
        Rebuild();
        if (!_loading && _active == host) SetActive(host, focus: true);
        return true;
    }

    /// <summary>A escolha do usuário para o programa manda; sem ela, volta no modo em que estava.</summary>
    private EmbedMode ModeFor(PlaceholderHost placeholder) =>
        AppController.Instance.Settings.GetOverride(placeholder.Identity.ExePath) != EmbedMode.Auto ? EmbedMode.Auto : placeholder.Mode;

    // ================= Ciclo de vida =================

    protected override void OnClosing(CancelEventArgs e)
    {
        _closeStarted = true;
        if (!_closeDecided && HasWindows)
        {
            var closeApps = ChoiceDialog.AskCloseGroups(this, $"Fechar o grupo \"{GroupName}\"?");
            // O diálogo é modal e bombeia mensagens: o Quit/Sair pode ter decidido enquanto ele estava aberto.
            if (!_closeDecided)
            {
                if (closeApps is null) { _closeStarted = false; e.Cancel = true; return; }
                _closeApps = closeApps.Value;
            }
        }

        if (IsSaved) Changed?.Invoke(this); // guarda posição, tamanho, painel ativo e pastas dos terminais
        _closing = true;
        _watch.Stop();
        _statusClear.Stop();
        _overlay.Close();

        // Precisa acontecer ANTES do WPF destruir o HWND desta janela: janelas filhas seriam destruídas junto.
        foreach (var host in Hosts)
        {
            var target = host.Target;
            var attached = host.IsAttached;
            host.Release();
            DetachFromTree(host);
            host.Dispose();
            if (_closeApps && attached && target != 0) Win32.PostMessage(target, Win32.WM_CLOSE, 0, 0);
        }
        base.OnClosing(e);
    }

    public IEnumerable<nint> HostedHandles => Hosts.Select(h => h.Target).Where(t => t != 0);

    /// <summary>O grupo deixou de ser salvo (ex.: o usuário apagou o salvo na janela principal).</summary>
    public void Unsave() => SetSaved(false);

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    private IGroupedWindow CreateHost(WindowCandidate candidate, EmbedMode mode = EmbedMode.Auto)
    {
        var app = AppController.Instance;
        if (mode == EmbedMode.Auto)
            mode = EmbedPolicy.Resolve(candidate.Handle, candidate.ProcessId, Win32.GetProcessPath(candidate.ProcessId), app.Settings);

        IGroupedWindow host;
        if (mode == EmbedMode.Dock)
        {
            var docked = DockedWindowHost.Create(candidate, this, app.DockWatcher);
            docked.UndockRequested += d => Dispatcher.BeginInvoke(() => Remove(d, closeApp: false, keepPosition: true));
            docked.MinimizeRequested += _ => WindowState = WindowState.Minimized;
            docked.TargetClicked += h => { SetActive(h, focus: false); ArrangeUnder((DockedWindowHost)h); };
            host = docked;
        }
        else
        {
            host = EmbeddedWindowHost.Create(candidate, new WindowInteropHelper(this).EnsureHandle());
            host.TargetClicked += h => SetActive(h, focus: false);
        }
        return host;
    }

    /// <summary>Troca o modo de um painel na hora (e lembra a escolha para o programa).</summary>
    private void SwitchMode(IGroupedWindow host, EmbedMode mode)
    {
        var app = AppController.Instance;
        app.Settings.SetOverride(host.Identity.ExePath, mode);
        var resolved = mode == EmbedMode.Auto
            ? EmbedPolicy.Resolve(host.Target, host.ProcessId, host.Identity.ExePath, app.Settings)
            : mode;
        if (resolved == host.Mode) { Status($"Modo \"{ModeTitle(resolved)}\" (já estava)."); return; }

        var hwnd = host.Target;
        host.Release();
        DetachFromTree(host);
        host.Dispose();
        var candidate = WindowCatalog.Describe(hwnd);
        IGroupedWindow? replacement = null;
        try { if (candidate is not null) replacement = CreateHost(candidate, resolved); }
        catch (Exception ex) { StatusError("Não deu para trocar o modo: " + ex.Message); }
        Mutate(() =>
        {
            if (replacement is not null) _tree.Replace(host, replacement);
            else _tree.Remove(host);
        });
        if (replacement is not null)
        {
            SetActive(replacement, focus: true);
            Status($"\"{replacement.CurrentTitle}\" agora está em \"{ModeTitle(resolved)}\". Lembrado para {System.IO.Path.GetFileName(host.Identity.ExePath)}.");
        }
    }

    /// <summary>Solta uma janela deste grupo sem mexer na posição dela (vai para outro grupo/lugar).</summary>
    public void ReleaseForMove(nint target)
    {
        if (FindHost(target) is { } host) Remove(host, closeApp: false, keepPosition: true);
    }

    public void ResyncHost(nint target)
    {
        if (FindHost(target) is DockedWindowHost docked) docked.Sync(force: true);
    }

    private void Watch()
    {
        if (_loading) return;
        var lost = Hosts.Where(h => !h.IsAttached).ToList();
        if (lost.Count > 0)
        {
            // Grupo salvo: o app fechado deixa um painel "Reabrir" no lugar (o layout salvo continua com ele).
            var keep = IsSaved ? lost.ToDictionary(h => h, StateOf) : null;
            foreach (var host in lost) { DetachFromTree(host); host.Dispose(); _lastDirectory.Remove(host); }
            Mutate(() =>
            {
                foreach (var host in lost)
                {
                    if (keep is null) { _tree.Remove(host); continue; }
                    var placeholder = CreatePlaceholder(keep[host], host.Mode);
                    placeholder.ShowClosed();
                    _tree.Replace(host, placeholder);
                    if (_active == host) _active = placeholder;
                    if (_maximized == host) _maximized = placeholder;
                }
            });
            var names = string.Join(", ", lost.Select(h => $"\"{h.CurrentTitle}\""));
            Status(IsSaved ? $"{names} foi fechada; o lugar dela fica guardado no grupo." : $"{names} foi fechada e saiu do grupo.");
        }

        foreach (var host in Hosts)
        {
            if (!host.RefreshTitle() || !_titles.TryGetValue(host, out var blocks)) continue;
            foreach (var block in blocks) block.Text = host.CurrentTitle;
        }
    }

    private void Remove(IGroupedWindow host, bool closeApp, bool keepPosition = false)
    {
        if (!Hosts.Contains(host)) return;
        var target = host.Target;
        var attached = host.IsAttached;
        host.Release(keepPosition);
        DetachFromTree(host);
        host.Dispose();
        _lastDirectory.Remove(host);
        Mutate(() => _tree.Remove(host));
        if (closeApp && attached && target != 0) Win32.PostMessage(target, Win32.WM_CLOSE, 0, 0);
        Status(host is PlaceholderHost ? "Painel removido do grupo." : keepPosition ? "Janela saiu do grupo." : "Janela devolvida à área de trabalho.");
    }

    // ================= Layout =================

    /// <summary>Guarda os tamanhos atuais, aplica a mudança, redesenha e avisa.</summary>
    private void Mutate(Action change)
    {
        CaptureWeights();
        change();
        Rebuild();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (_ready && !_loading && !_closing) Changed?.Invoke(this);
    }

    private void Rebuild()
    {
        foreach (var host in _panes.Keys.Concat(Hosts).Distinct().ToList()) DetachFromTree(host);
        _panes.Clear();
        _titles.Clear();
        _splitGrids.Clear();

        var hosts = Hosts;
        if (_maximized is not null && !hosts.Contains(_maximized)) _maximized = null;
        if (_active is null || !hosts.Contains(_active)) _active = hosts.FirstOrDefault();

        Title = $"{GroupName}  ·  SplitDeck";
        EmptyHint.Visibility = hosts.Count == 0 && !_loading ? Visibility.Visible : Visibility.Collapsed;
        ContentArea.Content = hosts.Count == 0 ? null
            : _mode == LayoutMode.Tabs ? BuildTabs(hosts)
            : _maximized is not null ? CreatePane(_maximized).Root
            : BuildNode(_tree.Root!);
        UpdateActiveVisuals();
    }

    private UIElement BuildNode(LayoutNode node)
    {
        if (node is LeafNode leaf) return CreatePane(leaf.Host).Root;

        var split = (SplitNode)node;
        var grid = new Grid();
        for (var i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
            {
                AddDefinition(grid, split.Horizontal, new GridLength(SplitterSize));
                var splitter = new GridSplitter
                {
                    ResizeDirection = split.Horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ShowsPreview = false,
                    Cursor = split.Horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                };
                splitter.DragCompleted += (_, _) => { CaptureWeights(); RaiseChanged(); };
                Place(splitter, split.Horizontal, Count(grid, split.Horizontal) - 1);
                grid.Children.Add(splitter);
            }
            AddDefinition(grid, split.Horizontal, new GridLength(Math.Max(0.05, split.Weights[i]), GridUnitType.Star));
            var child = BuildNode(split.Children[i]);
            Place(child, split.Horizontal, Count(grid, split.Horizontal) - 1);
            grid.Children.Add(child);
        }
        _splitGrids[split] = grid;
        return grid;

        static void AddDefinition(Grid g, bool horizontal, GridLength length)
        {
            if (horizontal) g.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
            else g.RowDefinitions.Add(new RowDefinition { Height = length });
        }
        static int Count(Grid g, bool horizontal) => horizontal ? g.ColumnDefinitions.Count : g.RowDefinitions.Count;
        static void Place(UIElement e, bool horizontal, int index)
        {
            if (horizontal) Grid.SetColumn(e, index);
            else Grid.SetRow(e, index);
        }
    }

    /// <summary>Lê os tamanhos (arrastados pelo usuário) dos grids atuais para a árvore.</summary>
    private void CaptureWeights()
    {
        foreach (var (split, grid) in _splitGrids)
        {
            var lengths = split.Horizontal
                ? grid.ColumnDefinitions.Where((_, i) => i % 2 == 0).Select(d => (d.Width, d.ActualWidth)).ToList()
                : grid.RowDefinitions.Where((_, i) => i % 2 == 0).Select(d => (d.Height, d.ActualHeight)).ToList();
            if (lengths.Count != split.Weights.Count) continue;
            for (var i = 0; i < lengths.Count; i++)
                split.Weights[i] = lengths[i].Item1.IsStar && lengths[i].Item1.Value > 0 ? lengths[i].Item1.Value : Math.Max(1, lengths[i].Item2);
        }
    }

    private PaneView CreatePane(IGroupedWindow host)
    {
        var view = new PaneView(host, TitleBlock(host));
        view.MaximizeButton.Click += (_, _) => ToggleMaximize(host);
        view.CloseButton.Click += (_, _) => Remove(host, closeApp: false);
        view.ModeButton.Click += (_, _) => ShowModeMenu(host, view.ModeButton);
        view.MaximizeButton.Content = GlyphIcon(_maximized == host ? '\uE73F' : '\uE740');
        view.MaximizeButton.ToolTip = _maximized == host ? "Restaurar layout (duplo clique no cabeçalho)" : "Maximizar painel (duplo clique no cabeçalho)";
        AutomationProperties.SetName(view.MaximizeButton, _maximized == host ? "Restaurar layout" : "Maximizar painel");
        AutomationProperties.SetName(view.CloseButton, "Desagrupar");

        var header = view.Header;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ToggleMaximize(host); e.Handled = true; return; }
            _dragSource = host;
            _dragStart = e.GetPosition(this);
            _dragging = false;
            header.CaptureMouse();
            SetActive(host, focus: host.Mode != EmbedMode.Dock);
            e.Handled = true;
        };
        header.MouseMove += (_, e) =>
        {
            if (_dragSource != host || !header.IsMouseCaptured) return;
            if (!_dragging)
            {
                var moved = e.GetPosition(this) - _dragStart;
                if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;
                if (_maximized is not null || Count < 2) return;
                _dragging = true;
                Status("Solte sobre outro painel: bordas dividem, centro troca de lugar.");
            }
            var screen = header.PointToScreen(e.GetPosition(header));
            UpdateDropTarget((int)screen.X, (int)screen.Y, host);
        };
        header.MouseLeftButtonUp += (_, _) =>
        {
            var source = _dragSource;
            var drop = _drop;
            var wasDragging = _dragging;
            EndHeaderDrag();
            header.ReleaseMouseCapture();
            if (wasDragging && source is not null && drop.Target is not null && drop.Zone != DropZone.None)
            {
                Mutate(() => _tree.Move(source, drop.Target, drop.Zone));
                SetActive(source, focus: true);
            }
            else if (!wasDragging && source is { Mode: EmbedMode.Dock }) SetActive(source, focus: true);
        };
        header.LostMouseCapture += (_, _) => EndHeaderDrag();
        header.MouseEnter += (_, _) => view.SetHover(true);
        header.MouseLeave += (_, _) => view.SetHover(false);

        _panes[host] = view;
        return view;
    }

    /// <summary>Nome do modo para o usuário. (O motivo técnico em EmbedPolicy continua com os nomes antigos.)</summary>
    private static string ModeTitle(EmbedMode mode) => mode switch
    {
        EmbedMode.Reparent => "Dentro do painel",
        EmbedMode.Dock => "Sobre o painel",
        _ => "Automático (recomendado)",
    };

    private void ShowModeMenu(IGroupedWindow host, Button anchor)
    {
        var app = AppController.Instance;
        var exe = System.IO.Path.GetFileName(host.Identity.ExePath) ?? "este app";
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var choice = app.Settings.GetOverride(host.Identity.ExePath);
        void Add(EmbedMode mode, string title, string hint)
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = (choice == mode ? "● " : "○ ") + title, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextDim"], TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => SwitchMode(host, mode);
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuItem { Header = "Como mostrar este app", IsEnabled = false, FontWeight = FontWeights.SemiBold });
        Add(EmbedMode.Auto, ModeTitle(EmbedMode.Auto), EmbedPolicy.Explain(host.Target, host.ProcessId, host.Identity.ExePath, app.Settings));
        Add(EmbedMode.Reparent, ModeTitle(EmbedMode.Reparent), "Mais integrado. Use para terminais e apps comuns.");
        Add(EmbedMode.Dock, ModeTitle(EmbedMode.Dock), "A janela fica por cima do painel. Use se o app ficar preto, piscando ou lento.");
        menu.Items.Add(new MenuItem { Header = $"A escolha fica salva para {exe}.", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void EndHeaderDrag()
    {
        _dragSource = null;
        _dragging = false;
        HideDrop();
    }

    private void UpdateDropTarget(int x, int y, IGroupedWindow? source)
    {
        foreach (var (host, pane) in _panes)
        {
            if (ScreenRect(pane.Root) is not { } rect || !Contains(rect, x, y)) continue;
            if (host == source) break;
            var zone = DropOverlay.ZoneFor(rect, x, y);
            _drop = (host, zone);
            _overlay.ShowZone(rect, zone, DropOverlay.Describe(zone, swap: source is not null));
            return;
        }
        HideDrop();
    }

    private UIElement BuildTabs(List<IGroupedWindow> hosts)
    {
        var tabs = new TabControl();
        foreach (var host in hosts)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var title = TitleBlock(host);
            title.MaxWidth = 220;
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(title);
            var close = new Button { Content = GlyphIcon('\uE8BB', 10), Style = (Style)FindResource("GhostButton"), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Desagrupar (o app continua aberto)", Focusable = false };
            AutomationProperties.SetName(close, "Desagrupar");
            close.Click += (_, _) => Remove(host, closeApp: false);
            header.Children.Add(close);
            tabs.Items.Add(new TabItem { Header = header, Content = host.View });
        }
        tabs.SelectedIndex = Math.Max(0, _active is null ? 0 : hosts.IndexOf(_active));
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != tabs || tabs.SelectedIndex < 0) return;
            SetActive(hosts[tabs.SelectedIndex], focus: true);
        };
        return tabs;
    }

    private TextBlock TitleBlock(IGroupedWindow host)
    {
        var block = new TextBlock { Text = host.CurrentTitle, TextTrimming = TextTrimming.CharacterEllipsis };
        if (!_titles.TryGetValue(host, out var list)) _titles[host] = list = new List<TextBlock>();
        list.Add(block);
        return block;
    }

    private void SetActive(IGroupedWindow host, bool focus)
    {
        _active = host;
        UpdateActiveVisuals();
        if (focus) Dispatcher.BeginInvoke(DispatcherPriority.Background, host.FocusTarget);
    }

    private void UpdateActiveVisuals()
    {
        foreach (var (host, pane) in _panes) pane.SetActive(host == _active);
    }

    private static void DetachFromTree(IGroupedWindow host)
    {
        switch (host.View.Parent)
        {
            case Panel panel: panel.Children.Remove(host.View); break;
            case ContentControl control: control.Content = null; break;
            case Decorator decorator: decorator.Child = null; break;
        }
    }

    private static Int32Rect? ScreenRect(FrameworkElement element)
    {
        if (PresentationSource.FromVisual(element) is null || element.ActualWidth <= 0) return null;
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Int32Rect((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
    }

    private static bool Contains(Int32Rect r, int x, int y) => x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;

    /// <summary>Mensagem de sucesso/info: some sozinha em 4 s.</summary>
    private void Status(string text) => ShowStatus(text, error: false);

    /// <summary>Erro: cor Danger, fica até o usuário clicar no ✕.</summary>
    private void StatusError(string text) => ShowStatus(text, error: true);

    private void ShowStatus(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(error ? "Danger" : "TextDim");
        AutomationProperties.SetLiveSetting(StatusText, error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        StatusClose.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Visible;
        _statusClear.Stop();
        if (!error) _statusClear.Start();
    }

    private void HideStatus()
    {
        _statusClear.Stop();
        StatusBar.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void StatusClose_Click(object sender, RoutedEventArgs e) => HideStatus();

    // ================= Barra do grupo =================

    private void BuildPresetButtons()
    {
        var presets = new (Preset Preset, string Tip, double[][] Rects)[]
        {
            (Preset.Columns, "Colunas lado a lado", new[] { new[] { 0, 0, .3, 1.0 }, new[] { .35, 0, .3, 1.0 }, new[] { .7, 0, .3, 1.0 } }),
            (Preset.Rows, "Linhas empilhadas", new[] { new[] { 0, 0, 1, .28 }, new[] { 0, .36, 1, .28 }, new[] { 0, .72, 1, .28 } }),
            (Preset.Grid, "Grade", new[] { new[] { 0, 0, .47, .44 }, new[] { .53, 0, .47, .44 }, new[] { 0, .56, .47, .44 }, new[] { .53, .56, .47, .44 } }),
            (Preset.MainLeft, "Principal à esquerda + pilha", new[] { new[] { 0, 0, .56, 1.0 }, new[] { .62, 0, .38, .44 }, new[] { .62, .56, .38, .44 } }),
            (Preset.MainTop, "Principal em cima + lado a lado", new[] { new[] { 0, 0, 1, .56 }, new[] { 0, .64, .47, .36 }, new[] { .53, .64, .47, .36 } }),
        };
        foreach (var (preset, tip, rects) in presets)
        {
            var canvas = new Canvas { Width = 20, Height = 14 };
            foreach (var r in rects)
            {
                var rect = new Rectangle { Width = r[2] * 20, Height = r[3] * 14, RadiusX = 1, RadiusY = 1, Fill = (Brush)FindResource("TextDim") };
                Canvas.SetLeft(rect, r[0] * 20);
                Canvas.SetTop(rect, r[1] * 14);
                canvas.Children.Add(rect);
            }
            var button = new Button { Content = canvas, ToolTip = tip, Style = (Style)FindResource("GhostButton"), Padding = new Thickness(6, 4, 6, 4) };
            AutomationProperties.SetName(button, tip);
            button.Click += (_, _) =>
            {
                if (_mode == LayoutMode.Tabs) GridModeButton.IsChecked = true;
                Mutate(() => { _tree.ApplyPreset(preset); _maximized = null; });
            };
            PresetPanel.Children.Add(button);
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var mode = TabsModeButton.IsChecked == true ? LayoutMode.Tabs : LayoutMode.Grid;
        if (mode == _mode) return;
        Mutate(() => { _mode = mode; _maximized = null; });
        if (_active is not null) SetActive(_active, focus: true);
    }

    private void Equalize_Click(object sender, RoutedEventArgs e) => Mutate(() => { _tree.Equalize(); _maximized = null; });

    private void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        Title = $"{GroupName}  ·  SplitDeck";
        RaiseChanged();
    }

    private void Save_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncingSave || (SaveToggle.IsChecked == true) == IsSaved) return;
        if (IsSaved)
        {
            // Já salvo: desligar sem perguntar era fácil demais de fazer por engano. Só desliga se confirmar.
            var forget = ChoiceDialog.Ask(this, "SplitDeck", "Deixar de lembrar este grupo?",
                new ChoiceOption("Deixar de lembrar", "O grupo continua aberto, mas não será reaberto sozinho.", Primary: true),
                new ChoiceOption("Cancelar")) == 0;
            if (!forget) { SetSaved(true); return; } // devolve o botão ao estado "salvo" (sem disparar o evento)
        }
        SetSaved(SaveToggle.IsChecked == true);
        SaveToggled?.Invoke(this);
        Status(IsSaved ? "Grupo salvo: layout e apps serão lembrados." : "Grupo não será mais lembrado.");
    }

    private void SetSaved(bool saved)
    {
        IsSaved = saved;
        _syncingSave = true;
        SaveToggle.IsChecked = saved;
        _syncingSave = false;
        SaveIcon.Text = saved ? "\uE735" : "\uE734"; // estrela cheia / vazia
        SaveLabel.Text = saved ? "Salvo" : "Salvar";
    }

    /// <summary>Ícone do Segoe MDL2 Assets (glifo separado do texto).</summary>
    private static TextBlock GlyphIcon(char glyph, double size = 12) =>
        new() { Text = glyph.ToString(), Style = (Style)Application.Current.Resources["IconText"], FontSize = size };

    private void NewGroup_Click(object sender, RoutedEventArgs e) => NewGroupRequested?.Invoke(this);

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = PlacementMode.Bottom };
        foreach (var candidate in WindowCatalog.Enumerate())
        {
            var item = new MenuItem { Header = candidate.Display, IsEnabled = candidate.CanEmbed };
            item.Click += (_, _) => TryAdd(candidate, out _);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Nenhuma outra janela aberta", IsEnabled = false });
        menu.IsOpen = true;
    }

    // ================= Visual de um painel =================

    private sealed class PaneView
    {
        private readonly TextBlock _title;
        private readonly StackPanel _buttons;
        private bool _active, _hover;

        public Border Root { get; }
        public Border Header { get; }
        public Button MaximizeButton { get; }
        public Button CloseButton { get; }
        public Button ModeButton { get; }

        public PaneView(IGroupedWindow host, TextBlock title)
        {
            var res = Application.Current.Resources;
            var ghost = (Style)res["GhostButton"];
            _title = title;
            _title.FontSize = 11.5;
            _title.VerticalAlignment = VerticalAlignment.Center;
            _title.Margin = new Thickness(4, 0, 4, 0);

            MaximizeButton = new Button { Style = ghost, Padding = new Thickness(6, 0, 6, 0), Focusable = false };
            CloseButton = new Button { Content = GlyphIcon('\uE8BB', 10), Style = ghost, Padding = new Thickness(6, 0, 6, 0), Focusable = false, ToolTip = "Desagrupar (o app continua aberto)" };
            ModeButton = new Button
            {
                Content = GlyphIcon(host.Mode == EmbedMode.Dock ? '\uE8A7' : '\uE8A1'), Style = ghost, Padding = new Thickness(6, 0, 6, 0), Focusable = false,
                ToolTip = "Como mostrar este app: " + ModeTitle(host.Mode) + ". Clique para trocar.",
            };
            AutomationProperties.SetName(ModeButton, "Como mostrar este app");
            _buttons = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Hidden };
            if (host is not PlaceholderHost) _buttons.Children.Add(ModeButton);
            _buttons.Children.Add(MaximizeButton);
            _buttons.Children.Add(CloseButton);

            var grip = GlyphIcon('\uE76F', 10);
            grip.Margin = new Thickness(7, 0, 2, 0);
            grip.Foreground = (Brush)res["TextDim"];
            grip.ToolTip = "Arraste para reorganizar";
            var bar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_buttons, Dock.Right);
            bar.Children.Add(_buttons);
            bar.Children.Add(grip);
            bar.Children.Add(_title);

            Header = new Border { Height = 24, Background = (Brush)res["Surface"], Child = bar, Cursor = Cursors.SizeAll };

            var body = new DockPanel { Background = (Brush)res["Bg"] };
            DockPanel.SetDock(Header, Dock.Top);
            body.Children.Add(Header);
            body.Children.Add(host.View);
            Root = new Border { BorderThickness = new Thickness(1), Child = body };
            SetActive(false);
        }

        public void SetActive(bool active) { _active = active; Refresh(); }
        public void SetHover(bool hover) { _hover = hover; Refresh(); }

        private void Refresh()
        {
            var res = Application.Current.Resources;
            Root.BorderBrush = (Brush)res[_active ? "Accent" : "Line"];
            Header.Background = (Brush)res[_active ? "Surface2" : "Surface"];
            _title.Foreground = (Brush)res[_active ? "Text" : "TextDim"];
            _buttons.Visibility = _active || _hover ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
