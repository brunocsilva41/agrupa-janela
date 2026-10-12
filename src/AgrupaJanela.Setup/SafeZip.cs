using System;
using System.Collections.Generic;
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

    private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>"CON", "nul.txt", "COM1 .log": o nome antes do primeiro ponto (sem espaços no fim) é de dispositivo.</summary>
    internal static bool IsReservedName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot < 0 ? segment : segment.Substring(0, dot)).TrimEnd(' ');
        return ReservedNames.Contains(stem);
    }

    /// <summary>Caminho final de uma entrada (termina em "\" se for pasta), null se vazia; lança se sair da pasta.</summary>
    public static string? ResolveEntryPath(string root, string entryName)
    {
        root = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        var name = entryName.Replace('/', '\\');
        if (name.Length == 0) return null;
        if (name.StartsWith("\\", StringComparison.Ordinal) || name.IndexOf(':') >= 0 || Path.IsPathRooted(name))
            throw new InvalidDataException($"Entrada inválida no pacote (caminho absoluto): {entryName}");
        // Nomes de dispositivo (CON, NUL, COM1...) em qualquer parte do caminho, com ou sem extensão. Não dependemos
        // do GetFullPath: no Windows 10 ele vira \\.\CON (e cai fora da pasta), no Windows 11 vira um arquivo comum.
        foreach (var segment in name.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            if (IsReservedName(segment))
                throw new InvalidDataException($"Entrada inválida no pacote (nome reservado do Windows): {entryName}");
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
