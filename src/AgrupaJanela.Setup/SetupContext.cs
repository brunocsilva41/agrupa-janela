using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

namespace AgrupaJanela.Setup;

/// <summary>
/// Onde o instalador mexe. No modo normal: pastas e chaves reais do usuário.
/// Com SPLITDECK_SETUP_TEST=1: tudo vai para uma "raiz de teste" (registro em
/// HKCU\Software\SplitDeck-SetupTest, atalhos/dados em %TEMP%\SplitDeck-SetupTest) e os
/// nomes de mutex/evento ganham o sufixo ".SetupTest" — a instalação real e o app aberto não são tocados.
/// </summary>
internal sealed class SetupContext
{
    public const string AppName = "SplitDeck";
    public const string Publisher = "Bruno Silva";
    public const string AppExeName = "SplitDeck.exe";
    public const string AppProcessName = "SplitDeck";
    public const string SetupExeName = "Setup.exe";
    public const string ShortcutFileName = "SplitDeck.lnk";
    public const string RunValueName = "SplitDeck";
    public const string RepositoryUrl = "https://github.com/brunocsilva41/agrupa-janela";
    public const string TestEnvironmentVariable = "SPLITDECK_SETUP_TEST";
    public const string TestRegistryRoot = @"Software\SplitDeck-SetupTest";

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
    /// <summary>Pasta de arquivos do modo teste (%TEMP%\SplitDeck-SetupTest), ou null.</summary>
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
        var baseName = $@"Local\SplitDeck.{sid}";

        if (!ctx.TestMode)
        {
            ctx.UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SplitDeck";
            ctx.RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            ctx.StartMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            ctx.DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            ctx.DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplitDeck");
            ctx.DefaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SplitDeck");
            ctx.MutexName = baseName;
            ctx.QuitEventName = baseName + ".Quit";
        }
        else
        {
            var root = Path.Combine(PathRules.ToLongPath(Path.GetTempPath()), "SplitDeck-SetupTest");
            ctx.TestFilesRoot = root;
            ctx.TestSlow = Environment.GetEnvironmentVariable("SPLITDECK_SETUP_TEST_SLOW") == "1";
            ctx.TestNoRuntime = Environment.GetEnvironmentVariable("SPLITDECK_SETUP_TEST_NORUNTIME") == "1";
            ctx.UninstallKeyPath = TestRegistryRoot + @"\Uninstall\SplitDeck";
            ctx.RunKeyPath = TestRegistryRoot + @"\Run";
            ctx.StartMenuDir = Path.Combine(root, "StartMenu");
            ctx.DesktopDir = Path.Combine(root, "Desktop");
            ctx.DataDir = Path.Combine(root, "AppData", "SplitDeck");
            ctx.DefaultInstallDir = Path.Combine(root, "Programs", "SplitDeck");
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
