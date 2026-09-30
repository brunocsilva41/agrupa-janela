using AgrupaJanela.Native;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Hospeda uma janela real de outro processo dentro do WPF.
/// A janela alvo vira filha (WS_CHILD) de um HWND "wrapper" nosso; o WPF posiciona o wrapper
/// e nós ajustamos a janela alvo ao tamanho do wrapper em pixels físicos.
/// </summary>
public sealed class EmbeddedWindowHost : HwndHost, IGroupedWindow
{
    private readonly Snapshot _snapshot;
    private nint _wrapper;
    private bool _released;
    private bool _built;

    /// <summary>
    /// Chromium/Electron mantêm uma borda de redimensionamento (≈7 px) mesmo como janela filha, que aparece como
    /// faixa preta. Nesses apps a janela é posicionada um pouco maior que o painel e a sobra fica cortada pelo painel.
    /// </summary>
    private readonly bool _cropFrame;

    /// <summary>
    /// Depois que o tamanho assenta, pede ao app para redesenhar tudo (inclusive a moldura). Sem isso, apps como o
    /// console (conhost) não pintam a área que cresceu e sobra uma faixa preta com a barra de rolagem no lugar antigo.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public nint Target { get; }
    public uint ProcessId { get; }
    public string CurrentTitle { get; private set; }
    public bool IsAttached => !_released && Win32.IsWindow(Target);
    public AppIdentity Identity { get; }

    /// <summary>O usuário clicou dentro da janela incorporada.</summary>
    public event Action<IGroupedWindow>? TargetClicked;
    public EmbedMode Mode => EmbedMode.Reparent;
    public FrameworkElement View => this;

    private EmbeddedWindowHost(WindowCandidate candidate, nint wrapper, Snapshot snapshot)
    {
        Target = candidate.Handle;
        ProcessId = candidate.ProcessId;
        CurrentTitle = candidate.Title;
        _wrapper = wrapper;
        _snapshot = snapshot;
        Identity = AppIdentity.Of(candidate);
        _cropFrame = Win32.GetClass(Target).StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            // Só invalida (sem UPDATENOW): o app redesenha no ritmo dele e um app travado nunca trava o nosso.
            if (IsAttached) Win32.RedrawWindow(Target, 0, 0, Win32.RDW_INVALIDATE | Win32.RDW_ERASE | Win32.RDW_FRAME | Win32.RDW_ALLCHILDREN);
        };
        HostRegistry.Add(this);
    }

    /// <summary>Incorpora a janela imediatamente. Lança exceção com mensagem legível se não for possível.</summary>
    public static EmbeddedWindowHost Create(WindowCandidate candidate, nint ownerWindow)
    {
        var fresh = WindowCatalog.Describe(candidate.Handle)
            ?? throw new InvalidOperationException($"A janela \"{candidate.Title}\" não existe mais ou não pode ser agrupada.");
        if (fresh.BlockReason is { } reason)
            throw new InvalidOperationException($"\"{fresh.Title}\": {reason}.");

        var wrapper = CreateWrapper(ownerWindow);
        try
        {
            return new EmbeddedWindowHost(fresh, wrapper, Attach(fresh.Handle, wrapper));
        }
        catch
        {
            Win32.DestroyWindow(wrapper);
            throw;
        }
    }

    /// <summary>Atualiza o título; retorna true se mudou.</summary>
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
        if (IsAttached) Win32.SetFocus(Target);
    }

    /// <summary>Devolve a janela à área de trabalho com estilo, posição e estado originais. Idempotente.</summary>
    public void Release(bool keepPosition = false)
    {
        if (_released) return;
        _released = true;
        HostRegistry.Remove(this);
        _settle.Stop();
        if (!Win32.IsWindow(Target)) return;

        Win32.SetParent(Target, 0);
        Win32.SetWindowLongPtr(Target, Win32.GWL_STYLE, _snapshot.Style);
        Win32.SetWindowLongPtr(Target, Win32.GWL_EXSTYLE, _snapshot.ExStyle);
        var placement = _snapshot.Placement;
        if (placement.ShowCmd == Win32.SW_SHOWMINIMIZED) placement.ShowCmd = Win32.SW_SHOWNORMAL;
        Win32.SetWindowPlacement(Target, ref placement);
        Win32.SetWindowPos(Target, 0, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        Win32.SetParent(_wrapper, hwndParent.Handle);
        _built = true;
        return new HandleRef(this, _wrapper);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Release();
        Win32.DestroyWindow(hwnd.Handle);
        _wrapper = 0;
    }

    protected override void Dispose(bool disposing)
    {
        Release();
        if (!_built && _wrapper != 0)
        {
            Win32.DestroyWindow(_wrapper);
            _wrapper = 0;
        }
        base.Dispose(disposing);
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_PARENTNOTIFY)
        {
            var evt = (int)(wParam & 0xFFFF);
            if (evt is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN) TargetClicked?.Invoke(this);
        }
        if (msg == Win32.WM_SIZE && IsAttached)
        {
            var width = (int)(lParam & 0xFFFF);
            var height = (int)((lParam >> 16) & 0xFFFF);
            var (left, top, right, bottom) = _cropFrame ? FrameInsets() : (0, 0, 0, 0);
            Win32.SetWindowPos(Target, 0, -left, -top, Math.Max(1, width + left + right), Math.Max(1, height + top + bottom),
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_ASYNCWINDOWPOS);
            _settle.Stop();
            _settle.Start(); // reinicia: só redesenha quando o redimensionamento parar
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    /// <summary>Quanto a janela reserva fora da área cliente (sem contar o que não é borda). Limitado para nunca cortar conteúdo.</summary>
    private (int Left, int Top, int Right, int Bottom) FrameInsets()
    {
        if (!Win32.GetWindowRect(Target, out var window) || !Win32.GetClientRect(Target, out var client)) return (0, 0, 0, 0);
        var origin = new Win32.POINT();
        if (!Win32.ClientToScreen(Target, ref origin)) return (0, 0, 0, 0);
        static int Clamp(int v) => Math.Clamp(v, 0, 12);
        return (Clamp(origin.X - window.Left), Clamp(origin.Y - window.Top),
                Clamp(window.Right - (origin.X + client.Right)), Clamp(window.Bottom - (origin.Y + client.Bottom)));
    }

    private static nint CreateWrapper(nint parent)
    {
        // Permite hospedar janelas com modo de DPI diferente do nosso (Windows 10 1803+).
        int? previous = null;
        try { previous = Win32.SetThreadDpiHostingBehavior(Win32.DPI_HOSTING_BEHAVIOR_MIXED); }
        catch (EntryPointNotFoundException) { }
        try
        {
            var style = (uint)(Win32.WS_CHILD | Win32.WS_CLIPCHILDREN) | Win32.SS_NOTIFY;
            var wrapper = Win32.CreateWindowEx(0, "Static", "", style, 0, 0, 1, 1, parent, 0, 0, 0);
            if (wrapper == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao criar o painel nativo.");
            return wrapper;
        }
        finally
        {
            if (previous is int value) Win32.SetThreadDpiHostingBehavior(value);
        }
    }

    private static Snapshot Attach(nint target, nint wrapper)
    {
        var placement = Win32.WINDOWPLACEMENT.Create();
        Win32.GetWindowPlacement(target, ref placement);
        if (Win32.IsIconic(target) || Win32.IsZoomed(target)) Win32.ShowWindow(target, Win32.SW_RESTORE);

        var style = Win32.GetWindowLongPtr(target, Win32.GWL_STYLE);
        var exStyle = Win32.GetWindowLongPtr(target, Win32.GWL_EXSTYLE);
        const long frame = Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX;
        const long exFrame = Win32.WS_EX_APPWINDOW | Win32.WS_EX_WINDOWEDGE | Win32.WS_EX_CLIENTEDGE | Win32.WS_EX_DLGMODALFRAME;

        // Documentação do SetParent: definir WS_CHILD antes de trocar o parent.
        Win32.SetWindowLongPtr(target, Win32.GWL_STYLE, (nint)(((long)style & ~frame) | Win32.WS_CHILD | Win32.WS_VISIBLE));
        Marshal.SetLastPInvokeError(0);
        var previousParent = Win32.SetParent(target, wrapper);
        var error = Marshal.GetLastPInvokeError();
        if (previousParent == 0 && error != 0)
        {
            Win32.SetWindowLongPtr(target, Win32.GWL_STYLE, style);
            throw new InvalidOperationException($"O Windows recusou incorporar a janela (erro {error}: {new Win32Exception(error).Message}).");
        }

        Win32.SetWindowLongPtr(target, Win32.GWL_EXSTYLE, (nint)((long)exStyle & ~exFrame));
        // Mantém o tamanho atual: o painel ajusta em seguida. (Encolher muito faz o console reorganizar/embaralhar o texto.)
        Win32.SetWindowPos(target, 0, 0, 0, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
        return new Snapshot(style, exStyle, placement);
    }

    private readonly record struct Snapshot(nint Style, nint ExStyle, Win32.WINDOWPLACEMENT Placement);
}

/// <summary>Registro global para devolver todas as janelas em caso de erro ou encerramento inesperado.</summary>
public static class HostRegistry
{
    private static readonly HashSet<IGroupedWindow> Hosts = new();

    internal static void Add(IGroupedWindow host) { lock (Hosts) Hosts.Add(host); }
    internal static void Remove(IGroupedWindow host) { lock (Hosts) Hosts.Remove(host); }

    public static void ReleaseAll()
    {
        IGroupedWindow[] all;
        lock (Hosts) all = Hosts.ToArray();
        foreach (var host in all)
        {
            try { host.Release(); } catch { /* continua devolvendo as demais */ }
        }
    }
}
