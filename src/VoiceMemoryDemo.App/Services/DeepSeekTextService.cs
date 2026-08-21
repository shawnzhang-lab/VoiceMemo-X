using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class DeepSeekTextService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public async Task WarmupAsync(string baseUrl)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var request = new HttpRequestMessage(HttpMethod.Head, baseUrl.TrimEnd('/') + "/");
            using var response = await Http.SendAsync(request, timeout.Token);
        }
        catch
        {
            // Warmup is best effort. The real request keeps the detailed error handling.
        }
    }

    public async Task<string> RefineAsync(
        string rawText,
        MemoryContext memory,
        AppSettings settings,
        string appName,
        CancellationToken cancellationToken = default)
    {
        var corrected = ApplyCorrections(rawText, memory.Corrections);
        if (!settings.NeedsDeepSeek)
        {
            return corrected;
        }
        if (!settings.HasDeepSeekCredentials)
        {
            throw new InvalidOperationException(settings.EnableTranslation
                ? "中文翻译需要 DeepSeek API Key，请先到“连接设置”完成配置。"
                : "AI 文本整理需要 DeepSeek API Key，请先到“连接设置”完成配置。");
        }

        var preferenceText = memory.Preferences.Count == 0
            ? "暂无额外的个人表达偏好。"
            : string.Join("\n", memory.Preferences.Select((item, index) => $"{index + 1}. {item}"));
        var recentExamples = memory.RecentExamples.Count == 0
            ? "暂无历史示例。"
            : string.Join("\n", memory.RecentExamples.Select((item, index) => $"{index + 1}. {item}"));

        var inputLanguage = GetInputLanguageName(settings.InputLanguage);
        var targetLanguage = GetTargetLanguageName(settings.TargetLanguage);
        var structureInstruction = settings.EnableSmartStructuring
            ? """
                输出结构规则：
                - 先完成语气词清理、重复删除、措辞修正和明显识别错词纠正，再判断内容结构；这些步骤必须在同一次处理内完成。
                - 在内部先把原文判定为 SINGLE（单一意思）或 MULTI（多个独立意思），不要输出判定标签。出现“几个问题”“第一/第二”“另外”“还有”“一方面/另一方面”等列举信号，并且确实包含至少两个不同意思时，必须判为 MULTI。
                - 当原文包含两个或以上独立的观点、问题、要求、步骤或决定时，使用“1. …”“2. …”“3. …”编号；每个编号项必须换行并独占一行，禁止把多个编号写在同一行；每点只表达一个核心意思，并按因果、先后或重要程度整理。
                - 当原文只有一个意图、一个问题或内容很短时，保持自然的一段话，不要为了格式强行添加“1.”。
                - MULTI 必须输出逐行编号，不能改写成冒号后的逗号或分号并列句；这条格式要求优先于“最小修改”。编号前不添加标题、引导语或总结；不得把同一意思拆成多个点，也不得合并本来不同的要求。
                - 只纠正能从上下文、固定词典或常识明确判断的识别错误；专有名词或数字不确定时保留原文，不得猜测或编造。
                """
            : """
                输出结构规则：保持自然段落和原有表达顺序，不主动添加编号列表。
                """;
        var systemPrompt = settings.EnableTranslation
            ? $"""
                你是桌面语音输入法的翻译与文本整理引擎。用户主要使用{inputLanguage}，你要把转写内容转换为自然、地道的{targetLanguage}，供用户直接发送或粘贴。
                必须遵守：
                1. 忠实保留原意、事实、语气、称呼、数字和格式，不回答用户，不新增信息，不进行解释。
                2. 先删除无意义语气词和重复，修正不自然措辞、明显识别错词、断句与标点，再翻译为{targetLanguage}。
                3. 专有名词使用给定的固定写法；有通行译名时使用通行译名，没有时保留原文或合理音译。
                4. 个人表达偏好只用于保持语气与风格，不得改变原意。
                5. 最终只输出{targetLanguage}正文，不附带中文原文、引号、标题、说明、语言标签或 Markdown 围栏。

                {structureInstruction}
                """
            : $"""
                你是一个桌面语音输入法的文本整理引擎。你的任务不是回答用户，而是把转写文本整理成可直接发送或粘贴的文字。
                必须遵守：
                1. 保留原意、事实、语气和语言，不新增信息，不进行解释。
                2. 删除无意义语气词、口头禅和重复，修正不自然措辞、明显识别错词、断句与标点。
                3. 严格采用给定的个人偏好和专有词写法。
                4. 只输出整理后的正文，不加引号、标题、说明或 Markdown 围栏。
                5. 如果原文是 SINGLE 且已经自然，只做必要的最小修改；MULTI 必须执行后面的逐行编号规则。

                {structureInstruction}
                """;
        var userPrompt = $"""
            当前输入应用：{appName}

            个人表达偏好：
            {preferenceText}

            最近在相同或相近场景中的成文示例（只模仿表达习惯，不照抄内容）：
            {recentExamples}

            待整理的语音转写：
            {corrected}
            """;

        var maxTokens = Math.Clamp(
            corrected.Length * (settings.EnableTranslation ? 3 : 2) + 256,
            512,
            2048);
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(settings.DeepSeekModel) ? "deepseek-v4-flash" : settings.DeepSeekModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            thinking = new { type = "disabled" },
            temperature = 0.1,
            max_tokens = maxTokens,
            stream = false
        };

        var baseUrl = settings.DeepSeekBaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeepSeekApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepSeek 整理失败（HTTP {(int)response.StatusCode}）。请检查 API Key、余额或模型名。 ");
        }

        using var json = JsonDocument.Parse(body);
        var text = json.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(text) ? corrected : text.Trim();
    }

    private static string ApplyCorrections(string input, IReadOnlyDictionary<string, string> corrections)
    {
        var result = input;
        foreach (var pair in corrections.OrderByDescending(pair => pair.Key.Length))
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    public static string GetTargetLanguageName(string code) => code switch
    {
        "zh" => "中文",
        "en" => "英语",
        "ja" => "日语",
        "ko" => "韩语",
        "fr" => "法语",
        "de" => "德语",
        "es" => "西班牙语",
        "ru" => "俄语",
        _ => "英语"
    };

    public static string GetInputLanguageName(string code) => code switch
    {
        "zh" => "中文普通话",
        "zh-PY" => "自动识别的中文、英语或粤语",
        "en" => "英语",
        "yue" => "粤语",
        "ja" => "日语",
        "ko" => "韩语",
        _ => "中文普通话"
    };
}
