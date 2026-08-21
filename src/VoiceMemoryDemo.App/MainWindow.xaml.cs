using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan NoSpeechTimeout = TimeSpan.FromSeconds(5);
    private const double SpeechDetectionLevel = 0.015;
    private const int SpeechDetectionFrames = 10;
    private const int PreludeFrameLimit = 150;

    private readonly SecureSettingsStore _settingsStore = new();
    private readonly AutoStartService _autoStart = new();
    private readonly MemoryRepository _memoryRepository = new();
    private readonly DeepSeekTextService _deepSeek = new();
    private readonly MeetingMinutesService _meetingMinutes = new();
    private readonly MeetingMediaImportService _meetingMediaImport = new();
    private readonly LocalEmbeddingService _embeddings = new();
    private readonly TextInjectionService _textInjection = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly ObservableCollection<MemoryItem> _memories = [];
    private readonly ObservableCollection<MeetingReportItem> _meetingReports = [];
    private readonly ObservableCollection<MeetingProcessingTaskItem> _processingReportTasks = [];
    private readonly object _audioGate = new();
    private readonly Queue<byte[]> _pendingAudio = [];

    private AppSettings _settings = new();
    private StatusOverlayWindow? _overlay;
    private GlobalRightAltService? _hotkey;
    private AudioRecorderService? _recorder;
    private TencentAsrSession? _asr;
    private ForegroundTarget _target = new(IntPtr.Zero, 0, "未知应用", string.Empty);
    private IntPtr _ownWindow;
    private volatile bool _recording;
    private volatile bool _speechDetected;
    private volatile bool _asrReady;
    private int _speechFrameCount;
    private Task? _asrActivationTask;
    private Exception? _asrActivationError;
    private CancellationTokenSource? _sessionCancellation;
    private CancellationTokenSource? _noSpeechCancellation;
    private CancellationTokenSource? _mediaImportCancellation;
    private MeetingMinutesSession? _meetingSession;
    private LiveSpeechDetectionService? _liveSpeechGate;
    private DateTimeOffset _recordingStartedAt;
    private string? _lastMeetingPath;
    private string? _activeReportTaskId;
    private MeetingReportItem? _selectedMeetingReport;
    private bool _reportTaskActive;
    private bool _closing;
    private bool _exitRequested;
    private bool _trayHintShown;
    private bool _settingsReady;
    private bool _mediaImportRunning;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private System.Windows.Forms.ToolStripMenuItem? _trayOpenItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayExitItem;
    private string _statusHeaderSource = "准备中";
    private string _statusMainSource = "按一下右 Alt，开始说话";
    private string _statusHintSource = "再按一次结束；5 秒内没有检测到语音会自动关闭。";

    public MainWindow()
    {
        InitializeComponent();
        MemoryList.ItemsSource = _memories;
        MeetingReportList.ItemsSource = _meetingReports;
        ActiveMeetingTaskList.ItemsSource = _processingReportTasks;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _ownWindow = new WindowInteropHelper(this).Handle;
            _overlay = new StatusOverlayWindow();
            _overlay.SetMode(meetingMode: false);
            InitializeTrayIcon();
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;
            await _memoryRepository.InitializeAsync();
            _settings = _settingsStore.Load();
            UiLanguageService.SetLanguage(_settings.UiLanguage);
            _overlay.RefreshLanguage();
            if (_settings.InputLanguageSelectionVersion < 2)
            {
                // 旧版自动模式仍指向已过时的 16k_zh-PY。升级到当前的
                // 中英粤混合大模型；保留用户已经手动选择的其它单语模式。
                if (string.IsNullOrWhiteSpace(_settings.InputLanguage) ||
                    string.Equals(_settings.InputLanguage, "zh-PY", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_settings.TencentEngineModel, "16k_zh-PY", StringComparison.OrdinalIgnoreCase))
                {
                    _settings.InputLanguage = "zh-PY";
                    _settings.TencentEngineModel = "16k_zh_en";
                }
                _settings.InputLanguageSelectionVersion = 2;
                _settingsStore.Save(_settings);
            }
            if (!_settings.CaptureSystemAudioInMeeting)
            {
                _settings.CaptureSystemAudioInMeeting = true;
                _settingsStore.Save(_settings);
            }
            PopulateSettings();
            ApplyUiLanguage();
            _autoStart.RefreshPathIfEnabled();
            AutoStartCheckBox.IsChecked = _autoStart.IsEnabled();
            UpdateAutoStartStatus();
            _settingsReady = true;
            await RefreshMemoriesAsync();
            await RefreshMeetingReportsAsync();
            _ = WarmConnectionsAsync();
            _ = InitializeSemanticMemoryAsync();

            _hotkey = new GlobalRightAltService(Dispatcher);
            _hotkey.RightPressed += RightAlt_Pressed;
            _hotkey.LeftPressed += LeftAlt_Pressed;
            _hotkey.Start();
            DiagnosticLogService.WriteEvent(
                "AltHotkeysReady",
                $"pid={Environment.ProcessId}; hwnd=0x{_ownWindow.ToInt64():X}; version={GetType().Assembly.GetName().Version}");
            SetStatus("空闲", "右 Alt 日常输入 · 左 Alt 会议纪要", "再次按同一个按键结束。", "#5ED79A");
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("Window_Loaded", ex);
            ShowError(ex.Message);
        }
    }

    private async void RightAlt_Pressed() => await HandleAltHotkeyAsync(meetingMode: false);

    private async void LeftAlt_Pressed() => await HandleAltHotkeyAsync(meetingMode: true);

    private async Task HandleAltHotkeyAsync(bool meetingMode)
    {
        if (_closing) return;
        DiagnosticLogService.WriteEvent(
            meetingMode ? "LeftAltPressed" : "RightAltPressed",
            $"recording={_recording}; activeMeetingMode={_settings.EnableMeetingMode}; " +
            $"mediaImport={_mediaImportRunning}; visible={IsVisible}; windowState={WindowState}");
        if (_transition.CurrentCount == 0)
        {
            _overlay?.SetMode(_settings.EnableMeetingMode);
            DiagnosticLogService.WriteEvent(
                "AltHotkeyDeferred",
                $"requestedMode={(meetingMode ? "meeting" : "daily")}; reason=previous-session-processing");
            SetStatus("处理中", "上一段内容正在收尾", "完成后即可再次使用左右 Alt；会议报告进度可在“会议报告”查看。", "#F2B45F");
            _overlay?.ShowStatus("正在处理上一段", OverlayVisualState.Transcribing, TimeSpan.FromSeconds(1.5));
            return;
        }
        if (_mediaImportRunning)
        {
            _overlay?.SetMode(meetingMode: true);
            SetStatus("文件处理中", "正在生成会议纪要", "可在“会议报告”查看进度或点击取消。", "#62C6FF");
            return;
        }

        if (_recording && _settings.EnableMeetingMode != meetingMode)
        {
            _overlay?.SetMode(_settings.EnableMeetingMode);
            var key = _settings.EnableMeetingMode
                ? UiLanguageService.Text("左 Alt", "Left Alt")
                : UiLanguageService.Text("右 Alt", "Right Alt");
            SetStatus(
                "正在录音",
                UiLanguageService.IsEnglish ? $"Press {key} again to stop" : $"请再次按 {key} 结束",
                "启动和结束必须使用同一组快捷键。",
                "#F2B45F");
            _overlay?.ShowStatus(
                UiLanguageService.IsEnglish ? $"Press {key} to stop" : $"请按 {key} 结束",
                OverlayVisualState.Listening,
                TimeSpan.FromSeconds(1.5));
            return;
        }

        if (!_recording)
        {
            _overlay?.SetMode(meetingMode);
            _overlay?.SetOutputLanguage(_settings.TargetLanguage, _settings.EnableTranslation);
            _settings.EnableMeetingMode = meetingMode;
            _settings.CaptureSystemAudioInMeeting = true;
            MeetingModeCheckBox.IsChecked = meetingMode;
            MeetingSystemAudioCheckBox.IsChecked = true;
            UpdateMeetingModeUi();
        }
        await ToggleRecordingAsync();
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null) return;
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            _trayIconImage = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
        }

        _trayOpenItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayOpenItem.Click += (_, _) => Dispatcher.Invoke(ShowFromTray);
        _trayExitItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayExitItem.Click += (_, _) => Dispatcher.Invoke(ExitFromTray);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(_trayOpenItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_trayExitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        RefreshTrayLanguage();
    }

    private void ShowFromTray()
    {
        if (_closing) return;
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        Close();
    }

    private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
    {
        _exitRequested = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested || _closing) return;
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        DiagnosticLogService.WriteEvent("WindowHiddenToTray", "rightAltActive=true; leftAltActive=true");
        if (_trayIcon is not null && !_trayHintShown)
        {
            _trayHintShown = true;
            _trayIcon.ShowBalloonTip(
                1800,
                UiLanguageService.Text("速说速记X仍在运行", "VoiceMemo X is still running"),
                UiLanguageService.Text(
                    "窗口已缩到右下角托盘，右 Alt 与左 Alt 仍可随时使用。",
                    "The window is in the system tray. Right Alt and Left Alt remain available."),
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e) => await ToggleRecordingAsync();

    private async void ImportMeetingFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaImportRunning || _recording) return;
        if (!_settings.HasTencentCredentials)
        {
            ShowError("还没有配置腾讯云 ASR。请先到“连接设置”填写 AppID、SecretID 和 SecretKey。");
            return;
        }
        if (!_settings.HasDeepSeekCredentials)
        {
            ShowError("导入文件生成会议纪要需要 DeepSeek API Key。");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = UiLanguageService.Text(
                "选择要生成会议纪要的音频或视频",
                "Choose audio or video for a meeting report"),
            Filter = UiLanguageService.IsEnglish
                ? "Supported audio and video|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.mp4;*.mov;*.m4v|Audio files|*.mp3;*.wav;*.m4a;*.aac;*.wma|Video files|*.mp4;*.mov;*.m4v|All files|*.*"
                : MeetingMediaImportService.FileDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunMeetingMediaImportAsync(dialog.FileName);
    }

    private async Task RunMeetingMediaImportAsync(string mediaPath)
    {
        _mediaImportRunning = true;
        _mediaImportCancellation?.Dispose();
        _mediaImportCancellation = new CancellationTokenSource();
        ToggleButton.IsEnabled = false;
        MeetingModeCheckBox.IsEnabled = false;
        MeetingSystemAudioCheckBox.IsEnabled = false;
        ImportMeetingFileButton.IsEnabled = false;
        ImportMeetingFileFromReportsButton.IsEnabled = false;
        CancelMeetingImportButton.Visibility = Visibility.Visible;
        CancelMeetingImportButton.IsEnabled = true;
        CancelMeetingImportFromReportsButton.Visibility = Visibility.Visible;
        CancelMeetingImportFromReportsButton.IsEnabled = true;
        OpenMeetingFolderButton.Visibility = Visibility.Collapsed;
        MeetingImportProgressBar.Visibility = Visibility.Visible;
        MeetingImportProgressBar.IsIndeterminate = true;
        MeetingImportProgressBar.Value = 0;
        MeetingImportProgressText.Text = UiLanguageService.Text("准备中", "Preparing");
        ImportFileNameText.Text = Path.GetFileName(mediaPath);
        RawTranscriptBox.Clear();
        FinalTranscriptBox.Clear();
        FinalTranscriptTitleText.Text = UiLanguageService.Text("最终会议纪要", "Final meeting report");
        FinalTranscriptDescriptionText.Text = UiLanguageService.Text(
            "导入文件 → 本地人数判断（影子验证）→ Speaker 2.0 → Flash → Thinking High",
            "Imported media → local speaker analysis (shadow mode) → Speaker 2.0 → Flash → Thinking High");
        SetStatus("读取文件中", "正在准备本地说话人数分析…", "当前是影子验证期，实际仍使用 Speaker 2.0。", "#62C6FF");
        UpdateReportTask(
            3,
            "准备中",
            $"正在创建报告：{Path.GetFileName(mediaPath)}",
            "正在读取音视频文件，尚未发送到语音识别服务。",
            active: true);

        var progress = new Progress<MeetingMediaImportProgress>(UpdateMeetingImportProgress);
        try
        {
            var result = await _meetingMediaImport.ImportAsync(
                mediaPath,
                _settings,
                progress,
                _mediaImportCancellation.Token);

            var routingSummary = result.Routing.Label == LocalSpeakerLabel.Single
                ? UiLanguageService.Text(
                    "本地判断：单人候选（影子验证，实际仍使用 Speaker 2.0）",
                    "Local result: single-speaker candidate (shadow mode still uses Speaker 2.0)")
                : result.Routing.Label == LocalSpeakerLabel.NoSpeech
                    ? UiLanguageService.Text(
                        "本地判断：未发现可靠语音，已停止云端提交",
                        "Local result: no reliable speech; cloud submission stopped")
                    : UiLanguageService.Text(
                        "本地判断：多人或不确定，使用 Speaker 2.0",
                        "Local result: multiple or uncertain speakers; using Speaker 2.0");
            FinalTranscriptDescriptionText.Text =
                UiLanguageService.IsEnglish
                    ? $"{routingSummary} · analyzed in {result.Routing.AnalysisElapsed.TotalSeconds:F1}s"
                    : $"{routingSummary} · 分析 {result.Routing.AnalysisElapsed.TotalSeconds:F1} 秒";

            RawTranscriptBox.Text = result.Transcript;
            RawTranscriptBox.ScrollToHome();
            FinalTranscriptBox.Text = result.Minutes;
            FinalTranscriptBox.ScrollToHome();
            _lastMeetingPath = result.ArchivePath;
            await RefreshMeetingReportsAsync(result.ArchivePath);
            UpdateReportTask(
                100,
                "已完成",
                "会议报告已经生成",
                $"已保存：{Path.GetFileName(result.ArchivePath)}",
                active: false,
                reportPath: result.ArchivePath);
            var clipboard = await _textInjection.CopyToClipboardAsync(
                _ownWindow,
                result.Minutes,
                "imported-meeting-minutes");
            if (_settings.SaveHistory)
            {
                try
                {
                    await _memoryRepository.AddHistoryAsync(result.Transcript, result.Minutes, "导入会议纪要");
                }
                catch (Exception ex)
                {
                    DiagnosticLogService.Write("ImportedMeetingHistory", ex);
                }
            }

            OpenMeetingFolderButton.Visibility = Visibility.Visible;
            ImportFileNameText.Text = UiLanguageService.IsEnglish
                ? $"{Path.GetFileName(mediaPath)} · {MeetingMinutesService.FormatElapsed(result.Duration)} · saved"
                : $"{Path.GetFileName(mediaPath)} · {MeetingMinutesService.FormatElapsed(result.Duration)} · 已保存";
            MeetingImportProgressText.Text = UiLanguageService.Text("已完成", "Completed");
            SetStatus("已完成", clipboard.Success ? "文件会议纪要已保存并复制" : "文件会议纪要已保存",
                clipboard.Success
                    ? $"阶段摘要 {result.SegmentCount} 段 · 文件：{Path.GetFileName(result.ArchivePath)}"
                    : $"剪贴板被其他程序持续占用约 {clipboard.ElapsedMilliseconds} 毫秒；纪要文件仍完整保存：{Path.GetFileName(result.ArchivePath)}", "#5ED79A");
        }
        catch (OperationCanceledException)
        {
            MeetingImportProgressText.Text = UiLanguageService.Text("已取消", "Cancelled");
            if (_reportTaskActive)
            {
                UpdateReportTask(0, "已取消", "文件处理已取消", "没有生成新的会议报告。", active: false);
            }
            SetStatus("已取消", "已停止处理文件", "未生成纪要，原始文件没有被修改。", "#687181");
        }
        catch (Exception ex)
        {
            MeetingImportProgressText.Text = UiLanguageService.Text("未完成", "Not completed");
            if (_reportTaskActive)
            {
                UpdateReportTask(0, "未完成", "会议报告生成失败", ToFriendlyMessage(ex), active: false);
            }
            ShowError(ToFriendlyMessage(ex));
        }
        finally
        {
            _mediaImportRunning = false;
            _mediaImportCancellation?.Dispose();
            _mediaImportCancellation = null;
            MeetingImportProgressBar.IsIndeterminate = false;
            CancelMeetingImportButton.Visibility = Visibility.Collapsed;
            CancelMeetingImportFromReportsButton.Visibility = Visibility.Collapsed;
            ImportMeetingFileButton.IsEnabled = true;
            ImportMeetingFileFromReportsButton.IsEnabled = true;
            RestoreIdleControls();
        }
    }

    private void UpdateMeetingImportProgress(MeetingMediaImportProgress progress)
    {
        if (!_mediaImportRunning) return;
        if (!string.IsNullOrWhiteSpace(progress.Transcript))
        {
            RawTranscriptBox.Text = progress.Transcript;
            RawTranscriptBox.ScrollToEnd();
            if (double.IsNaN(progress.Ratio)) return;
        }

        var localizedProgressMessage = UiLanguageService.Translate(progress.Message);
        MainHintText.Text = localizedProgressMessage;
        MeetingImportProgressText.Text = localizedProgressMessage;
        var transcribing = progress.Stage == MeetingMediaImportStage.Transcribing;
        MeetingImportProgressBar.IsIndeterminate = !transcribing;
        if (transcribing && !double.IsNaN(progress.Ratio)) MeetingImportProgressBar.Value = progress.Ratio;
        HeaderStatusText.Text = UiLanguageService.Translate(progress.Stage switch
        {
            MeetingMediaImportStage.Opening => "读取文件",
            MeetingMediaImportStage.AnalyzingSpeakers => "分析人数",
            MeetingMediaImportStage.Connecting => "连接中",
            MeetingMediaImportStage.Transcribing => "转写中",
            MeetingMediaImportStage.FinalizingTranscript => "收尾中",
            MeetingMediaImportStage.Summarizing => "总结中",
            MeetingMediaImportStage.Saving => "保存中",
            _ => "处理中"
        });

        var reportPercent = progress.Stage switch
        {
            MeetingMediaImportStage.Opening => 5,
            MeetingMediaImportStage.AnalyzingSpeakers => 8,
            MeetingMediaImportStage.Connecting => 10,
            MeetingMediaImportStage.Transcribing when !double.IsNaN(progress.Ratio) => 10 + (int)Math.Round(progress.Ratio * 60),
            MeetingMediaImportStage.Transcribing => 35,
            MeetingMediaImportStage.FinalizingTranscript => 75,
            MeetingMediaImportStage.Summarizing => 85,
            MeetingMediaImportStage.Saving => 95,
            _ => 5
        };
        var reportStage = UiLanguageService.Translate(progress.Stage switch
        {
            MeetingMediaImportStage.Opening => "步骤 1/6 · 读取文件",
            MeetingMediaImportStage.AnalyzingSpeakers => "步骤 2/6 · 本地人数判断",
            MeetingMediaImportStage.Connecting => "步骤 3/6 · 建立 ASR 连接",
            MeetingMediaImportStage.Transcribing => "步骤 3/6 · 文件转写",
            MeetingMediaImportStage.FinalizingTranscript => "步骤 4/6 · 转写收尾",
            MeetingMediaImportStage.Summarizing => "步骤 5/6 · AI 总结",
            MeetingMediaImportStage.Saving => "步骤 6/6 · 保存报告",
            _ => "处理中"
        });
        UpdateReportTask(
            reportPercent,
            reportStage,
            "正在生成导入文件的会议报告",
            progress.Message,
            active: true);
    }

    private void CancelMeetingImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_mediaImportRunning) return;
        CancelMeetingImportButton.IsEnabled = false;
        CancelMeetingImportFromReportsButton.IsEnabled = false;
        MeetingImportProgressText.Text = UiLanguageService.Text("正在取消…", "Cancelling…");
        _mediaImportCancellation?.Cancel();
    }

    private async Task ToggleRecordingAsync()
    {
        if (!await _transition.WaitAsync(0)) return;
        try
        {
            if (_recording) await FinishRecordingAsync();
            else await BeginRecordingAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("ToggleRecordingAsync", ex);
            await AbortSessionAsync();
            if (_reportTaskActive)
            {
                UpdateReportTask(
                    0,
                    "未完成",
                    "会议报告生成失败",
                    ToFriendlyMessage(ex),
                    active: false);
            }
            ShowError(ToFriendlyMessage(ex));
        }
        finally
        {
            _transition.Release();
        }
    }

    private Task BeginRecordingAsync()
    {
        _overlay?.SetMode(_settings.EnableMeetingMode);
        _overlay?.SetOutputLanguage(_settings.TargetLanguage, _settings.EnableTranslation);
        if (!_settings.HasTencentCredentials)
        {
            throw new InvalidOperationException("还没有配置腾讯云 ASR。请先到“连接设置”填写 AppID、SecretID 和 SecretKey 并保存。 ");
        }
        if (_settings.EnableMeetingMode && !_settings.HasDeepSeekCredentials)
        {
            throw new InvalidOperationException("会议纪要模式需要 DeepSeek API Key，请先到“连接设置”完成配置。 ");
        }

        _target = ForegroundWindowService.Capture();
        TargetAppText.Text = _settings.EnableMeetingMode
            ? UiLanguageService.Text(
                "会议模式：结束后保存 Markdown 并复制纪要，不会自动输入到当前窗口",
                "Meeting mode: saves a Markdown report and copies it instead of inserting into the active window")
            : _target.Handle == _ownWindow
                ? UiLanguageService.Text(
                    "本页测试：完成后会复制文字，请在其他输入框中按右 Alt 体验自动输入",
                    "In-app test: the text will be copied. Use Right Alt in another text field to test insertion.")
                : UiLanguageService.IsEnglish
                    ? $"Text will be inserted into: {_target.ProcessName}"
                    : $"这次会把文字输入到：{_target.ProcessName}";
        RawTranscriptBox.Clear();
        FinalTranscriptBox.Clear();
        _lastMeetingPath = null;
        OpenMeetingFolderButton.Visibility = Visibility.Collapsed;
        MeetingModeCheckBox.IsEnabled = false;
        MeetingSystemAudioCheckBox.IsEnabled = false;
        ImportMeetingFileButton.IsEnabled = false;
        ToggleButton.IsEnabled = false;

        lock (_audioGate)
        {
            _pendingAudio.Clear();
            _speechDetected = false;
            _asrReady = false;
            _speechFrameCount = 0;
        }
        DisposeLiveSpeechGate();
        try
        {
            _liveSpeechGate = new LiveSpeechDetectionService(
                _settings.EnableMeetingMode
                    ? TimeSpan.FromMilliseconds(600)
                    : TimeSpan.FromMilliseconds(240));
        }
        catch (Exception ex)
        {
            // A missing local model must not disable dictation. The fallback
            // below is intentionally stricter than the old 80ms RMS trigger.
            DiagnosticLogService.Write("LiveSpeechGateInitialization", ex);
        }
        _asrActivationTask = null;
        _asrActivationError = null;
        _sessionCancellation?.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        _noSpeechCancellation?.Dispose();
        _noSpeechCancellation = new CancellationTokenSource();
        _recordingStartedAt = DateTimeOffset.Now;
        if (_settings.EnableMeetingMode)
        {
            UpdateReportTask(
                8,
                "录制中",
                "正在记录实时会议",
                "结束会议后会依次完成转写、阶段摘要、Thinking 总结和本地保存。",
                active: true);
            var progress = new Progress<string>(message =>
            {
                if (_recording)
                {
                    MainHintText.Text = UiLanguageService.Translate(message);
                    return;
                }
                var thinking = message.Contains("Thinking", StringComparison.OrdinalIgnoreCase) ||
                               message.Contains("最终会议纪要", StringComparison.Ordinal);
                UpdateReportTask(
                    thinking ? 75 : 55,
                    thinking ? "步骤 4/5 · Thinking 总结" : "步骤 3/5 · Flash 阶段摘要",
                    "正在整理会议内容",
                    message,
                    active: true);
            });
            _meetingSession = new MeetingMinutesSession(
                _meetingMinutes,
                _settings,
                progress,
                _sessionCancellation.Token);
        }

        var captureSystemAudio = _settings.EnableMeetingMode && _settings.CaptureSystemAudioInMeeting;
        _recorder = new AudioRecorderService();
        _recorder.AudioFrameAvailable += OnAudioFrameAvailable;
        _recorder.LevelChanged += OnAudioLevelChanged;
        _recorder.Start(captureSystemAudio);
        TargetAppText.Text += UiLanguageService.IsEnglish
            ? $" · Audio: {_recorder.AudioSourceDescription}"
            : $" · 声音来源：{_recorder.AudioSourceDescription}";

        _recording = true;
        ToggleButton.IsEnabled = true;
        ToggleButton.Content = _settings.EnableMeetingMode
            ? UiLanguageService.Text(
                "结束会议并生成纪要（也可以再按一次左 Alt）",
                "Stop meeting and create report (or press Left Alt)")
            : UiLanguageService.Text(
                "结束并输入（也可以再按一次右 Alt）",
                "Stop and insert (or press Right Alt)");
        var captureHint = !string.IsNullOrWhiteSpace(_recorder.SystemAudioWarning)
            ? _recorder.SystemAudioWarning
            : _recorder.SystemAudioActive
                ? "麦克风和电脑播放声均已接入；建议佩戴耳机避免回声。"
                : "没有检测到语音会自动关闭，不会连接腾讯 ASR。";
        SetStatus("等待语音", "请在 5 秒内开始说话", captureHint, "#FF8A4A");
        _overlay?.ShowConnectedThenListening();
        _ = WatchForNoSpeechAsync(_noSpeechCancellation.Token);
        return Task.CompletedTask;
    }

    private async Task WatchForNoSpeechAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NoSpeechTimeout, cancellationToken);
            await Dispatcher.InvokeAsync(CancelForNoSpeechAsync).Task.Unwrap();
        }
        catch (OperationCanceledException)
        {
            // Speech or a manual stop cancelled the five-second safety timer.
        }
    }

    private async Task CancelForNoSpeechAsync()
    {
        // Five seconds is the time allowed to begin speaking. If the local VAD
        // is already inside a speech segment, allow a short confirmation grace
        // instead of cutting off a user who started near the deadline.
        if (_recording && !_speechDetected && _liveSpeechGate?.HasActiveSpeech == true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900));
        }

        if (!await _transition.WaitAsync(0)) return;
        try
        {
            if (!_recording || _speechDetected) return;
            await StopWithoutTranscriptionAsync();
            SetStatus("已自动关闭", "5 秒内没有检测到语音", "本次未连接腾讯 ASR，也不会产生 AI 整理用量。", "#687181");
            _overlay?.ShowStatus("已自动关闭", OverlayVisualState.Success, TimeSpan.FromSeconds(1.2));
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task ActivateAsrAsync()
    {
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SetStatus("连接中", "检测到语音，正在连接腾讯实时语音…", "开头的声音已在本地临时缓冲，不会丢失。", "#F2B45F");
                _overlay?.ShowStatus("连接中", OverlayVisualState.Connecting);
            });

            var cancellationToken = _sessionCancellation?.Token ?? CancellationToken.None;
            var asr = await StartAsrWithRetryAsync(cancellationToken);

            lock (_audioGate)
            {
                while (_pendingAudio.Count > 0)
                {
                    asr.QueueAudio(_pendingAudio.Dequeue());
                }
                _asrReady = true;
            }

            if (_recording)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SetStatus("录音中", _settings.EnableMeetingMode ? "正在记录并区分说话人…" : "正在听你说…",
                        _settings.EnableMeetingMode ? "实时显示说话人1/2/3；结束后生成最终纪要。" : "说完后再按一下右 Alt。", "#FF8A4A");
                    _overlay?.ShowStatus("请输入语音", OverlayVisualState.Listening);
                });
            }
        }
        catch (OperationCanceledException) when (_sessionCancellation?.IsCancellationRequested == true)
        {
            // The session was intentionally stopped before ASR finished connecting.
        }
        catch (Exception ex)
        {
            _asrActivationError = ex;
            DiagnosticLogService.Write("ActivateAsrAsync", ex);
            await Dispatcher.InvokeAsync(async () =>
            {
                if (_closing || !_recording) return;
                await AbortSessionAsync();
                ShowError(ToFriendlyMessage(ex));
            }).Task.Unwrap();
        }
    }

    private async Task FinishRecordingAsync()
    {
        _recording = false;
        _noSpeechCancellation?.Cancel();
        ToggleButton.IsEnabled = false;
        AudioLevelBar.Value = 0;

        if (_settings.EnableMeetingMode)
        {
            UpdateReportTask(
                18,
                "步骤 1/5 · 音频收尾",
                "会议已经结束，正在生成报告",
                "正在停止麦克风和电脑声音采集，保留最后一句话。",
                active: true);
        }

        if (!_speechDetected)
        {
            await StopWithoutTranscriptionAsync();
            if (_settings.EnableMeetingMode)
            {
                UpdateReportTask(0, "已取消", "没有检测到有效会议语音", "本次没有生成报告。", active: false);
            }
            SetStatus("已取消", "没有检测到有效语音", "按右 Alt 可以重新开始。", "#687181");
            _overlay?.Dismiss();
            return;
        }

        SetStatus("处理中", "正在完成转写…",
            _settings.EnableMeetingMode ? "随后将用 Thinking High 生成最终会议纪要。" : "随后会用你的记忆整理文字。", "#F2B45F");
        _overlay?.ShowStatus("转写中", OverlayVisualState.Transcribing);

        var maxCapturedLevel = _recorder?.MaxObservedLevel ?? 0;
        var maxMicrophoneLevel = _recorder?.MicrophoneMaxObservedLevel ?? 0;
        var maxSystemAudioLevel = _recorder?.SystemAudioMaxObservedLevel ?? 0;
        if (_recorder is not null)
        {
            await _recorder.StopAsync();
            DetachRecorder();
        }

        if (_settings.EnableMeetingMode)
        {
            UpdateReportTask(
                30,
                "步骤 2/5 · 最终转写",
                "正在确认完整转写",
                "音频已经收齐，正在等待腾讯 ASR 返回最后一句文字。",
                active: true);
        }

        if (_asrActivationTask is not null)
        {
            await _asrActivationTask;
            _asrActivationTask = null;
        }

        if (_asrActivationError is not null)
        {
            ExceptionDispatchInfo.Capture(_asrActivationError).Throw();
        }

        if (_asr is null) throw new InvalidOperationException("语音识别会话不存在。 ");
        var rawText = await _asr.CompleteAsync();
        _asr.TranscriptChanged -= OnTranscriptChanged;
        _asr.SegmentSettled -= OnMeetingSegmentSettled;
        await _asr.DisposeAsync();
        _asr = null;
        RawTranscriptBox.Text = rawText;

        if (_settings.EnableMeetingMode)
        {
            UpdateReportTask(
                48,
                "步骤 3/5 · Flash 阶段摘要",
                "转写完成，正在提炼会议重点",
                $"已获得 {rawText.Length} 个字符的完整转写。",
                active: true);
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            if (maxCapturedLevel < 0.002)
            {
                throw new InvalidOperationException("麦克风和电脑播放声都没有采集到声音。请检查 Windows 声音设备、麦克风权限以及会议软件的扬声器设置。");
            }
            throw new InvalidOperationException(
                $"设备有声音，但腾讯没有识别到有效语音（麦克风峰值 {maxMicrophoneLevel:F3}，电脑声音峰值 {maxSystemAudioLevel:F3}）；请连续播放或说一句完整的话后再结束。 ");
        }

        if (_settings.EnableMeetingMode && !MeetingTranscriptQualityService.HasMeaningfulContent(rawText))
        {
            throw new InvalidOperationException(
                "只检测到过短的语气声或环境声，已停止生成会议报告，避免继续消耗 AI 总结额度。 ");
        }

        if (_settings.EnableMeetingMode)
        {
            await CompleteMeetingMinutesAsync(rawText);
            DisposeSessionTokens();
            return;
        }
        DisposeSessionTokens();

        float[]? queryEmbedding = null;
        if (_settings.NeedsDeepSeek && _embeddings.ModelFilesAvailable)
        {
            try
            {
                queryEmbedding = await _embeddings.CreateQueryEmbeddingAsync(rawText);
            }
            catch
            {
                // Semantic retrieval is an enhancement. Recent-history fallback remains available.
            }
        }
        var memory = await _memoryRepository.BuildContextAsync(rawText, _target.ProcessName, queryEmbedding);
        var finalText = rawText;
        var aiWarning = string.Empty;
        try
        {
            if (_settings.NeedsDeepSeek && _settings.HasDeepSeekCredentials)
            {
                var processingHint = _settings.EnableTranslation
                    ? UiLanguageService.IsEnglish
                        ? $"DeepSeek is refining and translating into {GetLocalizedTargetLanguageName(_settings.TargetLanguage)}."
                        : $"DeepSeek 正在整理并翻译为{DeepSeekTextService.GetTargetLanguageName(_settings.TargetLanguage)}。"
                    : "DeepSeek V4 Flash 正在去口头禅、修正标点。";
                SetStatus("整理中", "正在按你的习惯处理文字…", processingHint, "#A48CFF");
                _overlay?.ShowStatus("转写中", OverlayVisualState.Transcribing);
            }
            finalText = await _deepSeek.RefineAsync(rawText, memory, _settings, _target.ProcessName);
        }
        catch (Exception ex)
        {
            aiWarning = ToFriendlyMessage(ex);
            DiagnosticLogService.Write("DeepSeekRefine", ex);
            if (_settings.EnableTranslation)
            {
                throw new InvalidOperationException(
                    UiLanguageService.IsEnglish
                        ? $"Translation to {GetLocalizedTargetLanguageName(_settings.TargetLanguage)} failed. The Chinese transcript was not inserted: {aiWarning}"
                        : $"翻译为{DeepSeekTextService.GetTargetLanguageName(_settings.TargetLanguage)}失败，已阻止把中文原文误当成翻译结果输入：{aiWarning}",
                    ex);
            }
            finalText = rawText;
        }

        FinalTranscriptBox.Text = finalText;
        long savedHistoryId = 0;
        if (_settings.SaveHistory)
        {
            savedHistoryId = await _memoryRepository.AddHistoryAsync(rawText, finalText, _target.ProcessName);
        }

        SetStatus("回填中", "正在把文字送回输入框…", "", "#62C6FF");
        _overlay?.Dismiss();
        var delivery = await _textInjection.DeliverAsync(_target, _ownWindow, finalText);
        RestoreIdleControls();

        if (delivery.WasInjected && !string.IsNullOrEmpty(aiWarning))
        {
            SetStatus("已回填原文", "文字已输入，但 AI 整理暂时失败", aiWarning, "#F2B45F");
        }
        else if (delivery.WasInjected)
        {
            SetStatus("完成", "文字已经输入", "继续按右 Alt 可以开始下一段。", "#5ED79A");
        }
        else if (delivery.Outcome == TextInjectionOutcome.CopiedOnly)
        {
            SetStatus(
                "已复制",
                "文字已经复制到剪贴板",
                delivery.Detail + UiLanguageService.Text(" 请在目标输入框按 Ctrl+V。", " Press Ctrl+V in the target field."),
                "#62C6FF");
            _overlay?.ShowStatus("已复制", OverlayVisualState.Success, TimeSpan.FromSeconds(1.2));
        }
        else
        {
            SetStatus("未能回填", "目标输入框或剪贴板暂时不可用", delivery.Detail, "#FF6B6B");
            _overlay?.ShowStatus("未完成", OverlayVisualState.Error, TimeSpan.FromSeconds(2));
        }

        if (savedHistoryId > 0)
        {
            _ = StoreHistoryEmbeddingAsync(savedHistoryId, finalText);
        }
    }

    private async Task<TencentAsrSession> StartAsrWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var asr = new TencentAsrSession();
            asr.TranscriptChanged += OnTranscriptChanged;
            if (_settings.EnableMeetingMode) asr.SegmentSettled += OnMeetingSegmentSettled;
            _asr = asr;

            try
            {
                await asr.StartAsync(
                    _settings,
                    cancellationToken,
                    enableSpeakerDiarization: _settings.EnableMeetingMode);
                return asr;
            }
            catch (Exception ex) when (
                attempt < maxAttempts &&
                !cancellationToken.IsCancellationRequested &&
                IsTransientAsrConnectionFailure(ex))
            {
                DiagnosticLogService.WriteEvent(
                    "TencentAsrConnectionRetry",
                    $"attempt={attempt}; reason={ex.GetBaseException().Message}");
                asr.TranscriptChanged -= OnTranscriptChanged;
                asr.SegmentSettled -= OnMeetingSegmentSettled;
                await asr.DisposeAsync();
                if (ReferenceEquals(_asr, asr)) _asr = null;

                await Dispatcher.InvokeAsync(() =>
                {
                    SetStatus("连接重试中", "网络有波动，正在重新连接腾讯实时语音…", "已录下的开头仍保存在本地。", "#F2B45F");
                    _overlay?.ShowStatus("连接中", OverlayVisualState.Connecting);
                });
                await Task.Delay(250, cancellationToken);
            }
        }

        throw new InvalidOperationException("腾讯 ASR 连接失败。 ");
    }

    private static bool IsTransientAsrConnectionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("403", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (current is TimeoutException or System.Net.WebSockets.WebSocketException or
                System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or IOException)
            {
                return true;
            }
        }
        return false;
    }

    private void OnAudioFrameAvailable(byte[] chunk, double level)
    {
        TencentAsrSession? readyAsr = null;
        var activate = false;
        var liveGate = _liveSpeechGate;
        var vadDetected = liveGate?.TryAcceptPcm16(chunk) == true;

        lock (_audioGate)
        {
            if (!_recording) return;

            if (_asrReady)
            {
                readyAsr = _asr;
            }
            else
            {
                _pendingAudio.Enqueue(chunk);
                if (!_speechDetected)
                {
                    while (_pendingAudio.Count > PreludeFrameLimit) _pendingAudio.Dequeue();
                    if (liveGate is null)
                    {
                        _speechFrameCount = level >= SpeechDetectionLevel ? _speechFrameCount + 1 : 0;
                    }
                    if (vadDetected || liveGate is null && _speechFrameCount >= SpeechDetectionFrames)
                    {
                        _speechDetected = true;
                        activate = true;
                    }
                }
            }
        }

        readyAsr?.QueueAudio(chunk);
        if (!activate) return;

        DisposeLiveSpeechGate();
        _noSpeechCancellation?.Cancel();
        _asrActivationTask = ActivateAsrAsync();
    }

    private void OnAudioLevelChanged(double level) => Dispatcher.BeginInvoke(() => AudioLevelBar.Value = level);

    private void OnTranscriptChanged(string text) => Dispatcher.BeginInvoke(() =>
    {
        RawTranscriptBox.Text = text;
        RawTranscriptBox.ScrollToEnd();
    });

    private void OnMeetingSegmentSettled(int index, string text) => _meetingSession?.AddSettledSegment(text);

    private async Task CompleteMeetingMinutesAsync(string rawText)
    {
        if (_meetingSession is null) throw new InvalidOperationException("会议整理会话不存在。 ");
        UpdateReportTask(
            55,
            "步骤 3/5 · Flash 阶段摘要",
            "正在整理会议片段",
            "阶段摘要用于压缩长会议；最终结论仍以完整转写为准。",
            active: true);
        SetStatus("总结中", "Thinking 正在生成最终会议纪要…", "请稍候，完成后会自动保存并复制。", "#A48CFF");
        _overlay?.ShowStatus("转写中", OverlayVisualState.Transcribing);

        var duration = DateTimeOffset.Now - _recordingStartedAt;
        var result = await _meetingSession.CompleteAsync(rawText, duration, CancellationToken.None);
        UpdateReportTask(
            88,
            "步骤 5/5 · 准备保存",
            "AI 总结已经完成",
            "正在把会议纪要和原始转写写入本机报告文件。",
            active: true);
        await _meetingSession.DisposeAsync();
        _meetingSession = null;
        FinalTranscriptBox.Text = result.Minutes;
        FinalTranscriptBox.ScrollToHome();

        _lastMeetingPath = await MeetingArchiveService.SaveAsync(
            result.Minutes,
            rawText,
            _recordingStartedAt,
            duration,
            _settings.UiLanguage);
        await RefreshMeetingReportsAsync(_lastMeetingPath);
        UpdateReportTask(
            100,
            "已完成",
            "会议报告已经生成",
            $"已保存：{Path.GetFileName(_lastMeetingPath)}",
            active: false,
            reportPath: _lastMeetingPath);

        if (_settings.SaveHistory)
        {
            try
            {
                await _memoryRepository.AddHistoryAsync(rawText, result.Minutes, "会议纪要");
            }
            catch (Exception ex)
            {
                DiagnosticLogService.Write("LiveMeetingHistory", ex);
            }
        }

        var clipboard = await _textInjection.CopyToClipboardAsync(
            _ownWindow,
            result.Minutes,
            "live-meeting-minutes");

        RestoreIdleControls();
        OpenMeetingFolderButton.Visibility = Visibility.Visible;
        _overlay?.Dismiss();
        SetStatus(
            "已完成",
            clipboard.Success ? "会议纪要已保存并复制" : "会议纪要已保存，剪贴板暂时不可用",
            clipboard.Success
                ? $"阶段摘要 {result.SegmentCount} 段 · 文件：{Path.GetFileName(_lastMeetingPath)}"
                : $"剪贴板被其他程序持续占用约 {clipboard.ElapsedMilliseconds} 毫秒；文件已完整保存：{Path.GetFileName(_lastMeetingPath)}",
            "#5ED79A");
        DiagnosticLogService.WriteEvent(
            "MeetingCompleted",
            $"path={_lastMeetingPath}; transcriptLength={rawText.Length}; minutesLength={result.Minutes.Length}; " +
            $"segments={result.SegmentCount}; clipboardSuccess={clipboard.Success}; " +
            $"clipboardAttempts={clipboard.Attempts}; clipboardElapsedMs={clipboard.ElapsedMilliseconds}");
    }

    private async Task AbortSessionAsync()
    {
        _recording = false;
        _noSpeechCancellation?.Cancel();
        _sessionCancellation?.Cancel();
        RestoreIdleControls();
        // Cancel any pending connected→listening transition before the caller
        // displays an error state. Otherwise a late phase tick can resurrect
        // a stale "Speak now" overlay after the session has already failed.
        _overlay?.Dismiss();
        DisposeLiveSpeechGate();
        AudioLevelBar.Value = 0;
        if (_recorder is not null)
        {
            try { await _recorder.StopAsync(); } catch { }
            DetachRecorder();
        }
        if (_asr is not null)
        {
            _asr.TranscriptChanged -= OnTranscriptChanged;
            _asr.SegmentSettled -= OnMeetingSegmentSettled;
            await _asr.DisposeAsync();
            _asr = null;
        }
        lock (_audioGate)
        {
            _pendingAudio.Clear();
            _asrReady = false;
            _speechDetected = false;
        }
        DisposeSessionTokens();
        if (_meetingSession is not null)
        {
            await _meetingSession.DisposeAsync();
            _meetingSession = null;
        }
    }

    private async Task StopWithoutTranscriptionAsync()
    {
        _recording = false;
        _noSpeechCancellation?.Cancel();
        _sessionCancellation?.Cancel();
        RestoreIdleControls();
        DisposeLiveSpeechGate();
        AudioLevelBar.Value = 0;

        if (_recorder is not null)
        {
            try { await _recorder.StopAsync(); } catch { }
            DetachRecorder();
        }
        if (_asrActivationTask is not null)
        {
            try { await _asrActivationTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            catch { }
        }
        if (_asr is not null)
        {
            _asr.TranscriptChanged -= OnTranscriptChanged;
            _asr.SegmentSettled -= OnMeetingSegmentSettled;
            await _asr.DisposeAsync();
            _asr = null;
        }

        lock (_audioGate)
        {
            _pendingAudio.Clear();
            _asrReady = false;
            _speechDetected = false;
        }
        _asrActivationTask = null;
        if (_meetingSession is not null)
        {
            await _meetingSession.DisposeAsync();
            _meetingSession = null;
        }
        DisposeSessionTokens();
    }

    private void DisposeSessionTokens()
    {
        _noSpeechCancellation?.Dispose();
        _noSpeechCancellation = null;
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
    }

    private void DisposeLiveSpeechGate()
    {
        var gate = Interlocked.Exchange(ref _liveSpeechGate, null);
        gate?.Dispose();
    }


    private void DetachRecorder()
    {
        if (_recorder is null) return;
        _recorder.AudioFrameAvailable -= OnAudioFrameAvailable;
        _recorder.LevelChanged -= OnAudioLevelChanged;
        _recorder.Dispose();
        _recorder = null;
    }

    private void PopulateSettings()
    {
        TencentAppIdBox.Text = _settings.TencentAppId;
        TencentSecretIdBox.Text = _settings.TencentSecretId;
        TencentSecretKeyBox.Password = _settings.TencentSecretKey;
        TencentEngineBox.Text = _settings.TencentEngineModel;
        DeepSeekApiKeyBox.Password = _settings.DeepSeekApiKey;
        DeepSeekModelBox.Text = _settings.DeepSeekModel;
        DeepSeekBaseUrlBox.Text = _settings.DeepSeekBaseUrl;
        EnableAiCheckBox.IsChecked = _settings.EnableAiRefinement;
        EnableSmartStructuringCheckBox.IsChecked = _settings.EnableSmartStructuring;
        EnableTranslationCheckBox.IsChecked = _settings.EnableTranslation;
        MeetingModeCheckBox.IsChecked = _settings.EnableMeetingMode;
        MeetingSystemAudioCheckBox.IsChecked = true;
        var selectedInputLanguage = InputLanguageComboBox.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, _settings.InputLanguage, StringComparison.OrdinalIgnoreCase));
        InputLanguageComboBox.SelectedItem = selectedInputLanguage ?? InputLanguageComboBox.Items[0];
        var selectedLanguage = TargetLanguageComboBox.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, _settings.TargetLanguage, StringComparison.OrdinalIgnoreCase));
        TargetLanguageComboBox.SelectedItem = selectedLanguage ?? TargetLanguageComboBox.Items[0];
        SyncOverlayOutputLanguage();
        SaveHistoryCheckBox.IsChecked = _settings.SaveHistory;
        UpdateMeetingModeUi();
        UpdateConfigStatus();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var inputLanguage = GetSelectedInputLanguage();
        _settings = new AppSettings
        {
            TencentAppId = TencentAppIdBox.Text.Trim(),
            TencentSecretId = TencentSecretIdBox.Text.Trim(),
            TencentSecretKey = TencentSecretKeyBox.Password.Trim(),
            TencentEngineModel = GetEngineModelForInputLanguage(inputLanguage),
            InputLanguage = inputLanguage,
            InputLanguageSelectionVersion = 2,
            UiLanguage = _settings.UiLanguage,
            DeepSeekApiKey = DeepSeekApiKeyBox.Password.Trim(),
            DeepSeekModel = string.IsNullOrWhiteSpace(DeepSeekModelBox.Text) ? "deepseek-v4-flash" : DeepSeekModelBox.Text.Trim(),
            DeepSeekBaseUrl = string.IsNullOrWhiteSpace(DeepSeekBaseUrlBox.Text) ? "https://api.deepseek.com" : DeepSeekBaseUrlBox.Text.Trim(),
            EnableAiRefinement = EnableAiCheckBox.IsChecked == true,
            EnableSmartStructuring = EnableSmartStructuringCheckBox.IsChecked == true,
            EnableTranslation = EnableTranslationCheckBox.IsChecked == true,
            TargetLanguage = GetSelectedTargetLanguage(),
            EnableMeetingMode = false,
            CaptureSystemAudioInMeeting = true,
            MeetingSummaryTemplate = _settings.MeetingSummaryTemplate,
            MeetingSummaryTemplateEnglish = _settings.MeetingSummaryTemplateEnglish,
            SaveHistory = SaveHistoryCheckBox.IsChecked == true
        };
        _settingsStore.Save(_settings);
        SyncOverlayOutputLanguage();
        UpdateConfigStatus();
        _ = WarmConnectionsAsync();
        SetStatus("设置已保存", "连接信息已安全保存在本机", "现在可以切回任意输入框按右 Alt 测试。", "#5ED79A");
    }

    private void UpdateConfigStatus()
    {
        var asr = _settings.HasTencentCredentials
            ? UiLanguageService.Text("腾讯 ASR 已配置", "Tencent ASR configured")
            : UiLanguageService.Text("腾讯 ASR 未配置", "Tencent ASR not configured");
        var ai = _settings.HasDeepSeekCredentials
            ? UiLanguageService.Text("DeepSeek 已配置", "DeepSeek configured")
            : UiLanguageService.Text("DeepSeek 未配置（将直接使用 ASR 原文）", "DeepSeek not configured (raw ASR text will be used)");
        var semantic = _embeddings.ModelFilesAvailable
            ? UiLanguageService.Text("本地语义记忆已就绪", "Local semantic memory ready")
            : UiLanguageService.Text("本地语义模型未安装", "Local semantic model not installed");
        ConfigStatusText.Text = UiLanguageService.IsEnglish
            ? $"{asr} · {ai} · {semantic} · keys encrypted for this Windows account"
            : $"{asr} · {ai} · {semantic} · 密钥由 Windows 当前账户加密保存";
    }

    private void UiLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.UiLanguage = UiLanguageService.IsEnglish
            ? UiLanguageService.Chinese
            : UiLanguageService.English;
        UiLanguageService.SetLanguage(_settings.UiLanguage);
        _settingsStore.Save(_settings);
        ApplyUiLanguage();
        SetStatus(
            "界面语言已切换",
            "客户端和悬浮提示已更新",
            "语言选择已经保存在本机，下次启动会继续使用。",
            "#5ED79A");
    }

    private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (MainTabs.SelectedItem is System.Windows.Controls.TabItem { Content: DependencyObject content })
                UiLanguageService.Apply(content);
            ApplyLanguageToRealizedItems(MeetingReportList);
            ApplyLanguageToRealizedItems(MemoryList);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void LocalizedElement_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element) UiLanguageService.Apply(element);
    }

    private static void ApplyLanguageToRealizedItems(System.Windows.Controls.ItemsControl itemsControl)
    {
        foreach (var item in itemsControl.Items)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container)
                UiLanguageService.Apply(container);
        }
    }

    private void ApplyUiLanguage()
    {
        UiLanguageService.Apply(this);
        AppTitleText.FontSize = UiLanguageService.IsEnglish ? 23 : 22;
        HomeSectionTitleText.FontSize = UiLanguageService.IsEnglish ? 24 : 23;
        DailyHotkeyTitleText.FontSize = UiLanguageService.IsEnglish ? 18.5 : 22;
        MeetingHotkeyTitleText.FontSize = UiLanguageService.IsEnglish ? 18.5 : 22;
        UiLanguageButtonPrimaryText.Text = UiLanguageService.IsEnglish ? "中文" : "English";
        UiLanguageButtonSecondaryText.Text = UiLanguageService.IsEnglish ? "界面语言" : "UI language";
        UiLanguageButton.ToolTip = UiLanguageService.Text(
            "切换客户端、通知和悬浮框语言",
            "Switch the interface, notifications, and status overlay language");
        _overlay?.RefreshLanguage();
        SyncOverlayOutputLanguage();
        RefreshTrayLanguage();
        UpdateAutoStartStatus();
        UpdateMeetingModeUi();
        UpdateConfigStatus();
        RenderStatus();
        ActiveMeetingTaskCountText.Text = UiLanguageService.IsEnglish
            ? $"{_processingReportTasks.Count} tasks"
            : $"{_processingReportTasks.Count} 项";
        MeetingReportCountText.Text = _meetingReports.Count == 0
            ? UiLanguageService.Text("还没有会议报告", "No meeting reports yet")
            : UiLanguageService.IsEnglish
                ? $"{_meetingReports.Count} reports · stored only on this computer"
                : $"共 {_meetingReports.Count} 份 · 只保存在这台电脑";
    }

    private void RefreshTrayLanguage()
    {
        if (_trayOpenItem is not null)
            _trayOpenItem.Text = UiLanguageService.Text("打开速说速记X", "Open VoiceMemo X");
        if (_trayExitItem is not null)
            _trayExitItem.Text = UiLanguageService.Text("退出客户端", "Exit VoiceMemo X");
        if (_trayIcon is not null)
            _trayIcon.Text = UiLanguageService.Text(
                "速说速记X · 右 Alt 输入 · 左 Alt 会议",
                "VoiceMemo X · Right Alt dictation · Left Alt meeting");
    }

    private void TranslationOption_Changed(object sender, RoutedEventArgs e)
    {
        SaveHomepageOptions();
    }

    private void StructureOption_Changed(object sender, RoutedEventArgs e)
    {
        SaveHomepageOptions();
    }

    private void MeetingModeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        _settings.EnableMeetingMode = MeetingModeCheckBox.IsChecked == true;
        _settingsStore.Save(_settings);
        UpdateMeetingModeUi();
        SetStatus("模式已切换",
            _settings.EnableMeetingMode ? "会议纪要模式已开启" : "普通语音输入模式已开启",
            _settings.EnableMeetingMode ? "按右 Alt 开始记录会议，再按一次结束并生成纪要。" : "按右 Alt 开始普通语音输入。",
            "#5ED79A");
    }

    private void MeetingSystemAudioCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        _settings.CaptureSystemAudioInMeeting = MeetingSystemAudioCheckBox.IsChecked == true;
        _settingsStore.Save(_settings);
        UpdateMeetingModeUi();
        SetStatus(
            "声音来源已更新",
            _settings.CaptureSystemAudioInMeeting ? "会议将同时收录对方声音" : "会议只收录本机麦克风",
            _settings.CaptureSystemAudioInMeeting
                ? "系统会捕获 Windows 默认播放设备中的飞书、微信或浏览器声音。"
                : "关闭后不会采集电脑扬声器输出。",
            "#5ED79A");
    }

    private void UpdateMeetingModeUi()
    {
        var enabled = MeetingModeCheckBox.IsChecked == true;
        MeetingModeSummaryText.Text = enabled
            ? MeetingSystemAudioCheckBox.IsChecked == true
                ? UiLanguageService.Text(
                    "已开启 · 麦克风 + 对方声音 · 腾讯大模型2.0区分说话人",
                    "On · microphone + remote audio · Tencent Speaker 2.0")
                : UiLanguageService.Text(
                    "已开启 · 仅麦克风 · 腾讯大模型2.0区分说话人",
                    "On · microphone only · Tencent Speaker 2.0")
            : UiLanguageService.Text(
                "关闭 · 仍使用普通语音输入模式",
                "Off · regular dictation remains available");
        MeetingSystemAudioCheckBox.IsEnabled = enabled && !_recording && !_mediaImportRunning;
        MeetingAudioSourceText.Text = !enabled
            ? UiLanguageService.Text(
                "此选项只影响会议模式，不会改变普通右 Alt 语音输入。",
                "This option affects meeting mode only. Right Alt dictation is unchanged.")
            : MeetingSystemAudioCheckBox.IsChecked == true
                ? UiLanguageService.Text(
                    "电脑上播放的远端参会者声音会直接采集；建议佩戴耳机避免回声。",
                    "Remote participant audio playing on this computer is captured. Use headphones to avoid echo.")
                : UiLanguageService.Text(
                    "当前只记录 Windows 默认麦克风，远端参会者可能无法清晰识别。",
                    "Only the default Windows microphone is recorded. Remote participants may not be recognized clearly.");
        FinalTranscriptTitleText.Text = enabled
            ? UiLanguageService.Text("最终会议纪要", "Final meeting report")
            : UiLanguageService.Text("整理 / 翻译后文字", "Refined / translated text");
        FinalTranscriptDescriptionText.Text = enabled
            ? UiLanguageService.Text(
                "完整转写为事实依据；纪要会保存到本机并复制到剪贴板",
                "The full transcript is the source of truth. The report is saved locally and copied.")
            : UiLanguageService.Text(
                "DeepSeek Flash + 你的本地记忆 + 目标语言",
                "DeepSeek Flash + local memory + target language");
        EnableTranslationCheckBox.IsEnabled = !enabled;
        TargetLanguageComboBox.IsEnabled = !enabled;
        ToggleButton.Content = enabled
            ? UiLanguageService.Text(
                "开始会议记录（也可以直接按右 Alt）",
                "Start meeting capture (or press Left Alt)")
            : UiLanguageService.Text(
                "开始录音（也可以直接按右 Alt）",
                "Start dictation (or press Right Alt)");
        if (!enabled) OpenMeetingFolderButton.Visibility = Visibility.Collapsed;
    }

    private void RestoreIdleControls()
    {
        ToggleButton.IsEnabled = !_mediaImportRunning;
        MeetingModeCheckBox.IsEnabled = !_mediaImportRunning;
        MeetingSystemAudioCheckBox.IsEnabled = !_mediaImportRunning && !_recording && _settings.EnableMeetingMode;
        ImportMeetingFileButton.IsEnabled = !_mediaImportRunning && !_recording;
        ImportMeetingFileFromReportsButton.IsEnabled = !_mediaImportRunning && !_recording;
        ToggleButton.Content = _settings.EnableMeetingMode
            ? UiLanguageService.Text(
                "开始会议记录（也可以直接按右 Alt）",
                "Start meeting capture (or press Left Alt)")
            : UiLanguageService.Text(
                "开始录音（也可以直接按右 Alt）",
                "Start dictation (or press Right Alt)");
    }

    private void UpdateReportTask(
        int percent,
        string stage,
        string title,
        string detail,
        bool active,
        string? reportPath = null)
    {
        _reportTaskActive = active;
        if (active)
        {
            _activeReportTaskId ??= $"report-{Guid.NewGuid():N}";
            var item = new MeetingProcessingTaskItem(
                _activeReportTaskId,
                UiLanguageService.Translate(title),
                UiLanguageService.Translate(stage),
                UiLanguageService.Translate(detail),
                Math.Clamp(percent, 0, 100));
            var index = _processingReportTasks
                .Select((task, taskIndex) => (task, taskIndex))
                .FirstOrDefault(pair => string.Equals(pair.task.Id, _activeReportTaskId, StringComparison.Ordinal))
                .taskIndex;
            if (_processingReportTasks.Count > 0 &&
                index >= 0 &&
                index < _processingReportTasks.Count &&
                string.Equals(_processingReportTasks[index].Id, _activeReportTaskId, StringComparison.Ordinal))
            {
                _processingReportTasks[index] = item;
            }
            else
            {
                _processingReportTasks.Add(item);
            }
        }
        else if (_activeReportTaskId is not null)
        {
            var existing = _processingReportTasks.FirstOrDefault(task =>
                string.Equals(task.Id, _activeReportTaskId, StringComparison.Ordinal));
            if (existing is not null) _processingReportTasks.Remove(existing);
            _activeReportTaskId = null;
        }

        ActiveMeetingTaskCountText.Text = UiLanguageService.IsEnglish
            ? $"{_processingReportTasks.Count} tasks"
            : $"{_processingReportTasks.Count} 项";
        var hasTasks = _processingReportTasks.Count > 0;
        NoActiveMeetingTasksText.Visibility = hasTasks ? Visibility.Collapsed : Visibility.Visible;
        ActiveMeetingTasksScroll.Visibility = hasTasks ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshMeetingReportsAsync(string? selectPath = null)
    {
        var preferredPath = selectPath ?? _selectedMeetingReport?.FullPath;
        var reports = await MeetingReportCatalogService.LoadAsync();
        _meetingReports.Clear();
        foreach (var report in reports) _meetingReports.Add(report);
        MeetingReportCountText.Text = reports.Count == 0
            ? UiLanguageService.Text("还没有会议报告", "No meeting reports yet")
            : UiLanguageService.IsEnglish
                ? $"{reports.Count} reports · stored only on this computer"
                : $"共 {reports.Count} 份 · 只保存在这台电脑";

        MeetingReportEmptyPanel.Visibility = reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MeetingReportList.Visibility = reports.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var preferred = !string.IsNullOrWhiteSpace(preferredPath)
            ? reports.FirstOrDefault(report => string.Equals(report.FullPath, preferredPath, StringComparison.OrdinalIgnoreCase))
            : null;
        MeetingReportList.SelectedItem = null;
        if (preferred is not null) MeetingReportList.ScrollIntoView(preferred);
    }

    private async void RefreshMeetingReportsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshMeetingReportsAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("RefreshMeetingReports", ex);
            ShowError("读取会议报告失败：" + ToFriendlyMessage(ex));
        }
    }

    private void EditMeetingSummaryTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var savedTemplate = UiLanguageService.IsEnglish
            ? _settings.MeetingSummaryTemplateEnglish
            : _settings.MeetingSummaryTemplate;
        var editor = new MeetingSummaryTemplateDialog(savedTemplate, _settings.UiLanguage)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true) return;

        if (UiLanguageService.IsEnglish)
        {
            _settings.MeetingSummaryTemplateEnglish = editor.TemplateText;
        }
        else
        {
            _settings.MeetingSummaryTemplate = editor.TemplateText;
        }
        _settingsStore.Save(_settings);
        var usesDefault = string.IsNullOrWhiteSpace(editor.TemplateText);
        DiagnosticLogService.WriteEvent(
            "MeetingSummaryTemplateSaved",
            $"language={_settings.UiLanguage}; usesDefault={usesDefault}; length={MeetingSummaryTemplateDefaults.Resolve(editor.TemplateText, _settings.UiLanguage).Length}");
        SetStatus(
            "模板已保存",
            usesDefault ? "已恢复默认会议总结模板" : "已更新会议总结模板",
            "从下一份会议报告开始使用，已有报告不会改变。",
            "#5ED79A");
    }

    private async void DeleteMeetingReportButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: MeetingReportItem report }) return;

        var confirmation = new DeleteMeetingReportDialog(report)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true) return;

        try
        {
            MeetingReportCatalogService.MoveToRecycleBin(report);
            DiagnosticLogService.WriteEvent(
                "DeleteMeetingReport",
                $"Moved report to Recycle Bin. path={report.FullPath}");

            if (string.Equals(_selectedMeetingReport?.FullPath, report.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                BackToMeetingReportsButton_Click(this, new RoutedEventArgs());
            }

            await RefreshMeetingReportsAsync();
            SetStatus(
                "已删除",
                "会议报告已移入回收站",
                "如果误删，可以从 Windows 回收站恢复。",
                "#5ED79A");
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("DeleteMeetingReport", ex);
            ShowError("删除报告失败：" + ToFriendlyMessage(ex));
        }
    }

    private async void MeetingReportList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var report = MeetingReportList.SelectedItem as MeetingReportItem;
        if (report is null) return;
        _selectedMeetingReport = report;
        var available = report is not null && File.Exists(report.FullPath);
        CopyMeetingReportButton.IsEnabled = available;
        ExportMeetingReportButton.IsEnabled = available;
        if (!available)
        {
            return;
        }

        MeetingReportHistoryView.Visibility = Visibility.Collapsed;
        MeetingReportDetailView.Visibility = Visibility.Visible;
        MeetingReportPreviewTitleText.Text = report!.Title;
        MeetingReportPreviewMetaText.Text = $"{report.CreatedAtText} · {report.DurationText} · {report.FileName}";
        MeetingReportDocumentViewer.Document = MeetingReportDocumentRenderer.CreateMessage("正在读取报告…");
        try
        {
            var content = await MeetingReportCatalogService.ReadAsync(report);
            if (ReferenceEquals(_selectedMeetingReport, report))
            {
                MeetingReportDocumentViewer.Document = MeetingReportDocumentRenderer.Create(content);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("ReadMeetingReport", ex);
            if (ReferenceEquals(_selectedMeetingReport, report))
            {
                MeetingReportDocumentViewer.Document = MeetingReportDocumentRenderer.CreateMessage(
                    "报告暂时无法读取：" + ToFriendlyMessage(ex));
            }
        }
    }

    private void BackToMeetingReportsButton_Click(object sender, RoutedEventArgs e)
    {
        MeetingReportDetailView.Visibility = Visibility.Collapsed;
        MeetingReportHistoryView.Visibility = Visibility.Visible;
        _selectedMeetingReport = null;
        MeetingReportList.SelectedItem = null;
        CopyMeetingReportButton.IsEnabled = false;
        ExportMeetingReportButton.IsEnabled = false;
    }

    private async void CopyMeetingReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeetingReport is null) return;
        try
        {
            var content = await MeetingReportCatalogService.ReadAsync(_selectedMeetingReport);
            var result = await _textInjection.CopyToClipboardAsync(
                _ownWindow,
                content,
                "meeting-report-page");
            SetStatus(
                result.Success ? "已复制" : "复制失败",
                result.Success ? "会议报告全文已复制" : "剪贴板暂时被其他程序占用",
                result.Success ? "可以粘贴到飞书、Word 或聊天窗口。" : result.ContentionDescription,
                result.Success ? "#5ED79A" : "#FF6B6B");
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("CopyMeetingReport", ex);
            ShowError(ToFriendlyMessage(ex));
        }
    }

    private async void ExportMeetingReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeetingReport is null) return;
        var dialog = new SaveFileDialog
        {
            Title = UiLanguageService.Text("导出会议报告", "Export meeting report"),
            FileName = _selectedMeetingReport.FileName,
            DefaultExt = ".md",
            Filter = UiLanguageService.IsEnglish
                ? "Markdown document|*.md|Plain text file|*.txt"
                : "Markdown 文档|*.md|纯文本文件|*.txt",
            FilterIndex = 1,
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var content = await MeetingReportCatalogService.ReadAsync(_selectedMeetingReport);
            if (string.Equals(Path.GetExtension(dialog.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
            {
                content = MeetingReportCatalogService.ToPlainText(content);
            }
            await File.WriteAllTextAsync(dialog.FileName, content, new System.Text.UTF8Encoding(false));
            SetStatus("导出完成", "会议报告已经导出", dialog.FileName, "#5ED79A");
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("ExportMeetingReport", ex);
            ShowError("导出失败：" + ToFriendlyMessage(ex));
        }
    }

    private void OpenMeetingFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.MeetingsDirectory);
        Process.Start(new ProcessStartInfo(AppPaths.MeetingsDirectory) { UseShellExecute = true });
    }

    private void TargetLanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settingsReady && e.AddedItems.Count > 0)
        {
            // The target-language selector is the user's primary intent. Do not
            // leave it in a misleading state where it says "English" while the
            // independent translation flag still silently outputs Chinese.
            EnableTranslationCheckBox.IsChecked = true;
        }
        SaveHomepageOptions();
    }

    private void TargetLanguageComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (!_settingsReady) return;

        // SelectionChanged does not fire when the user re-selects the currently
        // visible option. Opening this dedicated target-language control still
        // expresses the intent to use translation, so activate it immediately.
        EnableTranslationCheckBox.IsChecked = true;
        SaveHomepageOptions();
    }

    private void InputLanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SaveHomepageOptions();
    }

    private void SaveHomepageOptions()
    {
        if (!_settingsReady) return;
        _settings.EnableTranslation = EnableTranslationCheckBox.IsChecked == true;
        _settings.EnableSmartStructuring = EnableSmartStructuringCheckBox.IsChecked == true;
        _settings.InputLanguage = GetSelectedInputLanguage();
        _settings.TencentEngineModel = GetEngineModelForInputLanguage(_settings.InputLanguage);
        _settings.TargetLanguage = GetSelectedTargetLanguage();
        TencentEngineBox.Text = _settings.TencentEngineModel;
        _settingsStore.Save(_settings);
        SyncOverlayOutputLanguage();
        DiagnosticLogService.WriteEvent(
            "OutputLanguageSettings",
            $"translation={_settings.EnableTranslation}; target={_settings.TargetLanguage}; input={_settings.InputLanguage}");
    }

    private void SyncOverlayOutputLanguage()
    {
        _overlay?.SetOutputLanguage(
            GetSelectedTargetLanguage(),
            EnableTranslationCheckBox.IsChecked == true);
    }

    private string GetSelectedInputLanguage()
    {
        return (InputLanguageComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "zh-PY";
    }

    private string GetSelectedTargetLanguage()
    {
        return (TargetLanguageComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "en";
    }

    private static string GetLocalizedTargetLanguageName(string code) => UiLanguageService.IsEnglish
        ? code switch
        {
            "zh" => "Chinese",
            "ja" => "Japanese",
            "ko" => "Korean",
            "fr" => "French",
            "de" => "German",
            "es" => "Spanish",
            "ru" => "Russian",
            _ => "English"
        }
        : DeepSeekTextService.GetTargetLanguageName(code);

    private static string GetEngineModelForInputLanguage(string language) => language switch
    {
        "zh-PY" => "16k_zh_en",
        "en" => "16k_en_large",
        "yue" => "16k_yue",
        "ja" => "16k_ja",
        "ko" => "16k_ko",
        _ => "16k_zh"
    };

    private void AutoStartCheckBox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _autoStart.SetEnabled(AutoStartCheckBox.IsChecked == true);
            UpdateAutoStartStatus();
            SetStatus("设置已保存",
                AutoStartCheckBox.IsChecked == true ? "已开启开机自动启动" : "已关闭开机自动启动",
                "此设置只影响当前 Windows 账户。", "#5ED79A");
        }
        catch (Exception ex)
        {
            AutoStartCheckBox.IsChecked = _autoStart.IsEnabled();
            UpdateAutoStartStatus();
            ShowError(ToFriendlyMessage(ex));
        }
    }

    private void UpdateAutoStartStatus()
    {
        AutoStartStatusText.Text = AutoStartCheckBox.IsChecked == true
            ? UiLanguageService.Text("已开启 · 登录后自动运行", "On · runs after sign-in")
            : UiLanguageService.Text("当前未开启", "Currently off");
        AutoStartStatusText.Foreground = (Brush)new BrushConverter().ConvertFromString(
            AutoStartCheckBox.IsChecked == true ? "#5ED79A" : "#687181")!;
    }

    private async void AddPreference_Click(object sender, RoutedEventArgs e)
    {
        await _memoryRepository.AddPreferenceAsync(PreferenceInput.Text);
        PreferenceInput.Clear();
        await RefreshMemoriesAsync();
    }

    private async void DeletePreference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long id })
        {
            await _memoryRepository.DeletePreferenceAsync(id);
            await RefreshMemoriesAsync();
        }
    }

    private async void AddCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SpokenWordInput.Text) || string.IsNullOrWhiteSpace(PreferredWordInput.Text))
        {
            SetStatus("需要补充", "请把“经常识别成”和“希望写成”都填上", "", "#F2B45F");
            return;
        }

        await _memoryRepository.AddCorrectionAsync(SpokenWordInput.Text, PreferredWordInput.Text);
        SpokenWordInput.Clear();
        PreferredWordInput.Clear();
        SetStatus("已记住", "专有词纠错已经保存在本机", "下一次转写会自动套用。", "#5ED79A");
    }

    private void OpenCorrectionDictionaryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CorrectionDictionaryDialog(_memoryRepository)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async Task RefreshMemoriesAsync()
    {
        var items = await _memoryRepository.GetPreferencesAsync();
        _memories.Clear();
        foreach (var item in items) _memories.Add(item);
        var isEmpty = items.Count == 0;
        MemoryList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        MemoryEmptyPanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string header, string main, string hint, string color)
    {
        _statusHeaderSource = header;
        _statusMainSource = main;
        _statusHintSource = hint;
        HeaderStatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        RenderStatus();
    }

    private void RenderStatus()
    {
        HeaderStatusText.Text = UiLanguageService.Translate(_statusHeaderSource);
        MainStatusText.Text = UiLanguageService.Translate(_statusMainSource);
        MainHintText.Text = UiLanguageService.Translate(_statusHintSource);
    }

    private void ShowError(string message)
    {
        SetStatus("需要处理", "这次没有完成", message, "#FF6B6B");
        _overlay?.ShowStatus("未完成", OverlayVisualState.Error, TimeSpan.FromSeconds(2));
    }

    private async Task WarmConnectionsAsync()
    {
        await Task.WhenAll(
            TencentAsrSession.WarmupAsync(),
            _deepSeek.WarmupAsync(_settings.DeepSeekBaseUrl));
    }

    private async Task InitializeSemanticMemoryAsync()
    {
        if (!_embeddings.ModelFilesAvailable) return;
        try
        {
            var processed = 0;
            var pending = await _memoryRepository.GetUnembeddedHistoryAsync(20);
            if (pending.Count == 0) return;
            await _embeddings.WarmupAsync();
            while (!_closing && processed < 250)
            {
                if (pending.Count == 0) break;
                foreach (var item in pending)
                {
                    if (_closing) return;
                    await StoreHistoryEmbeddingAsync(item.Id, item.FinalText);
                    processed++;
                }
                pending = await _memoryRepository.GetUnembeddedHistoryAsync(20);
            }
        }
        catch
        {
            // Voice input keeps working with recent-history fallback if the local model fails.
        }
    }

    private async Task StoreHistoryEmbeddingAsync(long historyId, string text)
    {
        if (!_embeddings.ModelFilesAvailable || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var embedding = await _embeddings.CreatePassageEmbeddingAsync(text);
            await _memoryRepository.SaveEmbeddingAsync(historyId, embedding);
        }
        catch
        {
            // This history remains eligible for a later background retry.
        }
    }

    private static string ToFriendlyMessage(Exception exception)
    {
        var message = exception.Message.Contains("Windows 无法解码", StringComparison.OrdinalIgnoreCase)
            ? exception.Message
            : exception.GetBaseException().Message;
        if (message.Contains("腾讯 ASR 返回错误 4002", StringComparison.OrdinalIgnoreCase))
        {
            return "腾讯 ASR 未授权：请开通语音识别服务，或给当前 SecretID 授予 QcloudASRFullAccess。";
        }
        if (message.Contains("腾讯 ASR 返回错误 4003", StringComparison.OrdinalIgnoreCase))
        {
            return "腾讯实时说话人分离尚未开通：请进入腾讯 ASR 控制台开通实时语音识别服务。";
        }
        if (message.Contains("腾讯 ASR 返回错误 4004", StringComparison.OrdinalIgnoreCase))
        {
            return "腾讯 ASR 可用额度已经耗尽：请在控制台开启后付费或充值后重试。";
        }
        if (message.Contains("腾讯 ASR 返回错误 4005", StringComparison.OrdinalIgnoreCase))
        {
            return "腾讯云账户欠费，实时说话人分离已停止：请充值后重试。";
        }
        if (message.Contains("腾讯 ASR 返回错误 4010", StringComparison.OrdinalIgnoreCase))
        {
            return "腾讯 ASR 会话结束异常，请重新说一次；若重复出现可查看本机诊断日志。";
        }
        if (message.Contains("0x800401D0", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("CLIPBRD_E_CANT_OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 剪贴板正被其他程序占用，文字已保留在主窗口，请稍后重试。";
        }
        if (message.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase))
        {
            return "DeepSeek API Key 验证失败，请检查 DeepSeek 连接设置。";
        }
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return "服务鉴权失败（HTTP 401），请在诊断日志中确认具体服务。";
        }
        if (message.Contains("余额", StringComparison.OrdinalIgnoreCase)) return message;
        if (message.Contains("No such host", StringComparison.OrdinalIgnoreCase)) return "网络无法连接到服务，请检查网络或代理。";
        if (message.Contains("ConnectTimeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unable to connect to the remote server", StringComparison.OrdinalIgnoreCase))
        {
            return "连接腾讯 ASR 超时：当前网络可能正在切换或抖动，请确认网络稳定后重试。";
        }
        if (message.Contains("disposed object", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("ClientWebSocket", StringComparison.OrdinalIgnoreCase))
        {
            return "腾讯 ASR 连接没有成功建立，请重新尝试。";
        }
        return message;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenTencentConsole_Click(object sender, RoutedEventArgs e) => OpenUrl("https://console.cloud.tencent.com/asr");
    private void OpenTencentKeys_Click(object sender, RoutedEventArgs e) => OpenUrl("https://console.cloud.tencent.com/cam/capi");
    private void OpenDeepSeek_Click(object sender, RoutedEventArgs e) => OpenUrl("https://platform.deepseek.com/api_keys");

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closing = true;
        SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
        _mediaImportCancellation?.Cancel();
        if (_hotkey is not null)
        {
            _hotkey.RightPressed -= RightAlt_Pressed;
            _hotkey.LeftPressed -= LeftAlt_Pressed;
            _hotkey.Dispose();
        }
        _recorder?.Dispose();
        if (_asr is not null) _ = _asr.DisposeAsync();
        if (_meetingSession is not null) _ = _meetingSession.DisposeAsync();
        _overlay?.Close();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        _embeddings.Dispose();
        _transition.Dispose();
        _mediaImportCancellation?.Dispose();
    }
}
