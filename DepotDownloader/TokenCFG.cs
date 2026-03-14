#nullable disable
// Stub class for token configuration
using System.Collections.Generic;

namespace DepotDownloader
{
    static class TokenCFG
    {
        public static bool useAppToken = false;
        public static ulong appToken = 0;
        public static bool usePackageToken = false;
        public static ulong packageToken = 0;
        public static Dictionary<uint, ulong> AppTokens = new();
    }
}
