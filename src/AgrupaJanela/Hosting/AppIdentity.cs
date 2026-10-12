using AgrupaJanela.Native;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgrupaJanela.Hosting;

/// <summary>O suficiente para reencontrar ou reabrir o aplicativo de um painel salvo.</summary>
public sealed record AppIdentity(string? ExePath, string? Arguments, string Title, long LastHandle, uint LastProcessId)
{
    /// <summary>Terminais: a pasta em que estavam, para reabrir no mesmo lugar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; init; }

    public static AppIdentity Of(WindowCandidate candidate)
    {
        var path = Win32.GetProcessPath(candidate.ProcessId);
        return new AppIdentity(path, WithoutSecrets(ArgumentsOf(Win32.GetProcessCommandLine(candidate.ProcessId))), candidate.Title, candidate.Handle, candidate.ProcessId);
    }

    // Argumentos com cara de senha/token/chave não vão para o disco: o app reabre sem eles.
    // Falha fechada: na dúvida, descarta. Cada alternativa cobre um formato real (curl, sshpass, URLs assinadas,
    // tokens de GitHub/OpenAI-like, JWT). "-d C:\proj", "-p PowerShell" e --profile-directory=Default passam.
    private static readonly Regex SecretLike = new(
        @"(pass(word|wd)?|pwd|token|secret|api[-_]?key|access[-_]?key|auth|bearer|credential)" // nomes com cara de segredo
        + @"|(^|\s)-p\S"                                  // -pSENHA (encostado)
        + @"|://[^\s/]*:[^\s/]*@"                          // usuario:senha@host
        + @"|(^|\s)-u(\s|\S)"                              // curl -u usuario:senha
        + @"|(^|\s)--(user|key|cookie)(=|\s|$)"            // --user, --key=, --cookie
        + @"|\bsshpass\b"                                  // sshpass -p <senha> ...
        + @"|[?&;](sig|signature|X-Amz-Signature|X-Amz-Security-Token)=" // assinatura de URL
        + @"|(?<![A-Za-z0-9])gh[pousr]_[A-Za-z0-9_]+"      // tokens do GitHub (ghp_, gho_, ...)
        + @"|(?<![A-Za-z0-9])github_pat_[A-Za-z0-9_]+"     // token fine-grained do GitHub
        + @"|(?<![A-Za-z0-9_-])sk-[A-Za-z0-9]{20,}"        // chave de API no formato sk-...
        + @"|eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+",       // JWT (cabeçalho e payload em base64url)
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal static string? WithoutSecrets(string? arguments)
    {
        if (arguments is null) return null;
        try { return SecretLike.IsMatch(arguments) ? null : arguments; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>Remove o executável (primeiro token) da linha de comando.</summary>
    internal static string? ArgumentsOf(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var text = commandLine.TrimStart();
        int end;
        if (text.StartsWith('"'))
        {
            end = text.IndexOf('"', 1);
            end = end < 0 ? text.Length : end + 1;
        }
        else
        {
            end = text.IndexOfAny(new[] { ' ', '	' }); // o Windows aceita espaço ou TAB como separador
            if (end < 0) end = text.Length;
        }
        var args = text[end..].Trim();
        return args.Length == 0 ? null : args;
    }
}
