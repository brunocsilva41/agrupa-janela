using System.Runtime.InteropServices;

// P/Invoke deste assembly procura DLLs só no System32 (nunca na pasta do instalador nem no diretório atual).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
