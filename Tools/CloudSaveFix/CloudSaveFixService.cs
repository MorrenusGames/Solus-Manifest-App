using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SolusManifestApp.Tools.CloudSaveFix
{
    public class CloudSaveFixService
    {
        private const string HijackDll = "dwmapi.dll";

        // AES-256 key from Core.dll .rdata section
        private static readonly byte[] AesKey =
        {
            0x31, 0x4C, 0x20, 0x86, 0x15, 0x05, 0x74, 0xE1,
            0x5C, 0xF1, 0x1D, 0x1B, 0xC1, 0x71, 0x25, 0x1A,
            0x47, 0x08, 0x6C, 0x00, 0x26, 0x93, 0x55, 0xCD,
            0x51, 0xC9, 0x3A, 0x42, 0x3C, 0x14, 0x02, 0x94,
        };

        // dwmapi.dll patches: stop re-download and skip hash check
        private static readonly PatchEntry[] CorePatches =
        {
            // call download -> mov eax, 1 (fake success)
            new(0x272F,
                new byte[] { 0xE8, 0x7C, 0xF5, 0xFF, 0xFF },
                new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00 }),
            // jz (hash match) -> jmp (skip hash check)
            new(0x28B5,
                new byte[] { 0x74 },
                new byte[] { 0xEB }),
        };

        // payload.dll patches: disable cloud app ID rewrite
        private static readonly PatchEntry[] PayloadPatches =
        {
            // jz over rewrite path 1 -> nop + jmp (always skip)
            new(0x0D33F,
                new byte[] { 0x0F, 0x84, 0x3B, 0x01, 0x00, 0x00 },
                new byte[] { 0x90, 0xE9, 0x3B, 0x01, 0x00, 0x00 }),
            // mov ecx, [proxy_appid_760] -> xor ecx, ecx (zero it)
            new(0x0D649,
                new byte[] { 0x8B, 0x0D, 0x0D, 0xBC, 0x1B, 0x00 },
                new byte[] { 0x31, 0xC9, 0x90, 0x90, 0x90, 0x90 }),
            // mov [ipc_appid], edi -> nop (preserve IPC mapping)
            new(0x1D4F53,
                new byte[] { 0x89, 0x3D, 0x2F, 0xD5, 0xFE, 0xFF },
                new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }),
        };

        public delegate void LogHandler(string message);
        public event LogHandler OnLog;

        private void Log(string msg) => OnLog?.Invoke(msg);

        public PatchState GetPatchState(string steamPath)
        {
            var dllPath = Path.Combine(steamPath, HijackDll);
            if (!File.Exists(dllPath))
                return PatchState.NotInstalled;

            byte[] dll;
            try
            {
                using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                dll = new byte[fs.Length];
                fs.ReadExactly(dll);
            }
            catch (IOException)
            {
                Log($"Warning: could not read {HijackDll} (file may be in use by Steam)");
                return PatchState.Unpatched;
            }

            var resolvedCore = ResolveCorePatchOffsets(dll);
            if (resolvedCore == null)
                return PatchState.UnknownVersion;

            var (_, applied, skipped, errors) = CheckPatches(dll, resolvedCore);

            if (errors.Count > 0)
                return PatchState.UnknownVersion;
            if (applied == 0 && skipped == resolvedCore.Length)
                return PatchState.Patched;
            if (skipped == 0 && applied == resolvedCore.Length)
                return PatchState.Unpatched;

            return PatchState.PartiallyPatched;
        }

        public PatchResult Apply(string steamPath)
        {
            var result = new PatchResult();

            try
            {
                // --- Patch dwmapi.dll ---
                var dllPath = Path.Combine(steamPath, HijackDll);
                if (!File.Exists(dllPath))
                    return result.Fail($"{HijackDll} not found at {dllPath}. Is SteamTools installed?");

                var dllData = File.ReadAllBytes(dllPath);

                Log($"Patching {HijackDll}...");
                var resolvedCore = ResolveCorePatchOffsets(dllData);
                if (resolvedCore == null)
                    return result.Fail($"Could not identify patch locations in {HijackDll} - unsupported version?");

                Backup(dllPath);

                var (patchedDll, dllApplied, dllSkipped, dllErrors) = ApplyPatches(dllData, resolvedCore);
                if (dllErrors.Count > 0)
                {
                    foreach (var err in dllErrors) Log(err);
                    return result.Fail("Byte mismatch in " + HijackDll + " - wrong version?");
                }

                if (dllApplied > 0)
                {
                    File.WriteAllBytes(dllPath, patchedDll);
                    Log($"  {dllApplied} patch(es) applied" + (dllSkipped > 0 ? $", {dllSkipped} already done" : ""));
                }
                else
                {
                    Log("  Already patched");
                }
                result.DllPatched = true;

                // --- Patch payload in cache ---
                var cachePath = FindCachePath(steamPath);
                if (cachePath == null)
                {
                    Log("Payload cache not found. Will only patch DLL.");
                    Log("Run again after SteamTools downloads it.");
                    result.Succeeded = true;
                    return result;
                }

                Log($"Patching payload in cache...");
                Backup(cachePath);

                var raw = File.ReadAllBytes(cachePath);
                if (raw.Length < 32)
                    return result.Fail("Cache file too small");

                var iv = raw.AsSpan(0, 16).ToArray();
                var ct = raw.AsSpan(16).ToArray();

                Log("  Decrypting...");
                byte[] dec;
                try { dec = AesCbcDecrypt(ct, AesKey, iv); }
                catch (Exception ex) { return result.Fail($"Decryption failed: {ex.Message}"); }

                int expectedSize = BitConverter.ToInt32(dec, 0);
                byte[] payload;
                try
                {
                    using var zIn = new ZLibStream(
                        new MemoryStream(dec, 4, dec.Length - 4),
                        CompressionMode.Decompress);
                    using var ms = new MemoryStream();
                    zIn.CopyTo(ms);
                    payload = ms.ToArray();
                }
                catch (Exception ex) { return result.Fail($"Decompression failed: {ex.Message}"); }

                Log($"  Payload: {payload.Length} bytes");
                if (payload.Length != expectedSize)
                    Log($"  Warning: size mismatch ({payload.Length} vs header {expectedSize})");

                var resolvedPayload = ResolvePayloadPatchOffsets(payload);
                if (resolvedPayload == null)
                    return result.Fail("Could not identify patch locations in payload - unsupported version?");

                var (patchedPayload, plApplied, plSkipped, plErrors) = ApplyPatches(payload, resolvedPayload);
                if (plErrors.Count > 0)
                {
                    foreach (var err in plErrors) Log(err);
                    return result.Fail("Byte mismatch in payload - wrong version?");
                }

                if (plApplied > 0)
                {
                    Log("  Re-encrypting...");
                    using var compMs = new MemoryStream();
                    using (var zOut = new ZLibStream(compMs, CompressionLevel.Optimal, leaveOpen: true))
                        zOut.Write(patchedPayload, 0, patchedPayload.Length);

                    var blob = new byte[4 + compMs.Length];
                    BitConverter.TryWriteBytes(blob.AsSpan(0, 4), patchedPayload.Length);
                    compMs.ToArray().CopyTo(blob, 4);

                    var newCt = AesCbcEncrypt(blob, AesKey, iv);
                    var output = new byte[16 + newCt.Length];
                    iv.CopyTo(output, 0);
                    newCt.CopyTo(output, 16);
                    File.WriteAllBytes(cachePath, output);

                    Log($"  {plApplied} patch(es) applied" + (plSkipped > 0 ? $", {plSkipped} already done" : ""));
                }
                else
                {
                    Log("  Already patched");
                }
                result.CachePatched = true;

                result.Succeeded = true;
                Log("Done. Restart Steam if it's running.");
            }
            catch (Exception ex)
            {
                result.Fail($"Unexpected error: {ex.Message}");
                Log($"Error: {ex.Message}");
            }

            return result;
        }

        public PatchResult Restore(string steamPath)
        {
            var result = new PatchResult();
            int restored = 0;

            try
            {
                var dllPath = Path.Combine(steamPath, HijackDll);
                if (RestoreBackup(dllPath, HijackDll))
                    restored++;

                var cachePath = FindCachePath(steamPath);
                if (cachePath != null && RestoreBackup(cachePath, "payload cache"))
                    restored++;
                else
                {
                    // Try to find backup by scanning
                    var cacheDir = Path.Combine(steamPath, "appcache", "httpcache", "3b");
                    if (Directory.Exists(cacheDir))
                    {
                        foreach (var f in Directory.GetFiles(cacheDir, "*.bak"))
                        {
                            var orig = f[..^4]; // strip .bak
                            var name = Path.GetFileName(orig);
                            if (name.Length == 16)
                            {
                                RestoreBackup(orig, "payload cache");
                                restored++;
                                break;
                            }
                        }
                    }
                }

                if (restored > 0)
                {
                    Log($"Restored {restored} file(s). Restart Steam if it's running.");
                    result.Succeeded = true;
                }
                else
                {
                    Log("Nothing to restore (no backups found).");
                    result.Succeeded = true;
                }
            }
            catch (Exception ex)
            {
                result.Fail($"Restore failed: {ex.Message}");
                Log($"Error: {ex.Message}");
            }

            return result;
        }

        #region Cache Path / Fingerprint

        private string FindCachePath(string steamPath)
        {
            var cacheDir = Path.Combine(steamPath, "appcache", "httpcache", "3b");
            if (!Directory.Exists(cacheDir))
                return null;

            // Try computed fingerprint first
            try
            {
                var fp = ComputeFingerprint();
                var path = Path.Combine(cacheDir, fp);
                if (File.Exists(path))
                {
                    Log($"  Cache: {path}");
                    return path;
                }
                Log($"  Fingerprint {fp} computed but no cache file there");
            }
            catch (Exception ex)
            {
                Log($"  Fingerprint computation failed ({ex.Message}), scanning...");
            }

            // Fallback: scan for plausible cache files
            foreach (var f in Directory.GetFiles(cacheDir))
            {
                var name = Path.GetFileName(f);
                var info = new FileInfo(f);
                if (name.Length == 16 && info.Length > 500000 && info.Length < 5000000)
                {
                    Log($"  Cache (found by scan): {f}");
                    return f;
                }
            }

            return null;
        }

        // Replicates Core.dll's CPUID-based fingerprint derivation
        private unsafe string ComputeFingerprint()
        {
            // CPUID leaf 0 -> vendor string
            CpuId(0, out uint _, out uint ebx0, out uint ecx0, out uint edx0);
            var vendorBytes = new byte[12];
            BitConverter.TryWriteBytes(vendorBytes.AsSpan(0, 4), ebx0);
            BitConverter.TryWriteBytes(vendorBytes.AsSpan(4, 4), edx0);
            BitConverter.TryWriteBytes(vendorBytes.AsSpan(8, 4), ecx0);
            var vendor = System.Text.Encoding.ASCII.GetString(vendorBytes);

            // CPUID leaf 1 -> family/model
            CpuId(1, out uint eax1, out _, out _, out _);
            int family = ((int)eax1 >> 8) & 0xF;
            int model = ((int)eax1 >> 4) & 0xF;
            int nproc = Environment.ProcessorCount & 0xFF;

            var tag = System.Text.Encoding.ASCII.GetBytes(
                $"V{vendor}_F{family:X}_M{model:X}_C{nproc:X}");

            // XOR with "version"
            var xorKey = System.Text.Encoding.ASCII.GetBytes("version");
            var xored = new byte[tag.Length];
            for (int i = 0; i < tag.Length; i++)
                xored[i] = (byte)(tag[i] ^ xorKey[i % 7]);

            // MD5
            var md5Hex = System.Text.Encoding.ASCII.GetBytes(
                Convert.ToHexString(MD5.HashData(xored)).ToLowerInvariant());

            // CRC-64
            ulong crc = 0xFFFFFFFFFFFFFFFF;
            foreach (byte b in md5Hex)
            {
                crc ^= b;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0) crc ^= 0x85E1C3D753D46D27;
                    crc >>= 1;
                }
            }
            return (crc ^ 0xFFFFFFFFFFFFFFFF).ToString("X16");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);

        private unsafe void CpuId(uint leaf, out uint eax, out uint ebx, out uint ecx, out uint edx)
        {
            // x64 shellcode: push rbx; mov r8,rdx; mov eax,ecx; xor ecx,ecx; cpuid;
            //                mov [r8],eax; mov [r8+4],ebx; mov [r8+8],ecx; mov [r8+12],edx; pop rbx; ret
            ReadOnlySpan<byte> code = new byte[]
            {
                0x53, 0x49, 0x89, 0xD0, 0x89, 0xC8, 0x31, 0xC9, 0x0F, 0xA2,
                0x41, 0x89, 0x00, 0x41, 0x89, 0x58, 0x04, 0x41, 0x89, 0x48,
                0x08, 0x41, 0x89, 0x50, 0x0C, 0x5B, 0xC3
            };

            var mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)code.Length, 0x3000, 0x40);
            if (mem == IntPtr.Zero) throw new InvalidOperationException("VirtualAlloc failed");

            try
            {
                Marshal.Copy(code.ToArray(), 0, mem, code.Length);
                var regs = stackalloc uint[4];

                var fn = (delegate* unmanaged[Cdecl]<uint, uint*, void>)mem;
                fn(leaf, regs);

                eax = regs[0]; ebx = regs[1]; ecx = regs[2]; edx = regs[3];
            }
            finally
            {
                VirtualFree(mem, UIntPtr.Zero, 0x8000);
            }
        }

        #endregion

        #region Patch Helpers

        private static (byte[] data, int applied, int skipped, List<string> errors) CheckPatches(byte[] data, PatchEntry[] patches)
        {
            int applied = 0, skipped = 0;
            var errors = new List<string>();

            foreach (var p in patches)
            {
                if (BytesMatch(data, p.Offset, p.Replacement, 0, p.Replacement.Length))
                    skipped++;
                else if (BytesMatch(data, p.Offset, p.Original, 0, p.Original.Length))
                    applied++;
                else
                    errors.Add($"  Mismatch at 0x{p.Offset:X}: expected {BitConverter.ToString(p.Original)}, got {BitConverter.ToString(data, p.Offset, p.Original.Length)}");
            }

            return (data, applied, skipped, errors);
        }

        private static (byte[] data, int applied, int skipped, List<string> errors) ApplyPatches(byte[] data, PatchEntry[] patches)
        {
            var buf = (byte[])data.Clone();
            int applied = 0, skipped = 0;
            var errors = new List<string>();

            foreach (var p in patches)
            {
                if (BytesMatch(buf, p.Offset, p.Replacement, 0, p.Replacement.Length))
                {
                    skipped++;
                }
                else if (BytesMatch(buf, p.Offset, p.Original, 0, p.Original.Length))
                {
                    Buffer.BlockCopy(p.Replacement, 0, buf, p.Offset, p.Replacement.Length);
                    applied++;
                }
                else
                {
                    errors.Add($"  Mismatch at 0x{p.Offset:X}: expected {BitConverter.ToString(p.Original)}, got {BitConverter.ToString(buf, p.Offset, p.Original.Length)}");
                }
            }

            return (buf, applied, skipped, errors);
        }

        private static bool BytesMatch(byte[] data, int dataOffset, byte[] pattern, int patOffset, int length)
        {
            if (dataOffset + length > data.Length) return false;
            for (int i = 0; i < length; i++)
                if (data[dataOffset + i] != pattern[patOffset + i]) return false;
            return true;
        }

        private static int ScanForPattern(byte[] data, int start, int end, byte[] pattern, byte[] mask)
        {
            int limit = Math.Min(end, data.Length) - pattern.Length;
            for (int i = start; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (mask[j] != 0 && data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static int ScanForBytes(byte[] data, int start, int end, byte[] needle)
        {
            int limit = Math.Min(end, data.Length) - needle.Length;
            for (int i = start; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        #endregion

        #region Signature Scanning

        // Try hardcoded offsets first, fall back to signature scan
        private PatchEntry[] ResolveCorePatchOffsets(byte[] dll)
        {
            // AES key must be in .rdata to confirm it's the right DLL
            var sections = PeSection.Parse(dll);
            var rdataSec = PeSection.Find(sections, ".rdata");
            if (rdataSec == null)
            {
                Log("  Core.dll: no .rdata section found");
                return null;
            }

            int keyOffset = ScanForBytes(dll, rdataSec.Value.RawOffset,
                rdataSec.Value.RawOffset + rdataSec.Value.RawSize, AesKey);
            if (keyOffset < 0)
            {
                // Fallback: scan entire file in case it moved sections
                keyOffset = ScanForBytes(dll, 0, dll.Length, AesKey);
            }
            if (keyOffset < 0)
            {
                Log("  Core.dll: AES key not found - not a recognized SteamTools version");
                return null;
            }
            Log($"  AES key found at 0x{keyOffset:X}");

            var textSec = PeSection.Find(sections, ".text");
            if (textSec == null)
            {
                Log("  Core.dll: no .text section found");
                return null;
            }
            int tStart = textSec.Value.RawOffset;
            int tEnd = tStart + textSec.Value.RawSize;

            // --- Patch 1: call download -> mov eax, 1 ---
            int p1 = TryHardcodedOrScan(dll, 0x272F,
                CorePatches[0].Original, CorePatches[0].Replacement,
                () => FindCorePatch1(dll, tStart, tEnd));

            if (p1 < 0)
            {
                Log("  Core.dll: could not locate patch 1 (download call)");
                return null;
            }

            // --- Patch 2: jz -> jmp (skip hash check) ---
            int p2 = TryHardcodedOrScan(dll, 0x28B5,
                CorePatches[1].Original, CorePatches[1].Replacement,
                () => FindCorePatch2(dll, p1, Math.Min(p1 + 0x300, tEnd)));

            if (p2 < 0)
            {
                Log("  Core.dll: could not locate patch 2 (hash check jump)");
                return null;
            }

            Log($"  Core patches at 0x{p1:X}, 0x{p2:X}");
            return new PatchEntry[]
            {
                new(p1, CorePatches[0].Original, CorePatches[0].Replacement),
                new(p2, CorePatches[1].Original, CorePatches[1].Replacement),
            };
        }

        // Pattern: E8 ?? ?? ?? ?? 85 C0 0F 84 with negative call target
        private int FindCorePatch1(byte[] data, int start, int end)
        {
            byte[] pattern = { 0xE8, 0x00, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x0F, 0x84 };
            byte[] mask =    { 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };

            int pos = start;
            while (pos < end)
            {
                int hit = ScanForPattern(data, pos, end, pattern, mask);
                if (hit < 0) break;

                // Call target should be negative (download func is earlier)
                int rel = BitConverter.ToInt32(data, hit + 1);
                if (rel < 0)
                    return hit;

                pos = hit + 1;
            }
            return -1;
        }

        // Pattern: 85 C0 74 xx 33 FF E9 (hash compare fall-through)
        private int FindCorePatch2(byte[] data, int start, int end)
        {
            for (int i = start; i < end - 6; i++)
            {
                if (data[i] == 0x85 && data[i + 1] == 0xC0 &&
                    (data[i + 2] == 0x74 || data[i + 2] == 0xEB) &&
                    data[i + 4] == 0x33 && data[i + 5] == 0xFF)
                {
                    return i + 2; // The jz/jmp instruction
                }
            }

            // Fallback: looser match without leading test eax,eax
            for (int i = start; i < end - 5; i++)
            {
                if ((data[i] == 0x74 || data[i] == 0xEB) &&
                    data[i + 2] == 0x33 && data[i + 3] == 0xFF &&
                    data[i + 4] == 0xE9)
                {
                    return i;
                }
            }

            return -1;
        }

        private int TryHardcodedOrScan(byte[] data, int hardcoded,
            byte[] original, byte[] replacement, Func<int> scanFunc)
        {
            if (hardcoded >= 0 && hardcoded + original.Length <= data.Length)
            {
                if (BytesMatch(data, hardcoded, original, 0, original.Length) ||
                    BytesMatch(data, hardcoded, replacement, 0, replacement.Length))
                    return hardcoded;
            }

            Log("    Hardcoded offset miss, scanning...");
            return scanFunc();
        }

        private PatchEntry[] ResolvePayloadPatchOffsets(byte[] payload)
        {
            var sections = PeSection.Parse(payload);
            var textSec = PeSection.Find(sections, ".text");
            var gcjSec = PeSection.Find(sections, ".gCJ");

            if (textSec == null || gcjSec == null)
            {
                Log("  Payload: missing expected sections (.text, .gCJ)");
                return null;
            }

            int tStart = textSec.Value.RawOffset;
            int tEnd = tStart + textSec.Value.RawSize;
            int gStart = gcjSec.Value.RawOffset;
            int gEnd = gStart + gcjSec.Value.RawSize;

            // --- Patches 1 & 2: cloud API rewrite ---
            int p1 = TryHardcodedOrScan(payload, 0x0D33F,
                PayloadPatches[0].Original, PayloadPatches[0].Replacement,
                () => FindPayloadPatch1(payload, tStart, tEnd));

            if (p1 < 0)
            {
                Log("  Payload: could not locate patch 1 (cloud rewrite skip)");
                return null;
            }

            int p2 = TryHardcodedOrScan(payload, 0x0D649,
                PayloadPatches[1].Original, PayloadPatches[1].Replacement,
                () => FindPayloadPatch2(payload, p1, Math.Min(p1 + 0x500, tEnd)));

            if (p2 < 0)
            {
                Log("  Payload: could not locate patch 2 (proxy appid zero)");
                return null;
            }

            // --- Patch 3: anchored via Spacewar 480 constant in .gCJ ---
            int p3 = TryHardcodedOrScan(payload, 0x1D4F53,
                PayloadPatches[2].Original, PayloadPatches[2].Replacement,
                () => FindPayloadPatch3(payload, gStart, gEnd));

            if (p3 < 0)
            {
                Log("  Payload: could not locate patch 3 (IPC appid preserve)");
                return null;
            }

            Log($"  Payload patches at 0x{p1:X}, 0x{p2:X}, 0x{p3:X}");
            return new PatchEntry[]
            {
                new(p1, PayloadPatches[0].Original, PayloadPatches[0].Replacement),
                new(p2, PayloadPatches[1].Original, PayloadPatches[1].Replacement),
                new(p3, PayloadPatches[2].Original, PayloadPatches[2].Replacement),
            };
        }

        // Cloud rewrite jz: 85 C0 0F 85 ?? ?? 00 00 45 85 FF [0F 84 | 90 E9]
        private int FindPayloadPatch1(byte[] data, int tStart, int tEnd)
        {
            for (int i = tStart; i < tEnd - 17; i++)
            {
                if (data[i] == 0x85 && data[i + 1] == 0xC0 &&
                    data[i + 2] == 0x0F && data[i + 3] == 0x85 &&
                    data[i + 6] == 0x00 && data[i + 7] == 0x00 &&
                    data[i + 8] == 0x45 && data[i + 9] == 0x85 && data[i + 10] == 0xFF &&
                    data[i + 15] == 0x00 && data[i + 16] == 0x00)
                {
                    if ((data[i + 11] == 0x0F && data[i + 12] == 0x84) ||
                        (data[i + 11] == 0x90 && data[i + 12] == 0xE9))
                    {
                        return i + 11;
                    }
                }
            }

            Log("    Could not find cloud rewrite jz pattern in .text");
            return -1;
        }

        // Proxy appid load: [8B 0D | 31 C9] ?? ?? ?? ?? 48 8D 14 3E
        private int FindPayloadPatch2(byte[] data, int start, int end)
        {
            byte[] tail = { 0x48, 0x8D, 0x14, 0x3E };

            for (int i = start; i < end - 10; i++)
            {
                if (data[i + 6] == tail[0] && data[i + 7] == tail[1] &&
                    data[i + 8] == tail[2] && data[i + 9] == tail[3])
                {
                    if ((data[i] == 0x8B && data[i + 1] == 0x0D) ||
                        (data[i] == 0x31 && data[i + 1] == 0xC9))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        // Anchor: Spacewar 480 constant (C7 40 09 E0 01 00 00) in .gCJ, then next 89 3D
        private int FindPayloadPatch3(byte[] data, int gStart, int gEnd)
        {
            byte[] spacewar = { 0xC7, 0x40, 0x09, 0xE0, 0x01, 0x00, 0x00 };
            int anchor = ScanForBytes(data, gStart, gEnd, spacewar);
            if (anchor < 0)
            {
                Log("    Could not find Spacewar anchor in .gCJ");
                return -1;
            }

            int searchStart = anchor + spacewar.Length;
            int searchEnd = Math.Min(searchStart + 30, gEnd);
            for (int i = searchStart; i < searchEnd - 5; i++)
            {
                if (data[i] == 0x89 && data[i + 1] == 0x3D)
                    return i;
                if (data[i] == 0x90 && data[i + 1] == 0x90 &&
                    data[i + 2] == 0x90 && data[i + 3] == 0x90 &&
                    data[i + 4] == 0x90 && data[i + 5] == 0x90)
                    return i;
            }

            Log("    Could not find mov [rip+xx],edi after Spacewar anchor");
            return -1;
        }

        #endregion

        #region Backup / Restore

        private void Backup(string path)
        {
            var bak = path + ".bak";
            if (File.Exists(bak)) return;
            File.Copy(path, bak);
            Log($"  Backed up to {bak}");
        }

        private bool RestoreBackup(string path, string label)
        {
            var bak = path + ".bak";
            if (!File.Exists(bak))
            {
                Log($"  {label}: no backup found");
                return false;
            }
            File.Copy(bak, path, overwrite: true);
            File.Delete(bak);
            Log($"  {label}: restored from backup");
            return true;
        }

        #endregion

        #region Encryption

        private static byte[] AesCbcDecrypt(byte[] ct, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(ct, 0, ct.Length);
        }

        private static byte[] AesCbcEncrypt(byte[] pt, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var enc = aes.CreateEncryptor();
            return enc.TransformFinalBlock(pt, 0, pt.Length);
        }

        #endregion
    }

    public record PatchEntry(int Offset, byte[] Original, byte[] Replacement);

    internal readonly struct PeSection
    {
        public readonly string Name;
        public readonly int VirtualAddress;
        public readonly int VirtualSize;
        public readonly int RawOffset;
        public readonly int RawSize;

        public PeSection(string name, int va, int vsize, int raw, int rawSize)
        {
            Name = name;
            VirtualAddress = va;
            VirtualSize = vsize;
            RawOffset = raw;
            RawSize = rawSize;
        }

        public static PeSection[] Parse(byte[] pe)
        {
            if (pe.Length < 64) return Array.Empty<PeSection>();

            int peOff = BitConverter.ToInt32(pe, 0x3C);
            if (peOff < 0 || peOff + 24 > pe.Length) return Array.Empty<PeSection>();
            if (pe[peOff] != 'P' || pe[peOff + 1] != 'E') return Array.Empty<PeSection>();

            int numSections = BitConverter.ToUInt16(pe, peOff + 6);
            int optSize = BitConverter.ToUInt16(pe, peOff + 20);
            int firstSection = peOff + 24 + optSize;

            var result = new PeSection[numSections];
            for (int i = 0; i < numSections; i++)
            {
                int off = firstSection + i * 40;
                if (off + 40 > pe.Length) break;

                int nameEnd = 0;
                for (int j = 0; j < 8 && pe[off + j] != 0; j++) nameEnd = j + 1;
                string name = System.Text.Encoding.ASCII.GetString(pe, off, nameEnd);

                int vsize = BitConverter.ToInt32(pe, off + 8);
                int va = BitConverter.ToInt32(pe, off + 12);
                int rawSize = BitConverter.ToInt32(pe, off + 16);
                int rawPtr = BitConverter.ToInt32(pe, off + 20);

                result[i] = new PeSection(name, va, vsize, rawPtr, rawSize);
            }
            return result;
        }

        public static PeSection? Find(PeSection[] sections, string name)
        {
            for (int i = 0; i < sections.Length; i++)
                if (sections[i].Name == name) return sections[i];
            return null;
        }

        public static int RvaToRaw(PeSection[] sections, int rva)
        {
            for (int i = 0; i < sections.Length; i++)
            {
                var s = sections[i];
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.RawSize)
                    return s.RawOffset + (rva - s.VirtualAddress);
            }
            return -1;
        }
    }

    public enum PatchState
    {
        NotInstalled,
        Unpatched,
        Patched,
        PartiallyPatched,
        UnknownVersion,
    }

    public class PatchResult
    {
        public bool Succeeded { get; set; }
        public bool DllPatched { get; set; }
        public bool CachePatched { get; set; }
        public string Error { get; set; }

        public PatchResult Fail(string error)
        {
            Succeeded = false;
            Error = error;
            return this;
        }
    }
}
