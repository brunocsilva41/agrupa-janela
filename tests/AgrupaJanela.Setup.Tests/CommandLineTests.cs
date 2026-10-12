using System;
using Xunit;

namespace AgrupaJanela.Setup.Tests;

public class CommandLineTests
{
    /// <summary>Quebra a linha de comando por espaços (os argumentos aqui não têm espaços).</summary>
    private static string[] Args(string linha) =>
        linha.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Sem_argumentos_nao_liga_nenhum_modo()
    {
        var cmd = CommandLine.Parse(Array.Empty<string>());

        Assert.Null(cmd.Error);
        Assert.False(cmd.Silent);
        Assert.False(cmd.Update);
        Assert.False(cmd.Uninstall);
        Assert.False(cmd.Relaunch);
        Assert.Null(cmd.WaitPid);
    }

    [Theory]
    [InlineData("--silent", true, false, false, false, null)]
    [InlineData("--uninstall", false, false, true, false, null)]
    [InlineData("--uninstall --silent", true, false, true, false, null)]
    [InlineData("--update", false, true, false, false, null)]
    [InlineData("--update --relaunch", false, true, false, true, null)]
    [InlineData("--update --wait-pid 1234", false, true, false, false, 1234)]
    [InlineData("--update --wait-pid 1234 --relaunch", false, true, false, true, 1234)]
    [InlineData("--update --silent --wait-pid 1234 --relaunch", true, true, false, true, 1234)]
    [InlineData("--WAIT-PID 42 --UPDATE --Relaunch", false, true, false, true, 42)]
    public void Aceita_combinacoes_validas(string linha, bool silent, bool update, bool uninstall, bool relaunch, int? waitPid)
    {
        var cmd = CommandLine.Parse(Args(linha));

        Assert.Null(cmd.Error);
        Assert.Equal(silent, cmd.Silent);
        Assert.Equal(update, cmd.Update);
        Assert.Equal(uninstall, cmd.Uninstall);
        Assert.Equal(relaunch, cmd.Relaunch);
        Assert.Equal(waitPid, cmd.WaitPid);
    }

    [Fact]
    public void Aceita_install_dir_sem_validar_o_modo_de_teste()
    {
        // A recusa de --install-dir fora do modo de teste é feita em App.OnStartup, não aqui.
        var cmd = CommandLine.Parse(new[] { "--install-dir", @"C:\Teste\SplitDeck" });

        Assert.Null(cmd.Error);
        Assert.Equal(@"C:\Teste\SplitDeck", cmd.InstallDir);
    }

    [Theory]
    [InlineData("--uninstall --update")]
    [InlineData("--relaunch")]
    [InlineData("--wait-pid 1234")]
    [InlineData("--uninstall --wait-pid 1234")]
    [InlineData("--uninstall --relaunch")]
    [InlineData("--update --wait-pid")]
    [InlineData("--update --wait-pid abc")]
    [InlineData("--update --wait-pid 0")]
    [InlineData("--update --wait-pid -5")]
    [InlineData("--install-dir")]
    [InlineData("--instalar")]
    [InlineData("/silent")]
    public void Recusa_entradas_invalidas(string linha)
    {
        var cmd = CommandLine.Parse(Args(linha));

        Assert.NotNull(cmd.Error);
    }

    [Fact]
    public void Recusa_install_dir_com_valor_vazio()
    {
        var cmd = CommandLine.Parse(new[] { "--install-dir", "" });

        Assert.NotNull(cmd.Error);
    }
}
