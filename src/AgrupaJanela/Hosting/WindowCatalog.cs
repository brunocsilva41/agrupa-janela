using AgrupaJanela.Native;
using System.Diagnostics;

namespace AgrupaJanela.Hosting;

public sealed record WindowCandidate(nint Handle, uint ProcessId, string Title, string ProcessName, string? BlockReason)
{
    public bool CanEmbed => BlockReason is null;
    public string Display => BlockReason is null
        ? $"{Title}  —  {ProcessName}"
        : $"{Title}  —  {ProcessName}   [indisponível: {BlockReason}]";

    public override string ToString() => Display; // nome lido por leitores de tela
}

/// <summary>Lista janelas de nível superior que fazem sentido agrupar e explica por que outras não podem.</summary>
public static class WindowCatalog
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;
    internal static readonly bool SelfElevated = Win32.IsProcessElevated(OwnProcessId);
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>Como Enumerate, mas sem buscar o nome do processo (para laços de espera).</summary>
    public static List<WindowCandidate> EnumerateFast()
    {
        var result = new List<WindowCandidate>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (DescribeFast(hwnd) is { } candidate) result.Add(candidate);
            return true;
        }, 0);
        return result;
    }

    public static List<WindowCandidate> Enumerate()
    {
        var result = new List<WindowCandidate>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (Describe(hwnd) is { } candidate) result.Add(candidate);
            return true;
        }, 0);
        return result.OrderBy(x => x.CanEmbed ? 0 : 1).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Descreve uma janela de nível superior; retorna null se ela nem deve aparecer na lista.</summary>
    public static WindowCandidate? Describe(nint hwnd) => Describe(hwnd, withProcessName: true);

    /// <summary>Versão leve para eventos frequentes: não busca o nome do processo.</summary>
    public static WindowCandidate? DescribeFast(nint hwnd) => Describe(hwnd, withProcessName: false);

    private static WindowCandidate? Describe(nint hwnd, bool withProcessName)
    {
        if (!Win32.IsWindow(hwnd) || !Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return null;
        if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != 0) return null;

        var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        if ((style & Win32.WS_CHILD) != 0 || (exStyle & Win32.WS_EX_TOOLWINDOW) != 0) return null;

        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == OwnProcessId) return null;

        var className = Win32.GetClass(hwnd);
        if (ShellClasses.Contains(className)) return null;

        var title = Win32.GetText(hwnd);
        if (title.Length == 0) return null;

        return new WindowCandidate(hwnd, pid, title, withProcessName ? ProcessName(pid) : "", BlockReason(hwnd, pid, className));
    }

    private static string? BlockReason(nint hwnd, uint pid, string className)
    {
        if (Win32.IsHungAppWindow(hwnd)) return "o aplicativo não está respondendo";
        if (!SelfElevated && Win32.IsProcessElevated(pid)) return "roda como administrador; abra o SplitDeck como administrador";
        return null;
    }

    private static string ProcessName(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return $"pid {pid}"; }
    }
}
