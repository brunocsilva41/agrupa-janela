using System.Runtime.InteropServices;

namespace AgrupaJanela.Native;

internal static class Win32
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;

    public const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    public const long WS_CLIPCHILDREN = 0x02000000, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    public const long WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_WINDOWEDGE = 0x100;
    public const long WS_EX_CLIENTEDGE = 0x200, WS_EX_DLGMODALFRAME = 0x1;
    public const uint SS_NOTIFY = 0x100;

    public const int SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public const uint SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_ASYNCWINDOWPOS = 0x4000;

    public const int WM_SIZE = 0x0005, WM_CLOSE = 0x0010, WM_HOTKEY = 0x0312;
    public const uint GW_OWNER = 4, GA_ROOT = 2;
    public const int DWMWA_CLOAKED = 14;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x8;
    public const int TokenElevation = 20;
    public const int DPI_HOSTING_BEHAVIOR_MIXED = 1;

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint hwnd, char[] text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hwnd, char[] text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern int SetThreadDpiHostingBehavior(int value);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll")] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll")] public static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] public static extern bool GetTokenInformation(nint token, int infoClass, out int value, int length, out int returnLength);

    // ---- Tema, overlay, cursor ----
    public const int WM_PARENTNOTIFY = 0x0210, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    public const long WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    public static readonly nint HWND_TOPMOST = -1;
    public const int VK_SHIFT = 0x10;

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(nint hwnd, nint rect, nint region, uint flags);
    public const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_FRAME = 0x0400;

    public const int GWLP_HWNDPARENT = -8;

    /// <summary>Dono (owner) de uma janela top-level; aceita janela de outro processo.</summary>
    public static void SetOwner(nint hwnd, nint owner) => SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, owner);

    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(nint process, nint min, nint max);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    public static void TrimWorkingSet() => SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);

    public static bool IsShiftDown() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    public static void UseDarkTitleBar(nint hwnd)
    {
        var on = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
    }

    // ---- Eventos de mover/redimensionar janelas de outros processos ----
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);

    // ---- Informações de processo (caminho do exe e linha de comando) ----
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(nint process, int flags, char[] name, ref int size);
    [DllImport("ntdll.dll")] public static extern int NtQueryInformationProcess(nint process, int infoClass, nint buffer, int length, out int returnLength);
    private const int ProcessCommandLineInformation = 60;

    public static string? GetProcessPath(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally { CloseHandle(process); }
    }

    public static string? GetProcessCommandLine(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            NtQueryInformationProcess(process, ProcessCommandLineInformation, 0, 0, out var needed);
            if (needed <= 0) return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, needed, out _) != 0) return null;
                var length = (ushort)Marshal.ReadInt16(buffer);   // UNICODE_STRING.Length (bytes)
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size); // UNICODE_STRING.Buffer
                return text == 0 ? null : Marshal.PtrToStringUni(text, length / 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(process); }
    }

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int Length, Flags, ShowCmd;
        public POINT MinPosition, MaxPosition;
        public RECT NormalPosition;
        public static WINDOWPLACEMENT Create() => new() { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
    }

    public static string GetText(nint hwnd)
    {
        var buffer = new char[512];
        return new string(buffer, 0, GetWindowText(hwnd, buffer, buffer.Length)).Trim();
    }

    public static string GetClass(nint hwnd)
    {
        var buffer = new char[256];
        return new string(buffer, 0, GetClassName(hwnd, buffer, buffer.Length));
    }

    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>Retorna true se o processo roda elevado. Se não der para ler o token, assume elevado (fail-closed).</summary>
    public static bool IsProcessElevated(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return true;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return true;
            try { return !GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) || elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
}
