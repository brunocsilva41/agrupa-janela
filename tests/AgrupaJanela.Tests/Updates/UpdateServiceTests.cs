using AgrupaJanela.Updates;

namespace AgrupaJanela.Tests.Updates;

public class UpdateServiceTests
{
    private const string H1 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string H2 = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    private const string Setup = "SplitDeck-Setup-1.0.2.exe";
    private const string Official = "https://github.com/brunocsilva41/agrupa-janela/releases/download/v1.0.2/";
    private static readonly Version Current = new(1, 0, 0);

    // ---- SHA256SUMS.txt ----

    [Fact]
    public void Hash_FormatoHashEspacoDuplo() =>
        Assert.Equal(H1, UpdateService.ExpectedHash(H1 + "  " + Setup + "\n", Setup));

    [Fact]
    public void Hash_FormatoModoBinario_AsteriscoAntesDoNome() =>
        Assert.Equal(H1, UpdateService.ExpectedHash(H1 + " *" + Setup + "\n", Setup));

    [Fact]
    public void Hash_AceitaCRLF() =>
        Assert.Equal(H1, UpdateService.ExpectedHash(H2 + "  outro.zip\r\n" + H1 + "  " + Setup + "\r\n", Setup));

    [Fact]
    public void Hash_MaiusculoViraMinusculo() =>
        Assert.Equal(H1, UpdateService.ExpectedHash(H1.ToUpperInvariant() + "  " + Setup, Setup));

    [Theory]
    [InlineData(H1 + "  SplitDeck-Setup-1.0.2.exe.bak")]           // prefixo não basta
    [InlineData(H1 + "  SplitDeck-Setup-1.0.2.ex")]
    [InlineData(H1 + "  x" + Setup)]                               // sufixo não basta
    public void Hash_NomeParecidoNaoCasa_Null(string sums) =>
        Assert.Null(UpdateService.ExpectedHash(sums, Setup));

    [Fact]
    public void Hash_NomeExatoEntreVariantes_EscolheOExato() =>
        Assert.Equal(H2, UpdateService.ExpectedHash(H1 + "  " + Setup + ".bak\n" + H2 + "  " + Setup + "\n", Setup));

    [Fact]
    public void Hash_SemEntrada_Null() =>
        Assert.Null(UpdateService.ExpectedHash(H1 + "  outro.exe\n", Setup));

    [Fact]
    public void Hash_EntradasConflitantes_Null() =>
        Assert.Null(UpdateService.ExpectedHash(H1 + "  " + Setup + "\n" + H2 + "  " + Setup + "\n", Setup));

    [Theory]
    [InlineData("0123456789abcdef  " + Setup)]                    // hash curto
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  " + Setup)] // não é hex
    [InlineData(H1 + Setup)]                                      // sem separador
    [InlineData(H1 + " " + Setup)]                                // um espaço só
    public void Hash_LinhaMalFormada_Null(string sums) =>
        Assert.Null(UpdateService.ExpectedHash(sums, Setup));

    // ---- JSON de releases/latest ----

    [Fact]
    public void Release_Valido_DevolveAtualizacao()
    {
        var info = UpdateService.ParseRelease(Release(), Current);
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 0, 2), info!.Version);
        Assert.Equal(Official + Setup, info.SetupUrl);
        Assert.Equal(Official + "SHA256SUMS.txt", info.SumsUrl);
        Assert.Equal(1000, info.SetupSize);
        Assert.Equal("https://github.com/brunocsilva41/agrupa-janela/releases/tag/v1.0.2", info.PageUrl);
    }

    [Theory]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    public void Release_DraftOuPrerelease_Ignorado(string draft, string prerelease) =>
        Assert.Null(UpdateService.ParseRelease(Release(draft: draft, prerelease: prerelease), Current));

    [Fact]
    public void Release_DraftComoTexto_NaoLanca()
    {
        // Campo inesperado (texto em vez de booleano) não pode derrubar a checagem.
        var info = UpdateService.ParseRelease(Release(draft: "\"true\""), Current);
        Assert.NotNull(info);
    }

    [Fact]
    public void Release_AssetDeOutroRepositorio_Ignorado() =>
        Assert.Null(UpdateService.ParseRelease(Release(setupHost: "https://github.com/outro/repo/releases/download/v1.0.2/"), Current));

    [Fact]
    public void Release_SumsDeOutroRepositorio_Ignorado() =>
        Assert.Null(UpdateService.ParseRelease(Release(sumsHost: "https://evil.example/releases/download/v1.0.2/"), Current));

    [Fact]
    public void Release_TamanhoAcimaDoLimite_Ignorado() =>
        Assert.Null(UpdateService.ParseRelease(Release(size: 200L * 1024 * 1024 + 1), Current));

    [Fact]
    public void Release_TamanhoNoLimite_Aceito() =>
        Assert.NotNull(UpdateService.ParseRelease(Release(size: 200L * 1024 * 1024), Current));

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    public void Release_VersaoNaoMaisNova_Null(string tag) =>
        Assert.Null(UpdateService.ParseRelease(Release(tag: tag), Current));

    [Fact]
    public void Release_PaginaForaDoRepositorio_UsaRepositorio()
    {
        var info = UpdateService.ParseRelease(Release(html: "https://evil.example/pagina"), Current);
        Assert.Equal(UpdateService.RepoUrl, info!.PageUrl);
    }

    [Fact]
    public void Release_JsonInvalido_Lanca() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => UpdateService.ParseRelease("{ nao é json", Current));

    private static string Release(string draft = "false", string prerelease = "false", string tag = "v1.0.2",
        string setupHost = Official, string sumsHost = Official, long size = 1000,
        string html = "https://github.com/brunocsilva41/agrupa-janela/releases/tag/v1.0.2") =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "draft": {{draft}},
          "prerelease": {{prerelease}},
          "body": "Notas",
          "html_url": "{{html}}",
          "assets": [
            { "name": "{{Setup}}", "browser_download_url": "{{setupHost}}{{Setup}}", "size": {{size}} },
            { "name": "SHA256SUMS.txt", "browser_download_url": "{{sumsHost}}SHA256SUMS.txt", "size": 100 }
          ]
        }
        """;
}
