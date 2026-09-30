using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

namespace AgrupaJanela.Setup;

/// <summary>
/// Onde o instalador mexe. No modo normal: pastas e chaves reais do usuário.
/// Com AGRUPAJANELA_SETUP_TEST=1: tudo vai para uma "raiz de teste" (registro em
/// HKCU\Software\AgrupaJanela-SetupTest, atalhos/dados em %TEMP%\AgrupaJanela-SetupTest) e os
/// nomes de mutex/evento ganham o sufixo ".SetupTest" — a instalação real e o app aberto não são tocados.
/// </summary>
internal sealed class SetupContext
{
    public const string AppName = "Agrupa-Janela";
    public const string Publisher = "Bruno Silva";
    public const string AppExeName = "AgrupaJanela.exe";
    public const string AppProcessName = "AgrupaJanela";
    public const string SetupExeName = "Setup.exe";
    public const string ShortcutFileName = "Agrupa-Janela.lnk";
    public const string RunValueName = "AgrupaJanela";
    public const string RepositoryUrl = "https://github.com/brunocsilva41/agrupa-janela";
    public const string TestEnvironmentVariable = "AGRUPAJANELA_SETUP_TEST";
    public const string TestRegistryRoot = @"Software\AgrupaJanela-SetupTest";

    public bool TestMode { get; private set; }
    /// <summary>Só para capturas de tela: deixa as etapas de progresso visíveis por um instante (modo teste).</summary>
    public bool TestSlow { get; private set; }
    /// <summary>Só para capturas de tela: simula runtime ausente (modo teste).</summary>
    public bool TestNoRuntime { get; private set; }

    public string Version { get; private set; } = "0.0.0";
    public string UninstallKeyPath { get; private set; } = "";
    public string RunKeyPath { get; private set; } = "";
    public string StartMenuDir { get; private set; } = "";
    public string DesktopDir { get; private set; } = "";
    public string DataDir { get; private set; } = "";
    public string DefaultInstallDir { get; private set; } = "";
    public string MutexName { get; private set; } = "";
    public string QuitEventName { get; private set; } = "";
    /// <summary>Pasta de arquivos do modo teste (%TEMP%\AgrupaJanela-SetupTest), ou null.</summary>
    public string? TestFilesRoot { get; private set; }

    public string StartMenuShortcut => Path.Combine(StartMenuDir, ShortcutFileName);
    public string DesktopShortcut => Path.Combine(DesktopDir, ShortcutFileName);

    public static string SelfPath => Assembly.GetEntryAssembly()!.Location;

    public static SetupContext Create()
    {
        var ctx = new SetupContext
        {
            TestMode = Environment.GetEnvironmentVariable(TestEnvironmentVariable) == "1",
            Version = ReadVersion(),
        };
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var baseName = $@"Local\AgrupaJanela.{sid}";

        if (!ctx.TestMode)
        {
            ctx.UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AgrupaJanela";
            ctx.RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            ctx.StartMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            ctx.DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            ctx.DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgrupaJanela");
            ctx.DefaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AgrupaJanela");
            ctx.MutexName = baseName;
            ctx.QuitEventName = baseName + ".Quit";
        }
        else
        {
            var root = Path.Combine(PathRules.ToLongPath(Path.GetTempPath()), "AgrupaJanela-SetupTest");
            ctx.TestFilesRoot = root;
            ctx.TestSlow = Environment.GetEnvironmentVariable("AGRUPAJANELA_SETUP_TEST_SLOW") == "1";
            ctx.TestNoRuntime = Environment.GetEnvironmentVariable("AGRUPAJANELA_SETUP_TEST_NORUNTIME") == "1";
            ctx.UninstallKeyPath = TestRegistryRoot + @"\Uninstall\AgrupaJanela";
            ctx.RunKeyPath = TestRegistryRoot + @"\Run";
            ctx.StartMenuDir = Path.Combine(root, "StartMenu");
            ctx.DesktopDir = Path.Combine(root, "Desktop");
            ctx.DataDir = Path.Combine(root, "AppData", "AgrupaJanela");
            ctx.DefaultInstallDir = Path.Combine(root, "Programs", "AgrupaJanela");
            ctx.MutexName = baseName + ".SetupTest";
            ctx.QuitEventName = baseName + ".Quit.SetupTest";
        }
        return ctx;
    }

    private static string ReadVersion()
    {
        var info = Assembly.GetEntryAssembly()?.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "0.0.0";
        var plus = info!.IndexOf('+');
        return plus >= 0 ? info.Substring(0, plus) : info;
    }
}
