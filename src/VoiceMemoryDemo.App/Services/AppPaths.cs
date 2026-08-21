namespace VoiceMemoryDemo.App.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceMemoryDemo");

    public static string DatabasePath => Path.Combine(DataDirectory, "voice-memory.db");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.dat");
    public static string DiagnosticsPath => Path.Combine(DataDirectory, "diagnostics.log");
    public static string SpeakerRouterDiagnosticsPath => Path.Combine(DataDirectory, "speaker-router.jsonl");
    public static string MeetingsDirectory => Path.Combine(DataDirectory, "Meetings");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}
