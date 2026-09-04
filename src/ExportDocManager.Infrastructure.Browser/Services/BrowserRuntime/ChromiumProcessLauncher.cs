using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ExportDocManager.Services.BrowserRuntime;

internal static class ChromiumProcessLauncher
{
    private const uint ErrorMode = 0x0001 | 0x0002 | 0x8000;
    private static readonly object ErrorModeGate = new();
    internal static bool Start(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows()) return process.Start();
        lock (ErrorModeGate)
        {
            uint previousProcessMode = SetErrorMode(ErrorMode);
            bool restoreThreadMode = SetThreadErrorMode(ErrorMode, out uint previousThreadMode);
            try { return process.Start(); }
            finally
            {
                if (restoreThreadMode) SetThreadErrorMode(previousThreadMode, out _);
                SetErrorMode(previousProcessMode);
            }
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetErrorMode(uint newMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);
}
