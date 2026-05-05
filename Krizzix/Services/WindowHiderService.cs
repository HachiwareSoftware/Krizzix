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
        private readonly HashSet<IntPtr> _queuedWindows = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, DateTime> _lastHideLogTimes = new Dictionary<IntPtr, DateTime>();
        private readonly Dictionary<uint, string> _processNameCache = new Dictionary<uint, string>();
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

        public static int ShowConfiguredWindows(WindowHiderConfig config, AppLogger logger)
        {
            var service = new WindowHiderService((config ?? WindowHiderConfig.CreateDefault()).Normalize(), logger);
            return service.ShowMatchingWindows();
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

            lock (_sync)
            {
                _queuedWindows.Clear();
                _lastHideLogTimes.Clear();
                _processNameCache.Clear();
            }

            _logger.Info("Window hider service stopped.");
        }

        public void UpdateConfig(WindowHiderConfig config)
        {
            _config = (config ?? WindowHiderConfig.CreateDefault()).Normalize();
            lock (_sync)
            {
                _lastHideLogTimes.Clear();
                _processNameCache.Clear();
            }
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

            HideWindowFamily(hwnd, true);
        }

        private void OnForegroundEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (!_running || hwnd == IntPtr.Zero)
                return;

            HideWindowFamily(hwnd, true);
        }

        private void ScanAllWindows()
        {
            if (!_running && _pollTimer != null)
                return;

            try
            {
                NativeMethods.EnumWindows((hwnd, lParam) =>
                {
                    HideWindowFamily(hwnd, false);
                    return true;
                }, IntPtr.Zero);

                IntPtr current = IntPtr.Zero;
                while ((current = NativeMethods.FindWindowExW(IntPtr.Zero, current, IntPtr.Zero, IntPtr.Zero)) != IntPtr.Zero)
                    HideWindowFamily(current, false);
            }
            catch (Exception ex)
            {
                _logger.Error("Window scan failed.", ex);
            }
        }

        private void HideWindowFamily(IntPtr hwnd, bool queueRetry)
        {
            TryHideOrQueue(hwnd, queueRetry);

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && root != hwnd)
                TryHideOrQueue(root, queueRetry);

            IntPtr rootOwner = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
            if (rootOwner != IntPtr.Zero && rootOwner != hwnd && rootOwner != root)
                TryHideOrQueue(rootOwner, queueRetry);
        }

        private void TryHideOrQueue(IntPtr hwnd, bool queueRetry)
        {
            if (TryHide(hwnd) || !queueRetry)
                return;

            QueueHide(hwnd);
        }

        private void QueueHide(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            lock (_sync)
            {
                if (_queuedWindows.Contains(hwnd))
                    return;
                _queuedWindows.Add(hwnd);
            }

            ThreadPool.QueueUserWorkItem(_ => RetryHide(hwnd));
        }

        private void RetryHide(IntPtr hwnd)
        {
            try
            {
                int[] delays = { 0, 10, 10, 10 };
                foreach (int delay in delays)
                {
                    if (!_running)
                        return;
                    if (delay > 0)
                        Thread.Sleep(delay);
                    if (TryHide(hwnd))
                        return;
                }
            }
            finally
            {
                lock (_sync)
                {
                    _queuedWindows.Remove(hwnd);
                }
            }
        }

        private bool TryHide(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            if (!NativeMethods.IsWindowVisible(hwnd))
                return false;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0)
                return false;

            string processName = GetCachedProcessName(processId);
            if (!_config.MatchesProcessName(processName))
                return false;

            StopFlash(hwnd);
            bool wasVisible = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            HideFromTaskbar(hwnd);

            if (wasVisible && _logger.DebugEnabled && ShouldLogHide(hwnd))
                _logger.Info("Hidden window: " + processName + " pid=" + processId + " hwnd=0x" + hwnd.ToInt64().ToString("X") + " title=\"" + GetTitle(hwnd) + "\"");

            return true;
        }

        private int ShowMatchingWindows()
        {
            var processedWindows = new HashSet<IntPtr>();
            int shownCount = 0;

            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                shownCount += TryShowWindowFamily(hwnd, processedWindows);
                return true;
            }, IntPtr.Zero);

            IntPtr current = IntPtr.Zero;
            while ((current = NativeMethods.FindWindowExW(IntPtr.Zero, current, IntPtr.Zero, IntPtr.Zero)) != IntPtr.Zero)
                shownCount += TryShowWindowFamily(current, processedWindows);

            return shownCount;
        }

        private int TryShowWindowFamily(IntPtr hwnd, HashSet<IntPtr> processedWindows)
        {
            int shownCount = TryShow(hwnd, processedWindows) ? 1 : 0;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && root != hwnd)
                shownCount += TryShow(root, processedWindows) ? 1 : 0;

            IntPtr rootOwner = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
            if (rootOwner != IntPtr.Zero && rootOwner != hwnd && rootOwner != root)
                shownCount += TryShow(rootOwner, processedWindows) ? 1 : 0;

            return shownCount;
        }

        private bool TryShow(IntPtr hwnd, HashSet<IntPtr> processedWindows)
        {
            if (hwnd == IntPtr.Zero || processedWindows.Contains(hwnd) || !NativeMethods.IsWindow(hwnd))
                return false;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0)
                return false;

            string processName = GetCachedProcessName(processId);
            if (!_config.MatchesProcessName(processName))
                return false;

            processedWindows.Add(hwnd);
            bool wasHidden = !NativeMethods.IsWindowVisible(hwnd);
            RestoreToTaskbar(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

            if (wasHidden && _logger.DebugEnabled)
                _logger.Info("Shown window: " + processName + " pid=" + processId + " hwnd=0x" + hwnd.ToInt64().ToString("X") + " title=\"" + GetTitle(hwnd) + "\"");

            return wasHidden;
        }

        private bool ShouldLogHide(IntPtr hwnd)
        {
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                DateTime lastLogged;
                if (_lastHideLogTimes.TryGetValue(hwnd, out lastLogged)
                    && (now - lastLogged).TotalMilliseconds < 500)
                    return false;

                _lastHideLogTimes[hwnd] = now;
                return true;
            }
        }

        private string GetCachedProcessName(uint processId)
        {
            lock (_sync)
            {
                string cached;
                if (_processNameCache.TryGetValue(processId, out cached))
                    return cached;
            }

            string processName = _processNameResolver.GetProcessName(processId);

            lock (_sync)
            {
                _processNameCache[processId] = processName;
            }

            return processName;
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
            RefreshFrame(hwnd);
        }

        private static void RestoreToTaskbar(IntPtr hwnd)
        {
            IntPtr stylePtr = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            long style = stylePtr.ToInt64();
            style &= ~NativeMethods.WS_EX_TOOLWINDOW;
            style |= NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
            RefreshFrame(hwnd);
        }

        private static void RefreshFrame(IntPtr hwnd)
        {
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOP,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
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
