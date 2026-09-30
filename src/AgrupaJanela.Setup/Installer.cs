using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace AgrupaJanela.Setup;

internal sealed class StepProgress
{
    public StepProgress(int step, string detail, double percent)
    {
        Step = step;
        Detail = detail;
        Percent = percent;
    }

    public int Step { get; }
    public string Detail { get; }
    public double Percent { get; }
}

internal sealed class InstallOptions
{
    public string InstallDir { get; set; } = "";
    public bool DesktopShortcut { get; set; }
    /// <summary>true = liga; false = remove (se for desta instalação); null = não mexe (atualização/silencioso).</summary>
    public bool? StartWithWindows { get; set; }
}

/// <summary>Erro com mensagem já pronta para o usuário.</summary>
internal sealed class SetupException : Exception
{
    public SetupException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Instala/atualiza: extrai para uma pasta temporária ao lado, troca as pastas por renomeação e só então
/// registra. Se a troca falhar, a versão antiga continua intacta.
/// </summary>
internal sealed class Installer
{
    public static readonly string[] Steps =
    {
        "Conferir a integridade do pacote",
        "Extrair os arquivos",
        "Colocar na pasta de instalação",
        "Registrar em Programas e Recursos",
        "Criar atalhos e opções",
    };

    private readonly SetupContext _ctx;
    private readonly IProgress<StepProgress>? _progress;

    public Installer(SetupContext ctx, IProgress<StepProgress>? progress)
    {
        _ctx = ctx;
        _progress = progress;
    }

    public void Run(InstallOptions options)
    {
        var dir = options.InstallDir;
        Log.Info($"Instalando {_ctx.Version} em {dir} (atalho na área de trabalho={options.DesktopShortcut}, iniciar com o Windows={options.StartWithWindows?.ToString() ?? "manter"}).");

        Report(0, "Conferindo a integridade do pacote…", 2);
        Payload.Verify();
        Slow();

        var parent = Path.GetDirectoryName(dir) ?? throw new SetupException("Pasta de instalação inválida.");
        Directory.CreateDirectory(parent);
        var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
        string? staging = dir + ".novo-" + tag;
        string? old = null;
        try
        {
            Report(1, "Extraindo os arquivos…", 8);
            int files;
            using (var zip = Payload.Open())
                files = SafeZip.Extract(zip, staging, (i, total) => Report(1, $"Extraindo os arquivos ({i} de {total})…", 8 + 52.0 * i / total));
            if (!File.Exists(Path.Combine(staging, SetupContext.AppExeName)))
                throw new SetupException($"O pacote não contém {SetupContext.AppExeName}.");
            File.Copy(SetupContext.SelfPath, Path.Combine(staging, SetupContext.SetupExeName));
            Log.Info($"{files} arquivos extraídos em {staging} (+ {SetupContext.SetupExeName}).");
            Slow();

            Report(2, "Colocando na pasta de instalação…", 62);
            if (Directory.Exists(dir))
            {
                old = dir + ".antigo-" + tag;
                try { Directory.Move(dir, old); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    old = null;
                    throw new SetupException("Não foi possível substituir a versão instalada: algum arquivo da pasta está em uso. Nada foi alterado.", ex);
                }
            }
            try
            {
                Directory.Move(staging, dir);
                staging = null;
            }
            catch (Exception ex)
            {
                if (old != null)
                {
                    try { Directory.Move(old, dir); old = null; Log.Info("Versão anterior restaurada."); }
                    catch (Exception restoreEx) { Log.Error($"Falha ao restaurar a versão anterior (está em {old})", restoreEx); }
                }
                throw new SetupException("Não foi possível colocar os arquivos na pasta de instalação. A versão anterior foi mantida.", ex);
            }
        }
        finally
        {
            if (staging != null) TryDeleteDir(staging);
        }
        if (old != null) TryDeleteDir(old);
        Slow();

        Report(3, "Registrando em Programas e Recursos…", 80);
        WriteUninstallKey(dir);
        Slow();

        Report(4, "Criando atalhos…", 90);
        var exe = Path.Combine(dir, SetupContext.AppExeName);
        ShellLink.Create(_ctx.StartMenuShortcut, exe, dir, "Junte janelas de qualquer app em uma só.");
        Log.Info($"Atalho criado: {_ctx.StartMenuShortcut}");
        if (options.DesktopShortcut)
        {
            ShellLink.Create(_ctx.DesktopShortcut, exe, dir, "Junte janelas de qualquer app em uma só.");
            Log.Info($"Atalho criado: {_ctx.DesktopShortcut}");
        }
        else
        {
            Shortcuts.RemoveIfOurs(_ctx.DesktopShortcut, dir);
        }
        if (options.StartWithWindows == true) RunEntry.Set(_ctx, dir);
        else if (options.StartWithWindows == false) RunEntry.RemoveIfOurs(_ctx, dir);
        Slow();

        Report(Steps.Length, "Instalação concluída.", 100);
        Log.Info("Instalação concluída.");
    }

    private void WriteUninstallKey(string dir)
    {
        var setup = Path.Combine(dir, SetupContext.SetupExeName);
        var sizeKb = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
        using var key = Registry.CurrentUser.CreateSubKey(_ctx.UninstallKeyPath, true)!;
        key.SetValue("DisplayName", SetupContext.AppName);
        key.SetValue("DisplayVersion", _ctx.Version);
        key.SetValue("Publisher", SetupContext.Publisher);
        key.SetValue("InstallLocation", dir);
        key.SetValue("DisplayIcon", Path.Combine(dir, SetupContext.AppExeName) + ",0");
        key.SetValue("UninstallString", $"\"{setup}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{setup}\" --uninstall --silent");
        key.SetValue("URLInfoAbout", SetupContext.RepositoryUrl);
        key.SetValue("HelpLink", SetupContext.RepositoryUrl + "/issues");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
        Log.Info($"Registrado em HKCU\\{_ctx.UninstallKeyPath} ({sizeKb} KB).");
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível apagar a pasta temporária {dir}: {ex.Message}");
        }
    }

    private void Report(int step, string detail, double percent) => _progress?.Report(new StepProgress(step, detail, percent));

    private void Slow()
    {
        if (_ctx.TestSlow) Thread.Sleep(700);
    }
}

/// <summary>Desinstala: arquivos (menos o Setup.exe em execução), atalhos, chave Run, registro e, se pedido, os dados.</summary>
internal sealed class Uninstaller
{
    private readonly SetupContext _ctx;
    private readonly IProgress<StepProgress>? _progress;

    public Uninstaller(SetupContext ctx, IProgress<StepProgress>? progress)
    {
        _ctx = ctx;
        _progress = progress;
    }

    public static string[] StepsFor(bool removeData) => removeData
        ? new[] { "Remover os arquivos", "Remover atalhos e início com o Windows", "Remover de Programas e Recursos", "Apagar grupos salvos e preferências" }
        : new[] { "Remover os arquivos", "Remover atalhos e início com o Windows", "Remover de Programas e Recursos" };

    public void Run(string dir, bool removeData)
    {
        Log.Info($"Desinstalando de {dir} (apagar dados={removeData}).");
        var steps = StepsFor(removeData).Length;

        Report(0, "Removendo os arquivos…", 5);
        if (Directory.Exists(dir))
        {
            if (PathRules.ValidateLocation(dir, out _) != null || !PathRules.LooksLikeInstall(dir))
                Log.Warn($"{dir} não parece uma instalação do SplitDeck; a pasta não será apagada.");
            else
                DeleteInstallFiles(dir);
        }
        Slow();

        Report(1, "Removendo atalhos e início com o Windows…", 55);
        Shortcuts.RemoveIfOurs(_ctx.StartMenuShortcut, dir);
        Shortcuts.RemoveIfOurs(_ctx.DesktopShortcut, dir);
        RunEntry.RemoveIfOurs(_ctx, dir);
        Slow();

        Report(2, "Removendo de Programas e Recursos…", 75);
        Registry.CurrentUser.DeleteSubKeyTree(_ctx.UninstallKeyPath, false);
        Log.Info($"Chave removida: HKCU\\{_ctx.UninstallKeyPath}");
        Slow();

        if (removeData)
        {
            Report(3, "Apagando grupos salvos e preferências…", 90);
            if (Directory.Exists(_ctx.DataDir)) Directory.Delete(_ctx.DataDir, true);
            Log.Info($"Dados apagados: {_ctx.DataDir}");
        }
        else
        {
            Log.Info($"Dados mantidos: {_ctx.DataDir}");
        }

        if (_ctx.TestMode) CleanTestRoot();
        Report(steps, "Desinstalação concluída.", 100);
        Log.Info("Desinstalação concluída.");
    }

    private void DeleteInstallFiles(string dir)
    {
        var self = PathRules.ToLongPath(SetupContext.SelfPath);
        if (!PathRules.IsSameOrInside(self, dir))
        {
            Directory.Delete(dir, true);
            Log.Info($"Pasta apagada: {dir}");
            return;
        }

        // Rodando de dentro da pasta: apaga tudo menos o próprio Setup.exe; a pasta sai depois (SelfDelete).
        var failures = 0;
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            if (RunEntry.SamePath(file, self)) continue;
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex)
            {
                failures++;
                Log.Error($"Não foi possível apagar {file}", ex);
            }
        }
        if (failures > 0)
            throw new SetupException($"{failures} arquivo(s) não puderam ser apagados (em uso?). A entrada em Programas e Recursos foi mantida para tentar de novo.");
        foreach (var sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            Directory.Delete(sub, false);
        SelfDelete.PendingDir = dir;
        Log.Info($"Arquivos apagados; a pasta {dir} (com o Setup.exe) será removida quando o desinstalador fechar.");
    }

    /// <summary>Modo teste: apaga a raiz de registro de teste e pastas de atalho de teste vazias.</summary>
    private void CleanTestRoot()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(SetupContext.TestRegistryRoot, false);
            foreach (var d in new[] { _ctx.StartMenuDir, _ctx.DesktopDir })
                if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
        catch (Exception ex)
        {
            Log.Warn($"Limpeza do modo teste incompleta: {ex.Message}");
        }
    }

    private void Report(int step, string detail, double percent) => _progress?.Report(new StepProgress(step, detail, percent));

    private void Slow()
    {
        if (_ctx.TestSlow) Thread.Sleep(700);
    }
}

/// <summary>Auto-remoção da pasta instalada depois que o Setup.exe (que está nela) fechar.</summary>
internal static class SelfDelete
{
    public static string? PendingDir { get; set; }

    public static void ScheduleIfPending()
    {
        var dir = PendingDir;
        if (dir == null) return;
        PendingDir = null;
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                $"/d /c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{dir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath(),
            };
            Process.Start(psi)?.Dispose();
            Log.Info($"Remoção de {dir} agendada (cmd, ~2 s após o desinstalador fechar).");
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível agendar a remoção com cmd: {ex.Message}. Tentando no próximo reinício.");
            var self = SetupContext.SelfPath;
            var ok = NativeMethods.MoveFileEx(self, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT)
                     & NativeMethods.MoveFileEx(dir, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
            if (ok) Log.Info("Remoção agendada para o próximo reinício.");
            else Log.Warn($"MoveFileEx falhou (erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}); apague {dir} manualmente.");
        }
    }
}
