using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Qingdan.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\Qingdan.Todo.SingleInstance.v1";
    private const string WindowTitle = "Qingdan.SingleInstance.Host.v1";
    private const int ShowMainMessage = 0x8000 + 0x221;
    private const int QuietMessage = 0x8000 + 0x222;

    private Mutex? _mutex;
    private HwndSource? _messageSource;
    private bool _ownsMutex;

    public bool TryAcquire(out bool alreadyRunning)
    {
        alreadyRunning = false;
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            _ownsMutex = createdNew;
            alreadyRunning = !createdNew;
            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return createdNew;
        }
        catch
        {
            // If the mutex cannot be created (policy or unusual sandbox), allow a single
            // normal instance to run rather than preventing the app from starting at all.
            alreadyRunning = false;
            return true;
        }
    }

    public void StartListening(Action showMain)
    {
        try
        {
            var parameters = new HwndSourceParameters(WindowTitle)
            {
                Width = 1,
                Height = 1,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
                UsesPerPixelOpacity = false
            };

            _messageSource = new HwndSource(parameters);
            _messageSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == ShowMainMessage)
                {
                    handled = true;
                    showMain();
                    return IntPtr.Zero;
                }

                if (msg == QuietMessage)
                {
                    handled = true;
                    return IntPtr.Zero;
                }

                return IntPtr.Zero;
            });
        }
        catch
        {
            // Single-instance signalling is a convenience. The app still works without it.
        }
    }

    public static void NotifyExistingInstance(bool showMain)
    {
        var message = showMain ? ShowMainMessage : QuietMessage;
        var hwnd = IntPtr.Zero;

        for (var attempt = 0; attempt < 35 && hwnd == IntPtr.Zero; attempt++)
        {
            hwnd = FindWindow(null, WindowTitle);
            if (hwnd == IntPtr.Zero)
            {
                Thread.Sleep(60);
            }
        }

        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, message, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        _messageSource?.Dispose();
        _messageSource = null;

        if (_mutex is not null)
        {
            try
            {
                if (_ownsMutex)
                {
                    _mutex.ReleaseMutex();
                }
            }
            catch
            {
                // Ignore release failures during shutdown.
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
