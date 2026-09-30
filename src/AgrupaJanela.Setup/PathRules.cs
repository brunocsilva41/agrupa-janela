using System;
using System.IO;
using System.Linq;
using System.Text;

namespace AgrupaJanela.Setup;

/// <summary>Validação da pasta de instalação e utilitários de caminho.</summary>
internal static class PathRules
{
    private static readonly char[] ForbiddenChars = { '"', '%', '*', '?', '<', '>', '|' };

    /// <summary>Valida o formato e o local. Retorna a mensagem de erro (ou null) e o caminho completo normalizado.</summary>
    public static string? ValidateLocation(string? input, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(input)) return "Informe a pasta de instalação.";
        var raw = input!.Trim();
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
            return "Pastas de rede (\\\\servidor\\...) não são permitidas. Use uma pasta deste computador.";
        if (raw.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || raw.IndexOfAny(ForbiddenChars) >= 0)
            return "O caminho contém caracteres não permitidos.";
        if (raw.Length < 3 || !IsAsciiLetter(raw[0]) || raw[1] != ':' || (raw[2] != '\\' && raw[2] != '/') || raw.IndexOf(':', 2) >= 0)
            return "Use um caminho completo, por exemplo C:\\Users\\voce\\AppData\\Local\\Programs\\AgrupaJanela.";

        string full;
        try { full = ToLongPath(Path.GetFullPath(raw)).TrimEnd('\\', '/'); }
        catch (Exception) { return "Caminho inválido."; }

        if (full.Length <= 2) return "Não instale na raiz de uma unidade. Use uma pasta própria para o SplitDeck.";
        if (full.Length > 200) return "Caminho muito longo (máximo de 200 caracteres).";
        if (full.Split('\\').Skip(1).Any(s => s.Length == 0 || s.EndsWith(".", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal)))
            return "Caminho inválido (nomes de pasta não podem terminar em ponto ou espaço).";

        DriveType driveType;
        try { driveType = new DriveInfo(full.Substring(0, 1)).DriveType; }
        catch (Exception) { return "Unidade inválida."; }
        if (driveType == DriveType.Network) return "Unidades de rede não são permitidas. Use uma pasta deste computador.";
        if (driveType != DriveType.Fixed) return "Use uma pasta em um disco fixo deste computador.";

        foreach (var system in SystemRoots())
            if (IsSameOrInside(full, system))
                return $"Não é permitido instalar em {system}. O SplitDeck é instalado só para o seu usuário, sem administrador.";

        foreach (var known in KnownUserFolders())
            if (string.Equals(full, known, StringComparison.OrdinalIgnoreCase))
                return "Escolha uma pasta própria para o SplitDeck (a desinstalação apaga a pasta inteira).";

        fullPath = full;
        return null;
    }

    /// <summary>
    /// A pasta precisa ser nova, vazia ou de uma instalação anterior: a desinstalação apaga a pasta inteira,
    /// então não pode haver arquivos de outras coisas lá dentro.
    /// </summary>
    public static string? ValidateTarget(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath)) return "Já existe um arquivo com esse nome. Escolha outra pasta.";
            if (!Directory.Exists(fullPath)) return null;
            if (LooksLikeInstall(fullPath)) return null;
            if (!Directory.EnumerateFileSystemEntries(fullPath).Any()) return null;
            return "A pasta já existe e tem outros arquivos. Escolha uma pasta nova ou vazia (a desinstalação apaga a pasta inteira).";
        }
        catch (Exception ex)
        {
            return $"Não foi possível acessar a pasta: {ex.Message}";
        }
    }

    public static bool LooksLikeInstall(string dir) =>
        File.Exists(Path.Combine(dir, SetupContext.AppExeName)) || File.Exists(Path.Combine(dir, SetupContext.SetupExeName));

    public static bool IsSameOrInside(string path, string root)
    {
        root = root.TrimEnd('\\', '/');
        path = path.TrimEnd('\\', '/');
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Converte nomes curtos 8.3 (ex.: FULANO~1) no caminho longo; mantém o que não existir.</summary>
    public static string ToLongPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var sb = new StringBuilder(1024);
            var n = NativeMethods.GetLongPathName(full, sb, sb.Capacity);
            if (n > 0 && n < sb.Capacity) return sb.ToString();
            var parent = Path.GetDirectoryName(full.TrimEnd('\\'));
            if (parent == null || parent == full) return full;
            return Path.Combine(ToLongPath(parent), Path.GetFileName(full.TrimEnd('\\')));
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static string[] SystemRoots() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        }
        .Where(p => !string.IsNullOrEmpty(p)).Select(p => p.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] KnownUserFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                local,
                string.IsNullOrEmpty(local) ? "" : Path.Combine(local, "Programs"),
                Path.GetTempPath(),
                ToLongPath(Path.GetTempPath()),
            }
            .Where(p => !string.IsNullOrEmpty(p)).Select(p => p.TrimEnd('\\')).ToArray();
    }
}
