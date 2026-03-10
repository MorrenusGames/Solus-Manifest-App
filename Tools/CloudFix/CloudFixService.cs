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
        // Signature: call [rel32]; test eax,eax; jnz [rel32]; test r14d,r14d; jz [rel32]
        static readonly byte?[] HookSignature = ParsePattern(
            "E8 ?? ?? ?? ?? 85 C0 0F 85 ?? ?? ?? ?? 45 85 F6 0F 84 ?? ?? ?? ??");

        const int HookPatchOffset = 13;  // test r14d,r14d starts 13 bytes into the pattern
        const int HookPatchSize = 9;     // test r14d,r14d (3) + jz rel32 (6)

        static readonly byte[] PayloadFingerprint = Encoding.ASCII.GetBytes("Cloud.GetAppFileChangelist#1\0");
        const int PayloadFingerprintRva = 0x1A38F0;

        readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        readonly ConcurrentDictionary<uint, AppInfo> _appInfoCache = new();
        static readonly string[] BlockedPublishers = { "capcom" };

        public void Dispose() => _http.Dispose();

        const int CaveSize = 0x1000;
        const int CountOffset = 0xE00;
        const int ListOffset = 0xE04;
        const int MaxBlockedApps = 125;
        const int ScanChunkSize = 0x100000; // 1MB

        const uint PROCESS_VM_READ = 0x0010;
        const uint PROCESS_VM_WRITE = 0x0020;
        const uint PROCESS_VM_OPERATION = 0x0008;
        const uint PROCESS_QUERY_INFORMATION = 0x0400;
        const uint MEM_COMMIT = 0x1000;
        const uint MEM_RESERVE = 0x2000;
        const uint MEM_FREE = 0x10000;
        const uint PAGE_EXECUTE_READWRITE = 0x40;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern nuint VirtualAllocEx(nint hProcess, nuint address, nuint size, uint allocType, uint protect);

        static bool WriteBytes(nint handle, nuint address, byte[] data, bool makeWritable = false)
        {
            uint oldProtect = 0;
            if (makeWritable)
                VirtualProtectEx(handle, address, (nuint)data.Length, PAGE_EXECUTE_READWRITE, out oldProtect);

            bool ok = WriteProcessMemory(handle, address, data, (nuint)data.Length, out var written)
                      && written == (nuint)data.Length;

            if (makeWritable)
                VirtualProtectEx(handle, address, (nuint)data.Length, oldProtect, out _);

            return ok;
        }

        static byte[]? ReadBytes(nint handle, nuint address, int count)
        {
            var buf = new byte[count];
            if (ReadProcessMemory(handle, address, buf, (nuint)count, out var read) && read == (nuint)count)
                return buf;
            return null;
        }

        static nuint AllocateNear(nint handle, nuint target, nuint size)
        {
            const long range = 0x7FFF0000;
            long lo = Math.Max((long)target - range, 0x10000);
            long hi = (long)target + range;
            lo = (lo + 0xFFFF) & ~0xFFFF;

            nuint addr = (nuint)lo;
            while ((long)addr < hi)
            {
                var result = VirtualQueryEx(handle, addr, out var mbi, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
                if (result == 0 || mbi.RegionSize == 0) break;

                if (mbi.State == MEM_FREE)
                {
                    nuint candidate = VirtualAllocEx(handle, addr, size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                    if (candidate != 0) return candidate;
                }

                var next = mbi.BaseAddress + mbi.RegionSize;
                next = (next + 0xFFFF) & ~(nuint)0xFFFF;
                if (next <= addr) break;
                addr = next;
            }
            return 0;
        }

        static bool IsReadableRegion(MEMORY_BASIC_INFORMATION mbi)
        {
            return mbi.State == MEM_COMMIT &&
                   (mbi.Protect & 0x100) == 0 &&  // not PAGE_GUARD
                   mbi.Protect != 0 && mbi.Protect != 1 &&
                   mbi.RegionSize < 0x10000000;
        }

        static nuint? ScanForBytes(nint handle, byte[] needle)
        {
            nuint address = 0;
            nuint limit = (nuint)0x7FFFFFFFFFFF;
            var chunk = new byte[ScanChunkSize];

            while (address < limit)
            {
                var result = VirtualQueryEx(handle, address, out var mbi, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
                if (result == 0 || mbi.RegionSize == 0) break;

                if (IsReadableRegion(mbi))
                {
                    nuint regionEnd = mbi.BaseAddress + mbi.RegionSize;
                    nuint cursor = mbi.BaseAddress;

                    while (cursor < regionEnd)
                    {
                        nuint remaining = regionEnd - cursor;
                        if (remaining < (nuint)needle.Length) break;

                        nuint toRead = Math.Min((nuint)ScanChunkSize, remaining);
                        if (ReadProcessMemory(handle, cursor, chunk, toRead, out var bytesRead) && bytesRead >= (nuint)needle.Length)
                        {
                            int idx = FindBytes(chunk, (int)bytesRead, needle);
                            if (idx >= 0)
                                return cursor + (nuint)idx;
                            cursor += bytesRead - (nuint)(needle.Length - 1);
                        }
                        else break;
                    }
                }

                var next = mbi.BaseAddress + mbi.RegionSize;
                if (next <= address) break;
                address = next;
            }
            return null;
        }

        static nuint? ScanForPattern(nint handle, byte?[] pattern, nuint startAddr, nuint endAddr)
        {
            nuint address = startAddr;
            var chunk = new byte[ScanChunkSize];

            while (address < endAddr)
            {
                var result = VirtualQueryEx(handle, address, out var mbi, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
                if (result == 0 || mbi.RegionSize == 0) break;

                if (IsReadableRegion(mbi))
                {
                    nuint regionEnd = mbi.BaseAddress + mbi.RegionSize;
                    nuint cursor = mbi.BaseAddress < startAddr ? startAddr : mbi.BaseAddress;
                    nuint scanEnd = regionEnd < endAddr ? regionEnd : endAddr;

                    while (cursor < scanEnd)
                    {
                        nuint remaining = scanEnd - cursor;
                        if (remaining < (nuint)pattern.Length) break;

                        nuint toRead = Math.Min((nuint)ScanChunkSize, remaining);
                        if (ReadProcessMemory(handle, cursor, chunk, toRead, out var bytesRead) && bytesRead >= (nuint)pattern.Length)
                        {
                            int idx = FindPattern(chunk, (int)bytesRead, pattern);
                            if (idx >= 0)
                                return cursor + (nuint)idx;
                            cursor += bytesRead - (nuint)(pattern.Length - 1);
                        }
                        else break;
                    }
                }

                var next = mbi.BaseAddress + mbi.RegionSize;
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

        static int FindPattern(byte[] haystack, int haystackLen, byte?[] pattern)
        {
            int end = haystackLen - pattern.Length;
            for (int i = 0; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (pattern[j].HasValue && haystack[i + j] != pattern[j].Value) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        static byte?[] ParsePattern(string pattern)
        {
            return pattern.Split(' ')
                .Select(s => s == "??" ? (byte?)null : byte.Parse(s, System.Globalization.NumberStyles.HexNumber))
                .ToArray();
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

        // Skip-path jmp is at this fixed offset in our shellcode
        const int CaveSkipJmpOffset = 0x2B;

        public record AttachResult(nint ProcessHandle, nuint PayloadBase, nuint HookAddr, nuint SkipAddr, nuint CaveAddr, byte[] OriginalBytes, bool WasAlreadyHooked);

        public AttachResult? Attach(out string error)
        {
            error = "";
            var pid = FindSteamPid();
            if (pid == null) { error = "Steam process not found."; return null; }

            uint access = PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION;
            var handle = OpenProcess(access, false, (uint)pid.Value);
            if (handle == 0) { error = "Failed to open Steam process."; return null; }

            var sigAddr = ScanForBytes(handle, PayloadFingerprint);
            if (sigAddr == null) { CloseHandle(handle); error = "Payload fingerprint not found in Steam memory."; return null; }

            nuint payloadBase = sigAddr.Value - (nuint)PayloadFingerprintRva;

            nuint searchStart = payloadBase;
            nuint searchEnd = payloadBase + 0x200000;

            var patternAddr = ScanForPattern(handle, HookSignature, searchStart, searchEnd);
            if (patternAddr == null) { CloseHandle(handle); error = "Hook signature not found. SteamTools may have updated."; return null; }

            nuint hookAddr = patternAddr.Value + (nuint)HookPatchOffset;

            var verify = ReadBytes(handle, hookAddr, HookPatchSize);
            if (verify == null) { CloseHandle(handle); error = "Failed to read hook point bytes."; return null; }

            // Already hooked (e.g. Solus crashed without cleanup)
            if (verify[0] == 0xE9)
            {
                int existingRel = BitConverter.ToInt32(verify, 1);
                nuint existingCave = (nuint)((long)hookAddr + 5 + existingRel);

                // Read the skip-path jmp from our known fixed offset in the cave
                var skipJmpBytes = ReadBytes(handle, existingCave + CaveSkipJmpOffset, 5);
                if (skipJmpBytes == null || skipJmpBytes[0] != 0xE9)
                {
                    CloseHandle(handle); error = "Existing hook detected but cave unreadable."; return null;
                }

                int skipRel = BitConverter.ToInt32(skipJmpBytes, 1);
                nuint skipAddr = (nuint)((long)(existingCave + (nuint)CaveSkipJmpOffset) + 5 + skipRel);

                // Reconstruct original bytes: test r14d,r14d (45 85 F6) + jz rel32 (0F 84 xx xx xx xx)
                byte[] origBytes = new byte[HookPatchSize];
                origBytes[0] = 0x45; origBytes[1] = 0x85; origBytes[2] = 0xF6;
                origBytes[3] = 0x0F; origBytes[4] = 0x84;
                int reconstructedJzRel = (int)((long)skipAddr - (long)(hookAddr + 9));
                BitConverter.GetBytes(reconstructedJzRel).CopyTo(origBytes, 5);

                return new AttachResult(handle, payloadBase, hookAddr, skipAddr, existingCave, origBytes, true);
            }

            // Fresh hook — verify expected bytes
            if (verify[0] != 0x45 || verify[1] != 0x85 || verify[2] != 0xF6 || verify[3] != 0x0F || verify[4] != 0x84)
            {
                CloseHandle(handle);
                error = $"Unexpected bytes at hook point: {BitConverter.ToString(verify)}";
                return null;
            }

            int jzOffset = BitConverter.ToInt32(verify, 5);
            nuint skipAddr2 = (nuint)((long)hookAddr + 9 + jzOffset);

            byte[] origBytes2 = new byte[HookPatchSize];
            Array.Copy(verify, origBytes2, HookPatchSize);

            nuint caveAddr = AllocateNear(handle, hookAddr, (nuint)CaveSize);
            if (caveAddr == 0) { CloseHandle(handle); error = "Failed to allocate code cave within +/-2GB of hook point."; return null; }

            return new AttachResult(handle, payloadBase, hookAddr, skipAddr2, caveAddr, origBytes2, false);
        }

        public bool InstallHook(AttachResult ctx, uint[] blockedAppIds, out string error)
        {
            error = "";

            if (blockedAppIds.Length > MaxBlockedApps)
            {
                error = $"Too many blocked apps ({blockedAppIds.Length} > {MaxBlockedApps}).";
                return false;
            }

            byte[] cave = BuildCodeCave(ctx.CaveAddr, ctx.HookAddr, ctx.SkipAddr, blockedAppIds);

            if (!WriteBytes(ctx.ProcessHandle, ctx.CaveAddr, cave))
            {
                error = $"Failed to write code cave at 0x{ctx.CaveAddr:X}. Win32 error: {Marshal.GetLastWin32Error()}";
                return false;
            }

            long rel = (long)ctx.CaveAddr - (long)(ctx.HookAddr + 5);
            if (rel > int.MaxValue || rel < int.MinValue)
            {
                error = $"Cave 0x{ctx.CaveAddr:X} too far from hook 0x{ctx.HookAddr:X} (delta: {rel:X})";
                return false;
            }

            var patch = new byte[HookPatchSize];
            patch[0] = 0xE9;
            BitConverter.GetBytes((int)rel).CopyTo(patch, 1);
            for (int i = 5; i < HookPatchSize; i++) patch[i] = 0x90;

            if (!WriteBytes(ctx.ProcessHandle, ctx.HookAddr, patch, makeWritable: true))
            {
                error = $"Failed to patch hook point at 0x{ctx.HookAddr:X}. Win32 error: {Marshal.GetLastWin32Error()}";
                return false;
            }
            return true;
        }

        public bool UpdateBlockedList(AttachResult ctx, uint[] blockedAppIds)
        {
            if (blockedAppIds.Length > MaxBlockedApps) return false;

            var buf = new byte[4 + MaxBlockedApps * 4];
            BitConverter.GetBytes(blockedAppIds.Length).CopyTo(buf, 0);
            for (int i = 0; i < blockedAppIds.Length; i++)
                BitConverter.GetBytes(blockedAppIds[i]).CopyTo(buf, 4 + i * 4);

            return WriteBytes(ctx.ProcessHandle, ctx.CaveAddr + CountOffset, buf);
        }

        public void Detach(AttachResult ctx)
        {
            WriteBytes(ctx.ProcessHandle, ctx.HookAddr, ctx.OriginalBytes, makeWritable: true);
            // Don't free the cave — a Steam thread could be executing inside it
            CloseHandle(ctx.ProcessHandle);
        }

        byte[] BuildCodeCave(nuint caveBase, nuint hookAddr, nuint skipAddr, uint[] blockedAppIds)
        {
            var cave = new byte[CaveSize];
            int pos = 0;

            // Write initial blocked list data
            BitConverter.GetBytes(blockedAppIds.Length).CopyTo(cave, CountOffset);
            for (int i = 0; i < blockedAppIds.Length; i++)
                BitConverter.GetBytes(blockedAppIds[i]).CopyTo(cave, ListOffset + i * 4);

            nuint rewriteAddr = hookAddr + (nuint)HookPatchSize;

            // push rcx, push rax
            cave[pos++] = 0x51;
            cave[pos++] = 0x50;

            // test r14d, r14d (reproduce overwritten instruction)
            cave[pos++] = 0x45; cave[pos++] = 0x85; cave[pos++] = 0xF6;

            // jz .blocked — r14d==0 means no game, skip rewrite
            cave[pos++] = 0x0F; cave[pos++] = 0x84;
            int jzBlockedFixup = pos;
            pos += 4;

            // mov ecx, [rip + CountOffset]
            cave[pos++] = 0x8B; cave[pos++] = 0x0D;
            int movCountRipPos = pos;
            pos += 4;

            // test ecx, ecx
            cave[pos++] = 0x85; cave[pos++] = 0xC9;

            // jz .not_blocked
            cave[pos++] = 0x74;
            int jzNotBlockedFixup = pos;
            pos += 1;

            // lea rax, [rip + ListOffset]
            cave[pos++] = 0x48; cave[pos++] = 0x8D; cave[pos++] = 0x05;
            int leaListRipPos = pos;
            pos += 4;

            int loopPos = pos;

            // cmp r14d, [rax]
            cave[pos++] = 0x44; cave[pos++] = 0x3B; cave[pos++] = 0x30;

            // je .blocked
            cave[pos++] = 0x74;
            int jeBlockedFixup = pos;
            pos += 1;

            // add rax, 4
            cave[pos++] = 0x48; cave[pos++] = 0x83; cave[pos++] = 0xC0; cave[pos++] = 0x04;

            // dec ecx
            cave[pos++] = 0xFF; cave[pos++] = 0xC9;

            // jnz .loop
            cave[pos++] = 0x75;
            cave[pos] = (byte)(loopPos - (pos + 1));
            pos++;

            // .not_blocked: pop rax, pop rcx, jmp rewrite_path
            int notBlockedPos = pos;
            cave[pos++] = 0x58;
            cave[pos++] = 0x59;
            cave[pos++] = 0xE9;
            int jmpRewritePos = pos;
            pos += 4;

            // .blocked: pop rax, pop rcx, jmp skip_path
            int blockedPos = pos;
            Debug.Assert(blockedPos == CaveSkipJmpOffset - 2, "CaveSkipJmpOffset mismatch — update the constant");
            cave[pos++] = 0x58;
            cave[pos++] = 0x59;
            cave[pos++] = 0xE9;
            int jmpSkipPos = pos;
            Debug.Assert(jmpSkipPos == CaveSkipJmpOffset + 1, "CaveSkipJmpOffset mismatch — update the constant");
            pos += 4;

            // Fixups
            int jzBlockedRel = blockedPos - (jzBlockedFixup + 4);
            BitConverter.GetBytes(jzBlockedRel).CopyTo(cave, jzBlockedFixup);

            int movCountRel = (int)(CountOffset - (movCountRipPos + 4));
            BitConverter.GetBytes(movCountRel).CopyTo(cave, movCountRipPos);

            cave[jzNotBlockedFixup] = (byte)(notBlockedPos - (jzNotBlockedFixup + 1));

            int leaListRel = (int)(ListOffset - (leaListRipPos + 4));
            BitConverter.GetBytes(leaListRel).CopyTo(cave, leaListRipPos);

            cave[jeBlockedFixup] = (byte)(blockedPos - (jeBlockedFixup + 1));

            long jmpRewriteRel = (long)rewriteAddr - (long)(caveBase + (nuint)jmpRewritePos + 4);
            BitConverter.GetBytes((int)jmpRewriteRel).CopyTo(cave, jmpRewritePos);

            long jmpSkipRel = (long)skipAddr - (long)(caveBase + (nuint)jmpSkipPos + 4);
            BitConverter.GetBytes((int)jmpSkipRel).CopyTo(cave, jmpSkipPos);

            return cave;
        }

        const string StoreApiUrl = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1";
        public record AppInfo(string Name, string[] Developers, string[] Publishers, bool IsBlocked);

        public async Task<AppInfo?> QueryAppInfoAsync(uint appId, CancellationToken ct = default)
        {
            if (_appInfoCache.TryGetValue(appId, out var cached))
                return cached;

            try
            {
                var inputJson = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"en\",\"country_code\":\"US\"}},\"data_request\":{{\"include_basic_info\":true}}}}";
                var url = $"{StoreApiUrl}?input_json={Uri.EscapeDataString(inputJson)}";

                var resp = await _http.GetStringAsync(url, ct);
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
    }
}
