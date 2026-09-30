using AgrupaJanela.Native;
using System.Diagnostics;
using System.IO;

namespace AgrupaJanela.Hosting;

/// <summary>Reencontra a janela de um app salvo ou abre o app de novo.</summary>
public static class AppLauncher
{
    /// <summary>
    /// 1) Se a mesma janela ainda existe (mesmo HWND e processo), usa ela.
    /// 2) Senão, abre o executável com os mesmos argumentos e espera a janela aparecer.
    /// Nunca pega uma janela aleatória de outro app só porque é do mesmo programa.
    /// </summary>
    public static async Task<WindowCandidate?> ResolveAsync(AppIdentity app, ISet<nint> taken, CancellationToken cancel = default)
    {
        var same = WindowCatalog.Describe((nint)app.LastHandle);
        if (same is { CanEmbed: true } && same.ProcessId == app.LastProcessId && !taken.Contains(same.Handle)
            && string.Equals(Win32.GetProcessPath(same.ProcessId), app.ExePath, StringComparison.OrdinalIgnoreCase)) return same;

        // Rodando como administrador, nada é relançado a partir do arquivo (senão um arquivo editável
        // pelo usuário comum viraria uma forma de rodar programas elevados sem o aviso do UAC).
        if (WindowCatalog.SelfElevated || !CanLaunch(app)) return null;
        var before = WindowCatalog.EnumerateFast().Select(c => c.Handle).ToHashSet();
        if (!Start(app, out var pid)) return null;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !cancel.IsCancellationRequested)
        {
            await Task.Delay(250, cancel).ConfigureAwait(true);
            var fresh = WindowCatalog.EnumerateFast().Where(c => c.CanEmbed && !before.Contains(c.Handle) && !taken.Contains(c.Handle)).ToList();
            // Preferência: janela do próprio processo aberto; depois, janela nova do mesmo executável (apps que repassam para outra instância).
            var match = fresh.FirstOrDefault(c => pid != 0 && c.ProcessId == pid)
                     ?? fresh.FirstOrDefault(c => string.Equals(Win32.GetProcessPath(c.ProcessId), app.ExePath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                await Task.Delay(300, cancel).ConfigureAwait(true); // deixa o app terminar de montar a janela
                return WindowCatalog.Describe(match.Handle) ?? match;
            }
        }
        return null;
    }

    /// <summary>Segurança: só executáveis locais com caminho absoluto que existem (nada de rede, dispositivos ou PATH).</summary>
    internal static bool CanLaunch(AppIdentity app)
    {
        var exe = app.ExePath;
        if (string.Equals(Path.GetFileName(exe), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) return false; // não reabre o app da Store
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathFullyQualified(exe) || exe.StartsWith(@"\\") || exe.Contains('\0')
            || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return IsPackaged(exe) || File.Exists(exe);
    }

    /// <summary>App da Store: só dentro das pastas WindowsApps reais do sistema/usuário.</summary>
    private static bool IsPackaged(string exe)
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + "\\",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps") + "\\",
        };
        return roots.Any(r => exe.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Descrição legível do que será aberto (para o usuário confirmar).</summary>
    public static string Describe(AppIdentity app) =>
        app.Arguments is { Length: > 0 } ? $"{app.ExePath} {app.Arguments}" : app.ExePath ?? "(desconhecido)";

    private static bool Start(AppIdentity app, out int pid)
    {
        pid = 0;
        if (!CanLaunch(app)) return false;
        var packaged = IsPackaged(app.ExePath!);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(app.ExePath!, app.Arguments ?? "")
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
            pid = process?.Id ?? 0;
            return true;
        }
        catch when (packaged)
        {
            // Apps empacotados (WindowsApps) não podem ser iniciados pelo caminho: tenta pelo alias do app via shell.
            // Alias oficial do app empacotado (%LOCALAPPDATA%\Microsoft\WindowsApps\<nome>.exe) — caminho completo, nunca o PATH.
            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", Path.GetFileName(app.ExePath!));
            if (!File.Exists(alias)) return false;
            try
            {
                using var process = Process.Start(new ProcessStartInfo(alias, app.Arguments ?? "") { UseShellExecute = true });
                pid = process?.Id ?? 0;
                return true;
            }
            catch { return false; }
        }
        catch { return false; }
    }
}
