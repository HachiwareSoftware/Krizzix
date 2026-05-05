using System;
using System.IO;

namespace Krizzix.Services
{
    public sealed class AppLogger
    {
        private readonly object _sync = new object();
        private readonly string _logPath;

        public bool DebugEnabled { get; }

        public AppLogger(bool debugEnabled)
        {
            DebugEnabled = debugEnabled;
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "krizzix.log");
        }

        public void Info(string message)
        {
            Write("INFO", message, null);
        }

        public void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private void Write(string level, string message, Exception ex)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            if (ex != null)
                line += Environment.NewLine + ex;

            lock (_sync)
            {
                try { File.AppendAllText(_logPath, line + Environment.NewLine); }
                catch { }
            }

            if (DebugEnabled)
                Console.WriteLine(line);
        }
    }
}
