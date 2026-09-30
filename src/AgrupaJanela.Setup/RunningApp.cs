using System;
using System.Diagnostics;
using System.Linq;
using System.Security.AccessControl;
using System.Threading;

namespace AgrupaJanela.Setup;

/// <summary>
/// Conversa com o app aberto: mutex de instância `Local\SplitDeck.{SID}` e evento `...Quit`
/// (o app devolve as janelas agrupadas e sai). Nunca mata o processo: mataria as janelas agrupadas.
/// </summary>
internal sealed class RunningApp
{
    private readonly SetupContext _ctx;
    private readonly string _installDir;

    public RunningApp(SetupContext ctx, string installDir)
    {
        _ctx = ctx;
        _installDir = installDir;
    }

    public bool IsRunning() => MutexExists() || InstalledProcessIds().Length > 0;

    private bool MutexExists()
    {
        try
        {
            if (Mutex.TryOpenExisting(_ctx.MutexName, MutexRights.Synchronize, out var m))
            {
                m.Dispose();
                return true;
            }
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // existe, só não temos acesso
        }
    }

    /// <summary>Processos SplitDeck.exe rodando a partir da pasta instalada.</summary>
    private int[] InstalledProcessIds()
    {
        var ids = Process.GetProcessesByName(SetupContext.AppProcessName).Select(p =>
        {
            using (p)
            {
                var path = NativeMethods.GetProcessPath(p.Id);
                return path != null && PathRules.IsSameOrInside(PathRules.ToLongPath(path), _installDir) ? p.Id : 0;
            }
        });
        return ids.Where(id => id != 0).ToArray();
    }

    /// <summary>Sinaliza o evento Quit. False se o app não expõe o evento (versão antiga ou outro estado).</summary>
    public bool SignalQuit()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(_ctx.QuitEventName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, out var ev))
            {
                Log.Warn($"Evento {_ctx.QuitEventName} não existe; o app precisa ser fechado manualmente.");
                return false;
            }
            using (ev) ev.Set();
            Log.Info($"Evento {_ctx.QuitEventName} sinalizado.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível sinalizar {_ctx.QuitEventName}: {ex.Message}");
            return false;
        }
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (!IsRunning()) return true;
            Thread.Sleep(250);
        }
        return !IsRunning();
    }

    /// <summary>Para --update --wait-pid: espera o processo que chamou o atualizador sair.</summary>
    public static bool WaitForPid(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var exited = p.WaitForExit((int)timeout.TotalMilliseconds);
            Log.Info(exited ? $"Processo {pid} saiu." : $"Processo {pid} ainda está rodando após {timeout.TotalSeconds:0} s.");
            return exited;
        }
        catch (ArgumentException)
        {
            Log.Info($"Processo {pid} já tinha saído.");
            return true;
        }
    }
}
