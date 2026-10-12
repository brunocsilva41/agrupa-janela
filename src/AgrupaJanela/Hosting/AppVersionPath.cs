using System.IO;
using System.Text.RegularExpressions;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Programas que mudam de pasta a cada atualização: apps da Loja (WindowsApps\Nome_Versão_Arq__Editor) e
/// apps Squirrel como o Discord (raiz\app-1.0.9257\Discord.exe). O caminho salvo envelhece; o app continua o mesmo.
/// </summary>
public static class AppVersionPath
{
    private static readonly Regex SquirrelFolder = new(@"^app-\d+(\.\d+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Chave do app que não muda com a versão (comparar dois executáveis: mesmo app?).</summary>
    public static string? Key(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var parts = exe.Trim().Replace('/', '\\').Split('\\');
        for (var i = 1; i < parts.Length - 1; i++)
        {
            if (parts[i - 1].Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) && PackageFamily(parts[i]) is { } family) parts[i] = "pkg:" + family;
            else if (SquirrelFolder.IsMatch(parts[i])) parts[i] = "app-*";
        }
        return string.Join('\\', parts).ToLowerInvariant();
    }

    public static bool SameApp(string? a, string? b) => Key(a) is { } key && key == Key(b);

    /// <summary>Nome_Versão_Arquitetura_Recurso_Editor → Nome_Editor (a "família" do pacote). Null se não for pasta de pacote.</summary>
    internal static string? PackageFamily(string folder)
    {
        var parts = folder.Split('_');
        if (parts.Length != 5 || parts[0].Length == 0 || parts[4].Length == 0 || !Version.TryParse(parts[1], out _)) return null;
        return parts[0] + "_" + parts[4];
    }

    /// <summary>
    /// Executáveis que existem para abrir o app hoje, em ordem de preferência. Loja: os atalhos oficiais do pacote
    /// (%LOCALAPPDATA%\Microsoft\WindowsApps — o do Terminal é wt.exe) e depois o caminho salvo. Squirrel: a versão mais nova.
    /// </summary>
    public static IEnumerable<string> LaunchTargets(string exe, string userAliasRoot)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(exe) ?? "");
        var name = Path.GetFileName(exe);

        if (PackageOf(exe) is { } family)
        {
            // O atalho abre o app com a identidade do pacote; o caminho direto fica por último (e some quando o app atualiza).
            var familyAliases = Path.Combine(userAliasRoot, family);
            var sameName = Path.Combine(familyAliases, name);
            if (File.Exists(sameName)) yield return sameName;
            else if (Directory.Exists(familyAliases))
            {
                // Um único atalho no pacote (ex.: wt.exe para WindowsTerminal.exe): é o do app.
                var aliases = Directory.GetFiles(familyAliases, "*.exe");
                if (aliases.Length == 1) yield return aliases[0];
            }
            var rootAlias = Path.Combine(userAliasRoot, name);
            if (File.Exists(rootAlias)) yield return rootAlias;
            if (File.Exists(exe)) yield return exe;
            yield break;
        }

        if (SquirrelFolder.IsMatch(folder) && Path.GetDirectoryName(Path.GetDirectoryName(exe)) is { } root
            && File.Exists(Path.Combine(root, "Update.exe")) && NewestSquirrel(root, name) is { } newest)
        {
            yield return newest;
            yield break;
        }

        if (File.Exists(exe)) yield return exe;
    }

    private static string? NewestSquirrel(string root, string name)
    {
        try
        {
            return Directory.GetDirectories(root, "app-*")
                .Where(d => SquirrelFolder.IsMatch(Path.GetFileName(d)) && File.Exists(Path.Combine(d, name)))
                .OrderByDescending(d => Version.TryParse(Path.GetFileName(d)[4..], out var v) ? v : new Version())
                .Select(d => Path.Combine(d, name))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Família do pacote se o executável está numa pasta de pacote dentro de WindowsApps (em qualquer nível abaixo dela).</summary>
    private static string? PackageOf(string exe)
    {
        var parts = exe.Replace('/', '\\').Split('\\');
        var index = Array.FindIndex(parts, p => p.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < parts.Length - 1 ? PackageFamily(parts[index + 1]) : null;
    }
}
