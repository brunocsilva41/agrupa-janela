using AgrupaJanela.Hosting;
using AgrupaJanela.Layout;
using AgrupaJanela.Tests.Fakes;
using static AgrupaJanela.Tests.Layout.TreeAssert;

namespace AgrupaJanela.Tests.Layout;

public class LayoutTreeTests
{
    private readonly Dictionary<string, FakeWindow> _w = new();
    private FakeWindow W(string name) => _w.TryGetValue(name, out var w) ? w : _w[name] = new FakeWindow(name);
    private IGroupedWindow[] Hosts(params string[] names) => names.Select(n => (IGroupedWindow)W(n)).ToArray();

    /// <summary>H[a:1,b:1,c:1].</summary>
    private LayoutTree Columns(params string[] names)
    {
        var tree = new LayoutTree();
        foreach (var n in names) tree.Add(W(n), null, true);
        tree.ApplyPreset(Preset.Columns);
        return tree;
    }

    // ---------- Add ----------

    [Fact]
    public void Add_Primeiro_ViraRaizFolha()
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        var leaf = Assert.IsType<LeafNode>(tree.Root);
        Assert.Same(W("a"), leaf.Host);
        Assert.Null(leaf.Parent);
        Invariants(tree, Hosts("a"));
    }

    [Theory]
    [InlineData(true, "H[a:1,b:1]")]
    [InlineData(false, "V[a:1,b:1]")]
    public void Add_Segundo_DivideConformeWide(bool wide, string expected)
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, wide);
        tree.Add(W("b"), null, wide);
        Assert.Equal(expected, Describe(tree));
        Invariants(tree, Hosts("a", "b"));
    }

    [Fact]
    public void Add_TerceiroWide_DivideUltimoDentroDaMesmaDivisao()
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        tree.Add(W("b"), null, true);
        tree.Add(W("c"), null, true);
        Assert.Equal("H[a:1,b:0.5,c:0.5]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void Add_TerceiroNaoWide_EmpilhaSobreOUltimo()
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        tree.Add(W("b"), null, true);
        tree.Add(W("c"), null, false);
        Assert.Equal("H[a:1,V[b:1,c:1]:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void Add_ComBeside_DivideOPainelIndicado()
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        tree.Add(W("b"), null, true);
        tree.Add(W("c"), tree.Find(W("a")), false);
        Assert.Equal("H[V[a:1,c:1]:1,b:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    // ---------- InsertAt ----------

    [Theory]
    [InlineData(DropZone.Left, "H[b:1,a:1]")]
    [InlineData(DropZone.Right, "H[a:1,b:1]")]
    [InlineData(DropZone.Top, "V[b:1,a:1]")]
    [InlineData(DropZone.Bottom, "V[a:1,b:1]")]
    [InlineData(DropZone.Center, "H[b:1,a:1]")] // novo fica no lugar do alvo; alvo vai para a direita
    public void InsertAt_SobreRaizFolha(DropZone zone, string expected)
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        tree.InsertAt(W("b"), W("a"), zone);
        Assert.Equal(expected, Describe(tree));
        Invariants(tree, Hosts("a", "b"));
    }

    [Theory]
    [InlineData(DropZone.Left, "H[c:0.5,a:0.5,b:1]")]
    [InlineData(DropZone.Right, "H[a:0.5,c:0.5,b:1]")]
    [InlineData(DropZone.Top, "H[V[c:1,a:1]:1,b:1]")]
    [InlineData(DropZone.Bottom, "H[V[a:1,c:1]:1,b:1]")]
    [InlineData(DropZone.Center, "H[c:0.5,a:0.5,b:1]")]
    public void InsertAt_DentroDeDivisaoHorizontal(DropZone zone, string expected)
    {
        var tree = Columns("a", "b");
        tree.InsertAt(W("c"), W("a"), zone);
        Assert.Equal(expected, Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void InsertAt_AlvoInexistente_AdicionaADireitaDoUltimo()
    {
        var tree = Columns("a", "b");
        tree.InsertAt(W("c"), W("fantasma"), DropZone.Left);
        Assert.Equal("H[a:1,b:0.5,c:0.5]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void InsertAt_ArvoreVazia_ViraRaiz()
    {
        var tree = new LayoutTree();
        tree.InsertAt(W("a"), W("fantasma"), DropZone.Top);
        Assert.Equal("a", Describe(tree));
        Invariants(tree, Hosts("a"));
    }

    // ---------- Move ----------

    [Theory]
    [InlineData(DropZone.Right, "H[b:1,c:0.5,a:0.5]")]
    [InlineData(DropZone.Left, "H[b:1,a:0.5,c:0.5]")]
    [InlineData(DropZone.Top, "H[b:1,V[a:1,c:1]:1]")]
    [InlineData(DropZone.Bottom, "H[b:1,V[c:1,a:1]:1]")]
    [InlineData(DropZone.Center, "H[c:1,b:1,a:1]")] // troca
    public void Move_EntrePaineis(DropZone zone, string expected)
    {
        var tree = Columns("a", "b", "c");
        tree.Move(W("a"), W("c"), zone);
        Assert.Equal(expected, Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void Move_Centro_TrocaHostsMantendoNosEPesos()
    {
        var tree = Columns("a", "b");
        ((SplitNode)tree.Root!).Weights[0] = 3;
        var leafA = tree.Find(W("a"))!;
        var leafB = tree.Find(W("b"))!;
        tree.Move(W("a"), W("b"), DropZone.Center);
        Assert.Equal("H[b:3,a:1]", Describe(tree));
        Assert.Same(W("b"), leafA.Host);
        Assert.Same(W("a"), leafB.Host);
        Invariants(tree, Hosts("a", "b"));
    }

    [Fact]
    public void Move_ColapsaRaiz_ECriaNovaDivisao()
    {
        var tree = Columns("a", "b");
        tree.Move(W("a"), W("b"), DropZone.Bottom);
        Assert.Equal("V[b:1,a:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b"));
    }

    [Fact]
    public void Move_OrigemDeixaDivisaoComUmFilho_Colapsa()
    {
        var tree = Columns("a", "b");
        tree.InsertAt(W("c"), W("b"), DropZone.Bottom); // H[a:1,V[b:1,c:1]:1]
        tree.Move(W("b"), W("a"), DropZone.Left);
        Assert.Equal("H[b:0.5,a:0.5,c:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Theory]
    [InlineData("a", "a", DropZone.Left)]
    [InlineData("a", "b", DropZone.None)]
    [InlineData("fantasma", "a", DropZone.Left)]
    [InlineData("a", "fantasma", DropZone.Center)]
    public void Move_Invalido_NaoAltera(string source, string target, DropZone zone)
    {
        var tree = Columns("a", "b", "c");
        var before = Describe(tree);
        tree.Move(W(source), W(target), zone);
        Assert.Equal(before, Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    // ---------- Remove ----------

    [Fact]
    public void Remove_DeDuas_SobraFolhaNaRaiz()
    {
        var tree = Columns("a", "b");
        tree.Remove(W("a"));
        var leaf = Assert.IsType<LeafNode>(tree.Root);
        Assert.Same(W("b"), leaf.Host);
        Assert.Null(leaf.Parent);
        Invariants(tree, Hosts("b"));
    }

    [Fact]
    public void Remove_Unica_EsvaziaArvore()
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        tree.Remove(W("a"));
        Assert.Null(tree.Root);
        Assert.Empty(tree.Leaves());
        Assert.Null(tree.ToDto());
    }

    [Fact]
    public void Remove_Inexistente_NaoAltera()
    {
        var tree = Columns("a", "b");
        tree.Remove(W("fantasma"));
        Assert.Equal("H[a:1,b:1]", Describe(tree));
        new LayoutTree().Remove(W("a")); // árvore vazia: não lança
    }

    [Fact]
    public void Remove_ComTresFilhos_NaoColapsa()
    {
        var tree = Columns("a", "b", "c");
        tree.Remove(W("b"));
        Assert.Equal("H[a:1,c:1]", Describe(tree));
        Invariants(tree, Hosts("a", "c"));
    }

    [Fact]
    public void Remove_ColapsaDivisaoDeUmFilho_SemFusaoQuandoFolha()
    {
        var tree = Columns("a", "b");
        tree.InsertAt(W("c"), W("b"), DropZone.Bottom); // H[a:1,V[b:1,c:1]:1]
        ((SplitNode)tree.Root!).Weights[1] = 2;
        tree.Remove(W("c"));
        Assert.Equal("H[a:1,b:2]", Describe(tree));
        Invariants(tree, Hosts("a", "b"));
    }

    [Fact]
    public void Remove_FundeDivisoesDoMesmoSentido_PreservandoPesosProporcionais()
    {
        // H[a:1, V[b:1, H[c:3, d:1]:1]:2]
        var tree = Columns("a", "b");
        tree.InsertAt(W("c"), W("b"), DropZone.Bottom);
        tree.InsertAt(W("d"), W("c"), DropZone.Right);
        var root = (SplitNode)tree.Root!;
        root.Weights[1] = 2;
        var inner = (SplitNode)((SplitNode)root.Children[1]).Children[1];
        inner.Weights[0] = 3;
        inner.Weights[1] = 1;
        Assert.Equal("H[a:1,V[b:1,H[c:3,d:1]:1]:2]", Describe(tree));
        var sumBefore = root.Weights.Sum();

        tree.Remove(W("b"));

        Assert.Equal("H[a:1,c:1.5,d:0.5]", Describe(tree));
        var after = (SplitNode)tree.Root!;
        Assert.Same(root, after);
        Assert.Equal(sumBefore, after.Weights.Sum(), 10);
        Assert.Equal(3.0, after.Weights[1] / after.Weights[2], 10); // proporção c:d preservada
        Invariants(tree, Hosts("a", "c", "d"));
    }

    [Fact]
    public void Remove_ColapsaNaRaiz_DivisaoInternaViraRaiz()
    {
        // V[b:1, H[c:1, d:1]:1] -> remove b -> H[c:1,d:1] como raiz
        var tree = new LayoutTree();
        tree.Add(W("b"), null, false);
        tree.InsertAt(W("c"), W("b"), DropZone.Bottom);
        tree.InsertAt(W("d"), W("c"), DropZone.Right);
        Assert.Equal("V[b:1,H[c:1,d:1]:1]", Describe(tree));
        tree.Remove(W("b"));
        Assert.Equal("H[c:1,d:1]", Describe(tree));
        Invariants(tree, Hosts("c", "d"));
    }

    // ---------- Replace ----------

    [Fact]
    public void Replace_TrocaHostMantendoLugarEPeso()
    {
        var tree = Columns("a", "b");
        ((SplitNode)tree.Root!).Weights[0] = 2.5;
        var leaf = tree.Find(W("a"));
        tree.Replace(W("a"), W("x"));
        Assert.Equal("H[x:2.5,b:1]", Describe(tree));
        Assert.Null(tree.Find(W("a")));
        Assert.Same(leaf, tree.Find(W("x")));
        Invariants(tree, Hosts("x", "b"));
    }

    [Fact]
    public void Replace_Inexistente_NaoAltera()
    {
        var tree = Columns("a", "b");
        tree.Replace(W("fantasma"), W("x"));
        Assert.Equal("H[a:1,b:1]", Describe(tree));
    }

    // ---------- Equalize ----------

    [Fact]
    public void Equalize_TodosOsPesosViram1_EstruturaMantida()
    {
        var tree = new LayoutTree();
        tree.Add(W("a"), null, true);
        tree.Add(W("b"), null, true);
        tree.Add(W("c"), null, true);
        tree.InsertAt(W("d"), W("c"), DropZone.Bottom);
        var inner = (SplitNode)((SplitNode)tree.Root!).Children[2];
        inner.Weights[0] = 7;
        Assert.Equal("H[a:1,b:0.5,V[c:7,d:1]:0.5]", Describe(tree));
        tree.Equalize();
        Assert.Equal("H[a:1,b:1,V[c:1,d:1]:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c", "d"));
    }

    [Fact]
    public void Equalize_VaziaOuFolha_NaoLanca()
    {
        var tree = new LayoutTree();
        tree.Equalize();
        Assert.Null(tree.Root);
        tree.Add(W("a"), null, true);
        tree.Equalize();
        Assert.Equal("a", Describe(tree));
    }

    // ---------- ApplyPreset ----------

    public static IEnumerable<object[]> PresetCases()
    {
        static string Seq(int from, int to) => string.Join(",", Enumerable.Range(from, to - from + 1).Select(i => $"{{{i}}}:1"));
        for (var n = 1; n <= 6; n++)
        {
            yield return new object[] { Preset.Columns, n, n == 1 ? "{0}" : $"H[{Seq(0, n - 1)}]" };
            yield return new object[] { Preset.Rows, n, n == 1 ? "{0}" : $"V[{Seq(0, n - 1)}]" };
            yield return new object[] { Preset.MainLeft, n, n switch { 1 => "{0}", 2 => "H[{0}:1.6,{1}:1]", _ => $"H[{{0}}:1.6,V[{Seq(1, n - 1)}]:1]" } };
            yield return new object[] { Preset.MainTop, n, n switch { 1 => "{0}", 2 => "V[{0}:1.6,{1}:1]", _ => $"V[{{0}}:1.6,H[{Seq(1, n - 1)}]:1]" } };
        }
        yield return new object[] { Preset.Grid, 1, "{0}" };
        yield return new object[] { Preset.Grid, 2, "H[{0}:1,{1}:1]" };
        yield return new object[] { Preset.Grid, 3, "V[H[{0}:1,{1}:1]:1,{2}:1]" };
        yield return new object[] { Preset.Grid, 4, "V[H[{0}:1,{1}:1]:1,H[{2}:1,{3}:1]:1]" };
        yield return new object[] { Preset.Grid, 5, "V[H[{0}:1,{1}:1,{2}:1]:1,H[{3}:1,{4}:1]:1]" };
        yield return new object[] { Preset.Grid, 6, "V[H[{0}:1,{1}:1,{2}:1]:1,H[{3}:1,{4}:1,{5}:1]:1]" };
    }

    [Theory]
    [MemberData(nameof(PresetCases))]
    public void ApplyPreset_FormatoEsperado_OrdemDasFolhasPreservada(Preset preset, int count, string template)
    {
        // Árvore inicial bagunçada para a ordem das folhas não ser trivial.
        var names = new[] { "a", "b", "c", "d", "e", "f" }.Take(count).ToArray();
        var zones = new[] { DropZone.Left, DropZone.Bottom, DropZone.Top, DropZone.Right, DropZone.Center };
        var tree = new LayoutTree();
        for (var i = 0; i < names.Length; i++)
        {
            if (i == 0) tree.Add(W(names[i]), null, true);
            else tree.InsertAt(W(names[i]), W(names[i / 2]), zones[i % zones.Length]);
        }
        var order = LeafNames(tree);

        tree.ApplyPreset(preset);

        Assert.Equal(string.Format(template, order.Cast<object>().ToArray()), Describe(tree));
        Assert.Equal(order, LeafNames(tree));
        Invariants(tree, Hosts(names));
    }

    [Theory]
    [InlineData(Preset.Columns)]
    [InlineData(Preset.Grid)]
    [InlineData(Preset.MainTop)]
    public void ApplyPreset_ArvoreVazia_ContinuaVazia(Preset preset)
    {
        var tree = new LayoutTree();
        tree.ApplyPreset(preset);
        Assert.Null(tree.Root);
    }

    // ---------- ToDto / Load ----------

    private static void AssertSameStructure(LayoutNode? expected, LayoutNode? actual)
    {
        switch (expected)
        {
            case null: Assert.Null(actual); break;
            case LeafNode e:
                var a = Assert.IsType<LeafNode>(actual);
                Assert.Same(e.Host, a.Host);
                break;
            case SplitNode es:
                var s = Assert.IsType<SplitNode>(actual);
                Assert.Equal(es.Horizontal, s.Horizontal);
                Assert.Equal(es.Weights, s.Weights); // igualdade exata dos doubles
                Assert.Equal(es.Children.Count, s.Children.Count);
                for (var i = 0; i < es.Children.Count; i++) AssertSameStructure(es.Children[i], s.Children[i]);
                break;
        }
    }

    private LayoutTree Complex()
    {
        var tree = Columns("a", "b", "c");
        tree.InsertAt(W("d"), W("b"), DropZone.Top);
        tree.InsertAt(W("e"), W("d"), DropZone.Right);
        tree.InsertAt(W("f"), W("c"), DropZone.Bottom);
        var root = (SplitNode)tree.Root!;
        root.Weights[0] = 1.0 / 3;
        root.Weights[2] = 0.1 + 0.2; // valor não representável exatamente
        return tree;
    }

    private Func<AppIdentity, IGroupedWindow?> ResolveBy(params string[] names)
    {
        var map = names.ToDictionary(n => W(n).Identity, n => (IGroupedWindow)W(n));
        return id => map.TryGetValue(id, out var h) ? h : null;
    }

    [Fact]
    public void ToDtoLoad_RoundTrip_EstruturaEPesosIdenticos()
    {
        var tree = Complex();
        Assert.Equal("H[a:0.3333,V[H[d:1,e:1]:1,b:1]:1,V[c:1,f:1]:0.3]", Describe(tree));
        var dto = tree.ToDto();

        var loaded = new LayoutTree();
        loaded.Load(dto, ResolveBy("a", "b", "c", "d", "e", "f"));

        AssertSameStructure(tree.Root, loaded.Root);
        Invariants(loaded, Hosts("a", "b", "c", "d", "e", "f"));
    }

    [Fact]
    public void ToDto_FolhaGuardaIdentidade_DivisaoGuardaSentidoEPesos()
    {
        var tree = Columns("a", "b");
        var dto = tree.ToDto()!;
        Assert.Null(dto.App);
        Assert.True(dto.Horizontal);
        Assert.Equal(new[] { 1.0, 1.0 }, dto.Weights);
        Assert.Equal(W("a").Identity, dto.Children[0].App);
        Assert.Equal(W("b").Identity, dto.Children[1].App);
        Assert.Empty(dto.Children[0].Children);
    }

    [Fact]
    public void ToDto_CopiaPesos_NaoCompartilhaLista()
    {
        var tree = Columns("a", "b");
        var dto = tree.ToDto()!;
        dto.Weights[0] = 99;
        Assert.Equal(1.0, ((SplitNode)tree.Root!).Weights[0]);
    }

    [Fact]
    public void Load_FolhasNaoResolvidas_DescartadasEDivisoesColapsam()
    {
        // H[a:1, V[b:2, c:3]:4, d:5] sem c -> H[a:1, b:4, d:5]
        var dto = Split(true, (Leaf("a"), 1), (Split(false, (Leaf("b"), 2), (Leaf("c"), 3)), 4), (Leaf("d"), 5));
        var tree = new LayoutTree();
        tree.Load(dto, ResolveBy("a", "b", "d"));
        Assert.Equal("H[a:1,b:4,d:5]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "d"));
    }

    [Fact]
    public void Load_ColapsoGeraMesmoSentido_Funde()
    {
        // H[a:1, V[H[b:1,c:1]:1, x:1]:1] sem x -> esperado H[a:1,b:0.5,c:0.5]; real: H[a:1,H[b:1,c:1]:1]
        var dto = Split(true, (Leaf("a"), 1), (Split(false, (Split(true, (Leaf("b"), 1), (Leaf("c"), 1)), 1), (Leaf("x"), 1)), 1));
        var tree = new LayoutTree();
        tree.Load(dto, ResolveBy("a", "b", "c"));
        Assert.Equal("H[a:1,b:0.5,c:0.5]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void Load_RaizColapsaParaFolha_SemParent()
    {
        var dto = Split(true, (Leaf("a"), 1), (Leaf("x"), 1));
        var tree = new LayoutTree();
        tree.Load(dto, ResolveBy("a"));
        var leaf = Assert.IsType<LeafNode>(tree.Root);
        Assert.Null(leaf.Parent);
        Invariants(tree, Hosts("a"));
    }

    [Fact]
    public void Load_NadaResolvido_ArvoreVazia()
    {
        var dto = Split(true, (Leaf("a"), 1), (Split(false, (Leaf("b"), 1), (Leaf("c"), 1)), 1));
        var tree = Columns("z", "y"); // conteúdo anterior deve ser substituído
        tree.Load(dto, _ => null);
        Assert.Null(tree.Root);
    }

    [Fact]
    public void Load_Null_EsvaziaArvore()
    {
        var tree = Columns("a", "b");
        tree.Load(null, ResolveBy("a", "b"));
        Assert.Null(tree.Root);
    }

    [Fact]
    public void Load_PesosFaltando_Usam1()
    {
        var dto = new NodeDto { Horizontal = false, Weights = new() { 2 }, Children = new() { Leaf("a"), Leaf("b"), Leaf("c") } };
        var tree = new LayoutTree();
        tree.Load(dto, ResolveBy("a", "b", "c"));
        Assert.Equal("V[a:2,b:1,c:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b", "c"));
    }

    [Fact]
    public void Load_DivisaoSemFilhos_Descartada()
    {
        var dto = Split(true, (Leaf("a"), 1), (new NodeDto { Horizontal = false }, 1), (Leaf("b"), 1));
        var tree = new LayoutTree();
        tree.Load(dto, ResolveBy("a", "b"));
        Assert.Equal("H[a:1,b:1]", Describe(tree));
        Invariants(tree, Hosts("a", "b"));
    }

    private NodeDto Leaf(string name) => new() { App = W(name).Identity };

    private static NodeDto Split(bool horizontal, params (NodeDto Child, double Weight)[] children) => new()
    {
        Horizontal = horizontal,
        Weights = children.Select(c => c.Weight).ToList(),
        Children = children.Select(c => c.Child).ToList(),
    };
}
