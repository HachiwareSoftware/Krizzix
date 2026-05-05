using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Krizzix.Configuration;

namespace Krizzix.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly ConfigManager _configManager;

        public SettingsWindow(ConfigManager configManager)
        {
            _configManager = configManager;
            InitializeComponent();
            LoadConfig(_configManager.Current);
        }

        private void LoadConfig(WindowHiderConfig config)
        {
            if (config == null)
                config = WindowHiderConfig.CreateDefault().Normalize();

            TxtExecutables.Text = string.Join(Environment.NewLine, config.executables_to_hide ?? new List<string>());
            ChkPartialMatch.IsChecked = config.partial_match;
            TxtPollingInterval.Text = config.polling_interval_ms.ToString();
            HideValidation();
        }

        private WindowHiderConfig BuildConfigFromInputs()
        {
            int interval;
            if (!int.TryParse(TxtPollingInterval.Text.Trim(), out interval))
                interval = 100;

            return new WindowHiderConfig
            {
                executables_to_hide = TxtExecutables.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList(),
                partial_match = ChkPartialMatch.IsChecked == true,
                polling_interval_ms = interval
            };
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            WindowHiderConfig config = BuildConfigFromInputs();
            string error = _configManager.Save(config);
            if (error != null)
            {
                ShowValidation(error);
                return;
            }

            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowValidation(string message)
        {
            TxtValidation.Text = message;
            ValidationBar.Visibility = Visibility.Visible;
        }

        private void HideValidation()
        {
            TxtValidation.Text = string.Empty;
            ValidationBar.Visibility = Visibility.Collapsed;
        }
    }
}
