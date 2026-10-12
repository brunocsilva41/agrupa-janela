using System;
using System.IO;
using Xunit;

namespace AgrupaJanela.Setup.Tests;

public class PathRulesTests
{
    [Fact]
    public void Recusa_pasta_dentro_do_Windows()
    {
        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SplitDeckTeste");
        AssertRecusada(pasta, "Não é permitido instalar em");
    }

    [Fact]
    public void Recusa_pasta_dentro_do_Program_Files()
    {
        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SplitDeckTeste");
        AssertRecusada(pasta, "Não é permitido instalar em");
    }

    [Fact]
    public void Recusa_pasta_dentro_do_ProgramData()
    {
        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SplitDeckTeste");
        AssertRecusada(pasta, "Não é permitido instalar em");
    }

    [Theory]
    [InlineData(@"\\servidor\compartilhado\SplitDeck")]
    [InlineData(@"//servidor/compartilhado/SplitDeck")]
    public void Recusa_caminho_UNC(string caminho)
    {
        AssertRecusada(caminho, "Pastas de rede");
    }

    [Fact]
    public void Recusa_raiz_de_disco()
    {
        var raiz = Path.GetPathRoot(Environment.SystemDirectory)!;
        AssertRecusada(raiz, "raiz de uma unidade");
    }

    [Theory]
    [InlineData(@"C:\Users\Teste\%TEMP%\SplitDeck")]
    [InlineData("C:\\Users\\Teste\\\"SplitDeck\"")]
    public void Recusa_caminho_com_percentual_ou_aspas(string caminho)
    {
        AssertRecusada(caminho, "caracteres não permitidos");
    }

    [Fact]
    public void Aceita_pasta_sob_LocalAppData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pasta = Path.Combine(local, "Programs", "SplitDeck-Teste-" + Guid.NewGuid().ToString("N"));

        var erro = PathRules.ValidateLocation(pasta, out var full);

        Assert.Null(erro);
        Assert.StartsWith(PathRules.ToLongPath(local), full, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(pasta), "ValidateLocation não deve criar pastas");
    }

    private static void AssertRecusada(string caminho, string trechoDaMensagem)
    {
        var erro = PathRules.ValidateLocation(caminho, out _);
        Assert.NotNull(erro);
        Assert.Contains(trechoDaMensagem, erro);
    }
}
