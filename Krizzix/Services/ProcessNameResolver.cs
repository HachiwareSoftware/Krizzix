using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Krizzix.Interop;

namespace Krizzix.Services
{
    internal sealed class ProcessNameResolver
    {
        public string GetProcessName(uint processId)
        {
            string managedName = GetManagedProcessName(processId);
            if (!string.IsNullOrWhiteSpace(managedName))
                return managedName;

            return GetSnapshotProcessName(processId);
        }

        private static string GetManagedProcessName(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(unchecked((int)processId)))
                {
                    string name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name))
                        return string.Empty;
                    return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetSnapshotProcessName(uint processId)
        {
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snapshot == NativeMethods.INVALID_HANDLE_VALUE)
                return string.Empty;

            try
            {
                var entry = new NativeMethods.PROCESSENTRY32();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32));

                if (!NativeMethods.Process32FirstW(snapshot, ref entry))
                    return string.Empty;

                do
                {
                    if (entry.th32ProcessID == processId)
                        return entry.szExeFile ?? string.Empty;
                }
                while (NativeMethods.Process32NextW(snapshot, ref entry));
            }
            finally
            {
                NativeMethods.CloseHandle(snapshot);
            }

            return string.Empty;
        }
    }
}
