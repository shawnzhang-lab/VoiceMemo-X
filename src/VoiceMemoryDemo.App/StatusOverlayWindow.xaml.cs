using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.App;

public enum OverlayVisualState
{
    Connecting,
    Connected,
    Listening,
    Transcribing,
    Success,
    Error
}

public enum OverlayMode
{
    DailyInput,
    MeetingMinutes
}

public partial class StatusOverlayWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _phaseTimer;
    private readonly BitmapSource _spriteSheet;
    private OverlayMode _mode = OverlayMode.DailyInput;
    private string _sourceTitle = "连接中";
    private string _targetLanguage = "en";
    private bool _translationEnabled;

    public StatusOverlayWindow()
    {
        InitializeComponent();
        _spriteSheet = LoadSpriteSheet();
        ApplyModeChrome();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        _phaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
        _phaseTimer.Tick += (_, _) =>
        {
            _phaseTimer.Stop();
            ShowStatus(GetListeningTitle(), OverlayVisualState.Listening);
        };
    }

    public void SetMode(bool meetingMode)
    {
        _mode = meetingMode ? OverlayMode.MeetingMinutes : OverlayMode.DailyInput;
        ApplyModeChrome();
    }

    public void SetOutputLanguage(string targetLanguage, bool translationEnabled)
    {
        _targetLanguage = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage;
        _translationEnabled = translationEnabled;
        ApplyOutputLanguageBadge();
    }

    public void ShowConnectedThenListening()
    {
        ShowStatus(GetListeningTitle(), OverlayVisualState.Connected);
        _phaseTimer.Start();
    }

    public void ShowStatus(string title, OverlayVisualState state, TimeSpan? hideAfter = null)
    {
        _hideTimer.Stop();
        _phaseTimer.Stop();
        _sourceTitle = title;
        ApplyModeChrome();
        OverlayTitle.Text = GetDisplayTitle(title);
        SetCharacterFrame(state);
        SetAccent(state);
        StartStateAnimation(state);

        PositionNearBottom();
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        if (hideAfter is not null)
        {
            _hideTimer.Interval = hideAfter.Value;
            _hideTimer.Start();
        }
    }

    public void Dismiss()
    {
        _hideTimer.Stop();
        _phaseTimer.Stop();
        StopAnimations();
        Hide();
    }

    public void RefreshLanguage()
    {
        ApplyModeChrome();
        OverlayTitle.Text = GetDisplayTitle(_sourceTitle);
        UiLanguageService.Apply(this);
    }

    private string GetListeningTitle() => _mode == OverlayMode.MeetingMinutes
        ? "会议录制中"
        : "请输入语音";

    private string GetDisplayTitle(string title)
    {
        if (_mode == OverlayMode.MeetingMinutes)
        {
            var meetingTitle = title switch
            {
                "连接中" => "正在连接会议识别",
                "请输入语音" => "会议录制中",
                "转写中" => "正在整理会议",
                "已复制" => "会议报告已复制",
                _ => title
            };
            return UiLanguageService.Translate(meetingTitle);
        }

        var dailyTitle = title switch
        {
            "连接中" => "正在连接语音识别",
            "转写中" => "正在整理文字",
            _ => title
        };
        return UiLanguageService.Translate(dailyTitle);
    }

    private void ApplyModeChrome()
    {
        var meeting = _mode == OverlayMode.MeetingMinutes;
        var english = UiLanguageService.IsEnglish;
        Width = english ? 440 : 404;
        HotkeyColumn.Width = new GridLength(english ? 78 : 64);
        HotkeyBox.Width = english ? 70 : 58;
        ModeLabel.FontSize = english ? 9 : 10;
        OverlayTitle.FontSize = english ? 16 : 17;
        OverlayHint.FontSize = english ? 9 : 9.5;
        HotkeyLabel.FontSize = english ? 7.5 : 8.5;
        HotkeyText.FontSize = english ? 12.5 : 15;
        ModeLabel.Text = meeting
            ? UiLanguageService.Text("会议纪要 · 左 Alt", "MEETING · LEFT ALT")
            : UiLanguageService.Text("日常输入 · 右 Alt", "DICTATION · RIGHT ALT");
        OverlayHint.Text = meeting
            ? UiLanguageService.Text("同时记录麦克风与电脑声音", "Capturing microphone and computer audio")
            : UiLanguageService.Text("再按右 Alt 结束并输入", "Press Right Alt to stop and insert");
        HotkeyText.Text = meeting
            ? UiLanguageService.Text("左 Alt", "Left Alt")
            : UiLanguageService.Text("右 Alt", "Right Alt");
        ApplyOutputLanguageBadge();

        var rail = meeting ? Color.FromRgb(117, 141, 167) : Color.FromRgb(213, 154, 89);
        var label = meeting ? Color.FromRgb(174, 200, 231) : Color.FromRgb(230, 183, 121);
        var badge = meeting ? Color.FromRgb(24, 34, 53) : Color.FromRgb(43, 33, 24);
        var shell = meeting ? Color.FromArgb(242, 18, 23, 34) : Color.FromArgb(242, 23, 27, 34);

        ModeRail.Background = new SolidColorBrush(rail);
        ModeLabel.Foreground = new SolidColorBrush(label);
        ModeBadge.Background = new SolidColorBrush(badge);
        HotkeyText.Foreground = new SolidColorBrush(label);
        HotkeyBox.BorderBrush = new SolidColorBrush(rail) { Opacity = 0.58 };
        ShellBorder.Background = new SolidColorBrush(shell);
    }

    private void ApplyOutputLanguageBadge()
    {
        var meeting = _mode == OverlayMode.MeetingMinutes;
        OutputLanguageBadge.Visibility = meeting ? Visibility.Collapsed : Visibility.Visible;
        if (meeting) return;

        if (!_translationEnabled)
        {
            OutputLanguageText.Text = UiLanguageService.Text("保持原文", "OUTPUT: ORIGINAL");
            return;
        }

        var languageName = UiLanguageService.IsEnglish
            ? _targetLanguage.ToLowerInvariant() switch
            {
                "zh" => "CHINESE",
                "ja" => "JAPANESE",
                "ko" => "KOREAN",
                "fr" => "FRENCH",
                "de" => "GERMAN",
                "es" => "SPANISH",
                "ru" => "RUSSIAN",
                _ => "ENGLISH"
            }
            : DeepSeekTextService.GetTargetLanguageName(_targetLanguage);
        OutputLanguageText.Text = UiLanguageService.IsEnglish
            ? $"OUTPUT: {languageName}"
            : $"输出为{languageName}";
    }

    private static BitmapSource LoadSpriteSheet()
    {
        var image = new BitmapImage();
        image.BeginInit();
        var assemblyName = typeof(StatusOverlayWindow).Assembly.GetName().Name;
        image.UriSource = new Uri(
            $"pack://application:,,,/{assemblyName};component/Assets/character-states-xiaoye-v4.png",
            UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void SetCharacterFrame(OverlayVisualState state)
    {
        var halfWidth = _spriteSheet.PixelWidth / 2;
        var halfHeight = _spriteSheet.PixelHeight / 2;
        var rect = state switch
        {
            OverlayVisualState.Connecting => new Int32Rect(0, 0, halfWidth, halfHeight),
            OverlayVisualState.Connected or OverlayVisualState.Success => new Int32Rect(halfWidth, 0, halfWidth, halfHeight),
            OverlayVisualState.Listening or OverlayVisualState.Error => new Int32Rect(0, halfHeight, halfWidth, halfHeight),
            OverlayVisualState.Transcribing => new Int32Rect(halfWidth, halfHeight, halfWidth, halfHeight),
            _ => new Int32Rect(0, 0, halfWidth, halfHeight)
        };
        var crop = new CroppedBitmap(_spriteSheet, rect);
        crop.Freeze();
        CharacterImage.Source = crop;
    }

    private void SetAccent(OverlayVisualState state)
    {
        var meeting = _mode == OverlayMode.MeetingMinutes;
        var color = state switch
        {
            OverlayVisualState.Connecting => meeting
                ? Color.FromRgb(112, 183, 255)
                : Color.FromRgb(238, 169, 95),
            OverlayVisualState.Connected or OverlayVisualState.Listening => meeting
                ? Color.FromRgb(124, 158, 217)
                : Color.FromRgb(255, 158, 87),
            OverlayVisualState.Transcribing => meeting
                ? Color.FromRgb(153, 136, 255)
                : Color.FromRgb(213, 154, 89),
            OverlayVisualState.Success => meeting
                ? Color.FromRgb(104, 199, 193)
                : Color.FromRgb(94, 215, 154),
            OverlayVisualState.Error => Color.FromRgb(255, 107, 107),
            _ => meeting ? Color.FromRgb(124, 158, 217) : Color.FromRgb(213, 154, 89)
        };

        var brush = new SolidColorBrush(color);
        ShellBorder.BorderBrush = new SolidColorBrush(color) { Opacity = 0.48 };
        AccentHalo.Stroke = new SolidColorBrush(color) { Opacity = 0.72 };
        AccentHalo.Fill = new SolidColorBrush(color) { Opacity = 0.1 };
        DotOne.Fill = brush;
        DotTwo.Fill = brush;
        DotThree.Fill = brush;

        AnimateDot(DotOne, 0);
        AnimateDot(DotTwo, 140);
        AnimateDot(DotThree, 280);
    }

    private static void AnimateDot(UIElement dot, int delayMilliseconds)
    {
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.22, 1, TimeSpan.FromMilliseconds(520))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
    }

    private void StartStateAnimation(OverlayVisualState state)
    {
        StopCharacterAnimations();
        StartEffectAnimation(state);
        AccentHalo.BeginAnimation(OpacityProperty, new DoubleAnimation(0.28, 0.92, TimeSpan.FromMilliseconds(620))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });

        switch (state)
        {
            case OverlayVisualState.Connecting:
                if (_mode == OverlayMode.MeetingMinutes)
                {
                    CharacterRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, Forever(-0.8, 0.8, 1050));
                    CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, Forever(1.2, -1.6, 820));
                }
                else
                {
                    CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Forever(0.992, 1.018, 720));
                    CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Forever(0.992, 1.018, 720));
                    CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, Forever(0.7, -1.2, 920));
                }
                break;
            case OverlayVisualState.Connected:
            case OverlayVisualState.Success:
                var bounce = new DoubleAnimation(0.91, 1.07, TimeSpan.FromMilliseconds(210)) { AutoReverse = true };
                CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, bounce);
                CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, bounce);
                break;
            case OverlayVisualState.Listening:
                if (_mode == OverlayMode.MeetingMinutes)
                {
                    CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Forever(0.985, 1.025, 980));
                    CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Forever(0.985, 1.025, 980));
                    CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, Forever(0.8, -1.3, 980));
                }
                else
                {
                    var nod = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromMilliseconds(1600),
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    nod.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                    nod.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.48)));
                    nod.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromPercent(0.62)));
                    nod.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.78)));
                    CharacterRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, nod);
                }
                break;
            case OverlayVisualState.Transcribing:
                if (_mode == OverlayMode.MeetingMinutes)
                {
                    CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, Forever(0.4, -1.1, 560));
                    CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Forever(0.99, 1.018, 560));
                    CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Forever(0.99, 1.018, 560));
                }
                else
                {
                    CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, Forever(-0.55, 0.55, 150));
                    CharacterRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, Forever(-0.35, 0.35, 170));
                }
                break;
            case OverlayVisualState.Error:
                CharacterRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, Forever(-1.8, 1.8, 260));
                break;
        }
    }

    private static DoubleAnimation Forever(double from, double to, int milliseconds) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever
    };

    private void StopAnimations()
    {
        StopCharacterAnimations();
        StopEffectAnimations();
        AccentHalo.BeginAnimation(OpacityProperty, null);
        DotOne.BeginAnimation(OpacityProperty, null);
        DotTwo.BeginAnimation(OpacityProperty, null);
        DotThree.BeginAnimation(OpacityProperty, null);
    }

    private void StopCharacterAnimations()
    {
        CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        CharacterScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        CharacterRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        CharacterTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        CharacterScale.ScaleX = 1;
        CharacterScale.ScaleY = 1;
        CharacterRotate.Angle = 0;
        CharacterTranslate.X = 0;
        CharacterTranslate.Y = 0;
    }

    private void StartEffectAnimation(OverlayVisualState state)
    {
        StopEffectAnimations();

        switch (state)
        {
            case OverlayVisualState.Connecting:
                if (_mode == OverlayMode.MeetingMinutes)
                {
                    MeetingConnectingEffect.Visibility = Visibility.Visible;
                    AnimateMeetingNode(MeetingNodeOne, MeetingNodeTranslateOne, 0);
                    AnimateMeetingNode(MeetingNodeTwo, MeetingNodeTranslateTwo, 170);
                    AnimateMeetingNode(MeetingNodeThree, MeetingNodeTranslateThree, 340);
                }
                else
                {
                    ConnectingEffect.Visibility = Visibility.Visible;
                    AnimateSignalRing(SignalRingOne, SignalScaleOne, 0);
                    AnimateSignalRing(SignalRingTwo, SignalScaleTwo, 220);
                    SignalPulseDot.BeginAnimation(OpacityProperty, Forever(0.2, 1, 360));
                }
                break;
            case OverlayVisualState.Connected:
            case OverlayVisualState.Success:
                SuccessEffect.Visibility = Visibility.Visible;
                AnimateSpark(SuccessStarOne, SuccessScaleOne, 0);
                AnimateSpark(SuccessStarTwo, SuccessScaleTwo, 170);
                break;
            case OverlayVisualState.Listening:
                if (_mode == OverlayMode.MeetingMinutes)
                {
                    MeetingListeningEffect.Visibility = Visibility.Visible;
                    AnimateMeetingWave(MeetingWaveScaleOne, 0, 440);
                    AnimateMeetingWave(MeetingWaveScaleTwo, 110, 570);
                    AnimateMeetingWave(MeetingWaveScaleThree, 220, 490);
                }
                else
                {
                    ListeningEffect.Visibility = Visibility.Visible;
                    AnimateListeningDot(ListenDotOne, 0);
                    AnimateListeningDot(ListenDotTwo, 130);
                    AnimateListeningDot(ListenDotThree, 260);
                }
                break;
            case OverlayVisualState.Transcribing:
                if (_mode == OverlayMode.MeetingMinutes)
                {
                    MeetingTranscribingEffect.Visibility = Visibility.Visible;
                    AnimateMeetingLane(MeetingLaneOne, 0);
                    AnimateMeetingLane(MeetingLaneTwo, 130);
                    AnimateMeetingLane(MeetingLaneThree, 260);
                }
                else
                {
                    TranscribingEffect.Visibility = Visibility.Visible;
                    AnimateScribeLine(ScribeLineOne, ScribeTranslateOne, 0);
                    AnimateScribeLine(ScribeLineTwo, ScribeTranslateTwo, 85);
                    AnimateScribeLine(ScribeLineThree, ScribeTranslateThree, 170);
                    InkPulse.BeginAnimation(OpacityProperty, Forever(0.15, 0.95, 220));
                }
                break;
            case OverlayVisualState.Error:
                ErrorEffect.Visibility = Visibility.Visible;
                ErrorEffect.BeginAnimation(OpacityProperty, Forever(0.55, 1, 280));
                break;
        }
    }

    private static void AnimateSignalRing(UIElement ring, ScaleTransform scale, int delayMilliseconds)
    {
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.1, 0.95, TimeSpan.FromMilliseconds(560))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Forever(0.78, 1.1, 560));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Forever(0.78, 1.1, 560));
    }

    private static void AnimateSpark(UIElement star, ScaleTransform scale, int delayMilliseconds)
    {
        star.BeginAnimation(OpacityProperty, new DoubleAnimation(0.18, 1, TimeSpan.FromMilliseconds(360))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
        var pulse = Forever(0.72, 1.16, 360);
        pulse.BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private static void AnimateListeningDot(UIElement dot, int delayMilliseconds)
    {
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, 1, TimeSpan.FromMilliseconds(420))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
    }

    private static void AnimateScribeLine(UIElement line, TranslateTransform translate, int delayMilliseconds)
    {
        var motion = new DoubleAnimation(-1.5, 3.5, TimeSpan.FromMilliseconds(260))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        };
        translate.BeginAnimation(TranslateTransform.XProperty, motion);
        line.BeginAnimation(OpacityProperty, new DoubleAnimation(0.1, 0.95, TimeSpan.FromMilliseconds(130))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
    }

    private static void AnimateMeetingNode(UIElement node, TranslateTransform translate, int delayMilliseconds)
    {
        node.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(480))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
        var drift = Forever(1.6, -1.8, 720);
        drift.BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds);
        translate.BeginAnimation(TranslateTransform.YProperty, drift);
    }

    private static void AnimateMeetingWave(ScaleTransform scale, int delayMilliseconds, int durationMilliseconds)
    {
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.28, 1, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
    }

    private static void AnimateMeetingLane(UIElement lane, int delayMilliseconds)
    {
        lane.BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, 1, TimeSpan.FromMilliseconds(330))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds)
        });
    }

    private void StopEffectAnimations()
    {
        foreach (var effect in new UIElement[]
                 {
                     ConnectingEffect, ListeningEffect, TranscribingEffect, SuccessEffect, ErrorEffect,
                     MeetingConnectingEffect, MeetingListeningEffect, MeetingTranscribingEffect
                 })
        {
            effect.BeginAnimation(OpacityProperty, null);
            effect.Visibility = Visibility.Collapsed;
        }

        foreach (var element in new UIElement[]
                 {
                     SignalRingOne, SignalRingTwo, SignalPulseDot,
                     ListenDotOne, ListenDotTwo, ListenDotThree,
                     ScribeLineOne, ScribeLineTwo, ScribeLineThree, InkPulse,
                     SuccessStarOne, SuccessStarTwo,
                     MeetingNodeOne, MeetingNodeTwo, MeetingNodeThree,
                     MeetingLaneOne, MeetingLaneTwo, MeetingLaneThree
                 })
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1;
        }

        foreach (var scale in new[]
                 {
                     SignalScaleOne, SignalScaleTwo, SuccessScaleOne, SuccessScaleTwo,
                     MeetingWaveScaleOne, MeetingWaveScaleTwo, MeetingWaveScaleThree
                 })
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            var isMeetingWave = ReferenceEquals(scale, MeetingWaveScaleOne) ||
                                ReferenceEquals(scale, MeetingWaveScaleTwo) ||
                                ReferenceEquals(scale, MeetingWaveScaleThree);
            scale.ScaleY = isMeetingWave
                ? 0.35
                : 1;
        }

        foreach (var translate in new[] { ScribeTranslateOne, ScribeTranslateTwo, ScribeTranslateThree })
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
        }

        foreach (var translate in new[] { MeetingNodeTranslateOne, MeetingNodeTranslateTwo, MeetingNodeTranslateThree })
        {
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = 0;
        }
    }

    private void PositionNearBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 34;
    }
}
