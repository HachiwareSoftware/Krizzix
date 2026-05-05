using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Krizzix.Configuration;
using Krizzix.Interop;

namespace Krizzix.Services
{
    internal sealed class WindowHiderService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly AppLogger _logger;
        private readonly ProcessNameResolver _processNameResolver = new ProcessNameResolver();
        private readonly HashSet<IntPtr> _activeRetries = new HashSet<IntPtr>();
        private readonly NativeMethods.WinEventProc _windowEventProc;
        private readonly NativeMethods.WinEventProc _foregroundEventProc;
        private WindowHiderConfig _config;
        private IntPtr _windowHook;
        private IntPtr _foregroundHook;
        private Timer _pollTimer;
        private bool _running;
        private bool _disposed;

        public WindowHiderService(WindowHiderConfig config, AppLogger logger)
        {
            _config = config ?? WindowHiderConfig.CreateDefault().Normalize();
            _logger = logger;
            _windowEventProc = OnWindowEvent;
            _foregroundEventProc = OnForegroundEvent;
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            InstallHooks();
            ScanAllWindows();
            _pollTimer = new Timer(_ => ScanAllWindows(), null, _config.polling_interval_ms, _config.polling_interval_ms);
            _logger.Info("Window hider service started.");
        }

        public void Stop()
        {
            _running = false;

            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }

            if (_windowHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_windowHook);
                _windowHook = IntPtr.Zero;
            }

            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }

            _logger.Info("Window hider service stopped.");
        }

        public void UpdateConfig(WindowHiderConfig config)
        {
            _config = (config ?? WindowHiderConfig.CreateDefault()).Normalize();
            if (_pollTimer != null)
                _pollTimer.Change(_config.polling_interval_ms, _config.polling_interval_ms);
            ScanAllWindows();
        }

        private void InstallHooks()
        {
            _windowHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_CREATE,
                NativeMethods.EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                _windowEventProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_windowHook == IntPtr.Zero)
                _logger.Warn("Window event hook failed. Win32 error: " + Marshal.GetLastWin32Error());

            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _foregroundEventProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_foregroundHook == IntPtr.Zero)
                _logger.Warn("Foreground event hook failed. Win32 error: " + Marshal.GetLastWin32Error());
        }

        private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (!_running || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF || hwnd == IntPtr.Zero)
                return;

            QueueWindowFamily(hwnd);
        }

        private void OnForegroundEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (!_running || hwnd == IntPtr.Zero)
                return;

            QueueWindowFamily(hwnd);
        }

        private void ScanAllWindows()
        {
            if (!_running && _pollTimer != null)
                return;

            try
            {
                NativeMethods.EnumWindows((hwnd, lParam) =>
                {
                    QueueWindowFamily(hwnd);
                    return true;
                }, IntPtr.Zero);

                IntPtr current = IntPtr.Zero;
                while ((current = NativeMethods.FindWindowExW(IntPtr.Zero, current, IntPtr.Zero, IntPtr.Zero)) != IntPtr.Zero)
                    QueueWindowFamily(current);
            }
            catch (Exception ex)
            {
                _logger.Error("Window scan failed.", ex);
            }
        }

        private void QueueWindowFamily(IntPtr hwnd)
        {
            QueueHide(hwnd);

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && root != hwnd)
                QueueHide(root);

            IntPtr rootOwner = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
            if (rootOwner != IntPtr.Zero && rootOwner != hwnd && rootOwner != root)
                QueueHide(rootOwner);
        }

        private void QueueHide(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            lock (_sync)
            {
                if (_activeRetries.Contains(hwnd))
                    return;
                _activeRetries.Add(hwnd);
            }

            ThreadPool.QueueUserWorkItem(_ => RetryHide(hwnd));
        }

        private void RetryHide(IntPtr hwnd)
        {
            try
            {
                int[] delays = { 0, 50, 100, 200, 400, 800 };
                foreach (int delay in delays)
                {
                    if (!_running)
                        return;
                    if (delay > 0)
                        Thread.Sleep(delay);
                    TryHide(hwnd);
                }
            }
            finally
            {
                lock (_sync)
                {
                    _activeRetries.Remove(hwnd);
                }
            }
        }

        private void TryHide(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0)
                return;

            string processName = _processNameResolver.GetProcessName(processId);
            if (!_config.MatchesProcessName(processName))
                return;

            StopFlash(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            HideFromTaskbar(hwnd);

            if (_logger.DebugEnabled)
                _logger.Info("Hidden window: " + processName + " pid=" + processId + " hwnd=0x" + hwnd.ToInt64().ToString("X") + " title=\"" + GetTitle(hwnd) + "\"");
        }

        private static void StopFlash(IntPtr hwnd)
        {
            var flashInfo = new NativeMethods.FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.FLASHWINFO)),
                hwnd = hwnd,
                dwFlags = NativeMethods.FLASHW_STOP,
                uCount = 0,
                dwTimeout = 0
            };
            NativeMethods.FlashWindowEx(ref flashInfo);
        }

        private static void HideFromTaskbar(IntPtr hwnd)
        {
            IntPtr stylePtr = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            long style = stylePtr.ToInt64();
            style &= ~NativeMethods.WS_EX_APPWINDOW;
            style |= NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        }

        private static string GetTitle(IntPtr hwnd)
        {
            char[] buffer = new char[256];
            int length = NativeMethods.GetWindowTextW(hwnd, buffer, buffer.Length);
            return length <= 0 ? string.Empty : new string(buffer, 0, length);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }
    }
}
