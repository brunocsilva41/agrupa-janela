using System;
using System.IO;
using Microsoft.Win32;

namespace AgrupaJanela.Setup;

/// <summary>Valor "AgrupaJanela" na chave Run (mesmo formato que o app grava: "\"...\AgrupaJanela.exe\" --tray").</summary>
internal static class RunEntry
{
    public static string Command(string installDir) => $"\"{Path.Combine(installDir, SetupContext.AppExeName)}\" --tray";

    public static bool PointsTo(SetupContext ctx, string installDir)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ctx.RunKeyPath);
        if (key?.GetValue(SetupContext.RunValueName) is not string value) return false;
        var exe = ExtractExe(value);
        return exe != null && SamePath(exe, Path.Combine(installDir, SetupContext.AppExeName));
    }

    public static void Set(SetupContext ctx, string installDir)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ctx.RunKeyPath, true)!;
        key.SetValue(SetupContext.RunValueName, Command(installDir));
        Log.Info($"Início com o Windows ligado ({ctx.RunKeyPath}).");
    }

    /// <summary>Remove o valor só se ele aponta para esta instalação.</summary>
    public static void RemoveIfOurs(SetupContext ctx, string installDir)
    {
        if (!PointsTo(ctx, installDir)) return;
        using var key = Registry.CurrentUser.OpenSubKey(ctx.RunKeyPath, true);
        key?.DeleteValue(SetupContext.RunValueName, false);
        Log.Info($"Início com o Windows removido ({ctx.RunKeyPath}).");
    }

    private static string? ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command.Substring(1, end - 1) : null;
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command.Substring(0, space) : command;
    }

    internal static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(PathRules.ToLongPath(a).TrimEnd('\\'), PathRules.ToLongPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal static class Shortcuts
{
    public static bool PointsInto(string lnkPath, string installDir)
    {
        var target = ShellLink.ReadTarget(lnkPath);
        return target != null && PathRules.IsSameOrInside(PathRules.ToLongPath(target), PathRules.ToLongPath(installDir));
    }

    public static void RemoveIfOurs(string lnkPath, string installDir)
    {
        if (!File.Exists(lnkPath)) return;
        if (!PointsInto(lnkPath, installDir))
        {
            Log.Warn($"Atalho {lnkPath} aponta para outro lugar; mantido.");
            return;
        }
        File.Delete(lnkPath);
        Log.Info($"Atalho removido: {lnkPath}");
    }
}

/// <summary>Instalação já registrada (lida da chave de desinstalação).</summary>
internal sealed class ExistingInstall
{
    public string Location { get; private set; } = "";
    public string? Version { get; private set; }
    public bool DesktopShortcut { get; private set; }
    public bool StartWithWindows { get; private set; }

    public static ExistingInstall? Read(SetupContext ctx)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ctx.UninstallKeyPath);
            if (key?.GetValue("InstallLocation") is not string location || location.Trim().Length == 0) return null;
            location = location.Trim().TrimEnd('\\');
            return new ExistingInstall
            {
                Location = location,
                Version = key.GetValue("DisplayVersion") as string,
                DesktopShortcut = Shortcuts.PointsInto(ctx.DesktopShortcut, location),
                StartWithWindows = RunEntry.PointsTo(ctx, location),
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Leitura da instalação existente falhou: {ex.Message}");
            return null;
        }
    }
}
