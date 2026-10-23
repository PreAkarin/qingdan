using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Qingdan.Services;

/// <summary>
/// Keeps the floating widget on the desktop layer: above the shell's desktop host,
/// below ordinary application windows, and able to survive Show Desktop / Win+D.
/// The implementation deliberately avoids SetParent into Explorer: recent Windows 11
/// builds restructured the WorkerW hierarchy, and cross-process reparenting can fail.
/// Instead it watches the desktop z-order and follows the shell window.
/// </summary>
public sealed class DesktopLayerService : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const int SW_SHOWNOACTIVATE = 4;

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private readonly string? _logPath;

    private Window? _window;
    private IntPtr _hwnd;
    private IntPtr _anchor;
    private IntPtr _desktopHost;
    private HwndSource? _source;
    private HwndSourceHook? _sourceHook;
    private DesktopMonitor.WinEventProc? _winEventProc;
    private IntPtr _winEventHook;
    private DispatcherTimer? _timer;
    private bool _attached;
    private bool _visible;
    private bool _showDesktop;
    private bool _allowHide = true;
    private bool _disposed;
    private DateTime _lastHostCheck = DateTime.MinValue;
    private DateTime _lastLower = DateTime.MinValue;

    public DesktopLayerService(string? logPath = null)
    {
        _logPath = logPath;
    }

    public bool IsAttached => _attached;

    public bool IsDesktopShown => _showDesktop;

    public void Attach(Window widget)
    {
        if (_attached || _disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(widget);
        _window = widget;

        var helper = new WindowInteropHelper(widget);
        if (helper.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The widget window handle is not available yet.");
        }

        _hwnd = helper.Handle;
        ApplyToolWindowStyle();
        CreateAnchor();
        _source = HwndSource.FromHwnd(_hwnd);
        _sourceHook = WndProc;
        _source?.AddHook(_sourceHook);

        try
        {
            _winEventProc = OnForegroundChanged;
            _winEventHook = DesktopMonitor.SetWinEventHook(
                DesktopMonitor.EVENT_SYSTEM_FOREGROUND,
                DesktopMonitor.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                DesktopMonitor.WINEVENT_OUTOFCONTEXT | DesktopMonitor.WINEVENT_SKIPOWNPROCESS);
        }
        catch
        {
            _winEventHook = IntPtr.Zero;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _timer.Tick += (_, _) => Evaluate();
        _attached = true;
        _visible = widget.IsVisible;
        _allowHide = false;
        _timer.Start();

        SendToDesktopBottom();
        Evaluate();
        Log($"attached hwnd=0x{_hwnd.ToInt64():X} anchor=0x{_anchor.ToInt64():X} visible={_visible}");
    }

    public void SetVisible(bool visible)
    {
        if (_window is null || _disposed)
        {
            return;
        }

        if (visible)
        {
            _visible = true;
            _window.ShowActivated = false;
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _allowHide = false;
            SendToDesktopBottom();
            Evaluate(force: true);
            Log("widget shown");
        }
        else
        {
            _visible = false;
            _allowHide = true;
            try
            {
                _window.Hide();
            }
            finally
            {
                _allowHide = false;
            }

            Log("widget hidden");
        }
    }

    public void Refresh()
    {
        _lastHostCheck = DateTime.MinValue;
        Evaluate(force: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _allowHide = true;

        try
        {
            _timer?.Stop();
        }
        catch
        {
            // Ignore.
        }

        if (_winEventHook != IntPtr.Zero)
        {
            try
            {
                DesktopMonitor.UnhookWinEvent(_winEventHook);
            }
            catch
            {
                // Ignore.
            }

            _winEventHook = IntPtr.Zero;
        }

        if (_source is not null && _sourceHook is not null)
        {
            _source.RemoveHook(_sourceHook);
        }

        _sourceHook = null;
        _source = null;

        if (_anchor != IntPtr.Zero)
        {
            DesktopMonitor.DestroyWindow(_anchor);
            _anchor = IntPtr.Zero;
        }

        _attached = false;
        _window = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var windowPos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((windowPos.flags & SWP_HIDEWINDOW) != 0 && !_allowHide)
            {
                // Show Desktop, Win+D and similar shell commands must not hide the widget.
                windowPos.flags &= ~SWP_HIDEWINDOW;
                windowPos.flags |= SWP_SHOWWINDOW;
                Marshal.StructureToPtr(windowPos, lParam, fDeleteOld: false);
                handled = false;
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_SYSCOMMAND && ((int)wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE && !_allowHide)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void Evaluate(bool force = false)
    {
        if (!_attached || !_visible || _disposed || _window is null)
        {
            return;
        }

        if (!force && DateTime.Now - _lastHostCheck > TimeSpan.FromSeconds(2))
        {
            _lastHostCheck = DateTime.Now;
            if (_desktopHost == IntPtr.Zero || !DesktopMonitor.IsWindow(_desktopHost))
            {
                _desktopHost = FindDesktopHost();
            }
        }

        if (_desktopHost == IntPtr.Zero)
        {
            _desktopHost = FindDesktopHost();
        }

        if (_anchor != IntPtr.Zero && DesktopMonitor.IsWindow(_anchor))
        {
            // Keep the anchor at the bottom of the normal window stack. This is cheap
            // and makes the host comparison stable after other apps change z-order.
            DesktopMonitor.SetWindowPos(_anchor, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        var foreground = DesktopMonitor.GetForegroundWindow();
        var foregroundClass = DesktopMonitor.GetClassName(foreground);
        var foregroundPid = DesktopMonitor.GetWindowProcessId(foreground);
        var ownForeground = foregroundPid == (uint)Environment.ProcessId;
        var shellForeground = foreground == _desktopHost ||
                              foregroundClass is "Progman" or "WorkerW" ||
                              foreground == DesktopMonitor.GetShellWindow();

        // The authoritative signal is z-order: during Show Desktop, Explorer raises
        // the desktop host (Progman on Windows 11 24H2+) above all normal windows.
        // Foreground can stay on the previously active app during Win+D, so using it
        // to veto this state would make the widget disappear on Show Desktop.
        var showing = _desktopHost != IntPtr.Zero && _anchor != IntPtr.Zero &&
                      DesktopMonitor.IsWindowAbove(_desktopHost, _anchor);

        if (showing != _showDesktop || force)
        {
            _showDesktop = showing;
            if (showing)
            {
                ShowOnDesktopTop();
            }
            else
            {
                LowerToDesktopBottom();
            }

            Log($"state showDesktop={showing} fg=0x{foreground.ToInt64():X} class={foregroundClass} " +
                $"ownFg={ownForeground} shellFg={shellForeground}");
            return;
        }

        if (_showDesktop)
        {
            EnsureVisibleOnDesktop();
            var hwndAboveHost = _desktopHost != IntPtr.Zero && DesktopMonitor.IsWindowAbove(_hwnd, _desktopHost);
            if (!hwndAboveHost)
            {
                ShowOnDesktopTop();
            }
        }
        else if ((DateTime.Now - _lastLower).TotalSeconds > 4)
        {
            LowerToDesktopBottom();
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint threadId, uint eventTime)
    {
        if (_disposed || eventType != DesktopMonitor.EVENT_SYSTEM_FOREGROUND)
        {
            return;
        }

        try
        {
            var dispatcher = _window?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                Evaluate(force: true);
            }
            else
            {
                dispatcher.BeginInvoke(() => Evaluate(force: true), DispatcherPriority.Background);
            }
        }
        catch
        {
            // Hook callbacks must never throw into user32.
        }
    }

    private void ShowOnDesktopTop()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (DesktopMonitor.IsIconic(_hwnd) || !DesktopMonitor.IsWindowVisible(_hwnd))
            {
                DesktopMonitor.ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            }

            // During Show Desktop / Win+D the shell raises the desktop host above
            // the normal z-order band. HWND_TOP alone is not enough: the widget
            // must temporarily enter the topmost band so it is composited above
            // the desktop wallpaper, icons and taskbar. It is removed from the
            // topmost band immediately when the desktop is hidden again.
            DesktopMonitor.SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch
        {
            // Ignore z-order failures; the next timer pass retries.
        }
    }

    private void EnsureVisibleOnDesktop()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!DesktopMonitor.IsWindowVisible(_hwnd) || DesktopMonitor.IsIconic(_hwnd))
        {
            DesktopMonitor.ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        }
    }

    private void LowerToDesktopBottom()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Leave the topmost band before returning to the normal desktop layer,
            // so ordinary application windows can cover the widget again.
            DesktopMonitor.SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            DesktopMonitor.SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _lastLower = DateTime.Now;
        }
        catch
        {
            // Ignore.
        }
    }

    private void SendToDesktopBottom() => LowerToDesktopBottom();

    private void ApplyToolWindowStyle()
    {
        try
        {
            var exStyle = DesktopMonitor.GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            DesktopMonitor.SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        }
        catch
        {
            // Not fatal.
        }
    }

    private void CreateAnchor()
    {
        if (_anchor != IntPtr.Zero && DesktopMonitor.IsWindow(_anchor))
        {
            return;
        }

        _anchor = DesktopMonitor.CreateWindowEx(
            (uint)WS_EX_TOOLWINDOW,
            "STATIC",
            "Qingdan.DesktopLayer.Anchor",
            0x80000000, // WS_POPUP, intentionally not visible
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        DesktopMonitor.SetWindowPos(_anchor, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _desktopHost = FindDesktopHost();
    }

    private static IntPtr FindDesktopHost()
    {
        var shell = DesktopMonitor.GetShellWindow();
        if (shell == IntPtr.Zero)
        {
            shell = DesktopMonitor.FindWindow("Progman", null);
        }

        if (shell == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // Windows 11 24H2 moved SHELLDLL_DefView back under Progman. On earlier
        // versions it lives under a top-level WorkerW window.
        if (DesktopMonitor.FindWindowEx(shell, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
        {
            return shell;
        }

        var worker = IntPtr.Zero;
        while ((worker = DesktopMonitor.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            if (DesktopMonitor.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                return worker;
            }
        }

        return shell;
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break window behavior.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    private static class DesktopMonitor
    {
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint dwEventThread, uint dwmsEventTime);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        public static uint GetWindowProcessId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return 0;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            return pid;
        }

        public static string GetClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsWindowAbove(IntPtr first, IntPtr second)
        {
            if (first == IntPtr.Zero || second == IntPtr.Zero)
            {
                return false;
            }

            var firstIndex = -1;
            var secondIndex = -1;
            var index = 0;
            EnumWindows((hwnd, _) =>
            {
                if (hwnd == first)
                {
                    firstIndex = index;
                }

                if (hwnd == second)
                {
                    secondIndex = index;
                }

                index++;
                return firstIndex < 0 || secondIndex < 0;
            }, IntPtr.Zero);

            return firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex;
        }
    }
}
