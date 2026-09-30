using System.Windows;
using AgrupaJanela.Hosting;

namespace AgrupaJanela.Tests.Fakes;

/// <summary>
/// Janela falsa para testar o LayoutTree sem HWND real. O LayoutTree não usa View;
/// se passar a usar, o teste falha explicitamente em vez de exigir STA/WPF.
/// </summary>
internal sealed class FakeWindow : IGroupedWindow
{
    public FakeWindow(string name)
    {
        Name = name;
        Identity = new AppIdentity($@"C:\apps\{name}.exe", $"--id {name}", $"Janela {name}", 0, 0);
    }

    public string Name { get; }
    public nint Target => 0;
    public uint ProcessId => 0;
    public string CurrentTitle => Identity.Title;
    public bool IsAttached => true;
    public AppIdentity Identity { get; }
    public EmbedMode Mode => EmbedMode.Reparent;
    public FrameworkElement View => throw new NotSupportedException("FakeWindow.View não deve ser usado pelo LayoutTree.");

    public event Action<IGroupedWindow>? TargetClicked { add { } remove { } }

    public bool RefreshTitle() => false;
    public void FocusTarget() { }
    public void Release(bool keepPosition = false) { }
    public void Dispose() { }

    public override string ToString() => Name;
}
