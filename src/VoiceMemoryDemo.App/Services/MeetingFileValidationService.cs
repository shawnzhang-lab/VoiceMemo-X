using System.Diagnostics;
using System.Text;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class MeetingFileValidationService
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(40);
    private const int FrameBytes = 16_000 * 2 * 40 / 1000;

    private readonly MeetingMinutesService _minutesService = new();

    public async Task<MeetingFileValidationResult> RunAsync(
        string mediaPath,
        AppSettings settings,
        string reportDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("找不到测试媒体文件。", mediaPath);
        if (!settings.HasTencentCredentials) throw new InvalidOperationException("测试需要腾讯 ASR 配置。 ");
        if (!settings.HasDeepSeekCredentials) throw new InvalidOperationException("测试需要 DeepSeek API Key。 ");

        Directory.CreateDirectory(reportDirectory);
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var meetingSettings = CloneForMeeting(settings);
        var asr = new TencentAsrSession();
        var meetingSession = new MeetingMinutesSession(
            _minutesService,
            meetingSettings,
            progress,
            cancellationToken);
        var settledSegments = new SortedDictionary<int, string>();
        asr.SegmentSettled += (index, text) =>
        {
            lock (settledSegments) settledSegments[index] = text;
            meetingSession.AddSettledSegment(text);
        };

        progress?.Report("正在连接腾讯实时说话人分离 V2…");
        await asr.StartAsync(
            meetingSettings,
            cancellationToken,
            enableSpeakerDiarization: true);
        var ffmpeg = StartFfmpeg(mediaPath);
        var pcm = ffmpeg.StandardOutput.BaseStream;
        var frame = new byte[FrameBytes];
        long audioBytes = 0;
        var nextProgress = TimeSpan.FromMinutes(1);

        try
        {
            while (true)
            {
                var count = await ReadFrameAsync(pcm, frame, cancellationToken);
                if (count == 0) break;
                audioBytes += count;
                if (!asr.QueueAudio(count == frame.Length ? frame.ToArray() : frame[..count]))
                {
                    throw new InvalidOperationException("腾讯 ASR 音频队列已关闭。 ");
                }

                var audioDuration = TimeSpan.FromSeconds(audioBytes / (16_000d * 2));
                if (audioDuration >= nextProgress)
                {
                    progress?.Report($"已发送 {MeetingMinutesService.FormatDuration(audioDuration)} 音频…");
                    nextProgress += TimeSpan.FromMinutes(1);
                }
                await Task.Delay(FrameDuration, cancellationToken);
            }

            await ffmpeg.WaitForExitAsync(cancellationToken);
            var ffmpegError = await ffmpeg.StandardError.ReadToEndAsync(cancellationToken);
            if (ffmpeg.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg 读取媒体失败：{ffmpegError.Trim()}");
            }

            progress?.Report("音频发送完毕，正在等待最终转写…");
            var transcript = await asr.CompleteAsync(cancellationToken);
            var audioDurationFinal = TimeSpan.FromSeconds(audioBytes / (16_000d * 2));

            var minutesResult = await meetingSession.CompleteAsync(
                transcript,
                audioDurationFinal,
                cancellationToken);
            var minutes = minutesResult.Minutes;
            stopwatch.Stop();

            var prefix = $"{startedAt:yyyyMMdd-HHmmss}-{SafeName(Path.GetFileNameWithoutExtension(mediaPath))}";
            var transcriptPath = Path.Combine(reportDirectory, prefix + "-转写.txt");
            var minutesPath = Path.Combine(reportDirectory, prefix + "-纪要.md");
            var reportPath = Path.Combine(reportDirectory, prefix + "-报告.md");
            await File.WriteAllTextAsync(transcriptPath, transcript, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(minutesPath, minutes, new UTF8Encoding(false), cancellationToken);
            var report = BuildReport(
                mediaPath,
                audioDurationFinal,
                stopwatch.Elapsed,
                transcript,
                settledSegments.Count,
                minutesResult.SegmentCount,
                transcriptPath,
                minutesPath);
            await File.WriteAllTextAsync(reportPath, report, new UTF8Encoding(false), cancellationToken);

            return new MeetingFileValidationResult(
                mediaPath,
                audioDurationFinal,
                stopwatch.Elapsed,
                transcript.Length,
                settledSegments.Count,
                minutesResult.SegmentCount,
                transcriptPath,
                minutesPath,
                reportPath);
        }
        finally
        {
            if (!ffmpeg.HasExited)
            {
                try { ffmpeg.Kill(entireProcessTree: true); } catch { }
            }
            await asr.DisposeAsync();
            await meetingSession.DisposeAsync();
            ffmpeg.Dispose();
        }
    }

    private static AppSettings CloneForMeeting(AppSettings settings) => new()
    {
        TencentAppId = settings.TencentAppId,
        TencentSecretId = settings.TencentSecretId,
        TencentSecretKey = settings.TencentSecretKey,
        TencentEngineModel = settings.TencentEngineModel,
        UiLanguage = settings.UiLanguage,
        DeepSeekApiKey = settings.DeepSeekApiKey,
        DeepSeekBaseUrl = settings.DeepSeekBaseUrl,
        DeepSeekModel = settings.DeepSeekModel,
        MeetingSummaryTemplate = settings.MeetingSummaryTemplate,
        MeetingSummaryTemplateEnglish = settings.MeetingSummaryTemplateEnglish,
        EnableAiRefinement = true,
        EnableMeetingMode = true,
        SaveHistory = false
    };

    private static Process StartFfmpeg(string mediaPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(mediaPath);
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("16000");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("s16le");
        startInfo.ArgumentList.Add("pipe:1");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 FFmpeg。 ");
    }

    private static async Task<int> ReadFrameAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    internal static IReadOnlyList<string> SplitTranscript(string transcript, int targetCharacters = 1800)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return [];
        var chunks = new List<string>();
        var start = 0;
        while (start < transcript.Length)
        {
            var length = Math.Min(targetCharacters, transcript.Length - start);
            if (start + length < transcript.Length)
            {
                var searchStart = Math.Max(start, start + length - 300);
                var split = transcript.LastIndexOfAny(['。', '！', '？', '\n'], start + length - 1, start + length - searchStart);
                if (split >= searchStart) length = split - start + 1;
            }
            chunks.Add(transcript.Substring(start, length).Trim());
            start += length;
        }
        return chunks.Where(item => item.Length > 0).ToArray();
    }

    private static string BuildReport(
        string mediaPath,
        TimeSpan audioDuration,
        TimeSpan wallTime,
        string transcript,
        int settledSegments,
        int summaryCount,
        string transcriptPath,
        string minutesPath) => $"""
        # 会议文件验证报告

        - 媒体：{Path.GetFileName(mediaPath)}
        - 音频时长：{audioDuration:c}
        - 实际耗时：{wallTime:c}
        - 转写字符数：{transcript.Length}
        - 腾讯稳态分段数：{settledSegments}
        - Flash 阶段摘要数：{summaryCount}
        - 转写文件：{transcriptPath}
        - 纪要文件：{minutesPath}
        """;

    private static string SafeName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return value.Length <= 48 ? value : value[..48];
    }
}

public sealed record MeetingFileValidationResult(
    string MediaPath,
    TimeSpan AudioDuration,
    TimeSpan WallTime,
    int TranscriptCharacters,
    int SettledSegments,
    int SummaryCount,
    string TranscriptPath,
    string MinutesPath,
    string ReportPath);
