using System.Windows;

namespace AgrupaJanela.Hosting;

/// <summary>
/// Uma janela de outro app dentro de um grupo, qualquer que seja o modo:
/// Incorporada (<see cref="EmbeddedWindowHost"/>, vira filha do painel) ou
/// Acoplada (<see cref="DockedWindowHost"/>, continua top-level posicionada sobre o painel).
/// </summary>
public interface IGroupedWindow : IDisposable
{
    nint Target { get; }
    uint ProcessId { get; }
    string CurrentTitle { get; }
    bool IsAttached { get; }
    AppIdentity Identity { get; }
    EmbedMode Mode { get; }

    /// <summary>O que vai dentro do painel do grupo.</summary>
    FrameworkElement View { get; }

    /// <summary>O usuário clicou/ativou a janela (para marcar o painel ativo).</summary>
    event Action<IGroupedWindow>? TargetClicked;

    bool RefreshTitle();
    void FocusTarget();

    /// <summary>Devolve a janela ao estado original. keepPosition: fica onde está (ex.: foi arrastada para fora).</summary>
    void Release(bool keepPosition = false);
}
