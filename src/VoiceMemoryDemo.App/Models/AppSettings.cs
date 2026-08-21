namespace VoiceMemoryDemo.App.Models;

public sealed class AppSettings
{
    public string TencentAppId { get; set; } = string.Empty;
    public string TencentSecretId { get; set; } = string.Empty;
    public string TencentSecretKey { get; set; } = string.Empty;
    public string TencentEngineModel { get; set; } = "16k_zh_en";
    public string InputLanguage { get; set; } = "zh-PY";
    public int InputLanguageSelectionVersion { get; set; }
    public string UiLanguage { get; set; } = "zh-CN";
    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
    public string DeepSeekModel { get; set; } = "deepseek-v4-flash";
    public bool EnableAiRefinement { get; set; } = true;
    public bool EnableSmartStructuring { get; set; } = true;
    public bool EnableTranslation { get; set; }
    public string TargetLanguage { get; set; } = "en";
    public bool EnableMeetingMode { get; set; }
    public bool CaptureSystemAudioInMeeting { get; set; } = true;
    public string MeetingSummaryTemplate { get; set; } = string.Empty;
    public string MeetingSummaryTemplateEnglish { get; set; } = string.Empty;
    public bool SaveHistory { get; set; } = true;

    public bool HasTencentCredentials =>
        !string.IsNullOrWhiteSpace(TencentAppId) &&
        !string.IsNullOrWhiteSpace(TencentSecretId) &&
        !string.IsNullOrWhiteSpace(TencentSecretKey);

    public bool HasDeepSeekCredentials => !string.IsNullOrWhiteSpace(DeepSeekApiKey);

    public bool NeedsDeepSeek => EnableAiRefinement || EnableSmartStructuring || EnableTranslation;
}
