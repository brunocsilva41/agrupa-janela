using AgrupaJanela.Native;
using System.Windows.Threading;

namespace AgrupaJanela.Shell;

/// <summary>
/// Observa quando o usuário arrasta a janela de outro app (pela barra de título).
/// Enquanto arrasta com Shift pressionado, avisa a posição do cursor; ao soltar, avisa o fim.
/// </summary>
public sealed class WindowDragWatcher : IDisposable
{
    private readonly Win32.WinEventProc _proc; // referência mantida para o GC não coletar o callback
    private readonly nint _hook;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private nint _dragging;

    /// <summary>Início do arraste de uma janela (para tirar uma "foto" das janelas embaixo, uma vez só).</summary>
    public event Action<nint>? DragStarted;
    /// <summary>(janela arrastada, x, y, shift) durante o arraste.</summary>
    public event Action<nint, int, int, bool>? Dragging;
    /// <summary>(janela arrastada, x, y, shift) ao soltar.</summary>
    public event Action<nint, int, int, bool>? Dropped;

    public WindowDragWatcher()
    {
        _proc = OnEvent;
        _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MOVESIZESTART, Win32.EVENT_SYSTEM_MOVESIZEEND, 0, _proc, 0, 0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        _timer.Tick += (_, _) =>
        {
            if (_dragging == 0 || !Win32.GetCursorPos(out var p)) return;
            Dragging?.Invoke(_dragging, p.X, p.Y, Win32.IsShiftDown());
        };
    }

    private void OnEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Exceção aqui voltaria para código nativo e derrubaria o processo (perdendo janelas agrupadas).
        try { Handle(evt, hwnd, idObject); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void Handle(uint evt, nint hwnd, int idObject)
    {
        if (idObject != 0) return; // só a janela em si
        if (evt == Win32.EVENT_SYSTEM_MOVESIZESTART)
        {
            _dragging = hwnd;
            DragStarted?.Invoke(hwnd);
            _timer.Start();
        }
        else if (evt == Win32.EVENT_SYSTEM_MOVESIZEEND && hwnd == _dragging)
        {
            _timer.Stop();
            _dragging = 0;
            if (!Win32.GetCursorPos(out var p)) return;
            var shift = Win32.IsShiftDown();
            // Soltar pode abrir diálogos e gravar arquivos: roda fora do callback nativo.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Dropped?.Invoke(hwnd, p.X, p.Y, shift));
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != 0) Win32.UnhookWinEvent(_hook);
    }
}
