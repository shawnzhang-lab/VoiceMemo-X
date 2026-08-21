using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VoiceMemoryDemo.App.Services;

public sealed class GlobalRightAltService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint VkLMenu = 0xA4;
    private const uint VkRMenu = 0xA5;

    private readonly HookProc _callback;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hook;
    private bool _leftChorded;
    private bool _leftPressed;
    private bool _rightPressed;

    public event Action? LeftPressed;
    public event Action? RightPressed;

    public GlobalRightAltService(Dispatcher dispatcher)
    {
        _callback = HandleHook;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            DiagnosticLogService.WriteEvent("AltHookFailed", $"win32={error} (0x{error:X8})");
            throw new InvalidOperationException($"无法注册全局右 Alt 快捷键（Windows 错误 {error}）。");
        }
        DiagnosticLogService.WriteEvent("AltHookStarted", $"hook=0x{_hook.ToInt64():X}");
    }

    private IntPtr HandleHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            var message = wParam.ToInt32();
            var isKeyDown = message is WmKeyDown or WmSysKeyDown;
            var isKeyUp = message is WmKeyUp or WmSysKeyUp;

            if (data.VirtualKeyCode == VkLMenu)
            {
                if (isKeyDown && !_leftPressed)
                {
                    _leftPressed = true;
                    _leftChorded = false;
                }
                else if (isKeyUp)
                {
                    var shouldTrigger = _leftPressed && !_leftChorded;
                    _leftPressed = false;
                    _leftChorded = false;
                    if (shouldTrigger) _dispatcher.BeginInvoke(() => LeftPressed?.Invoke());
                }

                // Left Alt must continue to support Alt+Tab and application shortcuts.
                // A standalone tap is recognized on key-up only when no chord was used.
                return CallNextHookEx(_hook, code, wParam, lParam);
            }

            if (_leftPressed && isKeyDown)
            {
                _leftChorded = true;
            }

            if (data.VirtualKeyCode == VkRMenu)
            {
                if (isKeyDown && !_rightPressed)
                {
                    _rightPressed = true;
                    _dispatcher.BeginInvoke(() => RightPressed?.Invoke());
                }
                else if (isKeyUp)
                {
                    _rightPressed = false;
                }
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        DiagnosticLogService.WriteEvent("AltHookStopped", $"hook=0x{_hook.ToInt64():X}");
        _hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

}
