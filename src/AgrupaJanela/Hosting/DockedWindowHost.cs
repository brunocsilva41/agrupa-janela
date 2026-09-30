using AgrupaJanela.Native;
using AgrupaJanela.Persistence;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Modo Acoplado: a janela do outro app continua top-level (renderização, GPU, menus e atalhos 100% nativos)
/// e é mantida exatamente sobre o painel. Este elemento é só o "lugar" dela no layout:
/// quando ele aparece/muda de posição, a janela vai junto; quando sai da tela (aba inativa, painel
/// maximizado de outro, grupo minimizado), a janela é escondida.
/// O grupo vira o dono (owner) da janela: o Windows a mantém acima do grupo, esconde junto ao minimizar e tira
/// o botão da barra de tarefas. Medido: se o nosso processo morrer, a janela do app sobrevive (o owner vira 0).
/// </summary>
public sealed class DockedWindowHost : FrameworkElement, IGroupedWindow
{
    private const int UndockDistance = 80; // px físicos que o usuário precisa arrastar para "tirar" do grupo

    private readonly Window _group;
    private readonly nint _groupHwnd;
    private readonly DockWatcher _watcher;
    private readonly Win32.WINDOWPLACEMENT _placement;
    private Int32Rect _lastRect;
    private bool _shown, _released, _userMoving;

    public nint Target { get; }
    public uint ProcessId { get; }
    public string CurrentTitle { get; private set; }
    public AppIdentity Identity { get; }
    public EmbedMode Mode => EmbedMode.Dock;
    public FrameworkElement View => this;
    public bool IsAttached => !_released && Win32.IsWindow(Target);

    public event Action<IGroupedWindow>? TargetClicked;
    /// <summary>O usuário arrastou a janela para longe do painel: deve sair do grupo e ficar onde foi solta.</summary>
    public event Action<DockedWindowHost>? UndockRequested;
    /// <summary>O próprio app se minimizou (botão da barra dele): o grupo deve minimizar junto.</summary>
    public event Action<DockedWindowHost>? MinimizeRequested;

    private DockedWindowHost(WindowCandidate candidate, Window group, DockWatcher watcher)
    {
        Target = candidate.Handle;
        ProcessId = candidate.ProcessId;
        CurrentTitle = candidate.Title;
        Identity = AppIdentity.Of(candidate);
        _group = group;
        _groupHwnd = new WindowInteropHelper(group).EnsureHandle();
        _watcher = watcher;
        _placement = Win32.WINDOWPLACEMENT.Create();
        Win32.GetWindowPlacement(Target, ref _placement);

        ClipToBounds = true;
        Loaded += (_, _) => Sync(force: true);
        // Ao reconstruir o layout o painel sai e volta na mesma hora: só esconde se continuar fora (evita piscar).
        Unloaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { if (!IsLoaded) HideTarget(); });
        IsVisibleChanged += (_, _) => Sync(force: true);
        // LayoutUpdated dispara em toda passada de layout do app: só escuta enquanto está na tela.
        Loaded += (_, _) => { LayoutUpdated -= OnLayoutUpdated; LayoutUpdated += OnLayoutUpdated; };
        Unloaded += (_, _) => LayoutUpdated -= OnLayoutUpdated;

        Win32.SetOwner(Target, _groupHwnd);
        _watcher.Track(Target, ProcessId);
        _watcher.LocationChanged += OnLocationChanged;
        _watcher.MoveSizeStarted += OnMoveSizeStarted;
        _watcher.MoveSizeEnded += OnMoveSizeEnded;
        _watcher.Minimized += OnMinimized;
        _watcher.Activated += OnActivated;

        DockRecovery.Add(Target, ProcessId);
        HostRegistry.Add(this);
    }

    public static DockedWindowHost Create(WindowCandidate candidate, Window group, DockWatcher watcher)
    {
        var fresh = WindowCatalog.Describe(candidate.Handle)
            ?? throw new InvalidOperationException($"A janela \"{candidate.Title}\" não existe mais ou não pode ser agrupada.");
        if (fresh.BlockReason is { } reason) throw new InvalidOperationException($"\"{fresh.Title}\": {reason}.");
        return new DockedWindowHost(fresh, group, watcher);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => Sync(force: false); // só age se o retângulo mudou

    /// <summary>Recoloca a janela sobre o painel. Chamado pelo layout e quando o grupo se move.</summary>
    public void Sync(bool force)
    {
        if (!IsAttached || _userMoving) return;
        var onScreen = IsVisible && PresentationSource.FromVisual(this) is not null && ActualWidth > 1 && ActualHeight > 1
                       && _group.WindowState != WindowState.Minimized && _group.IsVisible;
        if (!onScreen) { HideTarget(); return; }

        var rect = ScreenRect();
        if (!force && _shown && rect == _lastRect) return;
        _lastRect = rect;
        _watcher.NoteOwnMove(Target);
        DockNative.FitVisibleFrame(Target, rect, async: true);
        if (!_shown)
        {
            _shown = true;
            DockNative.ShowNoActivate(Target);
        }
        DockNative.PlaceAbove(Target, _groupHwnd);
    }

    /// <summary>Garante a ordem Z: a janela logo acima do grupo (sem roubar foco).</summary>
    public void RaiseAboveGroup()
    {
        if (_shown && IsAttached) DockNative.PlaceAbove(Target, _groupHwnd);
    }

    private void HideTarget()
    {
        if (!_shown || !IsAttached) { _shown = false; return; }
        _shown = false;
        _watcher.NoteOwnMove(Target);
        DockNative.Hide(Target);
    }

    public bool RefreshTitle()
    {
        if (!IsAttached) return false;
        var title = Win32.GetText(Target);
        if (title.Length == 0 || title == CurrentTitle) return false;
        CurrentTitle = title;
        return true;
    }

    public void FocusTarget()
    {
        if (!IsAttached) return;
        Sync(force: true);
        Win32.SetForegroundWindow(Target);
    }

    public void Release(bool keepPosition = false)
    {
        if (_released) return;
        _released = true;
        _watcher.LocationChanged -= OnLocationChanged;
        _watcher.MoveSizeStarted -= OnMoveSizeStarted;
        _watcher.MoveSizeEnded -= OnMoveSizeEnded;
        _watcher.Minimized -= OnMinimized;
        _watcher.Activated -= OnActivated;
        LayoutUpdated -= OnLayoutUpdated;
        _watcher.Untrack(Target);
        HostRegistry.Remove(this);
        if (!Win32.IsWindow(Target)) { DockRecovery.Remove(Target); return; }

        if (!keepPosition)
        {
            var placement = _placement;
            if (placement.ShowCmd == Win32.SW_SHOWMINIMIZED) placement.ShowCmd = Win32.SW_SHOWNORMAL;
            Win32.SetWindowPlacement(Target, ref placement);
        }
        // Sem dono de novo; esconder e mostrar faz o shell recriar o botão na barra de tarefas (medido).
        Win32.SetOwner(Target, 0);
        DockNative.Hide(Target);
        DockNative.ShowNoActivate(Target);
        DockRecovery.Remove(Target);
    }

    public void Dispose() => Release();

    // ---------- Reações ao que acontece com a janela do app ----------

    private void OnLocationChanged(nint hwnd)
    {
        // O app mudou de tamanho/posição sozinho (ex.: maximizou): volta para o painel.
        if (hwnd == Target && !_userMoving) Sync(force: true);
    }

    private void OnMoveSizeStarted(nint hwnd)
    {
        if (hwnd == Target) _userMoving = true;
    }

    private void OnMoveSizeEnded(nint hwnd)
    {
        if (hwnd != Target) return;
        _userMoving = false;
        // Com Shift, quem decide é o arrastar-para-agrupar (pode estar indo para outro grupo).
        if (Win32.IsShiftDown()) return;
        if (DockNative.TryGetVisibleFrame(Target, out var frame)
            && Math.Abs(frame.Width - _lastRect.Width) < 8 && Math.Abs(frame.Height - _lastRect.Height) < 8 // moveu, não redimensionou
            && Distance(frame, _lastRect) > UndockDistance)
            UndockRequested?.Invoke(this);
        else
            Sync(force: true); // soltou perto: encaixa de volta
    }

    private void OnMinimized(nint hwnd)
    {
        if (hwnd == Target) MinimizeRequested?.Invoke(this);
    }

    private void OnActivated(nint hwnd)
    {
        if (hwnd == Target) TargetClicked?.Invoke(this);
    }

    private Int32Rect ScreenRect()
    {
        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        return new Int32Rect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X - topLeft.X), (int)Math.Round(bottomRight.Y - topLeft.Y));
    }

    private static double Distance(Int32Rect a, Int32Rect b)
    {
        double dx = (a.X + a.Width / 2.0) - (b.X + b.Width / 2.0), dy = (a.Y + a.Height / 2.0) - (b.Y + b.Height / 2.0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Fundo do painel enquanto a janela está escondida/carregando.
        dc.DrawRectangle((Brush)Application.Current.Resources["Bg"], null, new Rect(0, 0, ActualWidth, ActualHeight));
    }
}

/// <summary>
/// Janelas acopladas ficam escondidas (aba inativa) e fora da barra de tarefas. Se o app for encerrado à força,
/// elas ficariam invisíveis. Este arquivo lista quem está acoplado; na próxima abertura, reaparecem.
/// Cada entrada confere janela + processo + executável + horário de início (o Windows reaproveita handles e PIDs).
/// </summary>
public static class DockRecovery
{
    private sealed record Entry(long Hwnd, uint Pid, string? Exe, long ProcessStartUtcTicks);

    private static readonly Dictionary<long, Entry> Docked = new();

    public static void Add(nint hwnd, uint pid)
    {
        lock (Docked) { Docked[hwnd] = new Entry(hwnd, pid, Win32.GetProcessPath(pid), StartTicks(pid)); Flush(); }
    }

    public static void Remove(nint hwnd) { lock (Docked) { if (Docked.Remove(hwnd)) Flush(); } }

    /// <summary>Chamado ao iniciar: mostra de volta janelas que ficaram escondidas por um encerramento forçado.</summary>
    public static int RecoverOrphans()
    {
        var recovered = 0;
        try
        {
            var file = AppPaths.RecoveryFile;
            if (!File.Exists(file)) return 0;
            if (new FileInfo(file).Length < 64 * 1024)
            {
                var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(file)) ?? new();
                foreach (var e in list.Take(64))
                {
                    var hwnd = (nint)e.Hwnd;
                    if (!Win32.IsWindow(hwnd)) continue;
                    Win32.GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid != e.Pid || StartTicks(pid) != e.ProcessStartUtcTicks
                        || !string.Equals(Win32.GetProcessPath(pid), e.Exe, StringComparison.OrdinalIgnoreCase)) continue;
                    Win32.SetOwner(hwnd, 0);
                    DockNative.Hide(hwnd);          // esconder e mostrar recria o botão na barra de tarefas
                    DockNative.ShowNoActivate(hwnd);
                    recovered++;
                }
            }
            File.Delete(file);
        }
        catch { /* recuperação é best-effort */ }
        return recovered;
    }

    private static long StartTicks(uint pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); return p.StartTime.ToUniversalTime().Ticks; }
        catch { return 0; }
    }

    private static void Flush()
    {
        try
        {
            var file = AppPaths.RecoveryFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (Docked.Count == 0) { File.Delete(file); return; }
            File.WriteAllText(file, JsonSerializer.Serialize(Docked.Values.ToList()));
        }
        catch { }
    }
}
