using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AgrupaJanela.Setup;

/// <summary>Decisões comuns ao modo silencioso e ao assistente.</summary>
internal static class SetupFlow
{
    public const int ExitOk = 0, ExitFailed = 1, ExitBadArgs = 2, ExitAppStillRunning = 3, ExitCancelled = 4;

    /// <summary>Pasta para instalar/atualizar: --install-dir (teste) &gt; instalação existente &gt; padrão por usuário.</summary>
    public static string? ResolveInstallDir(SetupContext ctx, CommandLine cmd, ExistingInstall? existing, out string dir)
    {
        var candidate = cmd.InstallDir ?? existing?.Location ?? ctx.DefaultInstallDir;
        var error = PathRules.ValidateLocation(candidate, out dir);
        return error ?? PathRules.ValidateTarget(dir);
    }

    /// <summary>Pasta para desinstalar: --install-dir (teste) &gt; InstallLocation &gt; pasta do próprio Setup.exe &gt; padrão.</summary>
    public static string? ResolveUninstallDir(SetupContext ctx, CommandLine cmd, ExistingInstall? existing)
    {
        if (cmd.InstallDir != null)
            return PathRules.ValidateLocation(cmd.InstallDir, out var d) == null ? d : null;
        if (existing != null)
            return PathRules.ValidateLocation(existing.Location, out var d) == null ? d : existing.Location;
        var selfDir = Path.GetDirectoryName(PathRules.ToLongPath(SetupContext.SelfPath));
        if (selfDir != null && File.Exists(Path.Combine(selfDir, SetupContext.AppExeName))) return selfDir;
        return PathRules.LooksLikeInstall(ctx.DefaultInstallDir) ? ctx.DefaultInstallDir : null;
    }

    /// <summary>True se este Setup.exe é a cópia de dentro da pasta instalada (então ele é o desinstalador).</summary>
    public static bool RunningFromInstallDir(ExistingInstall? existing)
    {
        if (existing == null) return false;
        var selfDir = Path.GetDirectoryName(PathRules.ToLongPath(SetupContext.SelfPath));
        return selfDir != null && RunEntry.SamePath(selfDir, existing.Location);
    }

    /// <summary>Runtime 8.x x64 instalado (AGRUPAJANELA_SETUP_TEST_NORUNTIME=1 simula ausência, só no modo teste).</summary>
    public static string? FindRuntime(SetupContext ctx) => ctx.TestNoRuntime ? null : DesktopRuntime.FindInstalled();

    public static void LaunchApp(SetupContext ctx, string dir)
    {
        var exe = Path.Combine(dir, SetupContext.AppExeName);
        if (ctx.TestMode)
        {
            Log.Info($"Modo teste: o app NÃO é aberto (seria {exe}).");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir })?.Dispose();
            Log.Info($"App aberto: {exe}");
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível abrir o app", ex);
        }
    }

    /// <summary>
    /// Fecha o app aberto pelo evento Quit e espera até 20 s. `ask` é chamado (no modo interativo) para
    /// explicar/confirmar: recebe se é a primeira tentativa e retorna se deve (re)tentar.
    /// </summary>
    public static async Task<bool> EnsureAppClosedAsync(SetupContext ctx, string installDir, Func<bool, Task<bool>>? ask)
    {
        var app = new RunningApp(ctx, installDir);
        if (!app.IsRunning()) return true;
        Log.Info("O Agrupa-Janela está aberto.");
        var first = true;
        while (true)
        {
            if (ask != null && !await ask(first)) return false;
            var signaled = app.SignalQuit();
            var exited = await Task.Run(() => app.WaitForExit(TimeSpan.FromSeconds(signaled ? 20 : 1)));
            if (exited)
            {
                Log.Info("O Agrupa-Janela foi fechado.");
                return true;
            }
            Log.Warn("O Agrupa-Janela continua aberto.");
            if (ask == null) return false;
            first = false;
        }
    }
}
