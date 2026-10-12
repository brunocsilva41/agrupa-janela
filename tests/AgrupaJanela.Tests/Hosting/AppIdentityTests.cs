using AgrupaJanela.Hosting;

namespace AgrupaJanela.Tests.Hosting;

/// <summary>
/// AppIdentity.Of consulta o processo real via Win32; por isso o parser ArgumentsOf (internal) é testado direto.
/// </summary>
public class AppIdentityTests
{
    [Theory]
    [InlineData(@"C:\x\a.exe -k", "-k")]
    [InlineData(@"""C:\x\a.exe"" -k", "-k")]
    [InlineData(@"""C:\Program Files\x.exe"" --a b", "--a b")]
    [InlineData(@"""C:\Program Files\x.exe""   --a   b  ", "--a   b")] // só as pontas são aparadas
    [InlineData(@"   C:\x\a.exe   -k  ", "-k")]                        // espaços antes do executável
    [InlineData(@"""C:\x\a.exe""-k", "-k")]                            // sem espaço depois da aspa
    [InlineData(@"C:\x\a.exe ""arquivo com espaço.txt""", @"""arquivo com espaço.txt""")]
    public void ComArgumentos(string commandLine, string expected) =>
        Assert.Equal(expected, AppIdentity.ArgumentsOf(commandLine));

    [Theory]
    [InlineData(@"C:\x\a.exe")]
    [InlineData(@"""C:\Program Files\x.exe""")]
    [InlineData(@"""C:\Program Files\x.exe""    ")]
    [InlineData(@"notepad")]
    public void SemArgumentos_Null(string commandLine) =>
        Assert.Null(AppIdentity.ArgumentsOf(commandLine));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \r\n")]
    public void NuloOuSoEspacos_Null(string? commandLine) =>
        Assert.Null(AppIdentity.ArgumentsOf(commandLine));

    [Fact]
    public void AspasNaoFechadas_TudoEhOExecutavel()
    {
        // Sem a aspa de fechamento, a linha inteira é tratada como executável (sem argumentos).
        Assert.Null(AppIdentity.ArgumentsOf(@"""C:\Program Files\x.exe --a b"));
        Assert.Null(AppIdentity.ArgumentsOf(@""""));
    }

    [Fact]
    public void TabComoSeparador_PreservaArgumentos() =>
        Assert.Equal("-k", AppIdentity.ArgumentsOf("C:\\x\\a.exe\t-k"));

    [Fact]
    public void Record_IgualdadePorValor()
    {
        var a = new AppIdentity(@"C:\a.exe", "-k", "Título", 42, 7);
        var b = new AppIdentity(@"C:\a.exe", "-k", "Título", 42, 7);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { LastHandle = 43 });
    }
}
