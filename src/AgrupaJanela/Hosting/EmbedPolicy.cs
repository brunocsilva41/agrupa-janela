using AgrupaJanela.Native;
using AgrupaJanela.Persistence;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgrupaJanela.Hosting;

/// <summary>Como a janela entra no grupo: incorporada (filha do painel) ou acoplada (top-level sobre o painel).</summary>
[JsonConverter(typeof(EmbedModeJsonConverter))]
public enum EmbedMode { Auto, Reparent, Dock }

/// <summary>Decide o modo de agrupamento de cada janela: override do usuário, regras conhecidas, padrão Reparent.</summary>
public static class EmbedPolicy
{
    private const int MaxChildrenScanned = 200; // limite para a busca de filhos não custar caro

    // Prefixos/nomes de classe de janela que exigem acoplar (renderização por GPU ou app que espera ser top-level).
    private static readonly (string Class, bool Prefix, string Reason)[] DockClasses =
    {
        ("CASCADIA_HOSTING_WINDOW_CLASS", false, "Windows Terminal: acoplada porque renderiza com a GPU"),
        ("MozillaWindowClass", false, "Firefox: acoplada para manter a aceleração de vídeo"),
        ("HwndWrapper[", true, "App WPF: acoplada porque renderiza com DirectX"),
        ("ApplicationFrameWindow", false, "App da Microsoft Store (UWP): acoplada porque não aceita ser filha"),
        ("WinUIDesktopWin32WindowClass", false, "App WinUI 3: acoplada porque usa composição por GPU"),
        ("SunAwtFrame", false, "App Java: acoplada porque espera ser janela principal"),
    };

    private static readonly HashSet<string> GpuChildClasses = new(StringComparer.Ordinal)
    {
        "Intermediate D3D Window",
    };

    /// <summary>Decide o modo (nunca retorna Auto).</summary>
    public static EmbedMode Resolve(nint hwnd, uint processId, string? exePath, AppSettings settings) =>
        Decide(hwnd, processId, exePath, settings).Mode;

    /// <summary>Explicação curta e amigável da escolha, para tooltip.</summary>
    public static string Explain(nint hwnd, uint processId, string? exePath, AppSettings settings) =>
        Decide(hwnd, processId, exePath, settings).Reason;

    /// <summary>Texto para UI.</summary>
    public static string Label(EmbedMode mode) => mode switch
    {
        EmbedMode.Reparent => "Incorporada",
        EmbedMode.Dock => "Acoplada",
        _ => "Automático",
    };

    private static (EmbedMode Mode, string Reason) Decide(nint hwnd, uint processId, string? exePath, AppSettings settings)
    {
        exePath ??= processId != 0 ? Win32.GetProcessPath(processId) : null;
        var exeName = SafeFileName(exePath);

        // 1) Escolha salva pelo usuário.
        var chosen = settings?.GetOverride(exePath) ?? EmbedMode.Auto;
        if (chosen != EmbedMode.Auto)
            return (chosen, $"Escolhido por você para {exeName}: {Label(chosen).ToLowerInvariant()}");

        // 2a) Classe da janela.
        var className = hwnd != 0 ? Win32.GetClass(hwnd) : "";
        if (className.Length > 0)
        {
            foreach (var (cls, prefix, reason) in DockClasses)
                if (prefix ? className.StartsWith(cls, StringComparison.Ordinal) : className == cls)
                    return (EmbedMode.Dock, reason);

            if (className.StartsWith("Qt", StringComparison.Ordinal) && className.Contains("QWindow", StringComparison.Ordinal))
                return (EmbedMode.Dock, "App Qt: acoplada por segurança (pode renderizar com a GPU)");
            // Chromium/Electron (Chrome, Edge, Brave, VS Code, Discord, Docker Desktop…) funcionam incorporados — testado.
            if (className.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
                return (EmbedMode.Reparent, "Chromium/Electron: incorporada (se algo falhar, troque para acoplada)");
            if (className == "ConsoleWindowClass")
                return (EmbedMode.Reparent, "Terminal clássico (conhost): incorporada");
            if (className == "PseudoConsoleWindow")
                return (EmbedMode.Reparent, "Console oculto: incorporada");
        }

        // 2b) Processo.
        if (string.Equals(exeName, "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase))
            return (EmbedMode.Dock, "Windows Terminal: acoplada porque renderiza com a GPU");
        if (string.Equals(exeName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
            return (EmbedMode.Reparent, "Explorador de Arquivos: incorporada");

        // 2c) Filhos típicos de DirectComposition/GPU.
        if (hwnd != 0 && FindGpuChild(hwnd) is { } gpuChild)
            return (EmbedMode.Dock, $"Renderiza com a GPU ({gpuChild}): acoplada para não perder a imagem");

        // 3) Padrão.
        return (EmbedMode.Reparent, "Padrão: app clássico, incorporada");
    }

    private static string? FindGpuChild(nint hwnd)
    {
        string? found = null;
        var scanned = 0;
        EnumChildWindows(hwnd, (child, _) =>
        {
            var cls = Win32.GetClass(child);
            if (GpuChildClasses.Contains(cls)) { found = cls; return false; }
            return ++scanned < MaxChildrenScanned;
        }, 0);
        return found;
    }

    private static string SafeFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "este app";
        try { return Path.GetFileName(path.Trim().Trim('"')); }
        catch { return path; }
    }

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, Win32.EnumWindowsProc callback, nint lParam);
}

/// <summary>Salva EmbedMode como texto; valor desconhecido vira Auto em vez de quebrar o carregamento.</summary>
public sealed class EmbedModeJsonConverter : JsonConverter<EmbedMode>
{
    public override EmbedMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return Enum.TryParse<EmbedMode>(reader.GetString(), ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : EmbedMode.Auto;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined((EmbedMode)number))
            return (EmbedMode)number;
        reader.Skip();
        return EmbedMode.Auto;
    }

    public override void Write(Utf8JsonWriter writer, EmbedMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
