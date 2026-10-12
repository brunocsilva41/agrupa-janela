using AgrupaJanela.Native;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AgrupaJanela.Shell;

public enum HotkeyAction { GroupForeground, ToggleMode, NextPane, PreviousPane, MaximizePane }

/// <summary>Atalhos globais. Tenta Ctrl+Alt+tecla e, se outro programa já usa, Ctrl+Alt+Shift+tecla.</summary>
public sealed class HotkeyService : IDisposable
{
    private const uint MOD_SHIFT = 0x4;
    private const uint VK_RETURN = 0x0D, VK_LEFT = 0x25, VK_RIGHT = 0x27;

    private static readonly (HotkeyAction Action, uint Key, string Name, string Description)[] Definitions =
    {
        (HotkeyAction.GroupForeground, 'G', "G", "agrupar a janela ativa"),
        (HotkeyAction.ToggleMode, 'M', "M", "alternar grade/abas"),
        (HotkeyAction.NextPane, VK_RIGHT, "→", "próximo painel"),
        (HotkeyAction.PreviousPane, VK_LEFT, "←", "painel anterior"),
        (HotkeyAction.MaximizePane, VK_RETURN, "Enter", "maximizar/restaurar painel"),
    };

    private const string ClassName = "SplitDeck.Hotkeys";

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    // Campos mantêm vivos o delegate (chamado pelo Windows) e a instância da classe, até o Dispose.
    private readonly Win32.WindowProc _windowProc;
    private readonly nint _instance;
    private nint _hwnd;
    private readonly List<(string Shortcut, string Description)> _registered = new();

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Atalhos ativos, para mostrar na interface.</summary>
    public IReadOnlyList<(string Shortcut, string Description)> Registered => _registered;

    public HotkeyService()
    {
        // Janela só de mensagens (HWND_MESSAGE) em Win32 puro: recebe WM_HOTKEY sem aparecer em lugar nenhum.
        // Não usa HwndSource: ele liga a pilha de renderização do WPF (Direct3D, dezenas de MB) mesmo sem janelas.
        _windowProc = WndProc;
        _instance = Win32.GetModuleHandle(null);
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _windowProc,
            hInstance = _instance,
            lpszClassName = ClassName,
        };
        if (Win32.RegisterClassEx(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _hwnd = Win32.CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, _instance, 0);
        if (_hwnd == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Win32.UnregisterClass(ClassName, _instance);
            throw new Win32Exception(error);
        }

        foreach (var (action, key, name, description) in Definitions)
        {
            var id = (int)action + 1;
            if (Win32.RegisterHotKey(_hwnd, id, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, key))
                _registered.Add(($"Ctrl+Alt+{name}", description));
            else if (Win32.RegisterHotKey(_hwnd, id, Win32.MOD_CONTROL | Win32.MOD_ALT | MOD_SHIFT | Win32.MOD_NOREPEAT, key))
                _registered.Add(($"Ctrl+Alt+Shift+{name}", description));
            else
                _registered.Add(("(em uso)", description));
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg != Win32.WM_HOTKEY) return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        try { Pressed?.Invoke((HotkeyAction)((int)wParam - 1)); }
        catch (Exception ex)
        {
            // Não pode sair do callback nativo: devolve a exceção ao Dispatcher (mesmo tratamento de antes).
            _dispatcher.BeginInvoke(() => ExceptionDispatchInfo.Capture(ex).Throw());
        }
        return 0;
    }

    public void Dispose()
    {
        if (_hwnd == 0) return;
        foreach (var (action, _, _, _) in Definitions) Win32.UnregisterHotKey(_hwnd, (int)action + 1);
        Win32.DestroyWindow(_hwnd);
        Win32.UnregisterClass(ClassName, _instance);
        _hwnd = 0;
    }
}
