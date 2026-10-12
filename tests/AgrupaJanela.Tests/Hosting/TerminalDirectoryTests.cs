using System.IO;
using System.Diagnostics;
using AgrupaJanela.Hosting;
using AgrupaJanela.Native;

namespace AgrupaJanela.Tests.Hosting;

public class TerminalDirectoryTests
{
    [Theory]
    [InlineData(null, @"-d ""C:\proj""")]
    [InlineData("", @"-d ""C:\proj""")]
    [InlineData(@"-d ""C:\Users\Bruno Silva\Desktop\JOGOS\_hub""", @"-d ""C:\proj""")] // o caso real do groups.json
    [InlineData(@"-d C:\velho -p ""PowerShell""", @"-d ""C:\proj"" -p ""PowerShell""")]
    [InlineData(@"--startingDirectory ""C:\velho"" -p x", @"-d ""C:\proj"" -p x")]
    public void WindowsTerminal_TrocaAPasta(string? saved, string expected) =>
        Assert.Equal(expected, TerminalDirectory.WindowsTerminalArguments(saved, @"C:\proj"));

    [Theory]
    [InlineData(@"C:\proj", @"""C:\proj""")]
    [InlineData(@"C:\", @"""C:\\""")] // sem dobrar, \" escaparia a aspa final
    [InlineData(@"C:\com espaço", @"""C:\com espaço""")]
    public void Quote(string value, string expected) => Assert.Equal(expected, TerminalDirectory.Quote(value));

    [Fact]
    public void Clean_SoPastasLocaisQueExistem()
    {
        var temp = Path.GetTempPath();
        Assert.Equal(temp.TrimEnd('\\'), TerminalDirectory.Clean(temp));
        Assert.Equal(@"C:\", TerminalDirectory.Clean(@"C:\"));
        Assert.Null(TerminalDirectory.Clean(null));
        Assert.Null(TerminalDirectory.Clean("relativa"));
        Assert.Null(TerminalDirectory.Clean(@"\\servidor\pasta"));
        Assert.Null(TerminalDirectory.Clean(Path.Combine(temp, "nao-existe-" + Guid.NewGuid())));
    }

    [Fact]
    public void PastaAtualDeOutroProcesso_LidaDeVerdade()
    {
        // Processo real (escondido) aberto numa pasta conhecida: a leitura do PEB tem que devolver essa pasta.
        var dir = Directory.CreateTempSubdirectory("splitdeck-cwd-").FullName;
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ping -n 30 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        })!;
        try
        {
            Thread.Sleep(300);
            Assert.Equal(dir.TrimEnd('\\') + "\\", Win32.GetProcessCurrentDirectory((uint)process.Id), ignoreCase: true);

            // O filho mais recente do cmd (o ping) aparece na árvore de processos com o cmd como pai.
            Assert.Contains(Win32.ProcessParents(), p => p.Parent == (uint)process.Id);
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } catch { }
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PastaAtual_ProcessoInexistente_Null() => Assert.Null(Win32.GetProcessCurrentDirectory(0));
}
