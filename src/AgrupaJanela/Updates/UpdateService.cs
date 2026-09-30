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
/// Segurança: só baixa do repositório oficial, confere o SHA-256 publicado no release e só então executa
/// o instalador (que fecha o app devolvendo as janelas, instala e reabre).
/// </summary>
public static class UpdateService
{
    public const string Owner = "brunocsilva41", Repo = "agrupa-janela";
    public static readonly string RepoUrl = $"https://github.com/{Owner}/{Repo}";
    public static readonly string IssuesUrl = $"{RepoUrl}/issues/new/choose";
    private static readonly string ApiLatest = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private static readonly string DownloadPrefix = $"https://github.com/{Owner}/{Repo}/releases/download/";
    private const long MaxSetupBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion { get; } = ReadVersion();
    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>Instalado pelo instalador (tem o Setup.exe ao lado) — só assim a atualização automática se aplica.</summary>
    public static bool IsInstalled => File.Exists(SetupPath);
    private static string SetupPath => Path.Combine(AppContext.BaseDirectory, "Setup.exe");

    /// <summary>Consulta o último release. Retorna null se não houver versão mais nova (ou sem internet).</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var response = await Http.GetAsync(ApiLatest, cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
        var root = json.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        if (version <= CurrentVersion) return null;

        var versionText = $"{version.Major}.{version.Minor}.{version.Build}";
        string? setup = null, sums = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue; // só do repositório oficial
            if (name == $"SplitDeck-Setup-{versionText}.exe") { setup = url; size = asset.GetProperty("size").GetInt64(); }
            else if (name == "SHA256SUMS.txt") sums = url;
        }
        if (setup is null || sums is null || size <= 0 || size > MaxSetupBytes) return null;

        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? RepoUrl : RepoUrl;
        return new UpdateInfo(version, tag, notes, page, setup, sums, size);
    }

    /// <summary>Baixa o instalador, confere o SHA-256 e retorna o caminho verificado.</summary>
    public static async Task<string> DownloadVerifiedAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SplitDeck-update", update.Version.ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Path.GetFileName(new Uri(update.SetupUrl).LocalPath));

        var sums = await Http.GetStringAsync(update.SumsUrl, cancel).ConfigureAwait(false);
        var expected = sums.Split('\n')
            .Select(line => line.Trim().Split(new[] { ' ', '*' }, 2, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].Trim() == Path.GetFileName(file))?[0].ToLowerInvariant()
            ?? throw new InvalidOperationException("O release não publica o hash do instalador. Atualização cancelada por segurança.");

        using (var response = await Http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using var output = File.Create(file);
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

        await using (var check = File.OpenRead(file))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancel).ConfigureAwait(false)).ToLowerInvariant();
            if (actual != expected)
            {
                File.Delete(file);
                throw new InvalidOperationException("O arquivo baixado não confere com o hash publicado. Atualização cancelada por segurança.");
            }
        }
        return file;
    }

    /// <summary>Inicia o instalador verificado: ele espera este processo sair, instala e reabre o app.</summary>
    public static void LaunchInstaller(string verifiedSetup) =>
        Process.Start(new ProcessStartInfo(verifiedSetup, $"--update --silent --wait-pid {Environment.ProcessId} --relaunch") { UseShellExecute = false });

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Hora de checar sozinho? (ligado nas preferências e última checagem há mais de 1 dia)</summary>
    public static bool IsDue(AppSettings settings) =>
        settings.AutoCheckUpdates && DateTime.UtcNow - settings.LastUpdateCheck > TimeSpan.FromHours(24);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
