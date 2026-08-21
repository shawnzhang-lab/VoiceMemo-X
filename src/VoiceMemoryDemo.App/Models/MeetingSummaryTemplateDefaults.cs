namespace VoiceMemoryDemo.App.Models;

public static class MeetingSummaryTemplateDefaults
{
    public const int MaxLength = 8000;
    public const string RequiredSummaryHeading = "## 核心摘要";
    public const string RequiredEnglishSummaryHeading = "## Core summary";

    public const string DefaultText = """
        # 会议纪要

        ## 会议概览
        用简洁条目说明会议主题、时长、参与者和会议背景；无法确认的信息标记“待确认”。

        ## 核心摘要
        用 3～6 个要点概括最重要的讨论、结论和未决问题，让没有参会的人也能快速理解。

        ## 议题与讨论
        按议题分组，保留不同参与者的主要观点、意见变化以及讨论依据。

        ## 已确认项目约束
        只记录会议前已经存在且在原文中明确出现的目标、规则、预算、时间或其他约束；没有则写“无”。

        ## 本段新增决策
        只记录会议现场明确同意、批准或选定的决定；没有则写“无”。

        ## 行动项
        只有原文明确分配、承诺执行或要求跟进的事项才可列入。存在行动项时使用表格：事项｜负责人｜截止时间｜依据；没有则写“本次会议未形成明确行动项”。

        ## 风险与分歧
        记录原文明确提出的风险和真实分歧，不进行额外业务推演；没有则写“无”。

        ## 待确认问题
        汇总原文中尚未确认的问题、人名、数字、日期、负责人或截止时间；没有则写“无”。

        ## 识别质量警告
        只记录疑似听写、断句、数字或说话人归属问题；没有则写“无”。
        """;

    public const string DefaultEnglishText = """
        # Meeting report

        ## Meeting overview
        Briefly state the topic, duration, participants, and context. Mark anything uncertain as “To confirm”.

        ## Core summary
        Summarize the 3–6 most important discussions, conclusions, and unresolved questions so a non-attendee can understand them quickly.

        ## Topics and discussion
        Group the content by topic. Preserve key viewpoints, changes of opinion, and supporting evidence from different participants.

        ## Confirmed project constraints
        Include only goals, rules, budgets, timelines, or other constraints that existed before the meeting and are explicitly supported by the transcript. Write “None” when absent.

        ## New decisions in this meeting
        Include only decisions explicitly agreed, approved, or selected during this meeting. Write “None” when absent.

        ## Action items
        Include only executable follow-up work explicitly assigned, accepted, or requested. When action items exist, use a table with: Action | Owner | Due date | Evidence. Otherwise write “No explicit action items.”

        ## Risks and disagreements
        Include only risks, disagreements, or objections explicitly present in the transcript. Do not turn ASR errors into business disagreements. Write “None” when absent.

        ## Open questions
        List genuinely unresolved questions, names, numbers, dates, owners, or deadlines. Write “None” when absent.

        ## Recognition quality warnings
        Record possible ASR, punctuation, number, name, or speaker-attribution errors separately from the business summary. Write “None” when absent.
        """;

    public static string Resolve(string? customTemplate) =>
        string.IsNullOrWhiteSpace(customTemplate) ? DefaultText : customTemplate.Trim();

    public static string Resolve(string? customTemplate, string? uiLanguage) =>
        string.IsNullOrWhiteSpace(customTemplate) ? GetDefault(uiLanguage) : customTemplate.Trim();

    public static string NormalizeForStorage(string template) =>
        string.Equals(template.Trim(), DefaultText.Trim(), StringComparison.Ordinal)
            ? string.Empty
            : template.Trim();

    public static string NormalizeForStorage(string template, string? uiLanguage) =>
        string.Equals(template.Trim(), GetDefault(uiLanguage).Trim(), StringComparison.Ordinal)
            ? string.Empty
            : template.Trim();

    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "模板不能为空。";
        if (template.Length > MaxLength) return $"模板最多支持 {MaxLength} 个字符。";
        if (!template.Contains(RequiredSummaryHeading, StringComparison.Ordinal))
        {
            return $"请保留“{RequiredSummaryHeading}”章节，以便历史报告卡片显示会议概览。";
        }
        return null;
    }

    public static string? Validate(string? template, string? uiLanguage)
    {
        var english = IsEnglish(uiLanguage);
        if (string.IsNullOrWhiteSpace(template)) return english ? "The template cannot be empty." : "模板不能为空。";
        if (template.Length > MaxLength)
            return english ? $"The template supports up to {MaxLength} characters." : $"模板最多支持 {MaxLength} 个字符。";

        var requiredHeading = english ? RequiredEnglishSummaryHeading : RequiredSummaryHeading;
        if (!template.Contains(requiredHeading, StringComparison.Ordinal))
        {
            return english
                ? $"Keep the “{requiredHeading}” section so report history can show a preview."
                : $"请保留“{requiredHeading}”章节，以便历史报告卡片显示会议概览。";
        }
        return null;
    }

    public static string GetDefault(string? uiLanguage) => IsEnglish(uiLanguage)
        ? DefaultEnglishText
        : DefaultText;

    public static bool IsEnglish(string? uiLanguage) =>
        !string.IsNullOrWhiteSpace(uiLanguage) &&
        uiLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
}
