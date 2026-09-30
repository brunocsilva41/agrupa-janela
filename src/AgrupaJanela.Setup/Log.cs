using System;
using System.IO;
using System.Text;

namespace AgrupaJanela.Setup;

/// <summary>Log em %TEMP%\AgrupaJanela-Setup.log (acrescenta; nunca derruba o instalador).</summary>
internal static class Log
{
    public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "AgrupaJanela-Setup.log");
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("AVISO", message);
    public static void Error(string message, Exception? ex = null) => Write("ERRO", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}\r\n", Encoding.UTF8);
        }
        catch
        {
            // Sem log não é motivo para falhar a instalação.
        }
    }
}
