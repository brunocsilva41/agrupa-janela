using System.Windows.Forms;

namespace AgrupaJanela.Shell;

/// <summary>Ícone na bandeja (perto do relógio): o app continua rodando com a janela principal fechada.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _hintShown;

    public TrayIcon(Action showMain, Action newGroup, Func<IEnumerable<(string Name, Action Open)>> savedGroups, Action checkUpdates, Action exit)
    {
        var menu = new ContextMenuStrip();
        var saved = new ToolStripMenuItem("Abrir grupo salvo");
        menu.Items.Add("Abrir SplitDeck", null, (_, _) => showMain());
        menu.Items.Add("Novo grupo", null, (_, _) => newGroup());
        menu.Items.Add(saved);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Verificar atualizações", null, (_, _) => checkUpdates());
        menu.Items.Add("Sair", null, (_, _) => exit());
        menu.Opening += (_, _) =>
        {
            saved.DropDownItems.Clear();
            foreach (var (name, open) in savedGroups()) saved.DropDownItems.Add(name, null, (_, _) => open());
            saved.Enabled = saved.DropDownItems.Count > 0;
        };

        _icon = new NotifyIcon { Icon = AppIcon.Icon, Text = "SplitDeck", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) showMain(); };
    }

    /// <summary>Avisa uma vez que o app continua na bandeja.</summary>
    public void ShowStillRunningHint()
    {
        if (_hintShown) return;
        _hintShown = true;
        _icon.ShowBalloonTip(3000, "SplitDeck continua aberto", "Os atalhos continuam funcionando. Clique no ícone para abrir; botão direito → Sair para encerrar.", ToolTipIcon.Info);
    }

    public void ShowMessage(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
