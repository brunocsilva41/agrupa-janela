using System.IO;

namespace AgrupaJanela.Persistence;

/// <summary>
/// Gravação atômica: escreve num .tmp e troca pelo destino. Uma queda no meio da gravação nunca deixa
/// um JSON pela metade no lugar do bom.
/// </summary>
public static class AtomicFile
{
    private const int Attempts = 3;
    private const int RetryDelayMs = 80; // bloqueio passageiro (antivírus, backup) costuma passar em frações de segundo

    /// <summary>Grava o texto. Repete em bloqueios passageiros e, se nada der certo, lança a última exceção.</summary>
    public static void WriteAllText(string path, string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, text);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= Attempts) throw;
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    /// <summary>
    /// Guarda uma cópia de um arquivo ilegível como "nome.bak-AAAAMMDD-HHmmss". Nunca sobrescreve um .bak existente.
    /// Devolve o caminho da cópia, ou null se não foi possível copiar (o chamador não deve gravar por cima).
    /// </summary>
    public static string? CopyAsBackup(string path)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            for (var n = 0; n < 100; n++)
            {
                var target = path + ".bak-" + stamp + (n == 0 ? "" : "-" + n);
                if (File.Exists(target)) continue;
                File.Copy(path, target, overwrite: false);
                return target;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
