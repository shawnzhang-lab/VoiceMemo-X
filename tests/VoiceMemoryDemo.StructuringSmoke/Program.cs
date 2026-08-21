using System.Diagnostics;
using System.Text.RegularExpressions;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

var settings = new SecureSettingsStore().Load();
if (!settings.HasDeepSeekCredentials)
{
    Console.Error.WriteLine("FAIL: DeepSeek credentials are not configured.");
    return 2;
}

settings.EnableAiRefinement = true;
settings.EnableSmartStructuring = true;
settings.EnableTranslation = false;

var service = new DeepSeekTextService();
var memory = new MemoryContext
{
    Corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["扣的可斯"] = "Codex"
    }
};

var samples = new[]
{
    new
    {
        Name = "single-intent",
        Input = "嗯，帮我打开 Codex 的输入框。",
        ExpectNumbered = false
    },
    new
    {
        Name = "multi-point",
        Input = "嗯，我觉得目前有几个问题，就是连接速度有时候比较慢，然后提示信息太杂了，还有会议结束以后报告入口也不太好找。",
        ExpectNumbered = true
    },
    new
    {
        Name = "correction-and-structure",
        Input = "呃，第一是把扣的可斯改成正确写法，然后第二个就是短句不要强制分点，最后不要自己增加我没说过的内容。",
        ExpectNumbered = true
    }
};

foreach (var sample in samples)
{
    var stopwatch = Stopwatch.StartNew();
    var output = await service.RefineAsync(sample.Input, memory, settings, "structuring-smoke");
    stopwatch.Stop();
    var isNumbered = Regex.IsMatch(output, @"(?m)^\s*1[\.、)]\s*");
    var removedFillers = !output.Contains("嗯", StringComparison.Ordinal) &&
                         !output.Contains("呃", StringComparison.Ordinal);
    var shapePassed = isNumbered == sample.ExpectNumbered;
    var correctionPassed = sample.Name != "correction-and-structure" || output.Contains("Codex", StringComparison.OrdinalIgnoreCase);
    Console.WriteLine($"CASE {sample.Name}: elapsedMs={stopwatch.ElapsedMilliseconds}; numbered={isNumbered}; fillersRemoved={removedFillers}; correction={correctionPassed}");
    Console.WriteLine(output);
    Console.WriteLine("---");
    if (!shapePassed || !removedFillers || !correctionPassed)
    {
        Console.Error.WriteLine($"FAIL: {sample.Name}");
        return 1;
    }
}

settings.EnableTranslation = true;
settings.TargetLanguage = "en";
var translationInput = "请把明天下午三点的产品会议改到四点，并提醒李明参加。";
var translationOutput = await service.RefineAsync(
    translationInput,
    new MemoryContext(),
    settings,
    "translation-smoke");
var translationPassed = !Regex.IsMatch(translationOutput, @"\p{IsCJKUnifiedIdeographs}") &&
                        translationOutput.Contains("meeting", StringComparison.OrdinalIgnoreCase) &&
                        (translationOutput.Contains("four", StringComparison.OrdinalIgnoreCase) ||
                         Regex.IsMatch(translationOutput, @"\b4\b"));
Console.WriteLine($"CASE zh-to-en-translation: passed={translationPassed}");
Console.WriteLine(translationOutput);
Console.WriteLine("---");
if (!translationPassed)
{
    Console.Error.WriteLine("FAIL: zh-to-en-translation");
    return 1;
}

Console.WriteLine("PASS: smart structuring and Chinese-to-English translation met the smoke-test contract.");
return 0;
