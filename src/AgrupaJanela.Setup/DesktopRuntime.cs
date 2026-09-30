using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AgrupaJanela.Setup;

/// <summary>Detecção, download verificado e instalação do Microsoft .NET Desktop Runtime exigido pelo app (x64).</summary>
internal static class DesktopRuntime
{
    /// <summary>Versão principal do runtime que o app usa (igual ao TargetFramework do app). Mudar aqui ao migrar.</summary>
    public const int RequiredMajor = 10;

    public static readonly string DisplayName = $"Microsoft .NET Desktop Runtime {RequiredMajor} (x64)";
    public static readonly string DownloadUrl = $"https://aka.ms/dotnet/{RequiredMajor}.0/windowsdesktop-runtime-win-x64.exe";
    private const string FrameworkName = "Microsoft.WindowsDesktop.App";

    /// <summary>Maior versão {RequiredMajor}.x x64 instalada, ou null.</summary>
    public static string? FindInstalled()
    {
        var found = new List<Version>();
        string? installLocation = null;
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var fx = hklm.OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\{FrameworkName}"))
                foreach (var name in fx?.GetValueNames() ?? Array.Empty<string>())
                    if (TryParseRequired(name, out var v)) found.Add(v);
            using var x64 = hklm.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
            installLocation = x64?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) { Log.Warn($"Leitura do registro do .NET falhou: {ex.Message}"); }

        var roots = new[]
        {
            installLocation,
            Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
        };
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var dir = Path.Combine(root!, "shared", FrameworkName);
                if (!Directory.Exists(dir)) continue;
                foreach (var sub in Directory.GetDirectories(dir, RequiredMajor + ".*"))
                    if (TryParseRequired(Path.GetFileName(sub), out var v) && File.Exists(Path.Combine(sub, "PresentationFramework.dll")))
                        found.Add(v);
            }
            catch (Exception ex) { Log.Warn($"Leitura de {root} falhou: {ex.Message}"); }
        }
        return found.Count == 0 ? null : found.Max()!.ToString();
    }

    private static bool TryParseRequired(string text, out Version version)
    {
        var dash = text.IndexOf('-');
        var core = dash >= 0 ? text.Substring(0, dash) : text;
        if (Version.TryParse(core, out var parsed) && parsed.Major == RequiredMajor)
        {
            version = parsed;
            return true;
        }
        version = new Version();
        return false;
    }

    /// <summary>Baixa o instalador oficial para %TEMP%\SplitDeck-Setup\ (sobrescreve download anterior).</summary>
    public static async Task<string> DownloadAsync(IProgress<(long Received, long? Total)> progress, CancellationToken ct)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var dir = Path.Combine(Path.GetTempPath(), "SplitDeck-Setup");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "windowsdesktop-runtime-8-win-x64.exe");
        if (File.Exists(file)) File.Delete(file);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri == null || finalUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("O download não veio por HTTPS; cancelado por segurança.");
        Log.Info($"Baixando o runtime de {finalUri}");

        var total = response.Content.Headers.ContentLength;
        using (var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using (var dst = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                received += read;
                progress.Report((received, total));
            }
        }
        return file;
    }

    /// <summary>
    /// Verifica a assinatura Authenticode (WinVerifyTrust) e exige certificado do assinante da
    /// "Microsoft Corporation"; só então executa com /install /quiet /norestart. O arquivo fica aberto
    /// sem permitir escrita do início da verificação até o fim da execução (ninguém troca o arquivo no meio).
    /// Retorna o código de saída do instalador da Microsoft.
    /// </summary>
    public static async Task<int> VerifyAndRunAsync(string file)
    {
        using var lockStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var trust = VerifyAuthenticode(file, lockStream.SafeFileHandle.DangerousGetHandle());
        if (trust != 0)
            throw new InvalidOperationException($"A assinatura digital do arquivo baixado não é válida (WinVerifyTrust 0x{trust:X8}). Ele não foi executado.");

        string subject;
        using (var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(file)))
            subject = signer.Subject;
        if (subject.IndexOf("O=Microsoft Corporation", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException($"O arquivo baixado não é assinado pela Microsoft Corporation (assinante: {subject}). Ele não foi executado.");
        Log.Info($"Assinatura válida; assinante: {subject}");

        var psi = new ProcessStartInfo(file, "/install /quiet /norestart") { UseShellExecute = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador do runtime.");
        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
        Log.Info($"Instalador do runtime terminou com código {process.ExitCode}.");
        return process.ExitCode;
    }

    private static int VerifyAuthenticode(string file, IntPtr handle)
    {
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf(typeof(NativeMethods.WINTRUST_FILE_INFO)),
            pcwszFilePath = file,
            hFile = handle,
        };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.WINTRUST_FILE_INFO)));
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(NativeMethods.WINTRUST_DATA)),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
                dwProvFlags = NativeMethods.WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT,
            };
            var result = NativeMethods.WinVerifyTrust(IntPtr.Zero, NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(IntPtr.Zero, NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
            return result;
        }
        finally
        {
            Marshal.DestroyStructure(pFile, typeof(NativeMethods.WINTRUST_FILE_INFO));
            Marshal.FreeHGlobal(pFile);
        }
    }
}
