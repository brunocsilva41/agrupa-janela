using System;

namespace AgrupaJanela.Setup;

/// <summary>
/// (sem args) assistente | --silent | --update --wait-pid &lt;pid&gt; [--relaunch] | --uninstall [--silent]
/// | --install-dir &lt;pasta&gt; (só no modo teste, AGRUPAJANELA_SETUP_TEST=1).
/// </summary>
internal sealed class CommandLine
{
    public bool Silent { get; private set; }
    public bool Update { get; private set; }
    public bool Relaunch { get; private set; }
    public bool Uninstall { get; private set; }
    public int? WaitPid { get; private set; }
    public string? InstallDir { get; private set; }
    public string? Error { get; private set; }

    public static CommandLine Parse(string[] args)
    {
        var cmd = new CommandLine();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--silent": cmd.Silent = true; break;
                case "--update": cmd.Update = true; break;
                case "--relaunch": cmd.Relaunch = true; break;
                case "--uninstall": cmd.Uninstall = true; break;
                case "--wait-pid":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var pid) || pid <= 0)
                        return cmd.Fail("--wait-pid precisa de um número de processo.");
                    cmd.WaitPid = pid;
                    break;
                case "--install-dir":
                    if (i + 1 >= args.Length || args[i + 1].Length == 0)
                        return cmd.Fail("--install-dir precisa de uma pasta.");
                    cmd.InstallDir = args[++i];
                    break;
                default:
                    return cmd.Fail($"Opção desconhecida: {a}");
            }
        }
        if (cmd.Uninstall && cmd.Update) return cmd.Fail("--uninstall e --update não podem ser usados juntos.");
        if ((cmd.WaitPid != null || cmd.Relaunch) && !cmd.Update) return cmd.Fail("--wait-pid e --relaunch só valem com --update.");
        return cmd;
    }

    private CommandLine Fail(string message)
    {
        Error = message;
        return this;
    }

    public override string ToString() =>
        $"silent={Silent} update={Update} waitPid={WaitPid?.ToString() ?? "-"} relaunch={Relaunch} uninstall={Uninstall} installDir={InstallDir ?? "-"}";
}
