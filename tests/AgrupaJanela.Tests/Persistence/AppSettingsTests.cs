using System.Text.Json;
using AgrupaJanela.Hosting;
using AgrupaJanela.Persistence;

namespace AgrupaJanela.Tests.Persistence;

/// <summary>
/// Nunca chama Load/Save/SetOverride/StartWithWindows: eles tocam %APPDATA% e o registro reais.
/// A desserialização usa JsonSerializer com as mesmas opções de AppSettings.Load (padrão).
/// </summary>
public class AppSettingsTests
{
    private static AppSettings Parse(string json) => JsonSerializer.Deserialize<AppSettings>(json)!;

    [Fact]
    public void Padroes()
    {
        var s = new AppSettings();
        Assert.False(s.ReopenSavedOnStart);
        Assert.True(s.SystemMenuEnabled);
        Assert.True(s.HideMainAfterGrouping);
        Assert.Empty(s.EmbedOverrides);
    }

    // ---------- EmbedOverrides (setter) ----------

    [Fact]
    public void EmbedOverrides_Setter_NormalizaChaveEDescartaAuto()
    {
        var s = new AppSettings
        {
            EmbedOverrides = new Dictionary<string, EmbedMode>
            {
                ["Chrome.EXE"] = EmbedMode.Dock,
                [@"C:\Windows\System32\Notepad.exe"] = EmbedMode.Reparent,
                ["\"C:\\Program Files\\App\\Code.exe\""] = EmbedMode.Dock,
                ["foo.exe"] = EmbedMode.Auto,
                [""] = EmbedMode.Dock,
                ["   "] = EmbedMode.Dock,
                [@"C:\pasta\"] = EmbedMode.Dock, // sem nome de arquivo
            },
        };
        Assert.Equal(new[] { "chrome.exe", "code.exe", "notepad.exe" }, s.EmbedOverrides.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(EmbedMode.Dock, s.EmbedOverrides["chrome.exe"]);
        Assert.Equal(EmbedMode.Reparent, s.EmbedOverrides["notepad.exe"]);
        Assert.Equal(EmbedMode.Dock, s.EmbedOverrides["code.exe"]);
    }

    [Fact]
    public void EmbedOverrides_Setter_DicionarioResultanteIgnoraMaiusculas()
    {
        var s = new AppSettings { EmbedOverrides = new() { ["chrome.exe"] = EmbedMode.Dock } };
        Assert.True(s.EmbedOverrides.ContainsKey("CHROME.EXE"));
    }

    [Fact]
    public void EmbedOverrides_Setter_ChavesQueColidemAposNormalizar_ViramUma()
    {
        var s = new AppSettings
        {
            EmbedOverrides = new Dictionary<string, EmbedMode>(StringComparer.Ordinal)
            {
                ["A.exe"] = EmbedMode.Dock,
                [@"C:\x\a.EXE"] = EmbedMode.Reparent,
            },
        };
        var only = Assert.Single(s.EmbedOverrides);
        Assert.Equal("a.exe", only.Key);
    }

    [Fact]
    public void EmbedOverrides_Setter_Null_ViraVazio()
    {
        var s = new AppSettings { EmbedOverrides = null! };
        Assert.NotNull(s.EmbedOverrides);
        Assert.Empty(s.EmbedOverrides);
    }

    [Fact]
    public void EmbedOverrides_Setter_NaoGuardaReferenciaDoDicionarioOriginal()
    {
        var source = new Dictionary<string, EmbedMode> { ["a.exe"] = EmbedMode.Dock };
        var s = new AppSettings { EmbedOverrides = source };
        source["b.exe"] = EmbedMode.Dock;
        Assert.Single(s.EmbedOverrides);
    }

    // ---------- GetOverride ----------

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", EmbedMode.Dock)]
    [InlineData(@"D:\outro\lugar\CHROME.EXE", EmbedMode.Dock)]
    [InlineData("\"C:\\Program Files\\Google\\Chrome.exe\"", EmbedMode.Dock)]
    [InlineData("  chrome.exe  ", EmbedMode.Dock)]
    [InlineData("chrome.exe", EmbedMode.Dock)]
    [InlineData(@"C:\Windows\notepad.exe", EmbedMode.Reparent)]
    [InlineData(@"C:\x\desconhecido.exe", EmbedMode.Auto)]
    [InlineData(@"C:\chrome", EmbedMode.Auto)]
    [InlineData(@"C:\pasta\", EmbedMode.Auto)]
    [InlineData("", EmbedMode.Auto)]
    [InlineData("   ", EmbedMode.Auto)]
    [InlineData(null, EmbedMode.Auto)]
    public void GetOverride_ChavePeloNomeDoArquivo_SemDiferenciarMaiusculas(string? exePath, EmbedMode expected)
    {
        var s = new AppSettings { EmbedOverrides = new() { ["Chrome.exe"] = EmbedMode.Dock, ["notepad.exe"] = EmbedMode.Reparent } };
        Assert.Equal(expected, s.GetOverride(exePath));
    }

    // ---------- Desserialização ----------

    [Fact]
    public void Json_Vazio_UsaPadroes()
    {
        var s = Parse("{}");
        Assert.False(s.ReopenSavedOnStart);
        Assert.True(s.SystemMenuEnabled);
        Assert.True(s.HideMainAfterGrouping);
        Assert.Empty(s.EmbedOverrides);
    }

    [Fact]
    public void Json_ArquivoAntigoSemEmbedOverrides()
    {
        var s = Parse("""{"ReopenSavedOnStart":true,"SystemMenuEnabled":false}""");
        Assert.True(s.ReopenSavedOnStart);
        Assert.False(s.SystemMenuEnabled);
        Assert.True(s.HideMainAfterGrouping);
        Assert.Empty(s.EmbedOverrides);
        Assert.Equal(EmbedMode.Auto, s.GetOverride("chrome.exe"));
    }

    [Fact]
    public void Json_EmbedOverridesNull_ViraVazio()
    {
        var s = Parse("""{"EmbedOverrides":null}""");
        Assert.NotNull(s.EmbedOverrides);
        Assert.Empty(s.EmbedOverrides);
    }

    [Fact]
    public void Json_ValoresInvalidosViramAutoESaoDescartados_ChavesNormalizadas()
    {
        var s = Parse("""
        {
          "EmbedOverrides": {
            "Chrome.EXE": "Dock",
            "code.exe": "reparent",
            "bogus.exe": "Bogus",
            "num.exe": 2,
            "fora.exe": 99,
            "nulo.exe": null,
            "obj.exe": { "x": 1 },
            "auto.exe": "Auto",
            "": "Dock"
          }
        }
        """);
        Assert.Equal(new[] { "chrome.exe", "code.exe", "num.exe" }, s.EmbedOverrides.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(EmbedMode.Dock, s.GetOverride(@"C:\x\CHROME.exe"));
        Assert.Equal(EmbedMode.Reparent, s.GetOverride("Code.exe"));
        Assert.Equal(EmbedMode.Dock, s.GetOverride("num.exe"));
        Assert.Equal(EmbedMode.Auto, s.GetOverride("bogus.exe"));
    }

    [Fact]
    public void Json_Null_RetornaNull_LoadUsaPadrao()
    {
        // AppSettings.Load faz "?? new()" para este caso.
        Assert.Null(JsonSerializer.Deserialize<AppSettings>("null"));
    }

    [Theory]
    [InlineData("""{"SystemMenuEnabled":"sim"}""")]
    [InlineData("""{"EmbedOverrides":[1,2]}""")]
    [InlineData("{ quebrado")]
    [InlineData("")]
    public void Json_Corrompido_Lanca_LoadCaiNoCatch(string json)
    {
        // AppSettings.Load envolve a desserialização em try/catch e devolve os padrões.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppSettings>(json));
    }

    [Fact]
    public void Json_RoundTrip_ComAsOpcoesDoSave()
    {
        var original = new AppSettings
        {
            ReopenSavedOnStart = true,
            SystemMenuEnabled = false,
            HideMainAfterGrouping = false,
            EmbedOverrides = new() { ["chrome.exe"] = EmbedMode.Dock, ["notepad.exe"] = EmbedMode.Reparent },
        };
        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });

        Assert.Contains("\"Dock\"", json);           // enum gravado como texto
        Assert.DoesNotContain("StartWithWindows", json); // estático: não vai para o arquivo

        var loaded = Parse(json);
        Assert.True(loaded.ReopenSavedOnStart);
        Assert.False(loaded.SystemMenuEnabled);
        Assert.False(loaded.HideMainAfterGrouping);
        Assert.Equal(original.EmbedOverrides.OrderBy(p => p.Key), loaded.EmbedOverrides.OrderBy(p => p.Key));
        Assert.Equal(EmbedMode.Dock, loaded.GetOverride("CHROME.EXE"));
    }
}
