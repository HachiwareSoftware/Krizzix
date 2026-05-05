using System;
using System.Windows.Forms;
using Krizzix.Bootstrap;
using Krizzix.Configuration;
using Krizzix.Services;

namespace Krizzix.Application
{
    internal sealed class HeadlessApplicationContext : ApplicationContext
    {
        private readonly AppLogger _logger;
        private readonly ConfigManager _configManager;
        private readonly WindowHiderService _windowHiderService;

        public HeadlessApplicationContext(StartupOptions options, AppLogger logger)
        {
            _logger = logger;
            _configManager = new ConfigManager(_logger);
            WindowHiderConfig config = _configManager.LoadOrCreateDefault();

            _windowHiderService = new WindowHiderService(config, _logger);
            _windowHiderService.Start();

            PrintStartupBanner(options);
        }

        private void PrintStartupBanner(StartupOptions options)
        {
            WindowHiderConfig config = _configManager.Current;
            _logger.Info("=== Krizzix ===");
            _logger.Info("Debug mode: " + (options.DebugEnabled ? "Enabled" : "Disabled"));
            _logger.Info("Config: " + _configManager.ConfigFilePath);
            _logger.Info("Matching: " + (config.partial_match ? "Partial" : "Exact"));
            _logger.Info("Polling: " + config.polling_interval_ms + " ms");
            _logger.Info("Executables: " + string.Join(", ", config.executables_to_hide));
            _logger.Info("Use --open-settings to edit configuration.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _windowHiderService.Dispose();
                _logger.Info("Krizzix shutdown.");
            }

            base.Dispose(disposing);
        }
    }
}
