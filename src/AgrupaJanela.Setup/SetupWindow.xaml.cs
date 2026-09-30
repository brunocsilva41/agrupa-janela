using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AgrupaJanela.Setup;

internal enum SetupMode { Install, Update, Uninstall }

public partial class SetupWindow : Window
{
    private readonly SetupContext _ctx;
    private readonly CommandLine _cmd;
    private readonly SetupMode _mode;
    private readonly ExistingInstall? _existing;
    private readonly FrameworkElement[] _flow;
    private readonly List<(TextBlock Mark, TextBlock Text)> _steps = new();
    private int _index;
    private bool _busy, _done, _failed;
    private string _installDir = "";
    private string? _uninstallDir;
    private int _currentStep;

    public int ExitCode { get; private set; } = SetupFlow.ExitCancelled;

    internal SetupWindow(SetupContext ctx, CommandLine cmd, SetupMode mode, ExistingInstall? existing)
    {
        _ctx = ctx;
        _cmd = cmd;
        _mode = mode;
        _existing = existing;
        InitializeComponent();

        Logo.Source = SetupLogo.Create();
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        Closing += OnClosing;
        TestBadge.Visibility = ctx.TestMode ? Visibility.Visible : Visibility.Collapsed;
        VersionRun.Text = ctx.Version;
        LogPathText.Text = "Log: " + Log.FilePath;
        DataDirRun.Text = ctx.DataDir;
        RuntimeUrlBox.Text = DesktopRuntime.DownloadUrl;

        switch (mode)
        {
            case SetupMode.Install:
                Title = "Instalar o Agrupa-Janela";
                _flow = new FrameworkElement[] { PageWelcome, PageWhat, PageOptions, PageReqs, PageProgress, PageDone };
                PrepareInstall();
                break;
            case SetupMode.Update:
                Title = "Atualizar o Agrupa-Janela";
                _flow = new FrameworkElement[] { PageProgress, PageDone };
                Loaded += async (_, _) => await RunUpdateAsync();
                break;
            default:
                Title = "Desinstalar o Agrupa-Janela";
                _uninstallDir = SetupFlow.ResolveUninstallDir(ctx, cmd, existing);
                if (_uninstallDir == null)
                {
                    _flow = new FrameworkElement[] { PageMessage };
                    MessageHeading.Text = "O Agrupa-Janela não está instalado";
                    MessageText.Text = "Não há nada para remover.";
                    ExitCode = SetupFlow.ExitOk;
                    _done = true;
                }
                else
                {
                    _flow = new FrameworkElement[] { PageUninstall, PageProgress, PageDone };
                    UninstallDirRun.Text = _uninstallDir;
                    RemoveDataHint.Text = $"Se ficar desmarcado, eles continuam em {ctx.DataDir} para uma futura instalação.";
                }
                break;
        }
        ShowPage(0);
    }

    private void PrepareInstall()
    {
        if (_existing != null)
        {
            InstalledText.Visibility = Visibility.Visible;
            InstalledText.Text = $"Versão {_existing.Version ?? "?"} já instalada. Ela será substituída; seus grupos e preferências são mantidos.";
        }
        if (!Payload.IsPresent) NoPayloadBanner.Visibility = Visibility.Visible;

        DirBox.Text = _cmd.InstallDir ?? _existing?.Location ?? _ctx.DefaultInstallDir;
        if (_existing != null && _cmd.InstallDir == null)
        {
            DirBox.IsReadOnly = true;
            DefaultDirButton.Visibility = Visibility.Collapsed;
            DirNote.Visibility = Visibility.Visible;
            DirNote.Text = "Já instalado nesta pasta. Para mudar de pasta, desinstale antes.";
        }
        DesktopBox.IsChecked = _existing?.DesktopShortcut ?? false;
        StartupBox.IsChecked = _existing?.StartWithWindows ?? false;
        if (_ctx.TestMode) LaunchHint.Text = "Modo de teste: o app instalado NÃO é aberto.";
        ValidateDir();
    }

    // ---------------- Navegação ----------------

    private void ShowPage(int index)
    {
        _index = index;
        var page = _flow[index];
        foreach (UIElement child in Pages.Children) child.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        PageTitle.Text = TitleFor(page);

        StepDots.Children.Clear();
        if (_mode == SetupMode.Install)
        {
            for (var k = 0; k < _flow.Length; k++)
            {
                StepDots.Children.Add(new Border
                {
                    Width = 18, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(4, 0, 0, 0),
                    Background = (Brush)FindResource(k <= index ? "Accent" : "Line"),
                    Opacity = k < index ? 0.5 : 1,
                });
            }
        }

        var isProgress = page == PageProgress;
        var isEnd = page == PageDone || page == PageMessage;
        BackButton.Visibility = _mode == SetupMode.Install && index > 0 && !isProgress && !isEnd ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = !_busy;
        CancelButton.Visibility = isEnd ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.IsEnabled = !_busy && !isProgress;
        NextButton.Content = page == PageReqs ? "Instalar"
            : page == PageUninstall ? "Desinstalar"
            : page == PageDone ? "Concluir"
            : page == PageMessage ? "Fechar"
            : isProgress ? "Aguarde…"
            : "Avançar";
        NextButton.IsEnabled = !isProgress;

        if (page == PageWhat) ValidateDir();
        if (page == PageReqs) CheckRequirements();
    }

    private string TitleFor(FrameworkElement page)
    {
        if (page == PageWelcome) return "Boas-vindas";
        if (page == PageWhat) return "O que vai acontecer";
        if (page == PageOptions) return "Opções";
        if (page == PageReqs) return "Verificação de requisitos";
        if (page == PageProgress) return _mode == SetupMode.Uninstall ? "Removendo…" : _mode == SetupMode.Update ? "Atualizando…" : "Instalando…";
        if (page == PageDone) return "Concluído";
        if (page == PageUninstall) return "Desinstalar";
        return "";
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        var page = _flow[_index];
        if (page == PageReqs) await StartInstallAsync();
        else if (page == PageUninstall) await StartUninstallAsync();
        else if (page == PageDone) Finish();
        else if (page == PageMessage || (page == PageProgress && _failed)) Close();
        else ShowPage(_index + 1);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) ShowPage(_index - 1);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }
        if (_mode == SetupMode.Install && !_done && !_failed && _index > 0)
        {
            var choice = SetupDialog.Ask(this, "Cancelar a instalação? Nada foi alterado no seu computador.",
                ("Cancelar a instalação", "", false),
                ("Continuar", "", true));
            if (choice != 0) e.Cancel = true;
        }
    }

    private void Finish()
    {
        if (_mode == SetupMode.Install && LaunchBox.IsChecked == true) SetupFlow.LaunchApp(_ctx, _installDir);
        Close();
    }

    // ---------------- Página 2: pasta ----------------

    private void DirBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateDir();

    private void DefaultDir_Click(object sender, RoutedEventArgs e) => DirBox.Text = _ctx.DefaultInstallDir;

    private void ValidateDir()
    {
        if (_flow == null || _mode != SetupMode.Install) return;
        var error = PathRules.ValidateLocation(DirBox.Text, out var full) ?? PathRules.ValidateTarget(full);
        DirError.Text = error ?? "";
        DirError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        if (error == null) _installDir = full;
        if (_flow[_index] == PageWhat) NextButton.IsEnabled = error == null;
    }

    // ---------------- Página 4: requisitos ----------------

    private void CheckRequirements()
    {
        var is64 = Environment.Is64BitOperatingSystem;
        SetMark(OsMark, OsMarkText, is64);
        OsText.Text = is64
            ? $"Windows 64 bits (versão {Environment.OSVersion.Version})."
            : "Windows 32 bits: o Agrupa-Janela só funciona no Windows 64 bits.";

        var runtime = SetupFlow.FindRuntime(_ctx);
        SetMark(RuntimeMark, RuntimeMarkText, runtime != null);
        RuntimeText.Text = runtime != null
            ? $"Microsoft .NET Desktop Runtime {runtime} (x64) encontrado."
            : "Microsoft .NET Desktop Runtime 8 (x64) não encontrado.";
        RuntimeMissingPanel.Visibility = runtime == null && is64 ? Visibility.Visible : Visibility.Collapsed;

        var payload = Payload.IsPresent;
        var ready = is64 && runtime != null && payload;
        ReqsReady.Visibility = runtime != null || !payload ? Visibility.Visible : Visibility.Collapsed;
        ReqsReady.Text = payload ? "Tudo pronto. Clique em Instalar para começar." : "Este instalador não contém o pacote do app (build de desenvolvimento), então não pode instalar.";
        ReqsReady.Foreground = (Brush)FindResource(payload ? "TextDim" : "Danger");
        if (_flow[_index] == PageReqs) NextButton.IsEnabled = ready && !_busy;
    }

    private void SetMark(Border mark, TextBlock text, bool ok)
    {
        mark.Background = (Brush)FindResource(ok ? "Accent" : "Warn");
        text.Text = ok ? "✓" : "!";
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => CheckRequirements();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        DownloadButton.IsEnabled = RecheckButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        DownloadStatus.Text = "Baixando…";
        try
        {
            var progress = new Progress<(long Received, long? Total)>(p =>
            {
                if (p.Total is long total && total > 0)
                {
                    DownloadProgress.IsIndeterminate = false;
                    DownloadProgress.Value = 100.0 * p.Received / total;
                    DownloadStatus.Text = $"Baixando… {p.Received / 1048576.0:0.0} de {total / 1048576.0:0.0} MB";
                }
                else
                {
                    DownloadProgress.IsIndeterminate = true;
                    DownloadStatus.Text = $"Baixando… {p.Received / 1048576.0:0.0} MB";
                }
            });
            var file = await DesktopRuntime.DownloadAsync(progress, CancellationToken.None);
            DownloadProgress.IsIndeterminate = true;
            DownloadStatus.Text = "Conferindo a assinatura digital e instalando… Se o Windows pedir permissão (UAC), confirme.";
            var code = await DesktopRuntime.VerifyAndRunAsync(file);
            DownloadStatus.Text = code switch
            {
                0 => "Runtime instalado.",
                3010 => "Runtime instalado. O Windows pode pedir para reiniciar mais tarde.",
                1602 => "A instalação do runtime foi cancelada (permissão negada ou cancelada).",
                _ => $"O instalador do runtime terminou com o código {code}. Detalhes no log.",
            };
        }
        catch (Exception ex)
        {
            Log.Error("Download/instalação do runtime falhou", ex);
            DownloadStatus.Text = ex.Message;
        }
        finally
        {
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Visibility = Visibility.Collapsed;
            DownloadButton.IsEnabled = RecheckButton.IsEnabled = true;
            SetBusy(false);
            CheckRequirements();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BackButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        if (busy) NextButton.IsEnabled = false;
    }

    // ---------------- Instalação / atualização ----------------

    private async Task StartInstallAsync()
    {
        var options = new InstallOptions
        {
            InstallDir = _installDir,
            DesktopShortcut = DesktopBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true,
        };
        await RunInstallAsync(options);
    }

    private async Task RunUpdateAsync()
    {
        SetBusy(true);
        BuildSteps(Installer.Steps);
        if (_cmd.WaitPid is int pid)
        {
            ProgressText.Text = "Aguardando o Agrupa-Janela fechar…";
            await Task.Run(() => RunningApp.WaitForPid(pid, TimeSpan.FromSeconds(30)));
        }
        var error = SetupFlow.ResolveInstallDir(_ctx, _cmd, _existing, out var dir);
        if (error != null)
        {
            Fail(error, SetupFlow.ExitFailed);
            return;
        }
        var sameDir = _existing != null && RunEntry.SamePath(_existing.Location, dir);
        var ok = await RunInstallAsync(new InstallOptions
        {
            InstallDir = dir,
            DesktopShortcut = sameDir && _existing!.DesktopShortcut,
            StartWithWindows = null,
        });
        if (ok && _cmd.Relaunch)
        {
            SetupFlow.LaunchApp(_ctx, dir);
            Close();
        }
    }

    private async Task<bool> RunInstallAsync(InstallOptions options)
    {
        ShowPage(Array.IndexOf(_flow, PageProgress));
        SetBusy(true);
        if (_steps.Count == 0) BuildSteps(Installer.Steps);
        if (!Payload.IsPresent)
        {
            Fail("Este instalador não contém o pacote do app (build de desenvolvimento).", SetupFlow.ExitFailed);
            return false;
        }
        ProgressText.Text = "Verificando se o Agrupa-Janela está aberto…";
        if (!await SetupFlow.EnsureAppClosedAsync(_ctx, options.InstallDir, AskCloseAppAsync))
        {
            Fail("O Agrupa-Janela continua aberto, então a instalação foi interrompida. Nada foi alterado.", SetupFlow.ExitAppStillRunning);
            return false;
        }

        var progress = new Progress<StepProgress>(OnStep);
        try
        {
            await Task.Run(() => new Installer(_ctx, progress).Run(options));
        }
        catch (Exception ex)
        {
            Log.Error("A instalação falhou", ex);
            Fail(ex is SetupException || ex is InvalidDataException ? ex.Message : $"A instalação falhou: {ex.Message}", SetupFlow.ExitFailed);
            return false;
        }

        _installDir = options.InstallDir;
        _busy = false;
        _done = true;
        ExitCode = SetupFlow.ExitOk;
        DoneHeading.Text = _mode == SetupMode.Update ? "O Agrupa-Janela foi atualizado" : "O Agrupa-Janela foi instalado";
        DoneText.Text = $"Versão {_ctx.Version} em {options.InstallDir}\n"
                        + "Atalho: Menu Iniciar › Agrupa-Janela" + (options.DesktopShortcut ? " e Área de Trabalho" : "") + "\n"
                        + "Para desinstalar: Configurações › Aplicativos › Agrupa-Janela.";
        DoneNote.Text = _mode == SetupMode.Install
            ? (LaunchBox.IsChecked == true ? (_ctx.TestMode ? "Modo de teste: o app instalado não será aberto." : "O app abre quando você clicar em Concluir.") : "")
            : "";
        ShowPage(Array.IndexOf(_flow, PageDone));
        return true;
    }

    // ---------------- Desinstalação ----------------

    private async Task StartUninstallAsync()
    {
        var dir = _uninstallDir!;
        var removeData = RemoveDataBox.IsChecked == true;
        ShowPage(Array.IndexOf(_flow, PageProgress));
        SetBusy(true);
        BuildSteps(Uninstaller.StepsFor(removeData));
        ProgressText.Text = "Verificando se o Agrupa-Janela está aberto…";
        if (!await SetupFlow.EnsureAppClosedAsync(_ctx, dir, AskCloseAppAsync))
        {
            Fail("O Agrupa-Janela continua aberto, então a desinstalação foi interrompida. Nada foi removido.", SetupFlow.ExitAppStillRunning);
            return;
        }
        var progress = new Progress<StepProgress>(OnStep);
        try
        {
            await Task.Run(() => new Uninstaller(_ctx, progress).Run(dir, removeData));
        }
        catch (Exception ex)
        {
            Log.Error("A desinstalação falhou", ex);
            Fail(ex is SetupException ? ex.Message : $"A desinstalação falhou: {ex.Message}", SetupFlow.ExitFailed);
            return;
        }
        _busy = false;
        _done = true;
        ExitCode = SetupFlow.ExitOk;
        DoneHeading.Text = "O Agrupa-Janela foi removido";
        DoneText.Text = removeData
            ? "Arquivos, atalhos e registros foram removidos, assim como seus grupos salvos e preferências."
            : $"Arquivos, atalhos e registros foram removidos.\nSeus grupos salvos e preferências foram mantidos em {_ctx.DataDir}.";
        DoneNote.Text = SelfDelete.PendingDir != null ? "A pasta do desinstalador é apagada alguns segundos depois de fechar esta janela." : "";
        ShowPage(Array.IndexOf(_flow, PageDone));
    }

    // ---------------- Progresso ----------------

    private Task<bool> AskCloseAppAsync(bool first)
    {
        var choice = first
            ? SetupDialog.Ask(this,
                "O Agrupa-Janela está aberto. Para continuar, ele será fechado: as janelas agrupadas voltam para a área de trabalho, sem fechar nenhum app.",
                ("Fechar o Agrupa-Janela e continuar", "", true),
                ("Cancelar", "", false))
            : SetupDialog.Ask(this,
                "O Agrupa-Janela não fechou sozinho. Feche-o você mesmo (ícone na bandeja › Sair) e clique em Tentar de novo.",
                ("Tentar de novo", "", true),
                ("Cancelar", "", false));
        if (choice == 0) ProgressText.Text = "Fechando o Agrupa-Janela…";
        return Task.FromResult(choice == 0);
    }

    private void BuildSteps(string[] names)
    {
        StepList.Children.Clear();
        _steps.Clear();
        _currentStep = 0;
        InstallProgress.Value = 0;
        ErrorPanel.Visibility = Visibility.Collapsed;
        foreach (var name in names)
        {
            var mark = new TextBlock { Width = 18, Text = "•", Foreground = (Brush)FindResource("Line") };
            var text = new TextBlock { Text = name, Foreground = (Brush)FindResource("TextDim"), TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            row.Children.Add(mark);
            row.Children.Add(text);
            StepList.Children.Add(row);
            _steps.Add((mark, text));
        }
    }

    private void OnStep(StepProgress p)
    {
        if (_failed) return;
        _currentStep = p.Step;
        ProgressText.Text = p.Detail;
        InstallProgress.Value = p.Percent;
        for (var k = 0; k < _steps.Count; k++)
        {
            var (mark, text) = _steps[k];
            if (k < p.Step)
            {
                mark.Text = "✓";
                mark.Foreground = (Brush)FindResource("Accent");
                text.Foreground = (Brush)FindResource("Text");
                text.FontWeight = FontWeights.Normal;
            }
            else if (k == p.Step)
            {
                mark.Text = "›";
                mark.Foreground = (Brush)FindResource("Accent");
                text.Foreground = (Brush)FindResource("Text");
                text.FontWeight = FontWeights.SemiBold;
            }
        }
    }

    private void Fail(string message, int exitCode)
    {
        _busy = false;
        _failed = true;
        ExitCode = exitCode;
        if (_currentStep < _steps.Count)
        {
            var (mark, _) = _steps[_currentStep];
            mark.Text = "✗";
            mark.Foreground = (Brush)FindResource("Danger");
        }
        ProgressText.Text = "Não foi possível concluir.";
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        NextButton.Content = "Fechar";
        NextButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(Log.FilePath))
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Log.FilePath}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível abrir o log: {ex.Message}");
        }
    }
}
