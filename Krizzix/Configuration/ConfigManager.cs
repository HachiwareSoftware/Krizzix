using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Krizzix.Services;

namespace Krizzix.Configuration
{
    public sealed class ConfigManager
    {
        private readonly AppLogger _logger;

        public string ConfigFilePath { get; }
        public WindowHiderConfig Current { get; private set; }

        public ConfigManager(AppLogger logger)
        {
            _logger = logger;
            ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        public WindowHiderConfig LoadOrCreateDefault()
        {
            if (!File.Exists(ConfigFilePath))
            {
                Current = WindowHiderConfig.CreateDefault().Normalize();
                SaveInternal(Current);
                return Current;
            }

            try
            {
                string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                Current = (serializer.Deserialize<WindowHiderConfig>(json) ?? WindowHiderConfig.CreateDefault()).Normalize();
                return Current;
            }
            catch (Exception ex)
            {
                _logger.Error("config.json is corrupt; backing up and recreating defaults.", ex);
                BackupCorruptConfig();
                Current = WindowHiderConfig.CreateDefault().Normalize();
                SaveInternal(Current);
                return Current;
            }
        }

        public string Save(WindowHiderConfig config)
        {
            if (config == null)
                return "Config is empty.";

            config.Normalize();
            var errors = config.Validate();
            if (errors.Count > 0)
                return string.Join(Environment.NewLine, errors);

            try
            {
                SaveInternal(config);
                Current = config;
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save config.json.", ex);
                return ex.Message;
            }
        }

        private void SaveInternal(WindowHiderConfig config)
        {
            var serializer = new JavaScriptSerializer();
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"executables_to_hide\": [");
            for (int i = 0; i < config.executables_to_hide.Count; i++)
            {
                string suffix = i == config.executables_to_hide.Count - 1 ? "" : ",";
                builder.Append("    ").Append(serializer.Serialize(config.executables_to_hide[i])).AppendLine(suffix);
            }
            builder.AppendLine("  ],");
            builder.AppendLine("  \"partial_match\": " + (config.partial_match ? "true" : "false") + ",");
            builder.AppendLine("  \"polling_interval_ms\": " + config.polling_interval_ms);
            builder.AppendLine("}");
            File.WriteAllText(ConfigFilePath, builder.ToString(), Encoding.UTF8);
            _logger.Info("config.json saved.");
        }

        private void BackupCorruptConfig()
        {
            try
            {
                string backupPath = ConfigFilePath + ".bak." + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(ConfigFilePath, backupPath, overwrite: true);
            }
            catch { }
        }
    }
}
