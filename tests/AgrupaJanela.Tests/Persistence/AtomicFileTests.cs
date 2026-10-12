using AgrupaJanela.Persistence;
using System.IO;

namespace AgrupaJanela.Tests.Persistence;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "splitdeck-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Cria_o_arquivo_e_nao_deixa_tmp_para_tras()
    {
        var path = Path.Combine(_dir, "sub", "dados.json");

        AtomicFile.WriteAllText(path, "{\"ok\":true}");

        Assert.Equal("{\"ok\":true}", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Substitui_o_conteudo_anterior()
    {
        var path = Path.Combine(_dir, "dados.json");
        AtomicFile.WriteAllText(path, "antigo");

        AtomicFile.WriteAllText(path, "novo");

        Assert.Equal("novo", File.ReadAllText(path));
    }
}
