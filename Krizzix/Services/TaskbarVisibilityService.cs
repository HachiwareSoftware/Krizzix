using System;
using System.Collections.Generic;
using Krizzix.Interop;

namespace Krizzix.Services
{
    internal static class TaskbarVisibilityService
    {
        private const string PrimaryTaskbarClass = "Shell_TrayWnd";
        private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

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
                NativeMethods.ShowWindow(hwnd, command);

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
