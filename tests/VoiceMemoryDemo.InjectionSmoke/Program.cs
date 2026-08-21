using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Data.Sqlite;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.InjectionSmoke;

internal static class Program
{
    private static int _exitCode = 1;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--target", StringComparer.OrdinalIgnoreCase))
        {
            return RunDisposableTarget(args.Contains("--hold-clipboard", StringComparer.OrdinalIgnoreCase));
        }
        if (args.Contains("--history-metadata", StringComparer.OrdinalIgnoreCase))
        {
            return DumpHistoryMetadata();
        }
        if (args.Contains("--embedding-smoke", StringComparer.OrdinalIgnoreCase))
        {
            return RunEmbeddingSmokeAsync().GetAwaiter().GetResult();
        }
        if (args.Contains("--asr-connect", StringComparer.OrdinalIgnoreCase))
        {
            return RunAsrConnectSmokeAsync().GetAwaiter().GetResult();
        }
        if (args.Contains("--same-process-decoy", StringComparer.OrdinalIgnoreCase))
        {
            return RunSameProcessDecoySmoke();
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            Process? target = null;
            try
            {
                var executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Smoke executable path is unavailable.");
                var copyOnly = args.Contains("--copy-success", StringComparer.OrdinalIgnoreCase) ||
                               args.Contains("--copy-locked", StringComparer.OrdinalIgnoreCase);
                var copyLocked = args.Contains("--copy-locked", StringComparer.OrdinalIgnoreCase);
                var holdClipboard = args.Contains("--unicode-fallback", StringComparer.OrdinalIgnoreCase) ||
                                    args.Contains("--native-clipboard-only", StringComparer.OrdinalIgnoreCase) ||
                                    copyLocked;
                var targetArguments = holdClipboard ? "--target --hold-clipboard" : "--target";
                target = Process.Start(new ProcessStartInfo(executable, targetArguments)
                {
                    UseShellExecute = false
                });
                await Task.Delay(1_200);
                target?.Refresh();
                if (target is null || target.MainWindowHandle == IntPtr.Zero)
                {
                    Console.WriteLine("FAIL: Disposable target window was not found.");
                    return;
                }

                var marker = $"速说速记X Unicode 回填测试 {Guid.NewGuid():N}";
                var nativeClipboardOnly = args.Contains("--native-clipboard-only", StringComparer.OrdinalIgnoreCase);
                var targetProcessName = nativeClipboardOnly
                    ? "Weixin"
                    : args.Contains("--direct-unicode", StringComparer.OrdinalIgnoreCase)
                        ? "ChatGPT"
                        : "injection-smoke";
                var foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out var foregroundProcessId);
                Console.WriteLine($"DIAG: targetPid={target.Id}, foregroundPid={foregroundProcessId}, targetHandle={target.MainWindowHandle}, foregroundHandle={foreground}");
                var maximizedBefore = IsZoomed(target.MainWindowHandle);
                var service = new TextInjectionService();
                if (copyOnly)
                {
                    var operation = copyLocked ? "clipboard-smoke-locked" : "clipboard-smoke-success";
                    var copy = await service.CopyToClipboardAsync(IntPtr.Zero, marker, operation);
                    var clipboardMatches = false;
                    if (copy.Success)
                    {
                        try { clipboardMatches = Clipboard.GetText() == marker; }
                        catch { clipboardMatches = false; }
                    }
                    var log = File.Exists(AppPaths.DiagnosticsPath)
                        ? await File.ReadAllTextAsync(AppPaths.DiagnosticsPath)
                        : string.Empty;
                    var contentionLogged = log.Contains($"operation={operation}", StringComparison.Ordinal) &&
                                           log.Contains($"pid={target.Id}", StringComparison.Ordinal);
                    var copyPassed = copyLocked
                        ? !copy.Success && contentionLogged
                        : copy.Success && clipboardMatches;
                    _exitCode = copyPassed ? 0 : 1;
                    Console.WriteLine(copyPassed
                        ? $"PASS: copySuccess={copy.Success}, attempts={copy.Attempts}, elapsedMs={copy.ElapsedMilliseconds}, contentionLogged={contentionLogged}."
                        : $"FAIL: copySuccess={copy.Success}, attempts={copy.Attempts}, elapsedMs={copy.ElapsedMilliseconds}, clipboardMatches={clipboardMatches}, contentionLogged={contentionLogged}, detail={copy.ContentionDescription}");
                    return;
                }

                var result = await service.DeliverAsync(
                    new ForegroundTarget(
                        target.MainWindowHandle, (uint)target.Id, targetProcessName, target.MainWindowTitle),
                    IntPtr.Zero,
                    marker);
                await Task.Delay(500);
                target.Refresh();
                var edited = target.MainWindowTitle == "EDITED";
                var maximizedAfter = IsZoomed(target.MainWindowHandle);
                var inputType = typeof(TextInjectionService).GetNestedType("Input", System.Reflection.BindingFlags.NonPublic);
                if (inputType is not null) Console.WriteLine($"DIAG: INPUT size={Marshal.SizeOf(inputType)}");
                var passed = nativeClipboardOnly
                    ? result.Outcome == TextInjectionOutcome.Failed && !edited
                    : result.WasInjected && edited && maximizedBefore && maximizedAfter;
                _exitCode = passed ? 0 : 1;
                Console.WriteLine(_exitCode == 0
                    ? $"PASS: outcome={result.Outcome}, method={result.Method}, edited={edited}, target stayed maximized."
                    : $"FAIL: outcome={result.Outcome}, method={result.Method}, edited={edited}, maxBefore={maximizedBefore}, maxAfter={maximizedAfter}, win32={service.LastSendInputError}, title={target.MainWindowTitle}, detail={result.Detail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetBaseException().Message}");
            }
            finally
            {
                if (target is { HasExited: false }) target.Kill(true);
                application.Shutdown(_exitCode);
            }
        };
        application.Run();
        return _exitCode;
    }

    private static int RunSameProcessDecoySmoke()
    {
        var exitCode = 1;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            var targetInput = new TextBox { Margin = new Thickness(20) };
            var decoyInput = new TextBox { Margin = new Thickness(20) };
            var targetWindow = new Window { Title = "FEISHU_TARGET", Width = 480, Height = 220, Content = targetInput };
            var decoyWindow = new Window { Title = "FEISHU_DECOY", Width = 480, Height = 220, Content = decoyInput };
            try
            {
                targetWindow.Show();
                targetInput.Focus();
                var targetHandle = new WindowInteropHelper(targetWindow).Handle;
                decoyWindow.Show();
                decoyWindow.Activate();
                decoyInput.Focus();
                await Task.Delay(250);

                const string marker = "飞书同进程多窗口焦点测试";
                var result = await new TextInjectionService().DeliverAsync(
                    new ForegroundTarget(targetHandle, (uint)Environment.ProcessId, "Feishu", targetWindow.Title),
                    IntPtr.Zero,
                    marker);
                await Task.Delay(250);
                var passed = result.WasInjected && targetInput.Text == marker && string.IsNullOrEmpty(decoyInput.Text);
                exitCode = passed ? 0 : 1;
                Console.WriteLine(passed
                    ? "PASS: exact Feishu target received text; same-process decoy remained unchanged."
                    : $"FAIL: outcome={result.Outcome}; target={targetInput.Text}; decoy={decoyInput.Text}; detail={result.Detail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetBaseException().Message}");
            }
            finally
            {
                targetWindow.Close();
                decoyWindow.Close();
                application.Shutdown(exitCode);
            }
        };
        application.Run();
        return exitCode;
    }

    private static int RunDisposableTarget(bool holdClipboard)
    {
        var application = new Application();
        var input = new TextBox
        {
            FontSize = 18,
            Margin = new Thickness(24),
            AcceptsReturn = true
        };
        var window = new Window
        {
            Title = "READY",
            Width = 520,
            Height = 220,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = input
        };
        input.TextChanged += (_, _) =>
        {
            if (input.Text.Length > 0) window.Title = "EDITED";
        };
        window.Loaded += (_, _) =>
        {
            window.Activate();
            input.Focus();
            window.WindowState = WindowState.Maximized;
            if (holdClipboard && !OpenClipboard(new WindowInteropHelper(window).Handle))
            {
                window.Title = "CLIPBOARD_LOCK_FAILED";
            }
            input.Focus();
            System.Windows.Input.Keyboard.Focus(input);
        };
        window.Closed += (_, _) =>
        {
            if (holdClipboard) CloseClipboard();
        };
        return application.Run(window);
    }

    private static int DumpHistoryMetadata()
    {
        using var connection = new SqliteConnection($"Data Source={AppPaths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, app_name, length(raw_text), length(final_text), created_utc
            FROM text_history ORDER BY id DESC LIMIT 5;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"id={reader.GetInt64(0)} app={reader.GetString(1)} raw={reader.GetInt32(2)} final={reader.GetInt32(3)} at={reader.GetString(4)}");
        }
        return 0;
    }

    private static async Task<int> RunEmbeddingSmokeAsync()
    {
        try
        {
            var modelDirectory = Path.Combine(
                Directory.GetCurrentDirectory(), "Models", "multilingual-e5-small");
            using var service = new LocalEmbeddingService(modelDirectory);
            var query = await service.CreateQueryEmbeddingAsync("帮我礼貌拒绝客户的合作邀请");
            var related = await service.CreatePassageEmbeddingAsync(
                "感谢您的邀请，我们现阶段暂时无法参与合作，希望以后还有交流机会。");
            var unrelated = await service.CreatePassageEmbeddingAsync(
                "今天午餐准备做番茄炒鸡蛋，再煮一碗米饭。");
            var relatedScore = Dot(query, related);
            var unrelatedScore = Dot(query, unrelated);
            var queryNorm = Math.Sqrt(Dot(query, query));
            Console.WriteLine($"DIAG: dimensions={query.Length}, norm={queryNorm:F6}, related={relatedScore:F6}, unrelated={unrelatedScore:F6}");
            var passed = query.Length == LocalEmbeddingService.Dimensions &&
                         Math.Abs(queryNorm - 1) < 0.001 &&
                         relatedScore > unrelatedScore + 0.08;
            Console.WriteLine(passed
                ? "PASS: Local multilingual semantic embeddings rank the related Chinese memory higher."
                : "FAIL: Semantic ranking or vector normalization is incorrect.");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.GetBaseException()}");
            return 1;
        }
    }

    private static async Task<int> RunAsrConnectSmokeAsync()
    {
        try
        {
            var settings = new SecureSettingsStore().Load();
            await using var asr = new TencentAsrSession();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var started = Stopwatch.StartNew();
            await asr.StartAsync(settings, timeout.Token);
            started.Stop();
            Console.WriteLine($"PASS: Tencent ASR WebSocket connected in {started.Elapsed.TotalSeconds:F2}s.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.GetBaseException().Message}");
            return 1;
        }
    }

    private static double Dot(float[] left, float[] right)
    {
        double sum = 0;
        for (var index = 0; index < left.Length; index++) sum += left[index] * right[index];
        return sum;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
}
