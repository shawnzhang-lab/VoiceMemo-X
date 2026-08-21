using VoiceMemoryDemo.App.Services;
using VoiceMemoryDemo.App.Models;

if (args.Length >= 4 && string.Equals(args[0], "--resummarize", StringComparison.OrdinalIgnoreCase))
{
    var sourceReportPath = Path.GetFullPath(args[1]);
    var sourceMediaPath = Path.GetFullPath(args[2]);
    if (!double.TryParse(args[3], System.Globalization.CultureInfo.InvariantCulture, out var durationSeconds) ||
        durationSeconds <= 0)
    {
        Console.Error.WriteLine("--resummarize 的时长必须是正数秒数。");
        return 2;
    }

    var markdown = await File.ReadAllTextAsync(sourceReportPath);
    const string transcriptMarker = "## 原始转写附录";
    var markerIndex = markdown.IndexOf(transcriptMarker, StringComparison.Ordinal);
    if (markerIndex < 0)
    {
        Console.Error.WriteLine("源报告缺少原始转写附录。");
        return 2;
    }

    var transcript = markdown[(markerIndex + transcriptMarker.Length)..].Trim();
    var resummarizeSettings = new SecureSettingsStore().Load();
    var duration = TimeSpan.FromSeconds(durationSeconds);
    var minutes = await new MeetingMinutesService().CreateFinalMinutesAsync(
        transcript,
        [],
        duration,
        resummarizeSettings,
        CancellationToken.None);
    var archivePath = await MeetingArchiveService.SaveImportedAsync(
        minutes,
        transcript,
        DateTimeOffset.Now,
        duration,
        sourceMediaPath,
        new SpeakerRoutingResult(
            LocalSpeakerLabel.Uncertain,
            MeetingAsrRoute.Speaker20,
            MeetingAsrRoute.Speaker20,
            0,
            0,
            duration,
            duration,
            TimeSpan.Zero,
            "Manual resummarization",
            false,
            []),
        resummarizeSettings.UiLanguage,
        CancellationToken.None);
    Console.WriteLine($"PASS: resummarized transcript={transcript.Length}; duration={duration:c}");
    Console.WriteLine($"ARCHIVE: {archivePath}");
    return 0;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("用法：VoiceMemoryDemo.MediaImportSmoke <音视频文件> | --resummarize <报告> <媒体> <秒数>");
    return 2;
}

var mediaPath = Path.GetFullPath(args[0]);
var settings = new SecureSettingsStore().Load();
using var cancellation = new CancellationTokenSource();
var cancelIndex = Array.FindIndex(args, value =>
    string.Equals(value, "--cancel-after", StringComparison.OrdinalIgnoreCase));
if (cancelIndex >= 0 && cancelIndex + 1 < args.Length &&
    int.TryParse(args[cancelIndex + 1], out var cancelSeconds) && cancelSeconds > 0)
{
    cancellation.CancelAfter(TimeSpan.FromSeconds(cancelSeconds));
}
var progress = new Progress<MeetingMediaImportProgress>(item =>
{
    if (!string.IsNullOrWhiteSpace(item.Transcript)) return;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {item.Stage}: {item.Message}");
});

try
{
    var result = await new MeetingMediaImportService().ImportAsync(mediaPath, settings, progress, cancellation.Token);
    Console.WriteLine($"PASS: duration={result.Duration:c}, transcript={result.Transcript.Length}, segments={result.SegmentCount}");
    Console.WriteLine($"ARCHIVE: {result.ArchivePath}");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("PASS: import cancellation completed cleanly.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    return 1;
}
