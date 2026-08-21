using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

if (MeetingSummaryTemplateDefaults.Validate(MeetingSummaryTemplateDefaults.DefaultText) is not null)
{
    Console.Error.WriteLine("FAIL: built-in summary template is invalid.");
    return 1;
}
if (MeetingSummaryTemplateDefaults.Validate("## 自定义章节") is null)
{
    Console.Error.WriteLine("FAIL: a template without the required core summary heading was accepted.");
    return 1;
}
if (MeetingSummaryTemplateDefaults.Validate(MeetingSummaryTemplateDefaults.DefaultEnglishText, "en") is not null)
{
    Console.Error.WriteLine("FAIL: built-in English summary template is invalid.");
    return 1;
}
if (MeetingSummaryTemplateDefaults.Validate("## Custom section", "en") is null)
{
    Console.Error.WriteLine("FAIL: an English template without the required Core summary heading was accepted.");
    return 1;
}

if (args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("PASS: built-in Chinese and English meeting templates satisfy the local validation contract.");
    return 0;
}

var settings = new SecureSettingsStore().Load();
if (!settings.HasDeepSeekCredentials)
{
    Console.Error.WriteLine("FAIL: DeepSeek credentials are not configured.");
    return 2;
}

// Keep this Chinese-template scenario independent from the user's current UI
// language. English template behavior is covered separately above and by the
// English adversarial suite.
settings.UiLanguage = "zh-CN";

settings.MeetingSummaryTemplate = """
    # 项目沟通记录

    ## 核心摘要
    用两句话概括会议最重要的信息。

    ## 结论清单
    只列出会议现场明确形成的结论。

    ## 后续任务
    只列出原文明确承诺或分配的会后任务，并保留负责人和时间。
    """;

var transcript = """
    说话人1：今天只讨论登录页改版。大家确认主按钮文案改成“开始体验”。
    说话人2：文案稿我明天下午完成。预算和发布时间今天没有讨论。
    """;

var service = new MeetingMinutesService();
var started = DateTime.UtcNow;
var result = await service.CreateFinalMinutesAsync(
    transcript,
    [],
    TimeSpan.FromMinutes(2),
    settings,
    CancellationToken.None);
var elapsed = DateTime.UtcNow - started;

foreach (var heading in new[] { "# 项目沟通记录", "## 核心摘要", "## 结论清单", "## 后续任务" })
{
    if (!result.Contains(heading, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"FAIL: custom heading was not preserved: {heading}");
        Console.Error.WriteLine(result);
        return 1;
    }
}
if (!result.Contains("开始体验", StringComparison.Ordinal) ||
    !result.Contains("明天下午", StringComparison.Ordinal))
{
    Console.Error.WriteLine("FAIL: factual content was lost while applying the custom template.");
    Console.Error.WriteLine(result);
    return 1;
}

Console.WriteLine($"PASS: custom meeting template survived Thinking and audit; elapsedMs={(int)elapsed.TotalMilliseconds}.");
Console.WriteLine(result);
return 0;
