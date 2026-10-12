using System.IO;
using AgrupaJanela.Hosting;

namespace AgrupaJanela.Tests.Hosting;

/// <summary>Os casos reais que impediam a reabertura: Windows Terminal e Discord atualizados mudam de pasta.</summary>
public class AppVersionPathTests
{
    private const string TerminalOld = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe";
    private const string TerminalNew = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.12741.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe";

    [Fact]
    public void Loja_VersoesDiferentes_MesmoApp() => Assert.True(AppVersionPath.SameApp(TerminalOld, TerminalNew));

    [Fact]
    public void Loja_EditorDiferente_OutroApp() =>
        Assert.False(AppVersionPath.SameApp(TerminalOld, TerminalNew.Replace("8wekyb3d8bbwe", "outroeditor")));

    [Fact]
    public void Loja_OutroExecutavelDoMesmoPacote_OutroApp() =>
        Assert.False(AppVersionPath.SameApp(TerminalOld, TerminalNew.Replace("WindowsTerminal.exe", "OpenConsole.exe")));

    [Fact]
    public void Squirrel_VersoesDiferentes_MesmoApp() =>
        Assert.True(AppVersionPath.SameApp(@"C:\Users\x\AppData\Local\Discord\app-1.0.9257\Discord.exe", @"c:\users\X\appdata\local\discord\APP-1.0.9261\discord.exe"));

    [Fact]
    public void CaminhoComum_SoIgualQuandoIgual()
    {
        Assert.True(AppVersionPath.SameApp(@"C:\Windows\explorer.exe", @"c:\windows\EXPLORER.EXE"));
        Assert.False(AppVersionPath.SameApp(@"C:\a\app.exe", @"C:\b\app.exe"));
        Assert.False(AppVersionPath.SameApp(null, null));
        Assert.False(AppVersionPath.SameApp("", ""));
    }

    [Theory]
    [InlineData("Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe")]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe", null)] // pasta de atalhos (família), não de pacote
    [InlineData("Microsoft.WindowsTerminal_versao_x64__8wekyb3d8bbwe", null)]
    [InlineData("Program Files", null)]
    public void PackageFamily(string folder, string? expected) => Assert.Equal(expected, AppVersionPath.PackageFamily(folder));

    // ---------- O que abrir hoje (pastas temporárias imitando o disco) ----------

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "splitdeck-tests-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string File(string relative)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, "");
            return full;
        }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    [Fact]
    public void Loja_PastaAntigaSumiu_UsaOAtalhoUnicoDoPacote()
    {
        using var aliases = new TempDir();
        var wt = aliases.File(@"Microsoft.WindowsTerminal_8wekyb3d8bbwe\wt.exe");
        aliases.File("wt.exe"); // atalho na raiz com outro nome não serve como "mesmo nome"
        Assert.Equal(new[] { wt }, AppVersionPath.LaunchTargets(TerminalOld, aliases.Path));
    }

    [Fact]
    public void Loja_AtalhoComMesmoNome_TemPreferencia()
    {
        using var aliases = new TempDir();
        var same = aliases.File(@"Microsoft.WindowsTerminal_8wekyb3d8bbwe\WindowsTerminal.exe");
        aliases.File(@"Microsoft.WindowsTerminal_8wekyb3d8bbwe\wt.exe");
        Assert.Equal(same, AppVersionPath.LaunchTargets(TerminalOld, aliases.Path).First());
    }

    [Fact]
    public void Loja_VariosAtalhosSemOMesmoNome_NaoChuta()
    {
        using var aliases = new TempDir();
        aliases.File(@"Microsoft.WindowsTerminal_8wekyb3d8bbwe\a.exe");
        aliases.File(@"Microsoft.WindowsTerminal_8wekyb3d8bbwe\b.exe");
        Assert.Empty(AppVersionPath.LaunchTargets(TerminalOld, aliases.Path));
    }

    [Fact]
    public void Squirrel_AbreAVersaoMaisNova()
    {
        using var root = new TempDir();
        root.File("Update.exe");
        var old = root.File(@"app-1.0.9257\Discord.exe");
        root.File(@"app-1.0.10000\Discord.exe"); // 10000 > 9261: comparação numérica, não de texto
        root.File(@"app-1.0.9261\Discord.exe");
        root.File(@"app-2.0.0\outro.exe"); // versão mais nova sem o executável não conta
        Assert.Equal(new[] { System.IO.Path.Combine(root.Path, @"app-1.0.10000\Discord.exe") }, AppVersionPath.LaunchTargets(old, root.Path));
    }

    [Fact]
    public void Squirrel_PastaAntigaApagada_AindaAcha()
    {
        using var root = new TempDir();
        root.File("Update.exe");
        var current = root.File(@"app-1.0.9261\Discord.exe");
        var old = System.IO.Path.Combine(root.Path, @"app-1.0.9257\Discord.exe");
        Assert.Equal(new[] { current }, AppVersionPath.LaunchTargets(old, root.Path));
    }

    [Fact]
    public void SemUpdateExe_NaoEhSquirrel_UsaOCaminhoSalvo()
    {
        using var root = new TempDir();
        var saved = root.File(@"app-1.0\x.exe");
        root.File(@"app-2.0\x.exe");
        Assert.Equal(new[] { saved }, AppVersionPath.LaunchTargets(saved, root.Path));
    }

    [Fact]
    public void ExecutavelQueNaoExiste_NadaParaAbrir()
    {
        using var root = new TempDir();
        Assert.Empty(AppVersionPath.LaunchTargets(System.IO.Path.Combine(root.Path, "sumiu.exe"), root.Path));
    }
}
