using System;
using System.Windows.Forms;
using Krizzix.Application;
using Krizzix.Bootstrap;
using Krizzix.Configuration;
using Krizzix.Services;
using Krizzix.UI;

namespace Krizzix
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            StartupOptions options = StartupOptions.Parse(args);

            if (options.DebugEnabled)
                ConsoleManager.AllocDebugConsole();
            else
                ConsoleManager.FreeHeadlessConsole();

            var logger = new AppLogger(options.DebugEnabled);

            if (options.OpenSettings)
            {
                ShowSettingsWindow(logger);
                return;
            }

            if (options.ShowHiddenWindows)
            {
                ShowConfiguredWindows(logger);
                return;
            }

            if (options.HideTaskbar)
            {
                int hidden = TaskbarVisibilityService.HideTaskbars(logger);
                logger.Info("Hidden taskbars: " + hidden);
                return;
            }

            if (options.ShowTaskbar)
            {
                int shown = TaskbarVisibilityService.ShowTaskbars(logger);
                logger.Info("Shown taskbars: " + shown);
                return;
            }

            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.Run(new HeadlessApplicationContext(options, logger));
            }
            catch (Exception ex)
            {
                logger.Error("Fatal error.", ex);
                if (options.DebugEnabled)
                    MessageBox.Show(ex.ToString(), "Krizzix - Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowSettingsWindow(AppLogger logger)
        {
            var configManager = new ConfigManager(logger);
            configManager.LoadOrCreateDefault();

            if (System.Windows.Application.Current == null)
                new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

            new SettingsWindow(configManager).ShowDialog();
        }

        private static void ShowConfiguredWindows(AppLogger logger)
        {
            var configManager = new ConfigManager(logger);
            WindowHiderConfig config = configManager.LoadOrCreateDefault();
            int shown = WindowHiderService.ShowConfiguredWindows(config, logger);
            logger.Info("Shown windows: " + shown);
        }
    }
}
