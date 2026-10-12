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
}
