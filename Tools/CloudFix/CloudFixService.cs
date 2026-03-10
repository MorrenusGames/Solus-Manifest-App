using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SolusManifestApp.Tools.CloudFix
{
    public class CloudFixService : IDisposable
    {
        const int RVA_CLOUD_STRING = 0x1A38F0;
        const int RVA_REPLACEMENT_ID = 0x1C9E5C;
        const int RVA_CACHED_GAME_ID = 0x1C9E58;
        const int RVA_IPC_GAME_ID = 0x1C9A88;

        static readonly byte[] Signature = Encoding.ASCII.GetBytes("Cloud.GetAppFileChangelist#1\0");
        const uint OriginalReplacementId = 760;

        readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        readonly ConcurrentDictionary<uint, AppInfo> _appInfoCache = new();

        static readonly string[] BlockedPublishers = { "capcom" };

        public void Dispose()
        {
            _http.Dispose();
        }

        const uint PROCESS_VM_READ = 0x0010;
        const uint PROCESS_VM_WRITE = 0x0020;
        const uint PROCESS_VM_OPERATION = 0x0008;
        const uint PROCESS_QUERY_INFORMATION = 0x0400;
        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_READWRITE = 0x04;

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORY_BASIC_INFORMATION
        {
            public nuint BaseAddress;
            public nuint AllocationBase;
            public uint AllocationProtect;
            public nuint RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern nint OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(nint handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(nint hProcess, nuint baseAddr, byte[] buffer, nuint size, out nuint bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(nint hProcess, nuint baseAddr, byte[] buffer, nuint size, out nuint bytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern nuint VirtualQueryEx(nint hProcess, nuint address, out MEMORY_BASIC_INFORMATION mbi, nuint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(nint hProcess, nuint address, nuint size, uint newProtect, out uint oldProtect);

        static uint? ReadDword(nint handle, nuint address)
        {
            var buf = new byte[4];
            if (ReadProcessMemory(handle, address, buf, 4, out var read) && read == 4)
                return BitConverter.ToUInt32(buf, 0);
            return null;
        }

        static bool WriteDword(nint handle, nuint address, uint value)
        {
            VirtualProtectEx(handle, address, 4, PAGE_READWRITE, out uint oldProtect);
            var buf = BitConverter.GetBytes(value);
            bool ok = WriteProcessMemory(handle, address, buf, 4, out var written) && written == 4;
            VirtualProtectEx(handle, address, 4, oldProtect, out _);
            return ok;
        }

        static nuint? ScanForSignature(nint handle)
        {
            nuint address = 0;
            nuint limit = (nuint)0x7FFFFFFFFFFF;

            while (address < limit)
            {
                var result = VirtualQueryEx(handle, address, out var mbi, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
                if (result == 0) break;

                nuint regionSize = mbi.RegionSize;
                if (regionSize == 0) break;

                if (mbi.State == MEM_COMMIT &&
                    (mbi.Protect & 0x100) == 0 &&
                    mbi.Protect != 0 && mbi.Protect != 1 &&
                    regionSize < 0x10000000)
                {
                    var buf = new byte[regionSize];
                    if (ReadProcessMemory(handle, mbi.BaseAddress, buf, regionSize, out var bytesRead) && bytesRead > 0)
                    {
                        int idx = FindBytes(buf, (int)bytesRead, Signature);
                        if (idx >= 0)
                            return mbi.BaseAddress + (nuint)idx;
                    }
                }

                var next = mbi.BaseAddress + regionSize;
                if (next <= address) break;
                address = next;
            }

            return null;
        }

        static int FindBytes(byte[] haystack, int haystackLen, byte[] needle)
        {
            int end = haystackLen - needle.Length;
            for (int i = 0; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        public static int? FindSteamPid()
        {
            var processes = Process.GetProcessesByName("steam");
            try
            {
                foreach (var p in processes)
                {
                    try { return p.Id; } catch { }
                }
                return null;
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }

        const string StoreApiUrl = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1";

        public record AppInfo(string Name, string[] Developers, string[] Publishers, bool IsBlocked);

        public async Task<AppInfo?> QueryAppInfoAsync(uint appId)
        {
            if (_appInfoCache.TryGetValue(appId, out var cached))
                return cached;

            try
            {
                var inputJson = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"en\",\"country_code\":\"US\"}},\"data_request\":{{\"include_basic_info\":true}}}}";
                var url = $"{StoreApiUrl}?input_json={Uri.EscapeDataString(inputJson)}";

                var resp = await _http.GetStringAsync(url);
                var root = JObject.Parse(resp);
                var items = root["response"]?["store_items"];
                if (items == null || !items.HasValues) return null;

                var item = items[0];
                if (item?["success"]?.Value<int>() != 1) return null;

                var name = item["name"]?.Value<string>() ?? $"app {appId}";
                var basic = item["basic_info"];
                var devs = basic?["developers"]?.Select(d => d["name"]?.Value<string>() ?? "").Where(s => s.Length > 0).ToArray() ?? Array.Empty<string>();
                var pubs = basic?["publishers"]?.Select(p => p["name"]?.Value<string>() ?? "").Where(s => s.Length > 0).ToArray() ?? Array.Empty<string>();

                bool blocked = devs.Concat(pubs).Any(entity =>
                    BlockedPublishers.Any(b => entity.Contains(b, StringComparison.OrdinalIgnoreCase)));

                var info = new AppInfo(name, devs, pubs, blocked);
                _appInfoCache[appId] = info;
                return info;
            }
            catch
            {
                return null;
            }
        }

        public record PayloadInfo(nint ProcessHandle, nuint PayloadBase);

        public PayloadInfo? Attach()
        {
            var pid = FindSteamPid();
            if (pid == null) return null;

            uint access = PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION;
            var handle = OpenProcess(access, false, (uint)pid.Value);
            if (handle == 0) return null;

            var sigAddr = ScanForSignature(handle);
            if (sigAddr == null)
            {
                CloseHandle(handle);
                return null;
            }

            return new PayloadInfo(handle, sigAddr.Value - (nuint)RVA_CLOUD_STRING);
        }

        public void Detach(PayloadInfo info)
        {
            CloseHandle(info.ProcessHandle);
        }

        public record CloudFixStatus(uint ReplacementId, uint GameAppId, bool IsActive, bool IsDisabled);

        public CloudFixStatus? GetStatus(PayloadInfo info)
        {
            var replacement = ReadDword(info.ProcessHandle, info.PayloadBase + (nuint)RVA_REPLACEMENT_ID);
            var cachedId = ReadDword(info.ProcessHandle, info.PayloadBase + (nuint)RVA_CACHED_GAME_ID);
            var ipcId = ReadDword(info.ProcessHandle, info.PayloadBase + (nuint)RVA_IPC_GAME_ID);

            if (replacement == null) return null;

            uint gameId = (cachedId ?? 0) != 0 ? cachedId!.Value : (ipcId ?? 0);
            return new CloudFixStatus(
                replacement.Value,
                gameId,
                IsActive: replacement.Value == OriginalReplacementId,
                IsDisabled: replacement.Value == 0
            );
        }

        public bool DisableCloudFix(PayloadInfo info)
        {
            return WriteDword(info.ProcessHandle, info.PayloadBase + (nuint)RVA_REPLACEMENT_ID, 0);
        }

        public bool RestoreCloudFix(PayloadInfo info)
        {
            return WriteDword(info.ProcessHandle, info.PayloadBase + (nuint)RVA_REPLACEMENT_ID, OriginalReplacementId);
        }

        public async Task MonitorAsync(PayloadInfo info, Func<uint, Task<bool>> shouldDisable, Action<string> log, CancellationToken ct)
        {
            uint lastGameId = 0;
            uint lastReplacement = uint.MaxValue;

            while (!ct.IsCancellationRequested)
            {
                var status = GetStatus(info);
                if (status == null)
                {
                    log("Lost connection to Steam process.");
                    break;
                }

                if (status.GameAppId != lastGameId)
                {
                    if (status.GameAppId != 0)
                    {
                        bool disable = await shouldDisable(status.GameAppId);
                        var tag = disable ? " [BLOCKED]" : "";
                        log($"Game changed: app {status.GameAppId}{tag}");
                    }
                    else
                    {
                        log("No active game.");
                    }
                    lastGameId = status.GameAppId;
                }

                if (status.ReplacementId != lastReplacement)
                {
                    var state = status.IsActive ? "ACTIVE" : status.IsDisabled ? "DISABLED" : $"UNKNOWN({status.ReplacementId})";
                    log($"Cloud fix state: {state}");
                    lastReplacement = status.ReplacementId;
                }

                if (status.GameAppId != 0 && status.IsActive)
                {
                    if (await shouldDisable(status.GameAppId))
                    {
                        log($"Disabling cloud fix for app {status.GameAppId}...");
                        DisableCloudFix(info);
                    }
                }
                else if (status.GameAppId != 0 && status.IsDisabled)
                {
                    if (!await shouldDisable(status.GameAppId))
                    {
                        log($"Restoring cloud fix for app {status.GameAppId}...");
                        RestoreCloudFix(info);
                    }
                }

                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
