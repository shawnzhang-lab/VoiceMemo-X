using System.Text;
using System.Text.Json;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public static class SpeakerRouterLogService
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private const long MaxLogBytes = 2_000_000;

    public static void Write(string mediaPath, SpeakerRoutingResult result)
    {
        try
        {
            AppPaths.EnsureCreated();
            var info = new FileInfo(mediaPath);
            var entry = new
            {
                timestamp = DateTimeOffset.Now,
                sample = info.Name,
                fingerprint = $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}",
                label = result.Label.ToString(),
                recommendedRoute = result.RecommendedRoute.ToString(),
                executionRoute = result.ExecutionRoute.ToString(),
                result.Confidence,
                result.DetectedSpeakerCount,
                audioSeconds = result.AudioDuration.TotalSeconds,
                speechSeconds = result.SpeechDuration.TotalSeconds,
                elapsedMilliseconds = result.AnalysisElapsed.TotalMilliseconds,
                result.Reason,
                result.ShadowMode,
                segmentCount = result.Segments.Count
            };
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            lock (Gate)
            {
                RotateIfNeeded();
                File.AppendAllText(AppPaths.SpeakerRouterDiagnosticsPath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Routing diagnostics must never stop meeting transcription.
        }
    }

    private static void RotateIfNeeded()
    {
        var current = new FileInfo(AppPaths.SpeakerRouterDiagnosticsPath);
        if (!current.Exists || current.Length < MaxLogBytes) return;

        var previous = AppPaths.SpeakerRouterDiagnosticsPath + ".old";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(AppPaths.SpeakerRouterDiagnosticsPath, previous);
    }
}
