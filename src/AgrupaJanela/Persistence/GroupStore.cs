using AgrupaJanela.Layout;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace AgrupaJanela.Persistence;

public sealed class SavedGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Tabs { get; set; }
    public NodeDto? Layout { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 750;
    public bool Maximized { get; set; }
    /// <summary>Painel ativo (aba selecionada no modo abas), na ordem das folhas do layout; -1 = nenhum.</summary>
    public int ActiveIndex { get; set; } = -1;
    /// <summary>Painel maximizado dentro do grupo (modo grade), na ordem das folhas; -1 = nenhum.</summary>
    public int MaximizedIndex { get; set; } = -1;
    public DateTime SavedAt { get; set; }

    public int AppCount => Count(Layout);
    public string Summary => $"{AppCount} app(s) · salvo em {SavedAt:dd/MM HH:mm}";

    private static int Count(NodeDto? node) => node is null ? 0 : node.App is not null ? 1 : node.Children.Sum(Count);
}

/// <summary>Grupos salvos em %APPDATA%\SplitDeck\groups.json.</summary>
public sealed class GroupStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private const int MaxFileBytes = 2 * 1024 * 1024; // um groups.json legítimo tem poucos KB
    /// <summary>Versão do formato gravado: {"SchemaVersion": 2, "Groups": [...]}. A v1 era só o array de grupos.</summary>
    internal const int CurrentSchemaVersion = 2;
    private readonly string _path;

    /// <summary>O arquivo lido não pôde ser copiado para um .bak: não grava por cima dele até conseguir.</summary>
    private bool _unpreserved;

    /// <summary>Versão maior que a suportada (criada por um SplitDeck mais novo): trabalha em memória e não grava.</summary>
    private int _futureVersion;

    internal sealed class FileDto
    {
        public int SchemaVersion { get; set; }
        public List<SavedGroup>? Groups { get; set; }
    }

    /// <summary>Falha ao gravar (arquivo bloqueado por antivírus/backup etc.). Os grupos continuam funcionando.</summary>
    public event Action<string>? SaveFailed;

    public ObservableCollection<SavedGroup> Groups { get; } = new();

    public GroupStore() : this(AppPaths.GroupsFile) { }

    /// <summary>Pasta de teste: o construtor público sempre usa o groups.json real.</summary>
    internal GroupStore(string path)
    {
        _path = path;
        string? text = null;
        try
        {
            if (!File.Exists(_path)) return;
            // Grande demais para ser um groups.json legítimo: não é carregado, mas também não pode ser sobrescrito.
            if (new FileInfo(_path).Length > MaxFileBytes) { Preserve(); return; }
            text = File.ReadAllText(_path);
            var loaded = Parse(text, out var version);
            foreach (var group in loaded)
                if (Sanitize(group)) Groups.Add(group);
            if (version > CurrentSchemaVersion) _futureVersion = version;
        }
        catch
        {
            // Ilegível: guarda uma cópia antes de qualquer gravação e começa vazio.
            if (text is not null) _futureVersion = FutureVersionOf(text);
            Preserve();
        }
    }

    /// <summary>Lê o formato atual (envelope) e o antigo (array). Lança se o conteúdo não for reconhecível.</summary>
    internal static List<SavedGroup> Parse(string text, out int version)
    {
        if (text.TrimStart().StartsWith('['))
        {
            version = 1;
            return JsonSerializer.Deserialize<List<SavedGroup>>(text, Options) ?? new();
        }
        var file = JsonSerializer.Deserialize<FileDto>(text, Options) ?? throw new JsonException("groups.json vazio");
        if (file.SchemaVersion < 1 || file.Groups is null) throw new JsonException("groups.json sem SchemaVersion/Groups válidos");
        version = file.SchemaVersion;
        return file.Groups;
    }

    /// <summary>Versão do envelope, se for maior que a suportada (mesmo que o resto esteja ilegível); senão 0.</summary>
    private static int FutureVersionOf(string text)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty(nameof(FileDto.SchemaVersion), out var v)
                && v.TryGetInt32(out var version) && version > CurrentSchemaVersion) return version;
        }
        catch (System.Text.Json.JsonException) { }
        return 0;
    }

    private void Preserve()
    {
        if (File.Exists(_path)) _unpreserved = AtomicFile.CopyAsBackup(_path) is null;
    }

    public void Save(SavedGroup group)
    {
        group.SavedAt = DateTime.Now;
        var index = Groups.ToList().FindIndex(g => g.Id == group.Id);
        if (index >= 0) Groups[index] = group;
        else Groups.Add(group);
        Flush();
    }

    public void Delete(Guid id)
    {
        if (Groups.FirstOrDefault(g => g.Id == id) is { } group) Groups.Remove(group);
        Flush();
    }

    private void Flush()
    {
        // Arquivo de uma versão mais nova: gravar apagaria dados que este app não entende.
        if (_futureVersion > 0)
        {
            SaveFailed?.Invoke($"O groups.json foi salvo por uma versão mais nova do SplitDeck (formato {_futureVersion}). Os grupos não foram gravados para não substituí-lo.");
            return;
        }
        // Só grava depois de ter guardado uma cópia do arquivo ilegível que estava no lugar.
        if (_unpreserved)
        {
            if (File.Exists(_path) && AtomicFile.CopyAsBackup(_path) is null)
            {
                SaveFailed?.Invoke("O groups.json existente não pôde ser copiado; os grupos não foram gravados para não substituí-lo.");
                return;
            }
            _unpreserved = false;
        }
        var file = new FileDto { SchemaVersion = CurrentSchemaVersion, Groups = Groups.ToList() };
        try { AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Options)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SaveFailed?.Invoke(ex.Message); }
    }

    /// <summary>Descarta/corrige valores impossíveis de um arquivo corrompido ou adulterado.</summary>
    internal static bool Sanitize(SavedGroup group)
    {
        if (group.Id == Guid.Empty) return false;
        group.Name = string.IsNullOrWhiteSpace(group.Name) ? "Grupo" : group.Name.Trim()[..Math.Min(group.Name.Trim().Length, 80)];
        if (!double.IsFinite(group.Width) || group.Width < 300 || group.Width > 20000) group.Width = 1200;
        if (!double.IsFinite(group.Height) || group.Height < 200 || group.Height > 20000) group.Height = 750;
        if (!double.IsFinite(group.Left) || Math.Abs(group.Left) > 50000) group.Left = double.NaN;
        if (!double.IsFinite(group.Top) || Math.Abs(group.Top) > 50000) group.Top = double.NaN;
        var apps = 0;
        group.Layout = SanitizeNode(group.Layout, 0, ref apps);
        var kept = Math.Min(apps, MaxAppsPerGroup); // apps além do limite foram descartados acima
        if (group.ActiveIndex < -1 || group.ActiveIndex >= kept) group.ActiveIndex = -1;
        if (group.MaximizedIndex < -1 || group.MaximizedIndex >= kept) group.MaximizedIndex = -1;
        return true;
    }

    private const int MaxAppsPerGroup = 16, MaxDepth = 12;

    private static NodeDto? SanitizeNode(NodeDto? node, int depth, ref int apps)
    {
        if (node is null || depth > MaxDepth) return null;
        if (node.App is not null) return ++apps <= MaxAppsPerGroup ? node : null;
        node.Children ??= new();
        node.Weights ??= new();
        var children = new List<NodeDto>();
        var weights = new List<double>();
        for (var i = 0; i < node.Children.Count; i++)
        {
            if (SanitizeNode(node.Children[i], depth + 1, ref apps) is not { } child) continue;
            var w = i < node.Weights.Count ? node.Weights[i] : 1;
            children.Add(child);
            weights.Add(double.IsFinite(w) && w > 0 ? Math.Min(w, 1e6) : 1);
        }
        node.Children = children;
        node.Weights = weights;
        return children.Count == 0 ? null : node;
    }
}
