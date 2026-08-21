using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceMemoryDemo.App;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
    UiLanguageService.Initialize();
    UiLanguageService.SetLanguage(UiLanguageService.English);
    var application = new VoiceMemoryDemo.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    application.InitializeComponent();
    UiLanguageService.SetLanguage(UiLanguageService.English);

    if (UiLanguageService.TextFont.Source != "Segoe UI Variable Text" ||
        UiLanguageService.DisplayFont.Source != "Segoe UI Variable Display" ||
        UiLanguageService.TechnicalFont.Source != "Bahnschrift")
    {
        Console.Error.WriteLine("FAIL: English typography resources are not configured correctly.");
        return 1;
    }

    if (!MeetingSummaryTemplateDefaults.Resolve(null, "en")
            .StartsWith("# Meeting report", StringComparison.Ordinal) ||
        MeetingSummaryTemplateDefaults.Resolve(null, "en")
            .Contains("## 核心摘要", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("FAIL: English did not resolve to the English default template.");
        return 1;
    }

    var overlay = new StatusOverlayWindow();
    try
    {
        AssertLanguageComboBoxes();
        overlay.SetOutputLanguage("en", translationEnabled: true);

        AssertOverlay(overlay, false, "连接中", OverlayVisualState.Connecting, "Connecting to speech service");
        AssertOverlay(overlay, false, "请输入语音", OverlayVisualState.Listening, "Speak now");
        AssertOverlay(overlay, false, "转写中", OverlayVisualState.Transcribing, "Refining text");
        AssertOverlay(overlay, false, "已复制", OverlayVisualState.Success, "Copied");
        AssertOverlay(overlay, false, "未完成", OverlayVisualState.Error, "Not completed");
        AssertOverlay(overlay, false, "已自动关闭", OverlayVisualState.Success, "Closed automatically");
        AssertOverlay(overlay, false, "正在处理上一段", OverlayVisualState.Transcribing, "Finishing the previous session");

        AssertOverlay(overlay, true, "连接中", OverlayVisualState.Connecting, "Connecting to meeting service");
        AssertOverlay(overlay, true, "请输入语音", OverlayVisualState.Listening, "Recording meeting");
        AssertOverlay(overlay, true, "转写中", OverlayVisualState.Transcribing, "Building meeting report");
        AssertOverlay(overlay, true, "已复制", OverlayVisualState.Success, "Meeting report copied");
        AssertOverlay(overlay, true, "未完成", OverlayVisualState.Error, "Not completed");

        UiLanguageService.SetLanguage(UiLanguageService.Chinese);
        overlay.RefreshLanguage();
        overlay.SetMode(false);
        overlay.SetOutputLanguage("zh", translationEnabled: true);
        overlay.ShowStatus("请输入语音", OverlayVisualState.Listening);
        if (overlay.Width != 404 || UiLanguageService.TextFont.Source != "Microsoft YaHei UI" ||
            ((TextBlock)overlay.FindName("OverlayTitle")).Text != "请输入语音" ||
            ((TextBlock)overlay.FindName("OutputLanguageText")).Text != "输出为中文")
            throw new InvalidOperationException("Chinese typography did not restore correctly.");

        overlay.SetOutputLanguage("en", translationEnabled: false);
        if (((TextBlock)overlay.FindName("OutputLanguageText")).Text != "保持原文")
            throw new InvalidOperationException("The overlay did not expose the disabled translation state.");

        if (args.Contains("--render", StringComparer.OrdinalIgnoreCase))
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-smoke"));
            Directory.CreateDirectory(outputDirectory);

            UiLanguageService.SetLanguage(UiLanguageService.English);
            overlay.RefreshLanguage();
            overlay.SetMode(false);
            overlay.SetOutputLanguage("en", translationEnabled: true);
            overlay.ShowStatus("请输入语音", OverlayVisualState.Listening);
            RenderOverlay(overlay, Path.Combine(outputDirectory, "overlay-en-output-english.png"));

            UiLanguageService.SetLanguage(UiLanguageService.Chinese);
            overlay.RefreshLanguage();
            overlay.SetOutputLanguage("zh", translationEnabled: true);
            overlay.ShowStatus("请输入语音", OverlayVisualState.Listening);
            RenderOverlay(overlay, Path.Combine(outputDirectory, "overlay-zh-output-chinese.png"));
        }

        Console.WriteLine("PASS: bilingual typography, output-language badges, and 12 English overlay states are correct.");
        return 0;
    }

    catch (Exception ex)
    {
        Console.Error.WriteLine("FAIL: " + ex.Message);
        return 1;
    }
    finally
    {
        overlay.Close();
        Application.Current.Shutdown();
    }
    }

    private static void RenderOverlay(Window overlay, string path)
    {
        overlay.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(overlay.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(overlay.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(overlay);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void AssertLanguageComboBoxes()
    {
        var window = new MainWindow();
        var input = (ComboBox)window.FindName("InputLanguageComboBox");
        var output = (ComboBox)window.FindName("TargetLanguageComboBox");
        var expectedInputTags = new[] { "zh-PY", "zh", "en", "yue", "ja", "ko" };
        var expectedOutputTags = new[] { "zh", "en", "ja", "ko", "fr", "de", "es", "ru" };

        if (input.Items.Count != 6 || output.Items.Count != 8 ||
            input.Style is null || output.Style is null ||
            input.ItemContainerStyle is null || output.ItemContainerStyle is null)
            throw new InvalidOperationException("Language dropdown templates or option lists are incomplete.");

        AssertTagOrder(input, expectedInputTags, "input");
        AssertTagOrder(output, expectedOutputTags, "output");
        AssertContrast(input, "input");
        AssertContrast(output, "output");

        foreach (var uiLanguage in new[] { UiLanguageService.Chinese, UiLanguageService.English })
        {
            UiLanguageService.SetLanguage(uiLanguage);
            UiLanguageService.Apply(window);
            AssertEverySelection(input, expectedInputTags, $"{uiLanguage} input");
            AssertEverySelection(output, expectedOutputTags, $"{uiLanguage} output");
            AssertOptionLabels(input, uiLanguage == UiLanguageService.English
                ? new[] { "Auto (Mandarin / English / Cantonese)", "Mandarin Chinese", "English", "Cantonese", "Japanese 日本語", "Korean 한국어" }
                : new[] { "自动（中 / 英 / 粤）", "中文普通话", "英语 English", "粤语", "日语 日本語", "韩语 한국어" }, $"{uiLanguage} input");
            AssertOptionLabels(output, uiLanguage == UiLanguageService.English
                ? new[] { "Chinese", "English", "Japanese 日本語", "Korean 한국어", "French Français", "German Deutsch", "Spanish Español", "Russian Русский" }
                : new[] { "中文", "英语 English", "日语 日本語", "韩语 한국어", "法语 Français", "德语 Deutsch", "西班牙语 Español", "俄语 Русский" }, $"{uiLanguage} output");
        }

        input.SelectedIndex = 0;
        output.SelectedIndex = 1;
        if ((input.SelectedItem as ComboBoxItem)?.Tag as string != "zh-PY" ||
            (output.SelectedItem as ComboBoxItem)?.Tag as string != "en")
            throw new InvalidOperationException("Language dropdown selection did not restore the defaults.");

        window.ApplyTemplate();
        input.ApplyTemplate();
        output.ApplyTemplate();
        AssertSelectedTextBrush(input, "input");
        AssertSelectedTextBrush(output, "output");

        window.Hide();
    }

    private static void AssertTagOrder(ComboBox comboBox, IReadOnlyList<string> expected, string name)
    {
        var actual = comboBox.Items.OfType<ComboBoxItem>()
            .Select(item => item.Tag as string ?? string.Empty)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException($"The {name} language options or tag order changed unexpectedly.");
    }

    private static void AssertEverySelection(ComboBox comboBox, IReadOnlyList<string> expected, string name)
    {
        for (var index = 0; index < expected.Count; index++)
        {
            comboBox.SelectedIndex = index;
            var selectedTag = (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (!string.Equals(selectedTag, expected[index], StringComparison.Ordinal))
                throw new InvalidOperationException($"The {name} dropdown could not select '{expected[index]}'.");
        }
    }

    private static void AssertOptionLabels(ComboBox comboBox, IReadOnlyList<string> expected, string name)
    {
        var actual = comboBox.Items.OfType<ComboBoxItem>()
            .Select(item => item.Content as string ?? string.Empty)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException($"The {name} option labels were not fully localized.");
    }

    private static void AssertContrast(ComboBox comboBox, string name)
    {
        if (comboBox.Foreground is not SolidColorBrush foreground ||
            comboBox.Background is not SolidColorBrush background ||
            ContrastRatio(foreground.Color, background.Color) < 4.5)
            throw new InvalidOperationException($"The {name} dropdown selected text does not meet the 4.5:1 contrast target.");
    }

    private static void AssertSelectedTextBrush(ComboBox comboBox, string name)
    {
        var toggle = comboBox.Template.FindName("DropDownToggle", comboBox) as ToggleButton
            ?? throw new InvalidOperationException($"The {name} dropdown toggle template is missing.");
        toggle.ApplyTemplate();
        var presenter = toggle.Template.FindName("SelectedContentPresenter", toggle) as ContentPresenter
            ?? throw new InvalidOperationException($"The {name} selected-content presenter is missing.");
        var selectedForeground = TextElement.GetForeground(presenter);
        if (selectedForeground is not SolidColorBrush selectedBrush ||
            comboBox.Foreground is not SolidColorBrush comboBrush ||
            selectedBrush.Color != comboBrush.Color)
            throw new InvalidOperationException($"The {name} selected text is not inheriting the dark-theme foreground.");
    }

    private static double ContrastRatio(Color left, Color right)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        var brighter = Math.Max(Luminance(left), Luminance(right));
        var darker = Math.Min(Luminance(left), Luminance(right));
        return (brighter + 0.05) / (darker + 0.05);
    }

    private static void AssertOverlay(
        StatusOverlayWindow overlay,
        bool meetingMode,
        string sourceTitle,
        OverlayVisualState state,
        string expectedTitle)
    {
    overlay.SetMode(meetingMode);
    overlay.ShowStatus(sourceTitle, state);

    var actualTitle = ((TextBlock)overlay.FindName("OverlayTitle")).Text;
    if (!string.Equals(actualTitle, expectedTitle, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected overlay title '{expectedTitle}', got '{actualTitle}'.");

    var mode = ((TextBlock)overlay.FindName("ModeLabel")).Text;
    var hint = ((TextBlock)overlay.FindName("OverlayHint")).Text;
    var hotkey = ((TextBlock)overlay.FindName("HotkeyText")).Text;
    var outputBadge = (Border)overlay.FindName("OutputLanguageBadge");
    var outputLanguage = ((TextBlock)overlay.FindName("OutputLanguageText")).Text;
    var hotkeyElement = (TextBlock)overlay.FindName("HotkeyText");
    var expectedMode = meetingMode ? "MEETING · LEFT ALT" : "DICTATION · RIGHT ALT";
    var expectedHint = meetingMode
        ? "Capturing microphone and computer audio"
        : "Press Right Alt to stop and insert";
    var expectedHotkey = meetingMode ? "Left Alt" : "Right Alt";

    if (mode != expectedMode || hint != expectedHint || hotkey != expectedHotkey)
        throw new InvalidOperationException($"Overlay chrome mismatch: {mode} | {hint} | {hotkey}.");
    if (meetingMode && outputBadge.Visibility != Visibility.Collapsed)
        throw new InvalidOperationException("Meeting overlay should not show the dictation output-language badge.");
    if (!meetingMode && (outputBadge.Visibility != Visibility.Visible || outputLanguage != "OUTPUT: ENGLISH"))
        throw new InvalidOperationException($"Dictation output-language badge mismatch: {outputLanguage}.");
    if (overlay.Width != 440 || hotkeyElement.TextWrapping != TextWrapping.NoWrap ||
        hotkeyElement.FontFamily.Source != "Segoe UI Variable Display")
        throw new InvalidOperationException("English overlay typography or hotkey width was not applied.");
    }
}
