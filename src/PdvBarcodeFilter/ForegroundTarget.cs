using System.Diagnostics;

namespace PdvBarcodeFilter;

internal static class ForegroundTarget
{
    internal static string? GetProcessName()
    {
        try
        {
            var window = NativeMethods.GetForegroundWindow();
            if (window == 0)
            {
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            return processId == 0 ? null : Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
