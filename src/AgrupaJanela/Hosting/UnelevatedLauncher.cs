using AgrupaJanela.Native;
using System.Runtime.InteropServices;
using System.Text;

namespace AgrupaJanela.Hosting;

/// <summary>
/// SplitDeck rodando como administrador abre os apps salvos como USUÁRIO COMUM, com a identidade do Explorer
/// (o processo da área de trabalho). Assim o groups.json — que qualquer programa do usuário pode editar — nunca vira
/// um jeito de rodar algo elevado sem o aviso do UAC: o app abre exatamente como se fosse aberto pelo Menu Iniciar.
/// Um app que exija administrador mostra o próprio aviso do UAC, como faria normalmente.
/// </summary>
internal static class UnelevatedLauncher
{
    /// <summary>Abre o executável com o token do Explorer. False se não houver Explorer comum (não elevado) para usar.</summary>
    public static bool Start(string exe, string arguments, string directory, out int pid)
    {
        pid = 0;
        if (ShellToken() is not { } token) return false;
        nint environment = 0;
        try
        {
            if (!CreateEnvironmentBlock(out environment, token, false)) environment = 0;
            var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            // CreateProcessWithTokenW pode alterar a linha de comando: precisa ser um buffer gravável.
            var commandLine = new StringBuilder("\"" + exe + "\"" + (arguments.Length > 0 ? " " + arguments : ""), 32768);
            if (!CreateProcessWithTokenW(token, 0, null, commandLine, environment != 0 ? CREATE_UNICODE_ENVIRONMENT : 0,
                    environment, directory, ref startup, out var info)) return false;
            pid = info.dwProcessId;
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            return true;
        }
        finally
        {
            if (environment != 0) DestroyEnvironmentBlock(environment);
            CloseHandle(token);
        }
    }

    /// <summary>Token primário duplicado do Explorer, só se ele NÃO estiver elevado (senão não serviria para nada).</summary>
    private static nint? ShellToken()
    {
        var shell = GetShellWindow();
        if (shell == 0) return null;
        Win32.GetWindowThreadProcessId(shell, out var pid);
        if (pid == 0 || Win32.IsProcessElevated(pid)) return null;

        var process = Win32.OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_DUPLICATE, out var token)) return null;
            try
            {
                const uint access = TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID;
                return DuplicateTokenEx(token, access, 0, SecurityImpersonation, TokenPrimary, out var primary) ? primary : null;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private const uint PROCESS_QUERY_INFORMATION = 0x400;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x1, TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8, TOKEN_ADJUST_DEFAULT = 0x80, TOKEN_ADJUST_SESSIONID = 0x100;
    private const int SecurityImpersonation = 2, TokenPrimary = 1;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(nint token, uint access, nint attributes, int level, int type, out nint newToken);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out nint environment, nint token, bool inherit);
    [DllImport("userenv.dll")] private static extern bool DestroyEnvironmentBlock(nint environment);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessWithTokenW(nint token, uint logonFlags, string? application, StringBuilder commandLine,
        uint creationFlags, nint environment, string directory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);
}
