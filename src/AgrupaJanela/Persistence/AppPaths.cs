using System.IO;
using System.Security.Principal;

namespace AgrupaJanela.Persistence;

/// <summary>
/// Onde o app guarda dados e como se identifica para o sistema (instância única, sinais do instalador).
/// AGRUPAJANELA_DATA permite uma pasta de dados separada (testes/portátil) — e isola também a instância única,
/// para que um teste nunca interfira no app que o usuário está usando.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();

    public static string GroupsFile => Path.Combine(DataDir, "groups.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string RecoveryFile => Path.Combine(DataDir, "recovery.json");

    /// <summary>Base dos nomes de mutex/eventos: por sessão (Local\) e por usuário (SID).</summary>
    public static string InstanceName { get; } = BuildInstanceName();

    /// <summary>Instância única.</summary>
    public static string MutexName => InstanceName;
    /// <summary>Outra instância pede para mostrar a janela principal.</summary>
    public static string ShowEventName => InstanceName + ".Show";
    /// <summary>O instalador/atualizador pede para devolver todas as janelas e sair sem perguntar.</summary>
    public static string QuitEventName => InstanceName + ".Quit";

    public static bool IsCustomDataDir => Environment.GetEnvironmentVariable("AGRUPAJANELA_DATA") is { Length: > 0 };

    private static string ResolveDataDir()
    {
        var custom = Environment.GetEnvironmentVariable("AGRUPAJANELA_DATA");
        if (custom is { Length: > 0 } && Path.IsPathFullyQualified(custom) && !custom.StartsWith(@"\\")) return custom;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgrupaJanela");
    }

    private static string BuildInstanceName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var name = $@"Local\AgrupaJanela.{sid}";
        // Pasta de dados própria = instância própria (ex.: testes rodando com o app do usuário aberto).
        if (IsCustomDataDir) name += "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataDir.ToLowerInvariant())))[..12];
        return name;
    }
}
