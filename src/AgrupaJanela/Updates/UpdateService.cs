using AgrupaJanela.Hosting;
using AgrupaJanela.Persistence;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgrupaJanela.Updates;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string SetupUrl, string SumsUrl, long SetupSize);

/// <summary>
/// Atualização pelos Releases do GitHub. Única conexão de rede do app (pode ser desligada nas preferências).
/// Segurança: só baixa do repositório oficial, para uma pasta nova e aleatória em %LOCALAPPDATA%\SplitDeck\updates,
/// confere o SHA-256 publicado no release e só então executa o instalador (que fecha o app devolvendo as janelas,
/// instala e reabre). O arquivo verificado fica travado (sem escrita nem exclusão) até o instalador iniciar.
/// </summary>
public static class UpdateService
{
    public const string Owner = "brunocsilva41", Repo = "agrupa-janela";
    public static readonly string RepoUrl = $"https://github.com/{Owner}/{Repo}";
    public static readonly string IssuesUrl = $"{RepoUrl}/issues/new/choose";
    private static readonly string ApiLatest = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private static readonly string DownloadPrefix = $"https://github.com/{Owner}/{Repo}/releases/download/";
    private static readonly string ReleasesPrefix = $"{RepoUrl}/releases/";
    private const long MaxSetupBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    // Último instalador verificado e o handle que o mantém travado. Só LaunchInstaller o consome.
    private static readonly object VerifiedLock = new();
    private static string? _verifiedPath;
    private static FileStream? _verifiedHandle;

    public static Version CurrentVersion { get; } = ReadVersion();
    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>Instalado pelo instalador (tem o Setup.exe ao lado) — só assim a atualização automática se aplica.</summary>
    public static bool IsInstalled => File.Exists(SetupPath);
    private static string SetupPath => Path.Combine(AppContext.BaseDirectory, "Setup.exe");

    /// <summary>
    /// A UI usa isto para decidir entre "Atualizar" (baixa e executa o instalador verificado) e só oferecer
    /// abrir a página do release (<see cref="UpdateInfo.PageUrl"/>). Com o SplitDeck elevado (administrador)
    /// ou fora de uma instalação, nunca se atualiza sozinho: o instalador não é baixado nem executado.
    /// </summary>
    public static bool CanSelfUpdate => IsInstalled && !WindowCatalog.SelfElevated;

    /// <summary>Consulta o último release. Retorna null se não houver versão mais nova (ou sem internet).</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var response = await Http.GetAsync(ApiLatest, cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        return ParseRelease(json, CurrentVersion);
    }

    /// <summary>
    /// Interpreta o JSON de "releases/latest". Ignora draft e prerelease, versão não mais nova, assets que não são
    /// do repositório oficial e instalador acima do limite de tamanho. Qualquer dado inválido vira null (sem atualização).
    /// </summary>
    internal static UpdateInfo? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

        var tag = StringOf(root, "tag_name") ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var parsed)) return null;
        var version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        if (version <= current) return null;

        var versionText = $"{version.Major}.{version.Minor}.{version.Build}";
        string? setup = null, sums = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var name = StringOf(asset, "name");
                var url = StringOf(asset, "browser_download_url") ?? "";
                if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue; // só do repositório oficial
                if (name == $"SplitDeck-Setup-{versionText}.exe") { setup = url; size = Int64Of(asset, "size"); }
                else if (name == "SHA256SUMS.txt") sums = url;
            }
        }
        if (setup is null || sums is null || size <= 0 || size > MaxSetupBytes) return null;

        var notes = StringOf(root, "body") ?? "";
        var page = StringOf(root, "html_url") ?? "";
        if (!page.StartsWith(ReleasesPrefix, StringComparison.Ordinal)) page = RepoUrl;
        return new UpdateInfo(version, tag, notes, page, setup, sums, size);
    }

    /// <summary>
    /// Hash publicado para <paramref name="fileName"/> em um SHA256SUMS.txt ("hash  nome" ou "hash *nome", com CRLF ou LF).
    /// O nome precisa ser igual (não basta começar igual: "x.exe" não casa com "x.exe.bak"). Sem entrada, ou com
    /// duas entradas de hashes diferentes para o mesmo nome, retorna null.
    /// </summary>
    internal static string? ExpectedHash(string sumsText, string fileName)
    {
        string? found = null;
        foreach (var raw in sumsText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 67) continue;                     // 64 hex + espaço + marcador/espaço + nome
            var hash = line[..64];
            if (!IsHex(hash)) continue;
            var sep = line[64..];
            if (sep[0] != ' ' || (sep[1] != ' ' && sep[1] != '*')) continue;
            if (sep.Length < 3 || sep[2..] != fileName) continue;

            var lower = hash.ToLowerInvariant();
            if (found != null && found != lower) return null;   // ambíguo: falha fechada
            found = lower;
        }
        return found;
    }

    /// <summary>
    /// Baixa o instalador para uma pasta nova e aleatória, confere o SHA-256 pelo mesmo handle que ficará travado
    /// e retorna o caminho verificado. Use <see cref="LaunchInstaller"/> com esse caminho.
    /// </summary>
    public static async Task<string> DownloadVerifiedAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (WindowCatalog.SelfElevated) throw new InvalidOperationException(ElevatedMessage);
        ReleaseVerified();
        var fileName = Path.GetFileName(new Uri(update.SetupUrl).LocalPath);

        var sums = await Http.GetStringAsync(update.SumsUrl, cancel).ConfigureAwait(false);
        var expected = ExpectedHash(sums, fileName)
            ?? throw new InvalidOperationException("O release não publica o hash do instalador. Atualização cancelada por segurança.");

        var file = Path.Combine(NewUpdateFolder(), fileName);
        using (var response = await Http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            // CreateNew: nunca sobrescreve nem segue um arquivo que já exista na pasta nova.
            await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxSetupBytes) throw new InvalidOperationException("Download maior que o esperado. Atualização cancelada.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                progress?.Report((double)total / update.SetupSize);
            }
        }

        // FileShare.Read: ninguém mais escreve nem apaga o arquivo enquanto ele está verificado.
        var handle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(handle, cancel).ConfigureAwait(false)).ToLowerInvariant();
            if (actual != expected)
            {
                handle.Dispose();
                File.Delete(file);
                throw new InvalidOperationException("O arquivo baixado não confere com o hash publicado. Atualização cancelada por segurança.");
            }
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        lock (VerifiedLock)
        {
            _verifiedPath = file;
            _verifiedHandle = handle;
        }
        return file;
    }

    /// <summary>
    /// Inicia o instalador verificado: ele espera este processo sair, instala e reabre o app. Só aceita o caminho
    /// devolvido por <see cref="DownloadVerifiedAsync"/>; o handle que trava o arquivo é solto logo após o início.
    /// </summary>
    public static void LaunchInstaller(string verifiedSetup)
    {
        if (WindowCatalog.SelfElevated) throw new InvalidOperationException(ElevatedMessage);
        lock (VerifiedLock)
        {
            if (_verifiedPath is null || _verifiedHandle is null || !string.Equals(_verifiedPath, verifiedSetup, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Instalador não verificado; ele não será executado.");
            try
            {
                Process.Start(new ProcessStartInfo(_verifiedPath, $"--update --silent --wait-pid {Environment.ProcessId} --relaunch") { UseShellExecute = false });
            }
            finally
            {
                ReleaseVerified();
            }
        }
    }

    private const string ElevatedMessage = "O SplitDeck está rodando como administrador: a atualização não é instalada sozinha. Baixe a nova versão pela página do release.";

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Hora de checar sozinho? (ligado nas preferências e última checagem há mais de 1 dia)</summary>
    public static bool IsDue(AppSettings settings) =>
        settings.AutoCheckUpdates && DateTime.UtcNow - settings.LastUpdateCheck > TimeSpan.FromHours(24);

    /// <summary>Pasta nova e aleatória (GUID) para um download; as antigas são apagadas no próximo download.</summary>
    private static string NewUpdateFolder()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitDeck", "updates");
        Directory.CreateDirectory(root);
        foreach (var old in Directory.GetDirectories(root))
        {
            try { Directory.Delete(old, true); }
            catch (Exception) { /* em uso ou protegida: fica para a próxima vez */ }
        }
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ReleaseVerified()
    {
        lock (VerifiedLock)
        {
            _verifiedHandle?.Dispose();
            _verifiedHandle = null;
            _verifiedPath = null;
        }
    }

    private static bool IsHex(string text) => text.All(c => Uri.IsHexDigit(c));

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long Int64Of(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static HttpClient CreateClient()
    {
        // Conexão ociosa fecha logo: a checagem acontece 1x por dia, não vale manter socket/TLS vivos.
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10) }) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SplitDeck/{ReadVersion()}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version ReadVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }
}
