using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiceMemoryDemo.App.Services;

public sealed record ForegroundTarget(IntPtr Handle, uint ProcessId, string ProcessName, string WindowTitle);

public static class ForegroundWindowService
{
    public static ForegroundTarget Capture()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return new ForegroundTarget(IntPtr.Zero, 0, "未知应用", string.Empty);

        GetWindowThreadProcessId(handle, out var processId);
        var processName = "未知应用";
        try { processName = Process.GetProcessById((int)processId).ProcessName; } catch { }

        var titleLength = GetWindowTextLength(handle);
        var title = string.Empty;
        if (titleLength > 0)
        {
            var buffer = new StringBuilder(titleLength + 1);
            GetWindowText(handle, buffer, buffer.Capacity);
            title = buffer.ToString();
        }

        return new ForegroundTarget(handle, processId, processName, title);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);
}
