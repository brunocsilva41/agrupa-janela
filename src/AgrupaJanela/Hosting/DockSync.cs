using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Utilitários nativos do modo Acoplado. Tudo em pixels físicos de tela.
/// A janela do outro app continua top-level; aqui só a posicionamos, escondemos e mexemos na ordem Z.
/// </summary>
internal static class DockNative
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SW_HIDE = 0, SW_SHOWNA = 8, SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    private const uint SWP_NOOWNERZORDER = 0x200, SWP_ASYNCWINDOWPOS = 0x4000;
    private const uint GW_HWNDPREV = 3, GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
    private const long WS_EX_TOPMOST = 0x8;
    private static readonly nint HWND_TOP = 0;

    /// <summary>Frame visível (DWMWA_EXTENDED_FRAME_BOUNDS); se o DWM falhar, cai para GetWindowRect.</summary>
    public static bool TryGetVisibleFrame(nint hwnd, out Int32Rect rect)
    {
        rect = Int32Rect.Empty;
        if (hwnd == 0) return false;
        if (Api.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Api.RECT>()) != 0
            && !Api.GetWindowRect(hwnd, out r))
            return false;
        rect = new Int32Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        return true;
    }

    /// <summary>
    /// Posiciona a janela para que o frame VISÍVEL coincida com <paramref name="target"/>.
    /// As bordas invisíveis (sombra/redimensionamento do Win10+) variam por app e por DPI, por isso
    /// são medidas na hora em vez de supostas.
    /// </summary>
    public static void FitVisibleFrame(nint hwnd, Int32Rect target, bool async = true, bool keepZOrder = true, nint insertAfter = 0)
    {
        if (hwnd == 0) return;
        // Maximizada/minimizada, o SetWindowPos só mexeria na posição "restaurada" (ou brigaria com o estado).
        if (Api.IsIconic(hwnd) || Api.IsZoomed(hwnd)) Show(hwnd, SW_RESTORE);

        var left = 0; var top = 0; var right = 0; var bottom = 0;
        if (Api.GetWindowRect(hwnd, out var win)
            && Api.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var vis, Marshal.SizeOf<Api.RECT>()) == 0)
        {
            left = vis.Left - win.Left;
            top = vis.Top - win.Top;
            right = win.Right - vis.Right;
            bottom = win.Bottom - vis.Bottom;
        }

        var flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        if (async) flags |= SWP_ASYNCWINDOWPOS;
        if (keepZOrder) flags |= SWP_NOZORDER;
        var after = !keepZOrder && insertAfter != 0 ? insertAfter : HWND_TOP;

        Api.SetWindowPos(hwnd, after, target.X - left, target.Y - top,
            target.Width + left + right, target.Height + top + bottom, flags);
    }

    /// <summary>Coloca <paramref name="hwnd"/> logo acima de <paramref name="below"/> na ordem Z, sem ativar nem mover.</summary>
    public static void PlaceAbove(nint hwnd, nint below)
    {
        if (hwnd == 0 || below == 0 || hwnd == below) return;
        var prev = Api.GetWindow(below, GW_HWNDPREV);
        if (prev == hwnd) return; // já está no lugar: evita um SetWindowPos (e o eco de eventos) à toa

        // SetWindowPos insere DEPOIS de insertAfter; sem ninguém acima de `below` (ou se quem está acima é
        // topmost e `below` não), HWND_TOP é o equivalente e não arrasta a janela para a faixa topmost.
        var insertAfter = prev;
        if (prev == 0 || (IsTopmost(prev) && !IsTopmost(below))) insertAfter = HWND_TOP;

        // Assíncrono: se o outro app estiver travado, a thread de UI não fica esperando por ele.
        Api.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    public static void Hide(nint hwnd) => Show(hwnd, SW_HIDE);

    public static void ShowNoActivate(nint hwnd) => Show(hwnd, SW_SHOWNA);



    private static bool IsTopmost(nint hwnd) => (Api.GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    // ShowWindow cruza para a thread do outro app e espera; com app travado, usamos a versão que só posta.
    private static void Show(nint hwnd, int cmd)
    {
        if (hwnd == 0) return;
        if (Api.IsHungAppWindow(hwnd)) Api.ShowWindowAsync(hwnd, cmd);
        else Api.ShowWindow(hwnd, cmd);
    }

    // ---- Botão da barra de tarefas (ITaskbarList) ----

    private static ITaskbarList? _taskbar;

    /// <summary>
    /// Tira/devolve o botão da barra de tarefas de uma janela (inclusive de outro processo).
    /// A instância COM é criada uma vez, na thread chamadora (STA de UI). Falhas retornam false.
    /// </summary>
    public static bool SetTaskbarButton(nint hwnd, bool visible)
    {
        if (hwnd == 0) return false;
        try
        {
            if (_taskbar is null)
            {
                var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), throwOnError: true)!;
                var instance = (ITaskbarList)Activator.CreateInstance(type)!;
                instance.HrInit();
                _taskbar = instance; // só guarda depois do HrInit dar certo; senão tenta de novo na próxima
            }
            if (visible) _taskbar.AddTab(hwnd);
            else _taskbar.DeleteTab(hwnd);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DockNative.SetTaskbarButton: {ex.Message}");
            return false;
        }
    }

    // A ordem dos métodos precisa bater com a vtable nativa.
    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
    }

    private static class Api
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }
}

/// <summary>
/// Vigia janelas acopladas: ganchos WinEvent por PROCESSO vigiado (nunca globais), out-of-context,
/// criados e atendidos na thread de UI. Assim só recebemos eventos dos apps que importam e o custo
/// fica baixo mesmo com apps "barulhentos".
/// </summary>
public sealed class DockWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int OBJID_WINDOW = 0;
    private const long OwnMoveSuppressMs = 200;

    // Faixas estreitas: a 0x8000–0x80FF inteira traria nome/valor/foco etc. de cada controle do app.
    private static readonly (uint Min, uint Max)[] Ranges =
    {
        (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
        (EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND),
        (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART),
        (EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY),
        (EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE),
    };

    private readonly WinEventProc _proc; // referência mantida para o GC não coletar o callback
    private readonly Dictionary<nint, uint> _tracked = new();                  // hwnd -> pid
    private readonly Dictionary<uint, (nint[] Hooks, int Count)> _pids = new(); // pid -> ganchos + nº de hwnds
    private readonly Dictionary<nint, long> _suppressUntil = new();            // hwnd -> TickCount64
    private readonly HashSet<nint> _pending = new();
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    /// <summary>Posição/tamanho mudou por fora (não por nós). No máximo 1 por hwnd a cada ~30 ms.</summary>
    public event Action<nint>? LocationChanged;
    /// <summary>O usuário começou a arrastar/redimensionar a janela.</summary>
    public event Action<nint>? MoveSizeStarted;
    public event Action<nint>? MoveSizeEnded;
    public event Action<nint>? Minimized;
    public event Action<nint>? Destroyed;
    /// <summary>A janela vigiada virou primeiro plano.</summary>
    public event Action<nint>? Activated;

    public DockWatcher()
    {
        _proc = OnEvent;
        // Um timer só, ligado apenas enquanto há LocationChanged pendente (nada de polling).
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => FlushPending();
    }

    /// <summary>Começa a vigiar hwnd. O primeiro hwnd de um pid cria os ganchos daquele pid.</summary>
    public void Track(nint hwnd, uint pid)
    {
        if (_disposed || hwnd == 0 || pid == 0) return;
        // Console: a janela pertence ao conhost, não ao cmd/powershell (os eventos não chegam pelo PID do shell).
        // Nesse caso usa um gancho global (pid 0), filtrado pela janela como os demais.
        if (Native.Win32.GetClass(hwnd) == "ConsoleWindowClass") pid = uint.MaxValue;
        if (_tracked.TryGetValue(hwnd, out var oldPid))
        {
            if (oldPid == pid) return;
            Untrack(hwnd); // hwnd reaproveitado por outro processo
        }

        if (_pids.TryGetValue(pid, out var entry))
        {
            _pids[pid] = (entry.Hooks, entry.Count + 1);
        }
        else
        {
            var hooks = new nint[Ranges.Length];
            for (var i = 0; i < Ranges.Length; i++)
                hooks[i] = SetWinEventHook(Ranges[i].Min, Ranges[i].Max, 0, _proc, pid == uint.MaxValue ? 0 : pid, 0, WINEVENT_OUTOFCONTEXT);
            _pids[pid] = (hooks, 1);
        }
        _tracked[hwnd] = pid;
    }

    /// <summary>Para de vigiar hwnd. Se era o último do pid, desfaz os ganchos do pid.</summary>
    public void Untrack(nint hwnd)
    {
        if (!_tracked.Remove(hwnd, out var pid)) return;
        _pending.Remove(hwnd);
        _suppressUntil.Remove(hwnd);
        if (!_pids.TryGetValue(pid, out var entry)) return;
        if (entry.Count > 1)
        {
            _pids[pid] = (entry.Hooks, entry.Count - 1);
            return;
        }
        _pids.Remove(pid);
        Unhook(entry.Hooks);
    }

    /// <summary>Ignora por ~200 ms os LocationChanged de hwnd (eco dos nossos próprios SetWindowPos).</summary>
    public void NoteOwnMove(nint hwnd)
    {
        if (!_tracked.ContainsKey(hwnd)) return;
        _suppressUntil[hwnd] = Environment.TickCount64 + OwnMoveSuppressMs;
        _pending.Remove(hwnd); // o que estava pendente vai ser sobrescrito pela nossa posição mesmo
    }

    private void OnEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Exceção aqui subiria para código nativo e derrubaria o processo.
        try
        {
            // Filtro barato primeiro: LOCATIONCHANGE vem também de cursor/caret/controles filhos.
            if (idObject != OBJID_WINDOW || idChild != 0 || !_tracked.ContainsKey(hwnd)) return;

            switch (evt)
            {
                case EVENT_OBJECT_LOCATIONCHANGE:
                    if (_suppressUntil.TryGetValue(hwnd, out var until))
                    {
                        if (Environment.TickCount64 < until) return;
                        _suppressUntil.Remove(hwnd);
                    }
                    if (_pending.Add(hwnd) && !_timer.IsEnabled) _timer.Start();
                    break;
                case EVENT_SYSTEM_MOVESIZESTART:
                    MoveSizeStarted?.Invoke(hwnd);
                    break;
                case EVENT_SYSTEM_MOVESIZEEND:
                    MoveSizeEnded?.Invoke(hwnd);
                    break;
                case EVENT_SYSTEM_MINIMIZESTART:
                    Minimized?.Invoke(hwnd);
                    break;
                case EVENT_SYSTEM_FOREGROUND:
                    Activated?.Invoke(hwnd);
                    break;
                case EVENT_OBJECT_DESTROY:
                    try { Destroyed?.Invoke(hwnd); }
                    finally { Untrack(hwnd); } // handle morto pode ser reaproveitado pelo Windows
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DockWatcher.OnEvent(0x{evt:X4}): {ex}");
        }
    }

    private void FlushPending()
    {
        _timer.Stop();
        if (_pending.Count == 0) return;
        // Copia antes: o handler pode chamar Track/Untrack/NoteOwnMove.
        var batch = _pending.ToArray();
        _pending.Clear();
        foreach (var hwnd in batch)
        {
            if (!_tracked.ContainsKey(hwnd)) continue;
            try { LocationChanged?.Invoke(hwnd); }
            catch (Exception ex) { Debug.WriteLine($"DockWatcher.LocationChanged: {ex}"); }
        }
    }

    private static void Unhook(nint[] hooks)
    {
        foreach (var hook in hooks)
            if (hook != 0) UnhookWinEvent(hook);
    }

    /// <summary>Desfaz todos os ganchos e para o timer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        foreach (var entry in _pids.Values) Unhook(entry.Hooks);
        _pids.Clear();
        _tracked.Clear();
        _pending.Clear();
        _suppressUntil.Clear();
    }

    private delegate void WinEventProc(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
