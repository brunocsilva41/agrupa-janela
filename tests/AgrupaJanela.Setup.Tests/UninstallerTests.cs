using System;
using System.IO;
using AgrupaJanela.Setup.Tests.Helpers;
using Xunit;

namespace AgrupaJanela.Setup.Tests;

/// <summary>
/// Desinstalação com o modo de teste do instalador (SPLITDECK_SETUP_TEST=1): arquivos e registro vão para a raiz de teste
/// (%TEMP%\SplitDeck-SetupTest e HKCU\Software\SplitDeck-SetupTest), nunca para %APPDATA%\SplitDeck nem para a instalação real.
/// </summary>
public class UninstallerTests
{
    [Fact]
    public void Nao_apaga_pasta_que_nao_tem_SplitDeck_exe()
    {
        using var temp = TempDir.Create();
        using var modo = ModoDeTeste.Ligar();
        var pasta = Path.Combine(temp.Path, "Programs", "SplitDeck");
        Directory.CreateDirectory(Path.Combine(pasta, "subpasta"));
        var arquivo = Path.Combine(pasta, "documento-importante.txt");
        var outro = Path.Combine(pasta, "subpasta", "outro.txt");
        File.WriteAllText(arquivo, "não apague");
        File.WriteAllText(outro, "nem este");

        var ctx = SetupContext.Create();
        Assert.True(ctx.TestMode);

        new Uninstaller(ctx, null).Run(pasta, removeData: false);

        Assert.True(Directory.Exists(pasta));
        Assert.Equal("não apague", File.ReadAllText(arquivo));
        Assert.True(File.Exists(outro));
        Assert.Null(SelfDelete.PendingDir);
    }

    /// <summary>Liga SPLITDECK_SETUP_TEST=1 só durante o teste e restaura o valor anterior ao final.</summary>
    private sealed class ModoDeTeste : IDisposable
    {
        private readonly string? _anterior;

        private ModoDeTeste()
        {
            _anterior = Environment.GetEnvironmentVariable(SetupContext.TestEnvironmentVariable);
            Environment.SetEnvironmentVariable(SetupContext.TestEnvironmentVariable, "1");
        }

        public static ModoDeTeste Ligar() => new();

        public void Dispose() => Environment.SetEnvironmentVariable(SetupContext.TestEnvironmentVariable, _anterior);
    }
}
