using AgrupaJanela.Native;
using System.Text.RegularExpressions;

namespace AgrupaJanela.Hosting;

/// <summary>O suficiente para reencontrar ou reabrir o aplicativo de um painel salvo.</summary>
public sealed record AppIdentity(string? ExePath, string? Arguments, string Title, long LastHandle, uint LastProcessId)
{
    public static AppIdentity Of(WindowCandidate candidate)
    {
        var path = Win32.GetProcessPath(candidate.ProcessId);
        return new AppIdentity(path, WithoutSecrets(ArgumentsOf(Win32.GetProcessCommandLine(candidate.ProcessId))), candidate.Title, candidate.Handle, candidate.ProcessId);
    }

    // Argumentos com cara de senha/token não vão para o disco: o app reabre sem eles.
    private static readonly Regex SecretLike = new(
        @"(pass(word|wd)?|pwd|token|secret|api[-_]?key|access[-_]?key|auth|bearer|credential)|(^|\s)-p\S|://[^\s/]*:[^\s/]*@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal static string? WithoutSecrets(string? arguments)
    {
        if (arguments is null) return null;
        try { return SecretLike.IsMatch(arguments) ? null : arguments; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>Remove o executável (primeiro token) da linha de comando.</summary>
    private static string? ArgumentsOf(string? commandLine)
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
