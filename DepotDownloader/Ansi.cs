#nullable disable
// Stub class to replace console ANSI functionality
namespace DepotDownloader
{
    static class Ansi
    {
        public static string Progress(string message) => message;
        public static string Progress(string message, string state) => message;
        public static void Progress(ulong current, ulong total) { /* No-op for GUI mode */ }
        public static string Text(string message) => message;

        public static class ProgressState
        {
            public const string Indeterminate = "indeterminate";
            public const string Hidden = "hidden";
        }
    }
}
