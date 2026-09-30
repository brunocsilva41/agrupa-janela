using AgrupaJanela.Hosting;
using AgrupaJanela.Layout;
using AgrupaJanela.Tests.Fakes;
using static AgrupaJanela.Tests.Layout.TreeAssert;

namespace AgrupaJanela.Tests.Layout;

public class LayoutTreePropertyTests
{
    private static readonly DropZone[] AllZones = Enum.GetValues<DropZone>();
    private static readonly Preset[] AllPresets = Enum.GetValues<Preset>();

    [Theory]
    [InlineData(12345)]
    [InlineData(20260930)]
    [InlineData(7)]
    public void OperacoesAleatorias_MantemInvariantes(int seed)
    {
        const int operations = 500;
        var rng = new Random(seed);
        var tree = new LayoutTree();
        var present = new List<IGroupedWindow>(); // modelo: hosts adicionados e não removidos
        var byIdentity = new Dictionary<AppIdentity, IGroupedWindow>();
        var ghost = new FakeWindow("fantasma");
        var next = 0;

        FakeWindow NewHost()
        {
            var w = new FakeWindow("w" + next++);
            byIdentity[w.Identity] = w;
            return w;
        }

        IGroupedWindow PickPresentOrGhost() =>
            present.Count == 0 || rng.Next(10) == 0 ? ghost : present[rng.Next(present.Count)];

        for (var step = 0; step < operations; step++)
        {
            var op = rng.Next(present.Count < 3 ? 3 : 10);
            var label = "";
            switch (op)
            {
                case 0: // Add
                {
                    var host = NewHost();
                    var besideHost = present.Count > 0 && rng.Next(2) == 0 ? present[rng.Next(present.Count)] : null;
                    var wide = rng.Next(2) == 0;
                    tree.Add(host, besideHost is null ? null : tree.Find(besideHost), wide);
                    present.Add(host);
                    label = $"Add({host}, beside={besideHost}, wide={wide})";
                    break;
                }
                case 1: case 2: // InsertAt (inclui alvo inexistente e zona None)
                {
                    var host = NewHost();
                    var target = PickPresentOrGhost();
                    var zone = AllZones[rng.Next(AllZones.Length)];
                    tree.InsertAt(host, target, zone);
                    present.Add(host);
                    label = $"InsertAt({host}, {target}, {zone})";
                    break;
                }
                case 3: case 4: // Remove (às vezes inexistente)
                {
                    var host = PickPresentOrGhost();
                    tree.Remove(host);
                    present.Remove(host);
                    label = $"Remove({host})";
                    break;
                }
                case 5: case 6: // Move
                {
                    var source = PickPresentOrGhost();
                    var target = PickPresentOrGhost();
                    var zone = AllZones[rng.Next(AllZones.Length)];
                    tree.Move(source, target, zone);
                    label = $"Move({source}, {target}, {zone})";
                    break;
                }
                case 7: // Replace
                {
                    var old = PickPresentOrGhost();
                    var host = NewHost();
                    tree.Replace(old, host);
                    if (present.Remove(old)) present.Add(host);
                    label = $"Replace({old}, {host})";
                    break;
                }
                case 8: // ApplyPreset / Equalize
                {
                    if (rng.Next(3) == 0) { tree.Equalize(); label = "Equalize"; }
                    else
                    {
                        var order = tree.Leaves().Select(l => l.Host).ToList();
                        var preset = AllPresets[rng.Next(AllPresets.Length)];
                        tree.ApplyPreset(preset);
                        Assert.Equal(order, tree.Leaves().Select(l => l.Host).ToList());
                        label = $"ApplyPreset({preset})";
                    }
                    break;
                }
                default: // ToDto/Load round-trip
                {
                    var before = Describe(tree);
                    tree.Load(tree.ToDto(), id => byIdentity.TryGetValue(id, out var h) ? h : null);
                    Assert.Equal(before, Describe(tree));
                    label = "RoundTrip";
                    break;
                }
            }

            try
            {
                Invariants(tree, present);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"Invariante violada no passo {step} ({label}), semente {seed}: {ex.Message}\nÁrvore: {Describe(tree)}");
            }
        }
    }
}
