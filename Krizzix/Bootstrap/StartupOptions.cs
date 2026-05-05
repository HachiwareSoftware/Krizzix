namespace Krizzix.Bootstrap
{
    public sealed class StartupOptions
    {
        public bool DebugEnabled { get; private set; }
        public bool OpenSettings { get; private set; }
        public bool ShowHiddenWindows { get; private set; }
        public bool HideTaskbar { get; private set; }
        public bool ShowTaskbar { get; private set; }

        private StartupOptions() { }

        public static StartupOptions Parse(string[] args)
        {
            var options = new StartupOptions();
            if (args == null)
                return options;

            foreach (string arg in args)
            {
                string normalized = (arg ?? string.Empty).Trim().ToLowerInvariant();
                if (normalized == "--debug" || normalized == "-debug" || normalized == "/debug")
                    options.DebugEnabled = true;
                else if (normalized == "--open-settings" || normalized == "-open-settings" || normalized == "/open-settings")
                    options.OpenSettings = true;
                else if (normalized == "--show" || normalized == "-show" || normalized == "/show")
                    options.ShowHiddenWindows = true;
                else if (normalized == "--hide-taskbar" || normalized == "-hide-taskbar" || normalized == "/hide-taskbar")
                    options.HideTaskbar = true;
                else if (normalized == "--show-taskbar" || normalized == "-show-taskbar" || normalized == "/show-taskbar")
                    options.ShowTaskbar = true;
            }

            return options;
        }
    }
}
