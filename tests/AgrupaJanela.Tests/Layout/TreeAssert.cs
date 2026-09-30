using System.Globalization;
using AgrupaJanela.Hosting;
using AgrupaJanela.Layout;
using AgrupaJanela.Tests.Fakes;

namespace AgrupaJanela.Tests.Layout;

internal static class TreeAssert
{
    /// <summary>Forma compacta: H[a:1,V[b:0.5,c:0.5]:1]. Folhas pelo nome do FakeWindow.</summary>
    public static string Describe(LayoutNode? node) => node switch
    {
        null => "()",
        LeafNode leaf => ((FakeWindow)leaf.Host).Name,
        SplitNode split => (split.Horizontal ? "H[" : "V[")
            + string.Join(",", split.Children.Select((c, i) => Describe(c) + ":" + Fmt(split.Weights[i])))
            + "]",
        _ => throw new InvalidOperationException("Nó desconhecido"),
    };

    public static string Describe(LayoutTree tree) => Describe(tree.Root);

    private static string Fmt(double w) => w.ToString("0.####", CultureInfo.InvariantCulture);

    public static List<string> LeafNames(LayoutTree tree) => tree.Leaves().Select(l => ((FakeWindow)l.Host).Name).ToList();

    /// <summary>Checa as invariantes estruturais da árvore e o conjunto de hosts esperado.</summary>
    public static void Invariants(LayoutTree tree, IReadOnlyCollection<IGroupedWindow> expectedHosts, bool noSameDirectionNesting = true)
    {
        if (tree.Root is not null) Assert.Null(tree.Root.Parent);
        Check(tree.Root, noSameDirectionNesting);

        var hosts = tree.Leaves().Select(l => l.Host).ToList();
        Assert.Equal(hosts.Count, hosts.Distinct(ReferenceEqualityComparer.Instance).Count()); // sem host duplicado
        Assert.Equal(expectedHosts.Count, hosts.Count);
        foreach (var expected in expectedHosts)
            Assert.Contains(hosts, h => ReferenceEquals(h, expected));

        foreach (var leaf in tree.Leaves())
            Assert.Same(leaf, tree.Find(leaf.Host));
    }

    private static void Check(LayoutNode? node, bool noSameDirectionNesting)
    {
        if (node is not SplitNode split) return;
        Assert.True(split.Children.Count >= 2, $"SplitNode com {split.Children.Count} filho(s): {Describe(split)}");
        Assert.Equal(split.Children.Count, split.Weights.Count);
        foreach (var w in split.Weights)
            Assert.True(double.IsFinite(w) && w > 0, $"Peso inválido {w} em {Describe(split)}");
        foreach (var child in split.Children)
        {
            Assert.Same(split, child.Parent);
            if (noSameDirectionNesting && child is SplitNode inner)
                Assert.True(inner.Horizontal != split.Horizontal, $"Divisão aninhada no mesmo sentido: {Describe(split)}");
            Check(child, noSameDirectionNesting);
        }
    }
}
