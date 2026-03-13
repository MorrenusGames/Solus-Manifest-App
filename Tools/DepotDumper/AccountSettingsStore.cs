#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.IsolatedStorage;
using ProtoBuf;

namespace SolusManifestApp.Tools.DepotDumper
{
    [ProtoContract]
    class AccountSettingsStore
    {
        [ProtoMember(2, IsRequired = false)]
        public ConcurrentDictionary<string, int> ContentServerPenalty { get; private set; }

        [ProtoMember(4, IsRequired = false)]
        public Dictionary<string, string> LoginTokens { get; private set; }

        [ProtoMember(5, IsRequired = false)]
        public Dictionary<string, string> GuardData { get; private set; }

        string FileName;

        AccountSettingsStore()
        {
            ContentServerPenalty = new ConcurrentDictionary<string, int>();
            LoginTokens = [];
            GuardData = [];
        }

        static bool Loaded
        {
            get { return Instance != null; }
        }

        public static AccountSettingsStore Instance;
        static readonly IsolatedStorageFile IsolatedStorage = IsolatedStorageFile.GetUserStoreForAssembly();

        public static void LoadFromFile(string filename)
        {
            if (Loaded)
                return; // Changed from throwing exception

            if (IsolatedStorage.FileExists(filename))
            {
                try
                {
                    using var fs = IsolatedStorage.OpenFile(filename, FileMode.Open, FileAccess.Read);
                    using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                    Instance = Serializer.Deserialize<AccountSettingsStore>(ds);
                }
                catch (IOException)
                {
                    Instance = new AccountSettingsStore();
                }
            }
            else
            {
                Instance = new AccountSettingsStore();
            }

            Instance.FileName = filename;
        }

        public static void Save()
        {
            if (!Loaded)
                return; // Changed from throwing exception

            try
            {
                using var fs = IsolatedStorage.OpenFile(Instance.FileName, FileMode.Create, FileAccess.Write);
                using var ds = new DeflateStream(fs, CompressionMode.Compress);
                Serializer.Serialize(ds, Instance);
            }
            catch (IOException)
            {
                // Silently ignore save errors
            }
        }
    }
}
