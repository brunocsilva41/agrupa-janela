using System.Globalization;
using System.Reflection;
using System.Text.Json;
using AgrupaJanela.Hosting;
using AgrupaJanela.Layout;
using AgrupaJanela.Persistence;

namespace AgrupaJanela.Tests.Persistence;

/// <summary>
/// GroupStore não é instanciado (o construtor lê o groups.json real). As opções de JSON
/// são lidas do campo estático privado GroupStore.Options por reflexão, para testar exatamente as do app.
/// </summary>
public class SavedGroupTests
{
    private static readonly JsonSerializerOptions StoreOptions =
        (JsonSerializerOptions)(typeof(GroupStore).GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) ?? throw new InvalidOperationException("GroupStore.Options não encontrado (renomeado?)"));

    private static AppIdentity Id(string name) => new($@"C:\apps\{name}.exe", name == "a" ? null : $"--n {name}", $"Título {name}", 1000 + name[0], (uint)name[0]);
    private static NodeDto Leaf(string name) => new() { App = Id(name) };

    /// <summary>H[a:1.6, V[b:0.3333, H[c:1, d:2]:0.6667]:1]</summary>
    private static NodeDto Nested() => new()
    {
        Horizontal = true,
        Weights = new() { 1.6, 1 },
        Children = new()
        {
            Leaf("a"),
            new NodeDto
            {
                Horizontal = false,
                Weights = new() { 1.0 / 3, 2.0 / 3 },
                Children = new() { Leaf("b"), new NodeDto { Horizontal = true, Weights = new() { 1, 2 }, Children = new() { Leaf("c"), Leaf("d") } } },
            },
        },
    };

    // ---------- AppCount / Summary ----------

    [Fact]
    public void AppCount_SemLayout_Zero() => Assert.Equal(0, new SavedGroup().AppCount);

    [Fact]
    public void AppCount_Folha_Um() => Assert.Equal(1, new SavedGroup { Layout = Leaf("a") }.AppCount);

    [Fact]
    public void AppCount_Aninhado_ContaFolhas() => Assert.Equal(4, new SavedGroup { Layout = Nested() }.AppCount);

    [Fact]
    public void AppCount_DivisaoVazia_Zero() => Assert.Equal(0, new SavedGroup { Layout = new NodeDto { Horizontal = true } }.AppCount);

    [Fact]
    public void Summary_Formato()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var group = new SavedGroup { Layout = Nested(), SavedAt = new DateTime(2026, 3, 5, 14, 7, 59) };
            Assert.Equal("4 app(s) · salvo em 05/03 14:07", group.Summary);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Padroes()
    {
        var g = new SavedGroup();
        Assert.Equal("", g.Name);
        Assert.True(double.IsNaN(g.Left));
        Assert.True(double.IsNaN(g.Top));
        Assert.Equal(1200, g.Width);
        Assert.Equal(750, g.Height);
        Assert.Null(g.Layout);
    }

    // ---------- JSON ----------

    private static void AssertSameDto(NodeDto? expected, NodeDto? actual)
    {
        if (expected is null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected.App, actual.App);
        Assert.Equal(expected.Horizontal, actual.Horizontal);
        Assert.Equal(expected.Weights, actual.Weights);
        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (var i = 0; i < expected.Children.Count; i++) AssertSameDto(expected.Children[i], actual.Children[i]);
    }

    [Fact]
    public void Json_RoundTrip_SavedGroupComNodeDtoAninhado()
    {
        var original = new SavedGroup
        {
            Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            Name = "Trabalho \"principal\" · ç",
            Tabs = true,
            Layout = Nested(),
            Width = 1600.5,
            Height = 900,
            Maximized = true,
            SavedAt = new DateTime(2026, 9, 30, 15, 36, 12, 345),
            // Left/Top ficam NaN (padrão): exige AllowNamedFloatingPointLiterals.
        };

        var json = JsonSerializer.Serialize(new List<SavedGroup> { original }, StoreOptions);
        Assert.Contains("\"NaN\"", json);

        var loaded = Assert.Single(JsonSerializer.Deserialize<List<SavedGroup>>(json, StoreOptions)!);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Tabs, loaded.Tabs);
        Assert.True(double.IsNaN(loaded.Left));
        Assert.True(double.IsNaN(loaded.Top));
        Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.Height, loaded.Height);
        Assert.Equal(original.Maximized, loaded.Maximized);
        Assert.Equal(original.SavedAt, loaded.SavedAt);
        AssertSameDto(original.Layout, loaded.Layout);
        Assert.Equal(4, loaded.AppCount);
    }

    [Fact]
    public void Json_PropriedadesSoLeitura_SaoIgnoradasNaLeitura()
    {
        // AppCount/Summary são calculadas; um valor salvo no arquivo não pode prevalecer.
        var json = """[{"Name":"x","AppCount":99,"Summary":"lixo","Layout":{"App":{"ExePath":"a","Arguments":null,"Title":"t","LastHandle":1,"LastProcessId":2}}}]""";
        var loaded = Assert.Single(JsonSerializer.Deserialize<List<SavedGroup>>(json, StoreOptions)!);
        Assert.Equal(1, loaded.AppCount);
        Assert.Equal(new AppIdentity("a", null, "t", 1, 2), loaded.Layout!.App);
    }

    [Fact]
    public void Json_ArquivoAntigoSemCampos_UsaPadroes()
    {
        var loaded = Assert.Single(JsonSerializer.Deserialize<List<SavedGroup>>("""[{"Name":"antigo"}]""", StoreOptions)!);
        Assert.Equal("antigo", loaded.Name);
        Assert.True(double.IsNaN(loaded.Left));
        Assert.Equal(1200, loaded.Width);
        Assert.Null(loaded.Layout);
        Assert.Equal(0, loaded.AppCount);
    }

    [Fact]
    public void Json_EstadoDosPaineis_IdaEVolta()
    {
        var terminal = Id("t") with { WorkingDirectory = @"C:\proj" };
        var group = new SavedGroup
        {
            Id = Guid.NewGuid(),
            ActiveIndex = 1,
            MaximizedIndex = 0,
            Layout = new NodeDto { Horizontal = true, Weights = new() { 1, 1 }, Children = new() { new NodeDto { App = terminal, Mode = EmbedMode.Dock }, Leaf("b") } },
        };
        var json = JsonSerializer.Serialize(new List<SavedGroup> { group }, StoreOptions);
        var loaded = Assert.Single(JsonSerializer.Deserialize<List<SavedGroup>>(json, StoreOptions)!);
        Assert.Equal(1, loaded.ActiveIndex);
        Assert.Equal(0, loaded.MaximizedIndex);
        Assert.Equal(EmbedMode.Dock, loaded.Layout!.Children[0].Mode);
        Assert.Equal(EmbedMode.Auto, loaded.Layout.Children[1].Mode);
        Assert.Equal(terminal, loaded.Layout.Children[0].App); // inclui a pasta
        Assert.Equal(@"C:\proj", loaded.Layout.Children[0].App!.WorkingDirectory);
    }

    [Fact]
    public void Json_CamposNovosVazios_NaoPoluemOArquivo()
    {
        var json = JsonSerializer.Serialize(new NodeDto { App = Id("a") }, StoreOptions);
        Assert.DoesNotContain("WorkingDirectory", json);
        Assert.DoesNotContain("\"Mode\"", json);
    }

    [Fact]
    public void Json_ArquivoDaVersaoAnterior_SemEstado_Padroes()
    {
        var loaded = Assert.Single(JsonSerializer.Deserialize<List<SavedGroup>>("""[{"Id":"0f8fad5b-d9cb-469f-a165-70867728950e","Layout":{"App":{"ExePath":"a","Arguments":null,"Title":"t","LastHandle":1,"LastProcessId":2}}}]""", StoreOptions)!);
        Assert.Equal(-1, loaded.ActiveIndex);
        Assert.Equal(-1, loaded.MaximizedIndex);
        Assert.Equal(EmbedMode.Auto, loaded.Layout!.Mode);
        Assert.Null(loaded.Layout.App!.WorkingDirectory);
    }

    [Theory]
    [InlineData(-5, -1)]
    [InlineData(4, -1)] // só há 4 apps (0..3)
    [InlineData(3, 3)]
    [InlineData(-1, -1)]
    public void Sanitize_IndicesForaDoLayout_Descartados(int index, int expected)
    {
        var group = new SavedGroup { Id = Guid.NewGuid(), Layout = Nested(), ActiveIndex = index, MaximizedIndex = index };
        Assert.True(GroupStore.Sanitize(group));
        Assert.Equal(expected, group.ActiveIndex);
        Assert.Equal(expected, group.MaximizedIndex);
    }

    [Fact]
    public void Sanitize_IndiceDeAppDescartadoPeloLimite_ViraMenosUm()
    {
        var many = new NodeDto { Horizontal = true };
        for (var i = 0; i < 20; i++) { many.Children.Add(Leaf(((char)('a' + i)).ToString())); many.Weights.Add(1); }
        var group = new SavedGroup { Id = Guid.NewGuid(), Layout = many, ActiveIndex = 18, MaximizedIndex = 15 };
        Assert.True(GroupStore.Sanitize(group));
        Assert.Equal(16, group.AppCount);
        Assert.Equal(-1, group.ActiveIndex);
        Assert.Equal(15, group.MaximizedIndex);
    }

    [Fact]
    public void ToDto_UsaOEstadoAtualDeCadaApp()
    {
        var hosts = new[] { "a", "b" }.Select(n => new Fakes.FakeWindow(n)).ToList();
        var tree = new LayoutTree();
        foreach (var h in hosts) tree.Add(h, null, true);
        var dto = tree.ToDto(h => h.Identity with { Title = "agora " + ((Fakes.FakeWindow)h).Name });
        Assert.Equal(new[] { "agora a", "agora b" }, dto!.Children.Select(c => c.App!.Title));
        Assert.All(dto.Children, c => Assert.Equal(EmbedMode.Reparent, c.Mode));
    }

    [Fact]
    public void Json_TreeParaDtoParaJsonParaTree_PreservaEstrutura()
    {
        var hosts = new[] { "a", "b", "c", "d" }.Select(n => new Fakes.FakeWindow(n)).ToList();
        var tree = new LayoutTree();
        foreach (var h in hosts) tree.Add(h, null, h.Name != "c");
        var before = Layout.TreeAssert.Describe(tree);

        var group = new SavedGroup { Layout = tree.ToDto() };
        var json = JsonSerializer.Serialize(new List<SavedGroup> { group }, StoreOptions);
        var loaded = JsonSerializer.Deserialize<List<SavedGroup>>(json, StoreOptions)![0];

        var map = hosts.ToDictionary(h => h.Identity, h => (IGroupedWindow)h);
        var rebuilt = new LayoutTree();
        rebuilt.Load(loaded.Layout, id => map.TryGetValue(id, out var h) ? h : null);
        Assert.Equal(before, Layout.TreeAssert.Describe(rebuilt));
        Layout.TreeAssert.Invariants(rebuilt, hosts);
    }
}
