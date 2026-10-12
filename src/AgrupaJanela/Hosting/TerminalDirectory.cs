using AgrupaJanela.Native;
using System.IO;
using System.Text.RegularExpressions;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Pasta em que um terminal está, para reabri-lo no mesmo lugar.
/// Console clássico: o processo da janela é o próprio shell. Windows Terminal: um processo para todas as janelas;
/// cada aba tem uma PseudoConsoleWindow oculta cujo dono é a janela do Terminal e cujo processo é o shell da aba.
/// </summary>
public static class TerminalDirectory
{
    private const string ConsoleClass = "ConsoleWindowClass", TerminalClass = "CASCADIA_HOSTING_WINDOW_CLASS", PseudoConsoleClass = "PseudoConsoleWindow";

    public static bool IsTerminal(nint hwnd) => Win32.GetClass(hwnd) is ConsoleClass or TerminalClass;

    /// <summary>A pasta atual do terminal desta janela, ou null (não é terminal, não deu para ler, pasta não existe).</summary>
    /// <param name="processes">Foto de todos os processos, compartilhada entre várias janelas (tirada só quando precisar).</param>
    public static string? Of(nint hwnd, Lazy<List<(uint Pid, uint Parent, string Name)>>? processes = null)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd)) return null;
        var shells = new List<uint>();
        switch (Win32.GetClass(hwnd))
        {
            case ConsoleClass:
                Win32.GetWindowThreadProcessId(hwnd, out var pid);
                shells.Add(pid);
                break;
            case TerminalClass:
                Win32.EnumWindows((h, _) =>
                {
                    if (Win32.GetWindow(h, Win32.GW_OWNER) == hwnd && Win32.GetClass(h) == PseudoConsoleClass)
                    {
                        Win32.GetWindowThreadProcessId(h, out var shell);
                        shells.Add(shell);
                    }
                    return true;
                }, 0);
                break;
            default:
                return null;
        }
        if (shells.Count == 0) return null;

        var parents = (processes ?? new Lazy<List<(uint Pid, uint Parent, string Name)>>(Win32.ProcessParents)).Value;
        foreach (var shell in shells.Where(p => p != 0))
        {
            // O comando que está rodando no shell (ex.: claude, npm) foi aberto na pasta atual; o PowerShell não
            // atualiza a pasta do próprio processo ao fazer cd, então o filho é mais fiel que o shell.
            foreach (var pid in new[] { NewestChild(shell, parents), shell })
                if (pid is { } p && Clean(Win32.GetProcessCurrentDirectory(p)) is { } dir) return dir;
        }
        return null;
    }

    /// <summary>Hosts de console que o Windows cria junto com o shell: não são comandos do usuário (rodam em C:\Windows).</summary>
    private static readonly HashSet<string> ConsoleHosts = new(StringComparer.OrdinalIgnoreCase) { "conhost.exe", "OpenConsole.exe" };

    /// <summary>O filho mais recente do processo (ignorando hosts de console e PIDs reaproveitados, criados antes do pai).</summary>
    private static uint? NewestChild(uint parent, List<(uint Pid, uint Parent, string Name)> all)
    {
        var parentStart = Win32.GetProcessStart(parent);
        if (parentStart is null) return null;
        uint? best = null;
        long bestStart = 0;
        foreach (var (pid, owner, name) in all)
        {
            if (owner != parent || pid == parent || ConsoleHosts.Contains(name) || Win32.GetProcessStart(pid) is not { } start || start < parentStart) continue;
            if (best is null || start > bestStart) { best = pid; bestStart = start; }
        }
        return best;
    }

    /// <summary>Só pastas locais com caminho completo que existem (nada de rede ou caminhos de dispositivo).</summary>
    internal static string? Clean(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || dir.Contains('\0')) return null;
        dir = dir.Trim();
        if (dir.StartsWith(@"\\") || !Path.IsPathFullyQualified(dir)) return null;
        if (dir.Length > 3) dir = dir.TrimEnd('\\');
        return Directory.Exists(dir) ? dir : null;
    }

    private static readonly Regex StartDirectoryOption = new(
        @"(^|\s)(-d|--startingDirectory)\s+(""[^""]*""|\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Argumentos do Windows Terminal abrindo na pasta: troca o -d/--startingDirectory salvo pelo novo.
    /// Null se a pasta tiver ";": para o wt, ";" separa comandos mesmo entre aspas, e o resto do nome viraria outro comando.
    /// </summary>
    internal static string? WindowsTerminalArguments(string? arguments, string directory)
    {
        if (directory.Contains(';')) return null;
        string rest;
        try { rest = StartDirectoryOption.Replace(arguments ?? "", " ").Trim(); }
        catch (RegexMatchTimeoutException) { rest = ""; }
        var start = "-d " + Quote(directory);
        return rest.Length == 0 ? start : start + " " + rest;
    }

    /// <summary>Entre aspas no padrão do Windows: barras no fim são dobradas para não escaparem a aspa final (ex.: "C:\\").</summary>
    internal static string Quote(string value)
    {
        var trailing = value.Length - value.TrimEnd('\\').Length;
        return "\"" + value + new string('\\', trailing) + "\"";
    }
}
