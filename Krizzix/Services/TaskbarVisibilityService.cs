using System;
using System.Collections.Generic;
using System.Threading;
using Krizzix.Interop;

namespace Krizzix.Services
{
    internal static class TaskbarVisibilityService
    {
        private const string PrimaryTaskbarClass = "Shell_TrayWnd";
        private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
        private const int VisibilityRetryCount = 5;
        private const int VisibilityRetryDelayMilliseconds = 50;

        public static int HideTaskbars(AppLogger logger)
        {
            return SetTaskbarVisibility(NativeMethods.SW_HIDE, "Hidden taskbar.", logger);
        }

        public static int ShowTaskbars(AppLogger logger)
        {
            return SetTaskbarVisibility(NativeMethods.SW_SHOW, "Shown taskbar.", logger);
        }

        private static int SetTaskbarVisibility(int command, string logMessage, AppLogger logger)
        {
            int changed = 0;
            foreach (IntPtr hwnd in FindTaskbars())
            {
                if (!NativeMethods.IsWindow(hwnd))
                    continue;

                bool wasVisible = NativeMethods.IsWindowVisible(hwnd);
                if (!ApplyVisibility(hwnd, command))
                {
                    logger.Warn("Failed to change taskbar visibility. hwnd=0x" + hwnd.ToInt64().ToString("X"));
                    continue;
                }

                bool changedVisibility = command == NativeMethods.SW_HIDE
                    ? wasVisible
                    : !wasVisible;
                if (!changedVisibility)
                    continue;

                changed++;
                logger.Info(logMessage + " hwnd=0x" + hwnd.ToInt64().ToString("X"));
            }

            return changed;
        }

        private static bool ApplyVisibility(IntPtr hwnd, int command)
        {
            bool hide = command == NativeMethods.SW_HIDE;
            uint flags = NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED
                | (hide ? NativeMethods.SWP_HIDEWINDOW : NativeMethods.SWP_SHOWWINDOW);

            for (int attempt = 0; attempt < VisibilityRetryCount; attempt++)
            {
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0, flags);

                if (NativeMethods.IsWindowVisible(hwnd) != hide)
                    return true;

                Thread.Sleep(VisibilityRetryDelayMilliseconds);
            }

            return false;
        }

        private static IEnumerable<IntPtr> FindTaskbars()
        {
            IntPtr primary = NativeMethods.FindWindowW(PrimaryTaskbarClass, null);
            if (primary != IntPtr.Zero)
                yield return primary;

            IntPtr current = IntPtr.Zero;
            while (true)
            {
                current = NativeMethods.FindWindowExW(IntPtr.Zero, current, SecondaryTaskbarClass, null);
                if (current == IntPtr.Zero)
                    yield break;

                yield return current;
            }
        }
    }
}
