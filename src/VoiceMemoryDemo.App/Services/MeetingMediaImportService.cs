using NAudio.Wave;
using System.Net.Http;
using System.Net.WebSockets;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class MeetingMediaImportService
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(40);
    private static readonly WaveFormat TencentWaveFormat = new(16_000, 16, 1);
    private const int FrameBytes = 16_000 * 2 * 40 / 1000;

    private readonly MeetingMinutesService _minutesService = new();
    private readonly LocalSpeakerRouterService _speakerRouter = new();

    public static string FileDialogFilter =>
        "支持的音频和视频|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.mp4;*.mov;*.m4v|" +
        "音频文件|*.mp3;*.wav;*.m4a;*.aac;*.wma|" +
        "视频文件|*.mp4;*.mov;*.m4v|" +
        "所有文件|*.*";

    public async Task<MeetingMediaImportResult> ImportAsync(
        string mediaPath,
        AppSettings settings,
        IProgress<MeetingMediaImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("找不到要导入的音视频文件。", mediaPath);
        if (!settings.HasTencentCredentials) throw new InvalidOperationException("请先在“连接设置”中配置腾讯云 ASR。");
        if (!settings.HasDeepSeekCredentials) throw new InvalidOperationException("导入文件生成会议纪要需要 DeepSeek API Key。");

        var startedAt = DateTimeOffset.Now;
        var importSettings = CloneForMeeting(settings);
        TencentAsrSession? asr = null;
        var meetingSession = new MeetingMinutesSession(
            _minutesService,
            importSettings,
            new Progress<string>(message => progress?.Report(
                new MeetingMediaImportProgress(MeetingMediaImportStage.Summarizing, message, 0, null))),
            cancellationToken);

        Action<string> transcriptChanged = text => progress?.Report(
            new MeetingMediaImportProgress(MeetingMediaImportStage.Transcribing, "正在转写文件…", double.NaN, text));
        Action<int, string> segmentSettled = (_, text) => meetingSession.AddSettledSegment(text);

        MediaFoundationReader? reader = null;
        MediaFoundationResampler? resampler = null;
        try
        {
            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.Opening,
                "正在读取音视频文件…",
                0,
                null));
            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.AnalyzingSpeakers,
                "正在本地判断说话人数（影子验证，不消耗云端额度）…",
                0,
                null));
            var routing = await _speakerRouter.AnalyzeAsync(mediaPath, cancellationToken);
            var routeDescription = routing.Label switch
            {
                LocalSpeakerLabel.Single => "本地判断为单人；影子期仍使用 Speaker 2.0",
                LocalSpeakerLabel.Multi => "本地发现多人声纹特征；安全使用 Speaker 2.0",
                LocalSpeakerLabel.NoSpeech => "本地未发现可靠语音；已停止，不消耗云端额度",
                _ => "本地判断不确定；安全使用 Speaker 2.0"
            };
            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.AnalyzingSpeakers,
                $"{routeDescription} · 用时 {routing.AnalysisElapsed.TotalSeconds:F1} 秒",
                1,
                null));
            if (routing.ExecutionRoute == MeetingAsrRoute.BlockOrReview)
            {
                throw new InvalidOperationException("文件中没有检测到可靠语音，已停止处理，不会消耗腾讯云额度。");
            }
            try
            {
                reader = new MediaFoundationReader(mediaPath);
                resampler = new MediaFoundationResampler(reader, TencentWaveFormat)
                {
                    ResamplerQuality = 60
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Windows 无法解码这个文件。请先转换为 MP3、WAV、M4A 或 MP4 后再导入。",
                    ex);
            }

            var estimatedDuration = reader.TotalTime;
            var useSpeaker20 = routing.ExecutionRoute == MeetingAsrRoute.Speaker20;
            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.Connecting,
                useSpeaker20 ? "正在连接腾讯 Speaker 2.0…" : "正在连接腾讯普通 ASR…",
                0,
                null));
            asr = await ConnectWithRetryAsync(
                importSettings,
                transcriptChanged,
                segmentSettled,
                progress,
                useSpeaker20,
                cancellationToken);

            var frame = new byte[FrameBytes];
            long audioBytes = 0;
            var nextProgressUpdate = TimeSpan.Zero;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = ReadFrame(resampler, frame);
                if (count == 0) break;

                audioBytes += count;
                var packet = count == frame.Length ? frame.ToArray() : frame[..count];
                if (!asr.QueueAudio(packet))
                {
                    throw new InvalidOperationException("腾讯 ASR 音频队列已关闭。");
                }

                var processed = TimeSpan.FromSeconds(audioBytes / (16_000d * 2));
                var ratio = estimatedDuration.TotalSeconds <= 0
                    ? 0
                    : Math.Clamp(processed.TotalSeconds / estimatedDuration.TotalSeconds, 0, 1);
                if (processed >= nextProgressUpdate)
                {
                    progress?.Report(new MeetingMediaImportProgress(
                        MeetingMediaImportStage.Transcribing,
                        $"正在转写 {MeetingMinutesService.FormatElapsed(processed)} / {MeetingMinutesService.FormatElapsed(estimatedDuration)}",
                        ratio,
                        null));
                    nextProgressUpdate = processed + TimeSpan.FromMilliseconds(500);
                }

                await Task.Delay(FrameDuration, cancellationToken);
            }

            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.FinalizingTranscript,
                "文件已读完，正在等待最终转写…",
                1,
                null));
            var transcript = await asr.CompleteAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(transcript))
            {
                throw new InvalidOperationException("文件中没有识别到有效语音。");
            }

            var duration = TimeSpan.FromSeconds(audioBytes / (16_000d * 2));
            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.Summarizing,
                "Flash 阶段摘要已完成，Thinking 正在生成最终纪要…",
                1,
                transcript));
            var minutesResult = await meetingSession.CompleteAsync(transcript, duration, cancellationToken);

            progress?.Report(new MeetingMediaImportProgress(
                MeetingMediaImportStage.Saving,
                "正在保存会议纪要…",
                1,
                transcript));
            var archivePath = await MeetingArchiveService.SaveImportedAsync(
                minutesResult.Minutes,
                transcript,
                startedAt,
                duration,
                mediaPath,
                routing,
                importSettings.UiLanguage,
                cancellationToken);

            return new MeetingMediaImportResult(
                mediaPath,
                duration,
                transcript,
                minutesResult.Minutes,
                minutesResult.SegmentCount,
                archivePath,
                routing);
        }
        finally
        {
            resampler?.Dispose();
            reader?.Dispose();
            if (asr is not null) await asr.DisposeAsync();
            await meetingSession.DisposeAsync();
        }
    }

    private static async Task<TencentAsrSession> ConnectWithRetryAsync(
        AppSettings settings,
        Action<string> transcriptChanged,
        Action<int, string> segmentSettled,
        IProgress<MeetingMediaImportProgress>? progress,
        bool enableSpeakerDiarization,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new TencentAsrSession();
            candidate.TranscriptChanged += transcriptChanged;
            candidate.SegmentSettled += segmentSettled;
            try
            {
                await candidate.StartAsync(
                    settings,
                    cancellationToken,
                    enableSpeakerDiarization);
                return candidate;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientConnectionFailure(ex))
            {
                await candidate.DisposeAsync();
                progress?.Report(new MeetingMediaImportProgress(
                    MeetingMediaImportStage.Connecting,
                    $"连接暂时失败，正在重试（{attempt + 1}/{maxAttempts}）…",
                    0,
                    null));
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
            catch
            {
                await candidate.DisposeAsync();
                throw;
            }
        }

        throw new InvalidOperationException("腾讯实时语音连接失败，请稍后重试。 ");
    }

    private static bool IsTransientConnectionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is WebSocketException or HttpRequestException or IOException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadFrame(IWaveProvider source, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = source.Read(buffer, total, buffer.Length - total);
            if (read == 0) break;
            total += read;
        }
        return total;
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
        SaveHistory = settings.SaveHistory
    };
}

public enum MeetingMediaImportStage
{
    Opening,
    AnalyzingSpeakers,
    Connecting,
    Transcribing,
    FinalizingTranscript,
    Summarizing,
    Saving
}

public sealed record MeetingMediaImportProgress(
    MeetingMediaImportStage Stage,
    string Message,
    double Ratio,
    string? Transcript);

public sealed record MeetingMediaImportResult(
    string MediaPath,
    TimeSpan Duration,
    string Transcript,
    string Minutes,
    int SegmentCount,
    string ArchivePath,
    SpeakerRoutingResult Routing);
