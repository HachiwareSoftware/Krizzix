using System;
using System.IO;
using Krizzix.Interop;

namespace Krizzix.Bootstrap
{
    internal static class ConsoleManager
    {
        public static void AllocDebugConsole()
        {
            NativeMethods.AllocConsole();
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            catch { }
        }

        public static void FreeHeadlessConsole()
        {
            try { NativeMethods.FreeConsole(); }
            catch { }
        }
    }
}
