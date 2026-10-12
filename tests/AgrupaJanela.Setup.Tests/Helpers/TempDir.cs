using System;
using System.IO;

namespace AgrupaJanela.Setup.Tests.Helpers;

/// <summary>Pasta temporária única em %TEMP%, apagada ao fim do teste.</summary>
internal sealed class TempDir : IDisposable
{
    private TempDir(string path) => Path = path;

    public string Path { get; }

    public static TempDir Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "splitdeck-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDir(path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
        catch (IOException)
        {
            // Pasta de teste travada por outro processo: não derruba o teste.
        }
    }
}
