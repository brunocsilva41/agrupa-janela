using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AgrupaJanela.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Nunca ficar com a pasta instalada como diretório atual (atrapalharia apagá-la).
        try { Environment.CurrentDirectory = Path.GetTempPath(); } catch (Exception) { }

        var ctx = SetupContext.Create();
        var cmd = CommandLine.Parse(e.Args);
        Log.Info($"==== Instalador do SplitDeck {ctx.Version} | args: {string.Join(" ", e.Args)} | {cmd}{(ctx.TestMode ? " | MODO TESTE" : "")}");

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Erro inesperado", args.Exception);
            args.Handled = true;
            if (!cmd.Silent)
                SetupDialog.Info(null, $"Erro inesperado: {args.Exception.Message}\n\nDetalhes em {Log.FilePath}");
            Shutdown(SetupFlow.ExitFailed);
        };

        var error = cmd.Error;
        if (error == null && cmd.InstallDir != null && !ctx.TestMode)
            error = $"--install-dir só é aceito no modo de teste ({SetupContext.TestEnvironmentVariable}=1).";
        if (error != null)
        {
            Log.Error(error);
            if (!cmd.Silent)
                SetupDialog.Info(null, error + "\n\nUso: Setup.exe [--silent] | --update --wait-pid <pid> [--relaunch] | --uninstall [--silent]");
            Shutdown(SetupFlow.ExitBadArgs);
            return;
        }

        if (cmd.Silent)
        {
            var code = Task.Run(() => Headless.RunAsync(ctx, cmd)).GetAwaiter().GetResult();
            SelfDelete.ScheduleIfPending();
            Log.Info($"Fim (código {code}).");
            Shutdown(code);
            return;
        }

        var existing = ExistingInstall.Read(ctx);
        var mode = cmd.Uninstall || (!cmd.Update && SetupFlow.RunningFromInstallDir(existing)) ? SetupMode.Uninstall
            : cmd.Update ? SetupMode.Update
            : SetupMode.Install;
        var window = new SetupWindow(ctx, cmd, mode, existing);
        window.Closed += (_, _) =>
        {
            SelfDelete.ScheduleIfPending();
            Log.Info($"Fim (código {window.ExitCode}).");
            Shutdown(window.ExitCode);
        };
        window.Show();
    }
}

/// <summary>--silent: sem interface. Opções: mantém as da instalação existente; numa instalação nova, só o atalho do Menu Iniciar.</summary>
internal static class Headless
{
    public static async Task<int> RunAsync(SetupContext ctx, CommandLine cmd)
    {
        try
        {
            var existing = ExistingInstall.Read(ctx);
            if (cmd.Uninstall)
            {
                var dir = SetupFlow.ResolveUninstallDir(ctx, cmd, existing);
                if (dir == null)
                {
                    Log.Info("Nenhuma instalação encontrada; nada a remover.");
                    return SetupFlow.ExitOk;
                }
                if (!await SetupFlow.EnsureAppClosedAsync(ctx, dir, null)) return SetupFlow.ExitAppStillRunning;
                new Uninstaller(ctx, null).Run(dir, removeData: false);
                return SetupFlow.ExitOk;
            }

            if (cmd.Update && cmd.WaitPid is int pid) RunningApp.WaitForPid(pid, TimeSpan.FromSeconds(30));
            var error = SetupFlow.ResolveInstallDir(ctx, cmd, existing, out var installDir);
            if (error != null)
            {
                Log.Error($"Pasta de instalação recusada: {error}");
                return SetupFlow.ExitFailed;
            }
            if (!Payload.IsPresent)
            {
                Log.Error("Este instalador não contém o pacote do app (build de desenvolvimento).");
                return SetupFlow.ExitFailed;
            }
            if (SetupFlow.FindRuntime(ctx) == null)
                Log.Warn(DesktopRuntime.DisplayName + " não encontrado: o app só vai abrir depois de instalá-lo (" + DesktopRuntime.DownloadUrl + ").");
            if (!await SetupFlow.EnsureAppClosedAsync(ctx, installDir, null)) return SetupFlow.ExitAppStillRunning;

            var sameDir = existing != null && RunEntry.SamePath(existing.Location, installDir);
            new Installer(ctx, null).Run(new InstallOptions
            {
                InstallDir = installDir,
                DesktopShortcut = sameDir && existing!.DesktopShortcut,
                StartWithWindows = null,
            });
            if (cmd.Relaunch) SetupFlow.LaunchApp(ctx, installDir);
            return SetupFlow.ExitOk;
        }
        catch (Exception ex)
        {
            Log.Error("Falhou", ex);
            return SetupFlow.ExitFailed;
        }
    }
}
