using AgrupaJanela.Hosting;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace AgrupaJanela.Persistence;

/// <summary>Preferências em %APPDATA%\SplitDeck\settings.json (+ registro para iniciar com o Windows).</summary>
public sealed class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "SplitDeck";
    private static string FilePath => AppPaths.SettingsFile;

    /// <summary>Verificar atualizações nos Releases do GitHub (1x por dia, ao iniciar).</summary>
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    /// <summary>Versão que o usuário escolheu pular.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Reabrir automaticamente os grupos salvos quando o app inicia.</summary>
    public bool ReopenSavedOnStart { get; set; }

    /// <summary>Mostrar "Agrupar…" no menu da barra de título dos outros apps.</summary>
    public bool SystemMenuEnabled { get; set; } = true;

    /// <summary>Esconder a janela principal na bandeja depois de agrupar por ela.</summary>
    public bool HideMainAfterGrouping { get; set; } = true;

    private Dictionary<string, EmbedMode> _embedOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Modo escolhido pelo usuário por executável (chave = nome do arquivo em minúsculas, ex.: "chrome.exe").</summary>
    public Dictionary<string, EmbedMode> EmbedOverrides
    {
        get => _embedOverrides;
        set
        {
            // O JSON cria um dicionário sensível a maiúsculas; normaliza e descarta Auto (valor desconhecido/antigo).
            var normalized = new Dictionary<string, EmbedMode>(StringComparer.OrdinalIgnoreCase);
            if (value is not null)
                foreach (var (key, mode) in value)
                    if (OverrideKey(key) is { } k && mode != EmbedMode.Auto) normalized[k] = mode;
            _embedOverrides = normalized;
        }
    }

    /// <summary>Modo salvo para o executável; Auto se não houver.</summary>
    public EmbedMode GetOverride(string? exePath) =>
        OverrideKey(exePath) is { } key && _embedOverrides.TryGetValue(key, out var mode) ? mode : EmbedMode.Auto;

    /// <summary>Salva o modo para o executável (Auto remove) e grava o arquivo.</summary>
    public void SetOverride(string? exePath, EmbedMode mode)
    {
        if (OverrideKey(exePath) is not { } key) return;
        if (mode == EmbedMode.Auto) _embedOverrides.Remove(key);
        else _embedOverrides[key] = mode;
        Save();
    }

    private static string? OverrideKey(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try
        {
            var name = Path.GetFileName(exePath.Trim().Trim('"'));
            return name.Length == 0 ? null : name.ToLowerInvariant();
        }
        catch { return null; }
    }

    public static AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* preferências não podem derrubar o app */ }
    }

    /// <summary>Iniciar com o Windows (só para este usuário, sem precisar de administrador). Inicia direto na bandeja.</summary>
    private const string LegacyRunValue = "AgrupaJanela";

    /// <summary>Se o nome antigo estava configurado para iniciar com o Windows, passa para o novo executável.</summary>
    public static void MigrateLegacyStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyRunValue) is not string) return;
            key.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
            StartWithWindows = true;
        }
        catch { }
    }

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            // Só conta como ligado se apontar para ESTE executável (não para uma cópia antiga/movida).
            return key?.GetValue(RunValue) is string value && value.Contains($"\"{Environment.ProcessPath}\"", StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath ?? "";
            if (value)
            {
                if (exe.StartsWith(@"\\") || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Rode o SplitDeck de uma pasta local para iniciar com o Windows.");
                key.SetValue(RunValue, $"\"{exe}\" --tray");
            }
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
