using AgrupaJanela.Hosting;

namespace AgrupaJanela.Layout;

public enum DropZone { None, Left, Right, Top, Bottom, Center }
public enum Preset { Columns, Rows, Grid, MainLeft, MainTop }

public abstract class LayoutNode
{
    public SplitNode? Parent { get; internal set; }
}

public sealed class LeafNode : LayoutNode
{
    public LeafNode(IGroupedWindow host) => Host = host;
    public IGroupedWindow Host { get; internal set; }
}

/// <summary>Divisão com filhos lado a lado (Horizontal) ou empilhados (!Horizontal), cada um com seu peso.</summary>
public sealed class SplitNode : LayoutNode
{
    public SplitNode(bool horizontal) => Horizontal = horizontal;
    public bool Horizontal { get; }
    public List<LayoutNode> Children { get; } = new();
    public List<double> Weights { get; } = new();

    internal void Insert(int index, LayoutNode child, double weight)
    {
        child.Parent = this;
        Children.Insert(index, child);
        Weights.Insert(index, weight);
    }
}

/// <summary>Árvore de divisões de um grupo, no estilo de gerenciadores de janelas lado a lado.</summary>
public sealed class LayoutTree
{
    public LayoutNode? Root { get; private set; }

    public IEnumerable<LeafNode> Leaves() => Walk(Root);

    public LeafNode? Find(IGroupedWindow host) => Leaves().FirstOrDefault(l => l.Host == host);

    private static IEnumerable<LeafNode> Walk(LayoutNode? node)
    {
        switch (node)
        {
            case LeafNode leaf: yield return leaf; break;
            case SplitNode split:
                foreach (var child in split.Children)
                foreach (var leaf in Walk(child)) yield return leaf;
                break;
        }
    }

    /// <summary>Adiciona dividindo o painel de referência no sentido mais comprido.</summary>
    public void Add(IGroupedWindow host, LeafNode? beside, bool wide)
    {
        var leaf = new LeafNode(host);
        if (Root is null) { Root = leaf; return; }
        var target = beside ?? Leaves().Last();
        InsertBeside(target, leaf, wide ? DropZone.Right : DropZone.Bottom);
    }

    /// <summary>Troca o conteúdo de um painel mantendo o lugar e o tamanho (ex.: mudou de modo).</summary>
    public void Replace(IGroupedWindow oldHost, IGroupedWindow newHost)
    {
        if (Find(oldHost) is { } leaf) leaf.Host = newHost;
    }

    public void Remove(IGroupedWindow host)
    {
        if (Find(host) is { } leaf) Detach(leaf);
    }

    /// <summary>Move um painel para a borda (ou troca de lugar, no centro) de outro painel.</summary>
    public void Move(IGroupedWindow source, IGroupedWindow target, DropZone zone)
    {
        var from = Find(source);
        var to = Find(target);
        if (from is null || to is null || from == to || zone == DropZone.None) return;
        if (zone == DropZone.Center)
        {
            (from.Host, to.Host) = (to.Host, from.Host);
            return;
        }
        Detach(from);
        InsertBeside(to, from, zone);
    }

    /// <summary>Insere um host novo numa borda de um painel existente.</summary>
    public void InsertAt(IGroupedWindow host, IGroupedWindow target, DropZone zone)
    {
        var to = Find(target);
        var leaf = new LeafNode(host);
        if (to is null || Root is null) { Add(host, null, true); return; }
        if (zone == DropZone.Center)
        {
            // Centro com janela nova: fica no lugar do alvo, que vai para a direita.
            InsertBeside(to, leaf, DropZone.Left);
            return;
        }
        InsertBeside(to, leaf, zone);
    }

    public void ApplyPreset(Preset preset)
    {
        var leaves = Leaves().Select(l => l.Host).ToList();
        Root = null;
        if (leaves.Count == 0) return;
        if (leaves.Count == 1) { Root = new LeafNode(leaves[0]); return; }

        switch (preset)
        {
            case Preset.Columns: Root = Line(true, leaves); break;
            case Preset.Rows: Root = Line(false, leaves); break;
            case Preset.MainLeft:
            case Preset.MainTop:
            {
                var horizontal = preset == Preset.MainLeft;
                var root = new SplitNode(horizontal);
                root.Insert(0, new LeafNode(leaves[0]), 1.6);
                root.Insert(1, leaves.Count == 2 ? new LeafNode(leaves[1]) : Line(!horizontal, leaves.Skip(1)), 1);
                Root = root;
                break;
            }
            default:
            {
                var columns = (int)Math.Ceiling(Math.Sqrt(leaves.Count));
                var rows = leaves.Chunk(columns).Select(chunk => chunk.Length == 1 ? (LayoutNode)new LeafNode(chunk[0]) : Line(true, chunk)).ToList();
                if (rows.Count == 1) { Root = rows[0]; break; }
                var root = new SplitNode(false);
                foreach (var row in rows) root.Insert(root.Children.Count, row, 1);
                Root = root;
                break;
            }
        }
    }

    public void Equalize()
    {
        foreach (var split in Splits(Root))
            for (var i = 0; i < split.Weights.Count; i++) split.Weights[i] = 1;
    }

    private static IEnumerable<SplitNode> Splits(LayoutNode? node)
    {
        if (node is not SplitNode split) yield break;
        yield return split;
        foreach (var child in split.Children)
        foreach (var inner in Splits(child)) yield return inner;
    }

    private static SplitNode Line(bool horizontal, IEnumerable<IGroupedWindow> hosts)
    {
        var split = new SplitNode(horizontal);
        foreach (var host in hosts) split.Insert(split.Children.Count, new LeafNode(host), 1);
        return split;
    }

    private void InsertBeside(LeafNode target, LeafNode leaf, DropZone zone)
    {
        var horizontal = zone is DropZone.Left or DropZone.Right;
        var before = zone is DropZone.Left or DropZone.Top;
        var parent = target.Parent;

        if (parent is not null && parent.Horizontal == horizontal)
        {
            // Mesmo sentido: divide o espaço do alvo ao meio dentro da divisão existente.
            var index = parent.Children.IndexOf(target);
            var half = parent.Weights[index] / 2;
            parent.Weights[index] = half;
            parent.Insert(before ? index : index + 1, leaf, half);
            return;
        }

        var split = new SplitNode(horizontal);
        if (parent is null) Root = split;
        else
        {
            var index = parent.Children.IndexOf(target);
            parent.Children[index] = split;
            split.Parent = parent;
        }
        split.Insert(0, before ? leaf : target, 1);
        split.Insert(1, before ? target : leaf, 1);
    }

    private void Detach(LeafNode leaf)
    {
        var parent = leaf.Parent;
        leaf.Parent = null;
        if (parent is null) { if (Root == leaf) Root = null; return; }

        var index = parent.Children.IndexOf(leaf);
        parent.Children.RemoveAt(index);
        parent.Weights.RemoveAt(index);
        if (parent.Children.Count != 1) return;

        // Divisão com um filho só: o filho ocupa o lugar dela.
        var only = parent.Children[0];
        var grand = parent.Parent;
        if (grand is null) { Root = only; only.Parent = null; return; }
        var slot = grand.Children.IndexOf(parent);
        grand.Children[slot] = only;
        only.Parent = grand;

        // Se o filho é uma divisão no mesmo sentido do avô, funde para não aninhar à toa.
        if (only is SplitNode inner && inner.Horizontal == grand.Horizontal)
        {
            var weight = grand.Weights[slot];
            var total = inner.Weights.Sum();
            grand.Children.RemoveAt(slot);
            grand.Weights.RemoveAt(slot);
            for (var i = 0; i < inner.Children.Count; i++)
                grand.Insert(slot + i, inner.Children[i], weight * inner.Weights[i] / total);
        }
    }

    // ---------- Serialização ----------

    public NodeDto? ToDto() => ToDto(Root);

    private static NodeDto? ToDto(LayoutNode? node) => node switch
    {
        LeafNode leaf => new NodeDto { App = leaf.Host.Identity },
        SplitNode split => new NodeDto
        {
            Horizontal = split.Horizontal,
            Weights = split.Weights.ToList(),
            Children = split.Children.Select(ToDto).OfType<NodeDto>().ToList(),
        },
        _ => null,
    };

    /// <summary>Reconstrói a árvore; folhas sem host resolvido são descartadas.</summary>
    public void Load(NodeDto? dto, Func<AppIdentity, IGroupedWindow?> resolve)
    {
        Root = Build(dto, resolve);
        if (Root is not null) Root.Parent = null;
    }

    private static LayoutNode? Build(NodeDto? dto, Func<AppIdentity, IGroupedWindow?> resolve)
    {
        if (dto is null) return null;
        if (dto.App is not null) return resolve(dto.App) is { } host ? new LeafNode(host) : null;

        var split = new SplitNode(dto.Horizontal);
        for (var i = 0; i < dto.Children.Count; i++)
        {
            if (Build(dto.Children[i], resolve) is not { } child) continue;
            var weight = i < dto.Weights.Count ? dto.Weights[i] : 1;
            if (child is SplitNode inner && inner.Horizontal == split.Horizontal)
            {
                // Um app faltando pode deixar divisões no mesmo sentido aninhadas: funde, como o Detach faz.
                var total = inner.Weights.Sum();
                for (var k = 0; k < inner.Children.Count; k++)
                    split.Insert(split.Children.Count, inner.Children[k], weight * inner.Weights[k] / total);
                continue;
            }
            split.Insert(split.Children.Count, child, weight);
        }
        return split.Children.Count switch
        {
            0 => null,
            1 => Orphan(split.Children[0]),
            _ => split,
        };
    }

    private static LayoutNode Orphan(LayoutNode node)
    {
        node.Parent = null;
        return node;
    }
}

public sealed class NodeDto
{
    public AppIdentity? App { get; set; }
    public bool Horizontal { get; set; }
    public List<double> Weights { get; set; } = new();
    public List<NodeDto> Children { get; set; } = new();
}
