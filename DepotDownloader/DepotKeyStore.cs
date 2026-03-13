#nullable disable
// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Linq;

namespace DepotDownloader
{
    public static class DepotKeyStore
    {
        private static Dictionary<uint, byte[]> depotKeysCache = new Dictionary<uint, byte[]>();

        public static void AddAll(string[] values)
        {
            foreach (string value in values)
            {
                string[] split = value.Split(';');

                if (split.Length != 2)
                {
                    throw new FormatException($"Invalid depot key line: {value}");
                }

                depotKeysCache.Add(uint.Parse(split[0]), StringToByteArray(split[1]));
            }
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static bool ContainsKey(uint depotId)
        {
            return depotKeysCache.ContainsKey(depotId);
        }

        public static byte[] Get(uint depotId)
        {
            return depotKeysCache[depotId];
        }

        public static bool AddKey(string value)
        {
            string[] split = value.Split(';');

            if (split.Length != 2)
            {
                throw new FormatException($"Invalid depot key line: {value}");
            }

            uint depotId = uint.Parse(split[0]);
            if (depotKeysCache.ContainsKey(depotId))
            {
                return false; // Key already exists
            }

            depotKeysCache[depotId] = StringToByteArray(split[1]);
            return true; // Key was added
        }
    }
}
