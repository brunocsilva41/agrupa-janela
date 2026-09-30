using System.Windows;
using AgrupaJanela.Layout;

namespace AgrupaJanela.Tests.Layout;

/// <summary>DropOverlay.ZoneFor é estático e puro: não cria janela.</summary>
public class DropOverlayZoneTests
{
    // Painel em x=[100,500], y=[200,500].
    private static readonly Int32Rect Pane = new(100, 200, 400, 300);

    [Theory]
    [InlineData(300, 350, DropZone.Center)]
    [InlineData(221, 350, DropZone.Center)]   // fx = 0.3025 (logo depois do limite 0.3)
    [InlineData(379, 409, DropZone.Center)]   // fx = 0.6975, fy = 0.6967 (logo antes de 0.7)
    public void Centro(int x, int y, DropZone expected) =>
        Assert.Equal(expected, DropOverlay.ZoneFor(Pane, x, y));

    [Theory]
    [InlineData(110, 350, DropZone.Left)]
    [InlineData(490, 350, DropZone.Right)]
    [InlineData(300, 210, DropZone.Top)]
    [InlineData(300, 490, DropZone.Bottom)]
    [InlineData(100, 350, DropZone.Left)]    // exatamente na borda esquerda (inclusiva)
    [InlineData(500, 350, DropZone.Right)]   // exatamente na borda direita (inclusiva)
    [InlineData(300, 200, DropZone.Top)]
    [InlineData(300, 500, DropZone.Bottom)]
    [InlineData(220, 350, DropZone.Left)]    // fx = 0.3 exato: limite estrito, não é centro
    [InlineData(380, 350, DropZone.Right)]   // fx = 0.7 exato
    [InlineData(300, 290, DropZone.Top)]     // fy = 0.3 exato
    [InlineData(300, 410, DropZone.Bottom)]  // fy = 0.7 exato
    public void Bordas(int x, int y, DropZone expected) =>
        Assert.Equal(expected, DropOverlay.ZoneFor(Pane, x, y));

    [Theory]
    [InlineData(105, 202, DropZone.Top)]     // fx=0.0125, fy=0.0067: topo mais perto
    [InlineData(102, 205, DropZone.Left)]    // fx=0.005, fy=0.0167: esquerda mais perto
    [InlineData(495, 498, DropZone.Bottom)]  // 1-fx=0.0125, 1-fy=0.0067
    [InlineData(499, 490, DropZone.Right)]   // 1-fx=0.0025, 1-fy=0.033
    [InlineData(100, 200, DropZone.Left)]    // empate exato: primeira da lista (Left) vence
    [InlineData(500, 200, DropZone.Right)]   // empate Right/Top: Right vem antes
    [InlineData(100, 500, DropZone.Left)]
    [InlineData(500, 500, DropZone.Right)]
    public void Cantos(int x, int y, DropZone expected) =>
        Assert.Equal(expected, DropOverlay.ZoneFor(Pane, x, y));

    [Theory]
    [InlineData(99, 350)]
    [InlineData(501, 350)]
    [InlineData(300, 199)]
    [InlineData(300, 501)]
    [InlineData(-1000, -1000)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, 350)]
    public void ForaDoRetangulo_None(int x, int y) =>
        Assert.Equal(DropZone.None, DropOverlay.ZoneFor(Pane, x, y));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(100, -5)]
    public void RetanguloVazio_None(int width, int height) =>
        Assert.Equal(DropZone.None, DropOverlay.ZoneFor(new Int32Rect(0, 0, width, height), 0, 0));

    [Fact]
    public void Int32RectEmpty_None() =>
        Assert.Equal(DropZone.None, DropOverlay.ZoneFor(Int32Rect.Empty, 0, 0));

    [Fact]
    public void Painel1x1_NaoDivideErrado()
    {
        var pane = new Int32Rect(10, 10, 1, 1);
        Assert.Equal(DropZone.Left, DropOverlay.ZoneFor(pane, 10, 10));
        Assert.Equal(DropZone.Right, DropOverlay.ZoneFor(pane, 11, 11));
        Assert.Equal(DropZone.None, DropOverlay.ZoneFor(pane, 12, 10));
    }
}
