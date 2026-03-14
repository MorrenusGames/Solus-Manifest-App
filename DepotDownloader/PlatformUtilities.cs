#nullable disable
// Stub class to replace platform utilities
using System;

namespace DepotDownloader
{
    static class PlatformUtilities
    {
        public static bool IsRunningOnMono => Type.GetType("Mono.Runtime") != null;

        public static void SetConsoleTitle(string title)
        {
            // No-op for GUI mode
        }

        public static void SetExecutable(string path, bool executable)
        {
            // No-op for Windows GUI mode
        }
    }
}
