using System;
using System.IO;
using System.IO.Compression;

namespace AgrupaJanela.Setup;

/// <summary>Extração de zip protegida contra zip-slip: todo caminho final precisa ficar dentro da pasta destino.</summary>
internal static class SafeZip
{
    public static int Extract(Stream zip, string destinationDir, Action<int, int>? progress = null)
    {
        var root = Path.GetFullPath(destinationDir).TrimEnd('\\') + "\\";
        Directory.CreateDirectory(root);
        var count = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var total = archive.Entries.Count;
        for (var i = 0; i < total; i++)
        {
            var entry = archive.Entries[i];
            var target = ResolveEntryPath(root, entry.FullName);
            if (target == null) continue;
            if (target.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var src = entry.Open();
                using var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                src.CopyTo(dst);
                count++;
            }
            progress?.Invoke(i + 1, total);
        }
        return count;
    }

    /// <summary>Caminho final de uma entrada (termina em "\" se for pasta), null se vazia; lança se sair da pasta.</summary>
    public static string? ResolveEntryPath(string root, string entryName)
    {
        root = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        var name = entryName.Replace('/', '\\');
        if (name.Length == 0) return null;
        if (name.StartsWith("\\", StringComparison.Ordinal) || name.IndexOf(':') >= 0 || Path.IsPathRooted(name))
            throw new InvalidDataException($"Entrada inválida no pacote (caminho absoluto): {entryName}");
        var isDir = name.EndsWith("\\", StringComparison.Ordinal);
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, name)); }
        catch (Exception ex) { throw new InvalidDataException($"Entrada inválida no pacote: {entryName}", ex); }
        var check = full.TrimEnd('\\') + "\\";
        if (!check.StartsWith(root, StringComparison.OrdinalIgnoreCase) || check.Length == root.Length && !isDir)
            throw new InvalidDataException($"Entrada inválida no pacote (sai da pasta de destino): {entryName}");
        if (check.Length == root.Length) return null; // a própria raiz
        return isDir ? check : full;
    }
}
