using AgrupaJanela.Hosting;
using AgrupaJanela.Persistence;
using AgrupaJanela.Shell;
using System.Windows;

namespace AgrupaJanela;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _showSignal, _quitSignal;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Instância única (por usuário e sessão): abrir de novo só traz a janela principal para frente.
        bool first;
        try { _instance = new Mutex(true, AppPaths.MutexName, out first); }
        catch (UnauthorizedAccessException) { first = false; } // nome ocupado por outro processo: não abre duas vezes
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(AppPaths.ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.ShowEventName);
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.QuitEventName);
        StartSignalListener(_showSignal, () => _controller?.ShowMain());
        // O instalador/atualizador pede para sair: devolve tudo sem perguntar e encerra.
        StartSignalListener(_quitSignal, () =>
        {
            _controller?.ExitSilently();
            Shutdown();
        });

        // Em qualquer falha, devolve as janelas à área de trabalho antes de tudo:
        // se o nosso processo morrer com elas incorporadas, o Windows as destrói junto.
        DispatcherUnhandledException += (_, args) =>
        {
            HostRegistry.ReleaseAll();
            ChoiceDialog.Ask(null, "SplitDeck",
                $"Erro inesperado: {args.Exception.Message}\n\nPor segurança, as janelas agrupadas foram devolvidas à área de trabalho.",
                ("OK", "", true));
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => HostRegistry.ReleaseAll();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => HostRegistry.ReleaseAll();
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved(); // nunca derrubar o app por Task esquecida

        base.OnStartup(e);
        SessionEnding += (_, _) => _controller?.ExitSilently();
        _controller = new AppController();
        _controller.Start(startInTray: e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase));
    }

    private void StartSignalListener(EventWaitHandle signal, Action onSignal) =>
        new Thread(() =>
        {
            try { while (signal.WaitOne()) Dispatcher.BeginInvoke(onSignal); }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "SplitDeck.Signal" }.Start();

    protected override void OnExit(ExitEventArgs e)
    {
        HostRegistry.ReleaseAll();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
