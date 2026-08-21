using System.Text;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public static class MeetingArchiveService
{
    public static async Task<string> SaveAsync(
        string minutes,
        string rawTranscript,
        DateTimeOffset startedAt,
        TimeSpan duration,
        string? uiLanguage = null,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        Directory.CreateDirectory(AppPaths.MeetingsDirectory);
        var english = MeetingSummaryTemplateDefaults.IsEnglish(uiLanguage);
        var fileName = english
            ? $"{startedAt:yyyyMMdd-HHmmss}-meeting-report.md"
            : $"{startedAt:yyyyMMdd-HHmmss}-会议纪要.md";
        var path = Path.Combine(AppPaths.MeetingsDirectory, fileName);
        var document = english
            ? new StringBuilder()
                .AppendLine(minutes.Trim())
                .AppendLine()
                .AppendLine("---")
                .AppendLine()
                .AppendLine("## Recording details")
                .AppendLine()
                .AppendLine($"- Start time: {startedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"- Duration: {MeetingMinutesService.FormatEnglishDuration(duration)}")
                .AppendLine("- Generated with: Tencent Realtime Speaker Diarization V2 + DeepSeek V4 Flash segment processing + Thinking High final summary")
                .AppendLine()
                .AppendLine("## Full transcript appendix")
                .AppendLine()
                .AppendLine(rawTranscript.Trim())
                .ToString()
            : new StringBuilder()
                .AppendLine(minutes.Trim())
                .AppendLine()
                .AppendLine("---")
                .AppendLine()
                .AppendLine("## 记录信息")
                .AppendLine()
                .AppendLine($"- 开始时间：{startedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"- 记录时长：{MeetingMinutesService.FormatDuration(duration)}")
                .AppendLine("- 生成方式：腾讯实时说话人分离 V2 + DeepSeek V4 Flash 分段整理 + Thinking High 最终总结")
                .AppendLine()
                .AppendLine("## 原始转写附录")
                .AppendLine()
                .AppendLine(rawTranscript.Trim())
                .ToString();
        await File.WriteAllTextAsync(path, document, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static async Task<string> SaveImportedAsync(
        string minutes,
        string rawTranscript,
        DateTimeOffset startedAt,
        TimeSpan duration,
        string sourcePath,
        SpeakerRoutingResult routing,
        string? uiLanguage = null,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        Directory.CreateDirectory(AppPaths.MeetingsDirectory);
        var sourceName = SafeName(Path.GetFileNameWithoutExtension(sourcePath));
        var english = MeetingSummaryTemplateDefaults.IsEnglish(uiLanguage);
        var fileName = english
            ? $"{startedAt:yyyyMMdd-HHmmss}-{sourceName}-meeting-report.md"
            : $"{startedAt:yyyyMMdd-HHmmss}-{sourceName}-会议纪要.md";
        var path = Path.Combine(AppPaths.MeetingsDirectory, fileName);
        var generation = routing.UsesSpeaker20
            ? (english ? "Tencent Speaker 2.0 diarization" : "腾讯 Speaker 2.0 说话人分离")
            : (english ? "Tencent standard realtime ASR" : "腾讯普通实时 ASR");
        var localLabel = FormatSpeakerLabel(routing.Label, english);
        var recommendedRoute = FormatRoute(routing.RecommendedRoute, english);
        var executionRoute = FormatRoute(routing.ExecutionRoute, english);
        var document = english
            ? new StringBuilder()
                .AppendLine(minutes.Trim())
                .AppendLine()
                .AppendLine("---")
                .AppendLine()
                .AppendLine("## Recording details")
                .AppendLine()
                .AppendLine($"- Imported at: {startedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"- Source file: {Path.GetFileName(sourcePath)}")
                .AppendLine($"- Audio duration: {MeetingMinutesService.FormatElapsed(duration)}")
                .AppendLine($"- Generated with: {generation} + DeepSeek V4 Flash segment processing + Thinking High final summary")
                .AppendLine($"- Local speaker estimate: {localLabel} (confidence {routing.Confidence:P0}; diagnostic voice clusters do not equal the true speaker count)")
                .AppendLine($"- Recommended route: {recommendedRoute}; execution route: {executionRoute}{(routing.ShadowMode ? " (shadow validation)" : string.Empty)}")
                .AppendLine($"- Local analysis time: {routing.AnalysisElapsed.TotalSeconds:F2}s; evidence: {routing.Reason}")
                .AppendLine()
                .AppendLine("## Full transcript appendix")
                .AppendLine()
                .AppendLine(rawTranscript.Trim())
                .ToString()
            : new StringBuilder()
                .AppendLine(minutes.Trim())
                .AppendLine()
                .AppendLine("---")
                .AppendLine()
                .AppendLine("## 记录信息")
                .AppendLine()
                .AppendLine($"- 导入时间：{startedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"- 原始文件：{Path.GetFileName(sourcePath)}")
                .AppendLine($"- 音频时长：{MeetingMinutesService.FormatElapsed(duration)}")
                .AppendLine($"- 生成方式：{generation} + DeepSeek V4 Flash 分段整理 + Thinking High 最终总结")
                .AppendLine($"- 本地人数判断：{localLabel}（置信度 {routing.Confidence:P0}；声纹簇数量仅供诊断，不代表真实人数）")
                .AppendLine($"- 建议路由：{recommendedRoute}；实际路由：{executionRoute}{(routing.ShadowMode ? "（影子验证）" : string.Empty)}")
                .AppendLine($"- 本地分析耗时：{routing.AnalysisElapsed.TotalSeconds:F2} 秒；依据：{routing.Reason}")
                .AppendLine()
                .AppendLine("## 原始转写附录")
                .AppendLine()
                .AppendLine(rawTranscript.Trim())
                .ToString();
        await File.WriteAllTextAsync(path, document, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string SafeName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return value.Length <= 48 ? value : value[..48];
    }

    private static string FormatRoute(MeetingAsrRoute route, bool english) => route switch
    {
        MeetingAsrRoute.StandardAsr => english ? "Standard ASR" : "普通 ASR",
        MeetingAsrRoute.Speaker20 => "Speaker 2.0",
        _ => english ? "Stop or manual review" : "停止或人工复核"
    };

    private static string FormatSpeakerLabel(LocalSpeakerLabel label, bool english) => label switch
    {
        LocalSpeakerLabel.Single => english ? "Likely single speaker" : "单人候选",
        LocalSpeakerLabel.Multi => english ? "Multiple speakers" : "多人",
        LocalSpeakerLabel.NoSpeech => english ? "No reliable speech detected" : "未检测到可靠语音",
        _ => english ? "Uncertain" : "不确定"
    };

}
