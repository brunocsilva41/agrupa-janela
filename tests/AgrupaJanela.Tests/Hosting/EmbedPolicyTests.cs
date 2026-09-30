using System.Text.Json;
using AgrupaJanela.Hosting;

namespace AgrupaJanela.Tests.Hosting;

public class EmbedPolicyTests
{
    [Theory]
    [InlineData(EmbedMode.Reparent, "Incorporada")]
    [InlineData(EmbedMode.Dock, "Acoplada")]
    [InlineData(EmbedMode.Auto, "Automático")]
    [InlineData((EmbedMode)99, "Automático")]
    public void Label_ParaCadaModo(EmbedMode mode, string expected) =>
        Assert.Equal(expected, EmbedPolicy.Label(mode));

    // ---------- EmbedModeJsonConverter (via atributo no enum) ----------

    [Theory]
    [InlineData("\"Dock\"", EmbedMode.Dock)]
    [InlineData("\"dock\"", EmbedMode.Dock)]
    [InlineData("\"REPARENT\"", EmbedMode.Reparent)]
    [InlineData("\"Auto\"", EmbedMode.Auto)]
    [InlineData("\"Bogus\"", EmbedMode.Auto)]
    [InlineData("\"\"", EmbedMode.Auto)]
    [InlineData("\"5\"", EmbedMode.Auto)]
    [InlineData("\"1\"", EmbedMode.Reparent)]
    [InlineData("\"Reparent, Dock\"", EmbedMode.Auto)] // combinação não definida
    [InlineData("1", EmbedMode.Reparent)]
    [InlineData("2", EmbedMode.Dock)]
    [InlineData("0", EmbedMode.Auto)]
    [InlineData("99", EmbedMode.Auto)]
    [InlineData("-1", EmbedMode.Auto)]
    [InlineData("1.5", EmbedMode.Auto)]
    [InlineData("true", EmbedMode.Auto)]
    [InlineData("null", EmbedMode.Auto)]
    [InlineData("{\"x\":1}", EmbedMode.Auto)]
    [InlineData("[1,2]", EmbedMode.Auto)]
    public void Converter_Leitura_ValorDesconhecidoViraAuto(string json, EmbedMode expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<EmbedMode>(json));

    [Theory]
    [InlineData(EmbedMode.Auto, "\"Auto\"")]
    [InlineData(EmbedMode.Reparent, "\"Reparent\"")]
    [InlineData(EmbedMode.Dock, "\"Dock\"")]
    public void Converter_Escrita_ComoTexto(EmbedMode mode, string expected) =>
        Assert.Equal(expected, JsonSerializer.Serialize(mode));

    [Fact]
    public void Converter_ObjetoInvalidoNoMeio_NaoDesalinhaOLeitor()
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, EmbedMode>>("{\"a\":{\"x\":[1,{}]},\"b\":\"Dock\"}")!;
        Assert.Equal(EmbedMode.Auto, result["a"]);
        Assert.Equal(EmbedMode.Dock, result["b"]);
    }
}
