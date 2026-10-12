using AgrupaJanela.Native;
using System.Diagnostics;
using System.IO;

namespace AgrupaJanela.Hosting;

/// <summary>Reencontra a janela de um app salvo ou abre o app de novo.</summary>
public static class AppLauncher
{
    private static readonly string UserAliasRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");

    /// <summary>
    /// 1) Se a mesma janela ainda existe (mesmo HWND, processo e app), usa ela — e traz de volta se estiver
    ///    minimizada, escondida (ex.: na bandeja) ou em outra área de trabalho virtual.
    /// 2) Se o app já está aberto com uma janela de mesmo título que nenhum grupo usa, usa essa (o app foi reiniciado).
    /// 3) Senão, abre a versão atual do executável (a pasta muda quando o app se atualiza) com os mesmos argumentos,
    ///    na pasta em que o terminal estava, e espera a janela aparecer.
    /// Nunca pega uma janela aleatória de outro app só porque é do mesmo programa.
    /// </summary>
    public static async Task<WindowCandidate?> ResolveAsync(AppIdentity app, ISet<nint> taken, CancellationToken cancel = default)
    {
        if (Reclaim(app, taken) is { } same) return same;
        if (FindOpen(app, taken) is { } open) return open;

        // Rodando como administrador, nada é relançado a partir do arquivo (senão um arquivo editável
        // pelo usuário comum viraria uma forma de rodar programas elevados sem o aviso do UAC).
        if (WindowCatalog.SelfElevated || !CanLaunch(app)) return null;
        var before = WindowCatalog.EnumerateFast().Select(c => c.Handle).ToHashSet();
        var foregroundBefore = Win32.GetForegroundWindow();
        if (!Start(app, out var pid)) return null;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !cancel.IsCancellationRequested)
        {
            await Task.Delay(250, cancel).ConfigureAwait(true);
            var fresh = WindowCatalog.EnumerateFast().Where(c => c.CanEmbed && !before.Contains(c.Handle) && !taken.Contains(c.Handle)).ToList();
            // Preferência: janela do próprio processo aberto; depois, janela nova do mesmo app (apps que repassam para outra instância).
            var match = fresh.FirstOrDefault(c => pid != 0 && c.ProcessId == pid)
                     ?? fresh.FirstOrDefault(c => IsSameApp(c.ProcessId, app))
                     ?? ActivatedExisting(app, taken, foregroundBefore);
            if (match is not null)
            {
                await Task.Delay(300, cancel).ConfigureAwait(true); // deixa o app terminar de montar a janela
                return WindowCatalog.Describe(match.Handle) ?? match;
            }
        }
        return null;
    }

    /// <summary>Por que um app salvo não pôde ser aberto (texto para o painel do app que falhou).</summary>
    public static string WhyNot(AppIdentity app) =>
        WindowCatalog.SelfElevated ? "O SplitDeck está rodando como administrador e, por segurança, não reabre apps salvos."
        : !CanLaunch(app) ? $"Não foi possível encontrar o programa ({Path.GetFileName(app.ExePath) ?? "desconhecido"}). Ele foi desinstalado ou movido?"
        : "O app não mostrou a janela a tempo.";

    /// <summary>A mesma janela de antes, se ainda existe e ninguém a usa. Mostra de volta se estiver minimizada ou escondida.</summary>
    private static WindowCandidate? Reclaim(AppIdentity app, ISet<nint> taken)
    {
        var hwnd = (nint)app.LastHandle;
        if (hwnd == 0 || taken.Contains(hwnd) || !Win32.IsWindow(hwnd)) return null;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != app.LastProcessId || !IsSameApp(pid, app)) return null;

        // Escondida (fechada para a bandeja) ou minimizada: volta ao tamanho normal, sem roubar o foco.
        if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
        return WindowCatalog.DescribeAnyDesktop(hwnd) is { CanEmbed: true } same ? same : null;
    }

    /// <summary>Janela já aberta do mesmo app, com o mesmo título, que nenhum grupo usa.</summary>
    private static WindowCandidate? FindOpen(AppIdentity app, ISet<nint> taken) =>
        app.Title.Length == 0 ? null
        : WindowCatalog.EnumerateFast().FirstOrDefault(c => c.CanEmbed && !taken.Contains(c.Handle) && c.Title == app.Title && IsSameApp(c.ProcessId, app));

    /// <summary>Apps de instância única (ex.: Discord) só trazem a janela que já existia para a frente.</summary>
    private static WindowCandidate? ActivatedExisting(AppIdentity app, ISet<nint> taken, nint foregroundBefore)
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == 0 || foreground == foregroundBefore || taken.Contains(foreground)) return null;
        return WindowCatalog.DescribeFast(foreground) is { CanEmbed: true } candidate && IsSameApp(candidate.ProcessId, app) ? candidate : null;
    }

    private static bool IsSameApp(uint pid, AppIdentity app) => AppVersionPath.SameApp(Win32.GetProcessPath(pid), app.ExePath);

    /// <summary>Segurança: só executáveis locais com caminho absoluto que existem (nada de rede, dispositivos ou PATH).</summary>
    internal static bool CanLaunch(AppIdentity app)
    {
        var exe = app.ExePath;
        if (string.Equals(Path.GetFileName(exe), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) return false; // não reabre o app da Store
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathFullyQualified(exe) || exe.StartsWith(@"\\") || exe.Contains('\0')
            || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return Targets(exe).Any();
    }

    /// <summary>Executáveis candidatos, já filtrados para as mesmas regras de segurança (pastas reais, sem rede).</summary>
    private static IEnumerable<string> Targets(string exe) =>
        AppVersionPath.LaunchTargets(exe, UserAliasRoot).Where(t => Path.IsPathFullyQualified(t) && !t.StartsWith(@"\\"));

    /// <summary>Descrição legível do que será aberto (para o usuário confirmar).</summary>
    public static string Describe(AppIdentity app) =>
        app.Arguments is { Length: > 0 } ? $"{app.ExePath} {app.Arguments}" : app.ExePath ?? "(desconhecido)";

    private static bool Start(AppIdentity app, out int pid)
    {
        pid = 0;
        if (!CanLaunch(app)) return false;
        var directory = TerminalDirectory.Clean(app.WorkingDirectory);
        var arguments = app.Arguments ?? "";
        // Windows Terminal ignora a pasta do processo: a pasta vai como argumento (-d).
        if (directory is not null && string.Equals(Path.GetFileName(app.ExePath), "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase))
            arguments = TerminalDirectory.WindowsTerminalArguments(app.Arguments, directory);

        foreach (var target in Targets(app.ExePath!))
        {
            try
            {
                // Atalhos de apps empacotados (%LOCALAPPDATA%\Microsoft\WindowsApps) precisam do shell; o resto abre direto.
                var alias = target.StartsWith(UserAliasRoot + "\\", StringComparison.OrdinalIgnoreCase);
                using var process = Process.Start(new ProcessStartInfo(target, arguments)
                {
                    UseShellExecute = alias,
                    WorkingDirectory = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                });
                pid = process?.Id ?? 0;
                return true;
            }
            catch { /* tenta o próximo (ex.: a pasta do pacote é protegida ou sumiu com a atualização) */ }
        }
        return false;
    }
}
