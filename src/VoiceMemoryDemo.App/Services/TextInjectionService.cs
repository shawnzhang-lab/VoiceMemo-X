using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiceMemoryDemo.App.Services;

public enum TextInjectionOutcome
{
    Injected,
    CopiedOnly,
    Failed
}

public sealed record TextInjectionResult(
    TextInjectionOutcome Outcome,
    string Method,
    string Detail = "")
{
    public bool WasInjected => Outcome == TextInjectionOutcome.Injected;
}

public sealed record ClipboardCopyResult(
    bool Success,
    int Attempts,
    long ElapsedMilliseconds,
    int LastError,
    string ContentionDescription);

public sealed class TextInjectionService
{
    private const uint InputKeyboard = 1;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkV = 0x56;
    private const ushort VkReturn = 0x0D;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const int SwRestore = 9;
    private const int SwShowMaximized = 3;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int ClipboardAttempts = 9;
    private static readonly int[] ClipboardRetryDelaysMs = [40, 60, 90, 140, 220, 320, 450, 600];
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    public int LastSendInputError { get; private set; }

    public async Task<ClipboardCopyResult> CopyToClipboardAsync(
        IntPtr ownerWindow,
        string text,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new ClipboardCopyResult(false, 0, 0, 0, "没有可复制的文字。");
        }

        var result = await TrySetClipboardTextAsync(
            ownerWindow,
            text,
            string.IsNullOrWhiteSpace(purpose) ? "unspecified" : purpose,
            cancellationToken);
        DiagnosticLogService.WriteEvent(
            result.Success ? "ClipboardCopySucceeded" : "ClipboardCopyFailed",
            $"operation={purpose}; textLength={text.Length}; attempts={result.Attempts}; " +
            $"elapsedMs={result.ElapsedMilliseconds}; win32={result.LastError} (0x{result.LastError:X8}); " +
            $"contention={result.OwnerDescription}");
        return new ClipboardCopyResult(
            result.Success,
            result.Attempts,
            result.ElapsedMilliseconds,
            result.LastError,
            result.OwnerDescription);
    }

    public async Task<TextInjectionResult> DeliverAsync(
        ForegroundTarget target,
        IntPtr ownWindow,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TextInjectionResult(TextInjectionOutcome.Failed, "none", "没有可输入的文字。");
        }

        var processName = target.ProcessName;
        DiagnosticLogService.WriteEvent(
            "TextDeliveryTarget",
            $"app={processName}; pid={target.ProcessId}; hwnd=0x{target.Handle.ToInt64():X}; " +
            $"title={target.WindowTitle}; foreground=[{DescribeWindow(GetForegroundWindow())}]");
        var preferUnicode = ShouldPreferUnicode(processName);
        var clipboardOnly = ShouldRequireClipboard(processName);
        var clipboard = preferUnicode
            ? ClipboardWriteResult.NotAttempted
            : await TrySetClipboardTextAsync(ownWindow, text, $"text-input:{processName}", cancellationToken);

        if (clipboard.Attempted)
        {
            DiagnosticLogService.WriteEvent(
                clipboard.Success ? "ClipboardPrepared" : "ClipboardBusy",
                $"app={processName}; attempts={clipboard.Attempts}; elapsedMs={clipboard.ElapsedMilliseconds}; " +
                $"owner={clipboard.OwnerDescription}");
        }

        if (target.Handle == IntPtr.Zero || target.Handle == ownWindow)
        {
            if (!clipboard.Success)
            {
                clipboard = await TrySetClipboardTextAsync(ownWindow, text, $"copy-only:{processName}", cancellationToken);
            }
            return clipboard.Success
                ? LogResult(processName, new TextInjectionResult(TextInjectionOutcome.CopiedOnly, "clipboard", "目标输入框不可用，文字已复制。"))
                : LogResult(processName, new TextInjectionResult(TextInjectionOutcome.Failed, "none", ClipboardFailureDetail(clipboard)));
        }

        var wasMaximized = IsZoomed(target.Handle);
        if (!TryActivateWindow(target.Handle, target.ProcessId))
        {
            if (!clipboard.Success)
            {
                clipboard = await TrySetClipboardTextAsync(ownWindow, text, $"activation-fallback:{processName}", cancellationToken);
            }
            return clipboard.Success
                ? LogResult(processName, new TextInjectionResult(TextInjectionOutcome.CopiedOnly, "clipboard", "无法激活目标窗口，文字已复制。"))
                : LogResult(processName, new TextInjectionResult(TextInjectionOutcome.Failed, "none", "无法激活目标窗口，且剪贴板仍被占用。"));
        }

        await Task.Delay(180, cancellationToken);
        if (GetForegroundWindow() != target.Handle)
        {
            if (!clipboard.Success)
            {
                clipboard = await TrySetClipboardTextAsync(ownWindow, text, $"focus-fallback:{processName}", cancellationToken);
            }
            return clipboard.Success
                ? LogResult(processName, new TextInjectionResult(TextInjectionOutcome.CopiedOnly, "clipboard", "原来的目标窗口没有重新获得焦点，文字已复制。"))
                : LogResult(processName, new TextInjectionResult(TextInjectionOutcome.Failed, "none", "原来的目标窗口没有重新获得焦点，且剪贴板不可用。"));
        }
        if (wasMaximized && !IsZoomed(target.Handle))
        {
            ShowWindow(target.Handle, SwShowMaximized);
            await Task.Delay(120, cancellationToken);
        }

        if (clipboardOnly && !clipboard.Success)
        {
            return LogResult(processName, new TextInjectionResult(
                TextInjectionOutcome.Failed,
                "clipboard",
                ClipboardFailureDetail(clipboard)));
        }

        var useClipboard = clipboard.Success;
        var sentSuccessfully = useClipboard ? SendPasteShortcut() : SendUnicodeText(text);
        if (!sentSuccessfully)
        {
            if (!useClipboard)
            {
                clipboard = await TrySetClipboardTextAsync(ownWindow, text, $"unicode-fallback:{processName}", cancellationToken);
            }
            return clipboard.Success
                ? LogResult(processName, new TextInjectionResult(TextInjectionOutcome.CopiedOnly, "clipboard", "自动输入失败，完整文字已复制。"))
                : LogResult(processName, new TextInjectionResult(TextInjectionOutcome.Failed, useClipboard ? "clipboard" : "unicode", "自动输入失败，且剪贴板仍被占用。"));
        }

        await Task.Delay(useClipboard ? 120 : 80, cancellationToken);
        if (wasMaximized && !IsZoomed(target.Handle))
        {
            ShowWindow(target.Handle, SwShowMaximized);
        }

        return LogResult(processName, new TextInjectionResult(
            TextInjectionOutcome.Injected,
            useClipboard ? "clipboard" : "unicode"));
    }

    private bool SendPasteShortcut()
    {
        var inputs = new[]
        {
            KeyboardInput(VkControl, 0),
            KeyboardInput(VkV, 0),
            KeyboardInput(VkV, KeyEventKeyUp),
            KeyboardInput(VkControl, KeyEventKeyUp)
        };
        return SendKeyboardBatch(inputs);
    }

    private bool SendUnicodeText(string text)
    {
        var batch = new List<Input>(128);
        foreach (var character in text)
        {
            if (character == '\r') continue;

            if (character == '\n')
            {
                batch.Add(KeyboardInput(VkShift, 0));
                batch.Add(KeyboardInput(VkReturn, 0));
                batch.Add(KeyboardInput(VkReturn, KeyEventKeyUp));
                batch.Add(KeyboardInput(VkShift, KeyEventKeyUp));
            }
            else
            {
                batch.Add(UnicodeInput(character, KeyEventUnicode));
                batch.Add(UnicodeInput(character, KeyEventUnicode | KeyEventKeyUp));
            }

            if (batch.Count < 120) continue;
            if (!SendKeyboardBatch(batch.ToArray())) return false;
            batch.Clear();
        }

        return batch.Count == 0 || SendKeyboardBatch(batch.ToArray());
    }

    private bool SendKeyboardBatch(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length)
        {
            LastSendInputError = 0;
            return true;
        }

        LastSendInputError = Marshal.GetLastWin32Error();
        return false;
    }

    private static bool TryActivateWindow(IntPtr target, uint targetProcessId)
    {
        if (target == IntPtr.Zero || !IsWindow(target)) return false;
        if (IsIconic(target)) ShowWindowAsync(target, SwRestore);
        if (IsTargetWindowForeground(target)) return true;

        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(target, out var actualTargetProcessId);
        if (targetProcessId == 0) targetProcessId = actualTargetProcessId;
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var attachedCurrent = false;
        var attachedForeground = false;
        var attachedCurrentForeground = false;

        try
        {
            if (targetThread != 0 && currentThread != targetThread)
            {
                attachedCurrent = AttachThreadInput(currentThread, targetThread, true);
            }
            if (targetThread != 0 && foregroundThread != 0 &&
                foregroundThread != targetThread && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(foregroundThread, targetThread, true);
            }
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedCurrentForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }

            BringWindowToTop(target);
            SetForegroundWindow(target);
            Thread.Sleep(80);
            if (IsTargetWindowForeground(target)) return true;

            SetWindowPos(target, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            SetWindowPos(target, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            SwitchToThisWindow(target, false);
            Thread.Sleep(120);
            return IsTargetWindowForeground(target);
        }
        finally
        {
            if (attachedCurrentForeground) AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedForeground) AttachThreadInput(foregroundThread, targetThread, false);
            if (attachedCurrent) AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static bool IsTargetWindowForeground(IntPtr target) =>
        target != IntPtr.Zero && GetForegroundWindow() == target;

    private static bool ShouldPreferUnicode(string processName)
    {
        var knownWebInputs = new[]
        {
            "ChatGPT", "codex", "SLBrowser", "chrome", "msedge",
            "firefox", "brave", "opera", "QQBrowser"
        };
        return knownWebInputs.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ShouldRequireClipboard(string processName)
    {
        var nativeChatInputs = new[]
        {
            "Weixin", "WeChat", "WeChatAppEx", "WXWork", "WeCom", "QQ", "QQNT", "Feishu", "Lark"
        };
        return nativeChatInputs.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<ClipboardWriteResult> TrySetClipboardTextAsync(
        IntPtr ownerWindow,
        string text,
        string purpose,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var ownerDescription = "none";
        var lastError = 0;

        for (var attempt = 1; attempt <= ClipboardAttempts; attempt++)
        {
            if (TrySetClipboardText(
                    ownerWindow,
                    text,
                    out lastError,
                    out var failureStage,
                    out var contentionSnapshot))
            {
                stopwatch.Stop();
                return new ClipboardWriteResult(true, true, attempt, stopwatch.ElapsedMilliseconds, ownerDescription, 0);
            }

            if (!contentionSnapshot.StartsWith("none", StringComparison.OrdinalIgnoreCase))
            {
                ownerDescription = contentionSnapshot;
            }
            DiagnosticLogService.WriteEvent(
                "ClipboardContention",
                $"operation={purpose}; attempt={attempt}/{ClipboardAttempts}; stage={failureStage}; " +
                $"win32={lastError} (0x{lastError:X8}); {contentionSnapshot}");
            if (attempt < ClipboardAttempts)
            {
                await Task.Delay(ClipboardRetryDelaysMs[attempt - 1], cancellationToken);
            }
        }

        stopwatch.Stop();
        return new ClipboardWriteResult(true, false, ClipboardAttempts, stopwatch.ElapsedMilliseconds, ownerDescription, lastError);
    }

    private static bool TrySetClipboardText(
        IntPtr ownerWindow,
        string text,
        out int error,
        out string failureStage,
        out string contentionSnapshot)
    {
        error = 0;
        failureStage = "none";
        contentionSnapshot = "none";
        if (!OpenClipboard(ownerWindow))
        {
            error = Marshal.GetLastWin32Error();
            failureStage = "OpenClipboard";
            contentionSnapshot = DescribeClipboardState();
            return false;
        }

        IntPtr memory = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                error = Marshal.GetLastWin32Error();
                failureStage = "EmptyClipboard";
                contentionSnapshot = DescribeClipboardState();
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
            if (memory == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                failureStage = "GlobalAlloc";
                contentionSnapshot = DescribeClipboardState();
                return false;
            }

            var destination = GlobalLock(memory);
            if (destination == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                failureStage = "GlobalLock";
                contentionSnapshot = DescribeClipboardState();
                return false;
            }
            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                failureStage = "SetClipboardData";
                contentionSnapshot = DescribeClipboardState();
                return false;
            }

            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (memory != IntPtr.Zero) GlobalFree(memory);
            CloseClipboard();
        }
    }

    private static string ClipboardFailureDetail(ClipboardWriteResult result) =>
        $"剪贴板在约 {result.ElapsedMilliseconds} 毫秒内持续被占用，本次没有冒险模拟输入。完整文字仍保留在速说速记X结果框中。";

    private static TextInjectionResult LogResult(string processName, TextInjectionResult result)
    {
        DiagnosticLogService.WriteEvent(
            "TextInjection",
            $"app={processName}; method={result.Method}; outcome={result.Outcome}; win32={Marshal.GetLastWin32Error()}; detail={result.Detail}");
        return result;
    }

    private static string DescribeClipboardState()
    {
        var openWindow = GetOpenClipboardWindow();
        var dataOwner = GetClipboardOwner();
        var foreground = GetForegroundWindow();
        var sequence = GetClipboardSequenceNumber();
        return $"sequence={sequence}; openWindow=[{DescribeWindow(openWindow)}]; " +
               $"dataOwner=[{DescribeWindow(dataOwner)}]; foreground=[{DescribeWindow(foreground)}]";
    }

    private static string DescribeWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return "none (released or opened without a window handle)";

        GetWindowThreadProcessId(window, out var processId);
        var title = new StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return $"hwnd=0x{window.ToInt64():X}; pid={processId}; process={process.ProcessName}; " +
                   $"category={ClassifyProcess(process.ProcessName)}; window={title}; " +
                   $"path={process.MainModule?.FileName ?? "unknown"}";
        }
        catch
        {
            return $"hwnd=0x{window.ToInt64():X}; pid={processId}; process=unavailable; window={title}";
        }
    }

    private static string ClassifyProcess(string processName)
    {
        if (new[] { "explorer", "ShellExperienceHost", "StartMenuExperienceHost" }
            .Contains(processName, StringComparer.OrdinalIgnoreCase)) return "windows-shell-or-clipboard-history";
        if (new[] { "TextInputHost", "ctfmon", "SearchHost" }
            .Contains(processName, StringComparer.OrdinalIgnoreCase)) return "input-method-or-windows-text-service";
        if (new[] { "chrome", "msedge", "firefox", "brave", "opera", "SLBrowser", "ChatGPT", "codex" }
            .Contains(processName, StringComparer.OrdinalIgnoreCase)) return "browser-or-ai-client";
        if (new[] { "Weixin", "WeChat", "WeChatAppEx", "WXWork", "WeCom", "QQ", "QQNT" }
            .Contains(processName, StringComparer.OrdinalIgnoreCase)) return "chat-client";
        return "application";
    }

    private readonly record struct ClipboardWriteResult(
        bool Attempted,
        bool Success,
        int Attempts,
        long ElapsedMilliseconds,
        string OwnerDescription,
        int LastError)
    {
        public static ClipboardWriteResult NotAttempted => new(false, false, 0, 0, "not attempted", 0);
    }

    private static Input KeyboardInput(ushort key, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInputData { VirtualKey = key, Flags = flags }
        }
    };

    private static Input UnicodeInput(char character, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = 0,
                ScanCode = character,
                Flags = flags
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr window, bool altTab);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr ownerWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);
}
