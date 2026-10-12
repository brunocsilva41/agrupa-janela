using AgrupaJanela.Persistence;
using System.IO;
using System.Text;

namespace AgrupaJanela.Tests.Persistence;

/// <summary>Usa só pastas temporárias: nunca toca no groups.json real (%APPDATA% ou SPLITDECK_DATA).</summary>
public class GroupStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "splitdeck-tests-" + Guid.NewGuid().ToString("N"));
    private string GroupsPath => Path.Combine(_dir, "groups.json");

    public GroupStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string[] Backups() => Directory.GetFiles(_dir, "groups.json.bak-*");

    [Fact]
    public void JsonCorrompido_gera_bak_com_o_mesmo_conteudo_e_comeca_vazio()
    {
        const string corrupt = "[{\"Id\": \"nao fecha";
        File.WriteAllText(GroupsPath, corrupt);

        var store = new GroupStore(GroupsPath);

        Assert.Empty(store.Groups);
        var bak = Assert.Single(Backups());
        Assert.Equal(corrupt, File.ReadAllText(bak));
        Assert.Equal(corrupt, File.ReadAllText(GroupsPath)); // nada foi gravado ainda
    }

    [Fact]
    public void Primeira_gravacao_depois_do_corrompido_preserva_a_copia()
    {
        const string corrupt = "{ lixo";
        File.WriteAllText(GroupsPath, corrupt);
        var store = new GroupStore(GroupsPath);

        store.Save(new SavedGroup { Id = Guid.NewGuid(), Name = "Novo" });

        Assert.Equal(corrupt, File.ReadAllText(Backups().Single()));
        Assert.Contains("Novo", File.ReadAllText(GroupsPath));
    }

    [Fact]
    public void Arquivo_acima_de_2MB_nao_e_perdido()
    {
        // Um groups.json legítimo tem poucos KB: acima do limite, não carrega e não sobrescreve sem cópia.
        var big = "[" + new string('x', 2 * 1024 * 1024 + 10) + "]";
        File.WriteAllText(GroupsPath, big, Encoding.UTF8);
        var originalBytes = new FileInfo(GroupsPath).Length; // inclui o BOM do UTF-8

        var store = new GroupStore(GroupsPath);

        Assert.Empty(store.Groups);
        var bak = Assert.Single(Backups());
        Assert.Equal(originalBytes, new FileInfo(bak).Length);

        store.Save(new SavedGroup { Id = Guid.NewGuid(), Name = "Depois" });

        Assert.True(File.Exists(bak));
        Assert.Equal(originalBytes, new FileInfo(bak).Length); // a cópia continua íntegra
    }

    [Fact]
    public void Se_nao_der_para_copiar_nao_grava_por_cima()
    {
        const string content = "[{\"Id\": \"x\"}]";
        File.WriteAllText(GroupsPath, content);
        // Arquivo aberto com acesso exclusivo: nem a leitura nem a cópia funcionam.
        using (new FileStream(GroupsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var store = new GroupStore(GroupsPath);
            var failures = new List<string>();
            store.SaveFailed += failures.Add;

            store.Save(new SavedGroup { Id = Guid.NewGuid(), Name = "Novo" });

            Assert.Single(failures);
        }
        Assert.Equal(content, File.ReadAllText(GroupsPath));
        Assert.Empty(Backups());
    }

    private static string Group(string name) => "{\"Id\":\"11111111-2222-3333-4444-555555555555\",\"Name\":\"" + name + "\"}";

    [Fact]
    public void Le_formato_v1_array_e_grava_envelope_v2()
    {
        File.WriteAllText(GroupsPath, "[" + Group("Antigo") + "]");

        var store = new GroupStore(GroupsPath);

        Assert.Equal("Antigo", Assert.Single(store.Groups).Name);
        Assert.Empty(Backups());

        store.Save(new SavedGroup { Id = Guid.NewGuid(), Name = "Novo" });

        var text = File.ReadAllText(GroupsPath);
        Assert.Contains("\"SchemaVersion\": 2", text);
        Assert.Equal(2, new GroupStore(GroupsPath).Groups.Count);
    }

    [Fact]
    public void Le_formato_v2_envelope()
    {
        File.WriteAllText(GroupsPath, "{\"SchemaVersion\": 2, \"Groups\": [" + Group("Atual") + "]}");

        var store = new GroupStore(GroupsPath);

        Assert.Equal("Atual", Assert.Single(store.Groups).Name);
        Assert.Empty(Backups());
    }

    [Fact]
    public void Versao_futura_carrega_em_memoria_mas_nao_sobrescreve()
    {
        const string future = "{\"SchemaVersion\": 3, \"Groups\": [" + "{\"Id\":\"11111111-2222-3333-4444-555555555555\",\"Name\":\"Futuro\"}" + "]}";
        File.WriteAllText(GroupsPath, future);
        var store = new GroupStore(GroupsPath);
        var failures = new List<string>();
        store.SaveFailed += failures.Add;

        store.Save(new SavedGroup { Id = Guid.NewGuid(), Name = "Local" });

        Assert.Equal(2, store.Groups.Count);                 // trabalha em memória
        var message = Assert.Single(failures);
        Assert.Contains("versão mais nova", message);
        Assert.Equal(future, File.ReadAllText(GroupsPath));  // o arquivo do outro SplitDeck ficou intacto
    }

    [Fact]
    public void Versao_futura_ilegivel_nao_e_sobrescrita_e_fica_copiada()
    {
        const string future = "{\"SchemaVersion\": 3, \"Groups\": 5}";
        File.WriteAllText(GroupsPath, future);
        var store = new GroupStore(GroupsPath);
        var failures = new List<string>();
        store.SaveFailed += failures.Add;

        store.Save(new SavedGroup { Id = Guid.NewGuid(), Name = "Local" });

        Assert.Single(failures);
        Assert.Equal(future, File.ReadAllText(GroupsPath));
        Assert.Equal(future, File.ReadAllText(Backups().Single()));
    }

    [Fact]
    public void Arquivo_inexistente_cria_grupos_vazios_sem_bak()
    {
        var store = new GroupStore(GroupsPath);

        Assert.Empty(store.Groups);
        Assert.Empty(Backups());
    }
}
