using System.Windows;
using System.Windows.Media;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.App;

public partial class MeetingSummaryTemplateDialog : Window
{
    private readonly string _uiLanguage;
    public string TemplateText { get; private set; } = string.Empty;

    public MeetingSummaryTemplateDialog(string? savedTemplate, string? uiLanguage)
    {
        _uiLanguage = uiLanguage ?? "zh-CN";
        InitializeComponent();
        TemplateTextBox.Text = MeetingSummaryTemplateDefaults.Resolve(savedTemplate, _uiLanguage);
        TemplateTextBox.CaretIndex = 0;
        TemplateTextBox.ScrollToHome();
        UpdateLength();
        UiLanguageService.Apply(this);
        SummaryHeadingHint.Text = MeetingSummaryTemplateDefaults.IsEnglish(_uiLanguage)
            ? "Keep the “## Core summary” heading. Report history uses this section for card previews."
            : "请保留“## 核心摘要”，历史报告列表会读取这一节作为卡片概览。";
    }

    private void TemplateTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateLength();
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void RestoreDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        TemplateTextBox.Text = MeetingSummaryTemplateDefaults.GetDefault(_uiLanguage);
        TemplateTextBox.CaretIndex = 0;
        TemplateTextBox.ScrollToHome();
        ShowNotice("已载入默认模板，点击“保存模板”后生效。", "#75D8AA");
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var text = TemplateTextBox.Text.Trim();
        var validationError = MeetingSummaryTemplateDefaults.Validate(text, _uiLanguage);
        if (validationError is not null)
        {
            ShowNotice(validationError, "#FF8585");
            return;
        }

        TemplateText = MeetingSummaryTemplateDefaults.NormalizeForStorage(text, _uiLanguage);
        DialogResult = true;
    }

    private void UpdateLength()
    {
        if (TemplateLengthText is null || TemplateTextBox is null) return;
        TemplateLengthText.Text = $"{TemplateTextBox.Text.Length:N0} / {MeetingSummaryTemplateDefaults.MaxLength:N0}";
    }

    private void ShowNotice(string message, string color)
    {
        ValidationText.Text = UiLanguageService.Translate(message);
        ValidationText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        ValidationText.Visibility = Visibility.Visible;
    }
}
