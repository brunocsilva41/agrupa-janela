using AgrupaJanela.Native;
using System.Windows.Interop;

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

    private readonly HwndSource _source;
    private readonly List<(string Shortcut, string Description)> _registered = new();

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Atalhos ativos, para mostrar na interface.</summary>
    public IReadOnlyList<(string Shortcut, string Description)> Registered => _registered;

    public HotkeyService()
    {
        // Janela só de mensagens (HWND_MESSAGE = -3): recebe WM_HOTKEY sem aparecer em lugar nenhum.
        _source = new HwndSource(new HwndSourceParameters("SplitDeck.Hotkeys") { ParentWindow = -3, WindowStyle = 0 });
        _source.AddHook(Hook);

        foreach (var (action, key, name, description) in Definitions)
        {
            var id = (int)action + 1;
            if (Win32.RegisterHotKey(_source.Handle, id, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, key))
                _registered.Add(($"Ctrl+Alt+{name}", description));
            else if (Win32.RegisterHotKey(_source.Handle, id, Win32.MOD_CONTROL | Win32.MOD_ALT | MOD_SHIFT | Win32.MOD_NOREPEAT, key))
                _registered.Add(($"Ctrl+Alt+Shift+{name}", description));
            else
                _registered.Add(("(em uso)", description));
        }
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY) return 0;
        handled = true;
        Pressed?.Invoke((HotkeyAction)((int)wParam - 1));
        return 0;
    }

    public void Dispose()
    {
        foreach (var (action, _, _, _) in Definitions) Win32.UnregisterHotKey(_source.Handle, (int)action + 1);
        _source.Dispose();
    }
}
