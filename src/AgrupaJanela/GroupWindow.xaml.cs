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
    private readonly DispatcherTimer _statusClear = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DropOverlay _overlay = new();

    private LayoutMode _mode = LayoutMode.Grid;
    private IGroupedWindow? _active;
    private IGroupedWindow? _maximized;
    private bool _syncingSave;
    private bool _ready, _loading, _closing, _closed, _closeDecided, _closeApps;

    // Arraste de painel pelo cabeçalho
    private IGroupedWindow? _dragSource;
    private Point _dragStart;
    private bool _dragging;
    private (IGroupedWindow? Target, DropZone Zone) _drop;

    public Guid Id { get; }
    public string GroupName => string.IsNullOrWhiteSpace(NameBox.Text) ? "Grupo" : NameBox.Text.Trim();
    public bool IsSaved { get; private set; }
    public int Count => _tree.Leaves().Count();
    private List<IGroupedWindow> Hosts => _tree.Leaves().Select(l => l.Host).ToList();

    /// <summary>
    /// Algo mudou. persist=true: mudança feita pelo usuário (regrava o layout salvo).
    /// persist=false: só estado da janela (app fechado por fora, fechamento, reabertura) — o layout salvo é preservado.
    /// </summary>
    public event Action<GroupWindow, bool>? Changed;
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
        _statusClear.Tick += (_, _) => { _statusClear.Stop(); StatusText.Text = ""; };
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
            Status(error);
            return false;
        }
        if (AppController.Instance.OwnerOf(candidate.Handle) is { } owner)
        {
            error = $"\"{candidate.Title}\" já está em \"{owner.GroupName}\".";
            Status(error);
            return false;
        }
        IGroupedWindow host;
        try { host = CreateHost(candidate); }
        catch (Exception ex)
        {
            error = ex.Message;
            Status("Não foi possível agrupar: " + ex.Message);
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

    public IGroupedWindow? FindHost(nint target) =>
        Hosts.FirstOrDefault(h => h.Target == target) ?? _pendingHosts.OfType<IGroupedWindow>().FirstOrDefault(h => h.Target == target);

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
        if (!ok) ChoiceDialog.Ask(this, "Agrupa-Janela", error, ("OK", "", true));
        Activate();
        return ok;
    }

    public SavedGroup ToSaved()
    {
        var bounds = WindowState == WindowState.Normal || RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new SavedGroup
        {
            Id = Id, Name = GroupName, Tabs = _mode == LayoutMode.Tabs, Layout = _tree.ToDto(),
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    /// <summary>Reabre um grupo salvo: reencontra ou reabre cada app e monta o layout salvo.</summary>
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
        var apps = new List<AppIdentity>();
        CollectApps(saved.Layout, apps);
        var hosts = _pendingHosts;
        hosts.Clear();
        var taken = AppController.Instance.HostedHandles();
        for (var i = 0; i < apps.Count; i++)
        {
            Status($"Abrindo apps… {i + 1}/{apps.Count}");
            IGroupedWindow? host = null;
            WindowCandidate? candidate = null;
            try { candidate = await AppLauncher.ResolveAsync(apps[i], taken); }
            catch { /* um app que falha não impede os outros */ }
            if (_closed) break;
            if (candidate is not null)
            {
                try { host = CreateHost(candidate); taken.Add(candidate.Handle); }
                catch { host = null; }
            }
            hosts.Add(host);
        }

        if (_closed)
        {
            ReleasePending();
            return;
        }

        var queue = new Queue<IGroupedWindow?>(hosts.ToList());
        hosts.Clear(); // a partir daqui eles estão na árvore
        _tree.Load(saved.Layout, _ => queue.Count > 0 ? queue.Dequeue() : null);
        _loading = false;
        Rebuild();
        if (Hosts.FirstOrDefault() is { } first) SetActive(first, focus: true);
        var ok = hosts.Count(h => h is not null);
        Status(ok == apps.Count ? $"{ok} app(s) restaurado(s)." : $"{ok} de {apps.Count} app(s) restaurado(s); os demais não abriram a tempo.");
        RaiseChanged(persist: false); // não esquece os apps que não abriram
    }

    private static void CollectApps(NodeDto? node, List<AppIdentity> apps)
    {
        if (node is null) return;
        if (node.App is not null) { apps.Add(node.App); return; }
        foreach (var child in node.Children) CollectApps(child, apps);
    }

    // ================= Ciclo de vida =================

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closeDecided && Count > 0)
        {
            var closeApps = ChoiceDialog.AskCloseGroups(this, $"\"{GroupName}\"");
            if (closeApps is null) { e.Cancel = true; return; }
            _closeApps = closeApps.Value;
        }

        if (IsSaved) Changed?.Invoke(this, false); // guarda posição/tamanho sem mexer no layout salvo
        _closing = true;
        _watch.Stop();
        _statusClear.Stop();
        ReleasePending(); // apps já reabertos de um grupo que ainda estava carregando
        _overlay.Close();

        // Precisa acontecer ANTES do WPF destruir o HWND desta janela: janelas filhas seriam destruídas junto.
        foreach (var host in Hosts)
        {
            var target = host.Target;
            var attached = host.IsAttached;
            host.Release();
            DetachFromTree(host);
            host.Dispose();
            if (_closeApps && attached) Win32.PostMessage(target, Win32.WM_CLOSE, 0, 0);
        }
        base.OnClosing(e);
    }

    /// <summary>Hosts criados durante o carregamento, ainda fora da árvore. Precisam ser devolvidos se o grupo fechar antes.</summary>
    private readonly List<IGroupedWindow?> _pendingHosts = new();

    private void ReleasePending()
    {
        foreach (var host in _pendingHosts.OfType<IGroupedWindow>().ToList())
        {
            try { host.Release(); DetachFromTree(host); host.Dispose(); } catch { }
        }
        _pendingHosts.Clear();
    }

    public IEnumerable<nint> HostedHandles => Hosts.Select(h => h.Target).Concat(_pendingHosts.OfType<IGroupedWindow>().Select(h => h.Target));

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
        if (resolved == host.Mode) { Status($"Modo {EmbedPolicy.Label(resolved).ToLowerInvariant()} (já estava)."); return; }

        var hwnd = host.Target;
        host.Release();
        DetachFromTree(host);
        host.Dispose();
        var candidate = WindowCatalog.Describe(hwnd);
        IGroupedWindow? replacement = null;
        try { if (candidate is not null) replacement = CreateHost(candidate, resolved); }
        catch (Exception ex) { Status("Não deu para trocar o modo: " + ex.Message); }
        Mutate(() =>
        {
            if (replacement is not null) _tree.Replace(host, replacement);
            else _tree.Remove(host);
        });
        if (replacement is not null)
        {
            SetActive(replacement, focus: true);
            Status($"\"{replacement.CurrentTitle}\" agora está {EmbedPolicy.Label(resolved).ToLowerInvariant()}. Lembrado para {System.IO.Path.GetFileName(host.Identity.ExePath)}.");
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
            foreach (var host in lost) { DetachFromTree(host); host.Dispose(); }
            Mutate(() => { foreach (var host in lost) _tree.Remove(host); }, persist: false);
            Status($"{string.Join(", ", lost.Select(h => $"\"{h.CurrentTitle}\""))} foi fechada e saiu do grupo.");
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
        Mutate(() => _tree.Remove(host));
        if (closeApp && attached) Win32.PostMessage(target, Win32.WM_CLOSE, 0, 0);
        Status(keepPosition ? "Janela saiu do grupo." : "Janela devolvida à área de trabalho.");
    }

    // ================= Layout =================

    /// <summary>Guarda os tamanhos atuais, aplica a mudança, redesenha e avisa.</summary>
    private void Mutate(Action change, bool persist = true)
    {
        CaptureWeights();
        change();
        Rebuild();
        RaiseChanged(persist);
    }

    private void RaiseChanged(bool persist = true)
    {
        if (_ready && !_loading && !_closing) Changed?.Invoke(this, persist);
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

        Title = $"{GroupName}  ·  Agrupa-Janela";
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
        view.MaximizeButton.Content = _maximized == host ? "⤡" : "⤢";
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
            header.Children.Add(new TextBlock { Text = hint, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => SwitchMode(host, mode);
            menu.Items.Add(item);
        }
        Add(EmbedMode.Auto, "Automático", EmbedPolicy.Explain(host.Target, host.ProcessId, host.Identity.ExePath, app.Settings));
        Add(EmbedMode.Reparent, "Incorporada", "A janela vira parte do painel. Ideal para terminais e apps clássicos.");
        Add(EmbedMode.Dock, "Acoplada", "A janela continua real, posicionada sobre o painel. Ideal para navegadores, VS Code, Discord e apps com GPU.");
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
            var close = new Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 10, ToolTip = "Desagrupar (o app continua aberto)", Focusable = false };
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

    private void Status(string text)
    {
        StatusText.Text = text;
        _statusClear.Stop();
        _statusClear.Start();
    }

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
        Title = $"{GroupName}  ·  Agrupa-Janela";
        RaiseChanged();
    }

    private void Save_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncingSave || (SaveToggle.IsChecked == true) == IsSaved) return;
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
        SaveLabel.Text = saved ? "★ Salvo" : "☆ Salvar";
    }

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
            CloseButton = new Button { Content = "✕", Style = ghost, Padding = new Thickness(6, 0, 6, 0), FontSize = 10, Focusable = false, ToolTip = "Desagrupar (o app continua aberto)" };
            ModeButton = new Button
            {
                Content = host.Mode == EmbedMode.Dock ? "◳" : "▣", Style = ghost, Padding = new Thickness(6, 0, 6, 0), Focusable = false,
                ToolTip = host.Mode == EmbedMode.Dock
                    ? "Acoplada: janela real posicionada sobre o painel (ideal para navegadores e apps com GPU). Clique para trocar."
                    : "Incorporada: janela dentro do painel (ideal para terminais e apps clássicos). Clique para trocar.",
            };
            AutomationProperties.SetName(ModeButton, "Modo do painel");
            _buttons = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Hidden };
            _buttons.Children.Add(ModeButton);
            _buttons.Children.Add(MaximizeButton);
            _buttons.Children.Add(CloseButton);

            var grip = new TextBlock { Text = "⋮⋮", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 2, 0), Foreground = (Brush)res["TextDim"], ToolTip = "Arraste para reorganizar" };
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
