using AgrupaJanela.Hosting;
using AgrupaJanela.Native;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AgrupaJanela.Shell;

/// <summary>
/// Coloca "Agrupar…" no menu do botão direito da barra de título (menu de sistema) de outros apps.
///
/// Como funciona, sem injetar código em outros processos:
/// - quando uma janela vem para frente, adicionamos nossos itens ao menu de sistema dela;
/// - quando o usuário escolhe um item, o Windows dispara EVENT_OBJECT_INVOKED com o ID do comando;
///   a janela dona do menu vem do EVENT_SYSTEM_MENUSTART anterior;
/// - o app dono ignora o WM_SYSCOMMAND com um ID que ele não conhece.
/// Ao sair, removemos só os nossos itens (nunca "resetamos" o menu, que apagaria itens do próprio app).
/// Funciona em apps de janela clássica (cmd, PowerShell, Bloco de Notas, Explorer…). Apps com barra de
/// título própria (Chrome, Windows Terminal, VS Code) desenham o menu deles e não mostram o item.
/// </summary>
public sealed class SystemMenuIntegration : IDisposable
{
    // Múltiplos de 16: os 4 bits baixos de WM_SYSCOMMAND são reservados ao sistema.
    private const uint IdGroupCurrent = 0xA170, IdGroupNew = 0xA180, IdSeparator = 0xA190;
    private const int OBJID_SYSMENU = -1, OBJID_MENU = -3;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_BYCOMMAND = 0x0, MF_BYPOSITION = 0x400;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MENUSTART = 0x0004, EVENT_SYSTEM_MENUEND = 0x0005, EVENT_OBJECT_INVOKED = 0x8013;

    private readonly Win32.WinEventProc _proc; // referência mantida para o GC
    private readonly nint _hookForeground, _hookInvoked;
    private readonly HashSet<nint> _tagged = new();
    private readonly Func<string?> _currentGroupName;
    private nint _menuOwner;

    /// <summary>(janela, novoGrupo) — o usuário escolheu agrupar pelo menu.</summary>
    public event Action<nint, bool>? GroupRequested;

    public SystemMenuIntegration(Func<string?> currentGroupName)
    {
        _currentGroupName = currentGroupName;
        Current = this;
        _proc = OnEvent;
        _hookForeground = Win32.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MENUEND, 0, _proc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        _hookInvoked = Win32.SetWinEventHook(EVENT_OBJECT_INVOKED, EVENT_OBJECT_INVOKED, 0, _proc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        // Janelas que já estavam abertas antes do app iniciar.
        Win32.EnumWindows((h, _) => { TryTag(h); return true; }, 0);
    }

    private void OnEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Exceção aqui voltaria para código nativo e derrubaria o processo (perdendo janelas agrupadas).
        try { Handle(evt, hwnd, idObject, idChild); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void Handle(uint evt, nint hwnd, int idObject, int idChild)
    {
        switch (evt)
        {
            case EVENT_SYSTEM_FOREGROUND:
                // Janela recém-criada pode chegar aqui ainda sem título/visível: tenta de novo logo depois.
                if (!TryTag(hwnd)) Retry(hwnd);
                break;
            case EVENT_SYSTEM_MENUSTART:
                _menuOwner = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
                TryTag(_menuOwner); // garante o texto atualizado mesmo se o foreground não chegou antes
                break;
            case EVENT_SYSTEM_MENUEND:
                // O INVOKED chega antes do MENUEND; depois disso nenhum clique pertence mais a este menu.
                System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => _menuOwner = 0);
                break;
            case EVENT_OBJECT_INVOKED when idObject is OBJID_SYSMENU or OBJID_MENU && (uint)idChild is IdGroupCurrent or IdGroupNew
                                           && _menuOwner != 0 && IsOurs(_menuOwner):
                var owner = _menuOwner;
                _menuOwner = 0;
                GroupRequested?.Invoke(owner, (uint)idChild == IdGroupNew);
                break;
        }
    }

    private static void Retry(nint hwnd)
    {
        foreach (var delay in new[] { 250, 1000 })
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (Win32.GetForegroundWindow() == hwnd) Current?.TryTag(hwnd);
            };
            timer.Start();
        }
    }

    private static SystemMenuIntegration? Current;

    /// <summary>Adiciona (ou atualiza o texto de) nossos itens no menu de sistema da janela. False se a janela não serve (ainda).</summary>
    private bool TryTag(nint hwnd)
    {
        hwnd = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        if (hwnd == 0 || WindowCatalog.DescribeFast(hwnd) is not { CanEmbed: true }) return false;
        var menu = GetSystemMenu(hwnd, false);
        if (menu == 0) return false;

        // O ID já existe num menu que não fomos nós que marcamos: é item do próprio app. Não mexe.
        if (GetMenuState(menu, IdGroupCurrent, MF_BYCOMMAND) != uint.MaxValue && !IsOurs(hwnd)) return false;

        var group = _currentGroupName();
        var label = group is null ? "Agrupar janela  (Agrupa-Janela)" : $"Agrupar em \"{group}\"  (Agrupa-Janela)";
        if (GetMenuState(menu, IdGroupCurrent, MF_BYCOMMAND) == uint.MaxValue)
        {
            AppendMenu(menu, MF_SEPARATOR, IdSeparator, null);
            AppendMenu(menu, MF_STRING, IdGroupCurrent, label);
            AppendMenu(menu, MF_STRING, IdGroupNew, "Agrupar em novo grupo");
            lock (_tagged) _tagged.Add(hwnd);
        }
        else
        {
            ModifyMenu(menu, IdGroupCurrent, MF_BYCOMMAND | MF_STRING, IdGroupCurrent, label);
        }
        // A opção "grupo atual" só existe se houver grupo; sem grupo, as duas fariam o mesmo.
        var hasGroup = group is not null;
        if ((GetMenuState(menu, IdGroupNew, MF_BYCOMMAND) != uint.MaxValue) != hasGroup)
        {
            if (hasGroup) AppendMenu(menu, MF_STRING, IdGroupNew, "Agrupar em novo grupo");
            else RemoveMenu(menu, IdGroupNew, MF_BYCOMMAND);
        }
        return true;
    }

    private bool IsOurs(nint hwnd) { lock (_tagged) return _tagged.Contains(hwnd); }

    /// <summary>Remove só os nossos itens dos menus que alteramos.</summary>
    public void Dispose()
    {
        if (Current == this) Current = null;
        if (_hookForeground != 0) Win32.UnhookWinEvent(_hookForeground);
        if (_hookInvoked != 0) Win32.UnhookWinEvent(_hookInvoked);
        nint[] tagged;
        lock (_tagged) tagged = _tagged.ToArray();
        foreach (var hwnd in tagged)
        {
            if (!Win32.IsWindow(hwnd)) continue;
            var menu = GetSystemMenu(hwnd, false);
            if (menu == 0) continue;
            RemoveMenu(menu, IdGroupCurrent, MF_BYCOMMAND);
            RemoveMenu(menu, IdGroupNew, MF_BYCOMMAND);
            if (!RemoveMenu(menu, IdSeparator, MF_BYCOMMAND))
            {
                // Plano B: remove o separador só se for o último item (o que nós acrescentamos).
                var last = GetMenuItemCount(menu) - 1;
                if (last >= 0 && (GetMenuState(menu, (uint)last, MF_BYPOSITION) & MF_SEPARATOR) != 0) RemoveMenu(menu, (uint)last, MF_BYPOSITION);
            }
        }
    }

    [DllImport("user32.dll")] private static extern nint GetSystemMenu(nint hwnd, bool revert);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool ModifyMenu(nint menu, uint position, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool RemoveMenu(nint menu, uint position, uint flags);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint id, uint flags);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
}
