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
    public DateTime SavedAt { get; set; }

    public int AppCount => Count(Layout);
    public string Summary => $"{AppCount} app(s) · salvo em {SavedAt:dd/MM HH:mm}";

    private static int Count(NodeDto? node) => node is null ? 0 : node.App is not null ? 1 : node.Children.Sum(Count);
}

/// <summary>Grupos salvos em %APPDATA%\AgrupaJanela\groups.json.</summary>
public sealed class GroupStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private const int MaxFileBytes = 2 * 1024 * 1024; // um groups.json legítimo tem poucos KB
    private readonly string _path = AppPaths.GroupsFile;

    /// <summary>Falha ao gravar (arquivo bloqueado por antivírus/backup etc.). Os grupos continuam funcionando.</summary>
    public event Action<string>? SaveFailed;

    public ObservableCollection<SavedGroup> Groups { get; } = new();

    public GroupStore()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > MaxFileBytes) return;
            foreach (var group in JsonSerializer.Deserialize<List<SavedGroup>>(File.ReadAllText(_path), Options) ?? new())
                if (Sanitize(group)) Groups.Add(group);
        }
        catch
        {
            // Arquivo corrompido: preserva uma cópia e começa vazio.
            try { File.Copy(_path, _path + ".bak", overwrite: true); } catch { }
        }
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
        // Gravação atômica com uma nova tentativa: um bloqueio passageiro nunca pode desmontar os grupos.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Groups.ToList(), Options));
                File.Move(temp, _path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2) { SaveFailed?.Invoke(ex.Message); return; }
                Thread.Sleep(80);
            }
        }
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
