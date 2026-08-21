using System.Text;

namespace VoiceMemoryDemo.App.Services;

public static class DiagnosticLogService
{
    private static readonly object Gate = new();
    private const long MaxLogBytes = 1_000_000;

    public static void Write(string stage, Exception exception)
    {
        WriteEntry(stage, exception.ToString());
    }

    public static void WriteEvent(string stage, string message)
    {
        WriteEntry(stage, message);
    }

    private static void WriteEntry(string stage, string details)
    {
        try
        {
            AppPaths.EnsureCreated();
            lock (Gate)
            {
                RotateIfNeeded();
                var entry = $"[{DateTimeOffset.Now:O}] {stage}{Environment.NewLine}" +
                            $"{details}{Environment.NewLine}" +
                            new string('-', 72) + Environment.NewLine;
                File.AppendAllText(AppPaths.DiagnosticsPath, entry, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never interrupt voice input.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(AppPaths.DiagnosticsPath);
        if (!file.Exists || file.Length < MaxLogBytes) return;

        var previous = AppPaths.DiagnosticsPath + ".old";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(AppPaths.DiagnosticsPath, previous);
    }
}
