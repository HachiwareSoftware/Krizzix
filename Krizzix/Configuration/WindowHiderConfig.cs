using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Krizzix.Configuration
{
    public sealed class WindowHiderConfig
    {
        public List<string> executables_to_hide { get; set; }
        public bool partial_match { get; set; }
        public int polling_interval_ms { get; set; }

        public static WindowHiderConfig CreateDefault()
        {
            return new WindowHiderConfig
            {
                executables_to_hide = new List<string> { "notepad.exe", "calc.exe", "mspaint.exe" },
                partial_match = true,
                polling_interval_ms = 100
            };
        }

        public WindowHiderConfig Normalize()
        {
            if (executables_to_hide == null)
                executables_to_hide = new List<string>();

            executables_to_hide = executables_to_hide
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Path.GetFileName(x.Trim()).ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (polling_interval_ms < 10)
                polling_interval_ms = 100;
            if (polling_interval_ms > 10000)
                polling_interval_ms = 10000;

            return this;
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (executables_to_hide == null || executables_to_hide.Count == 0)
                errors.Add("Add at least one executable name to hide.");
            if (polling_interval_ms < 10 || polling_interval_ms > 10000)
                errors.Add("Polling interval must be between 10 and 10000 ms.");
            return errors;
        }

        public bool MatchesProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            string normalizedProcess = Path.GetFileName(processName.Trim()).ToLowerInvariant();
            foreach (string executable in executables_to_hide ?? new List<string>())
            {
                string target = Path.GetFileName((executable ?? string.Empty).Trim()).ToLowerInvariant();
                if (target.Length == 0)
                    continue;

                if (partial_match)
                {
                    if (normalizedProcess.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0
                        || target.IndexOf(normalizedProcess, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                else if (string.Equals(normalizedProcess, target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
