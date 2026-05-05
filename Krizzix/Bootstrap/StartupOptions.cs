namespace Krizzix.Bootstrap
{
    public sealed class StartupOptions
    {
        public bool DebugEnabled { get; private set; }
        public bool OpenSettings { get; private set; }

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
            }

            return options;
        }
    }
}
