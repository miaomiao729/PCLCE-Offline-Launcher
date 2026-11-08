// PCLCE-Patch —— PCL2 CE 正版验证 + 区域限制 双解锁（原地等长 IL 补丁，不重打包）
//
// 目标（PCL2 CE 2.15.x，单文件 bundle，未压缩内嵌程序集）：
//   正版验证：
//     PCL.Core.Minecraft.Profile.ProfileService::get_HasMicrosoftProfile  -> true
//         · McLaunchPrecheck 的 brtrue 分支直接放行，不再弹“必须登录正版账号”
//         · ProfileUi.CanCreateOtherProfile 首个分支直接 true，允许新建离线档案
//     PCL.ProfileUi::CanCreateOtherProfile                                -> true
//   区域限制（StillMi/PCLCE-Unlock 那套闸门）：
//     PCL.RestrictedFeaturePolicy::Check                                  -> true
//     PCL.RestrictedFeaturePolicy::get_IsRestricted                       -> false
//     PCL.RestrictedFeaturePolicy::ThrowIfRestricted                      -> return
//     PCL.Core.App.Localization.Lang::get_IsFeaturesUnrestricted          -> true
//
// 因为 bundle 未压缩、且补丁不改变任何长度，可直接改写内嵌程序集字节，
// 输出文件大小与官方完全一致，bundle 布局与全部偏移保持有效。
//
// IL 末尾必须是终止指令：常量桩写成 prologue + nop… + ret（nop 在前，ret 在后）。
//
// 用法：
//   PCLCE-Patch.exe                 自动在当前目录找 PCL2_CE_*.exe 并打补丁
//   PCLCE-Patch.exe <exe>           指定目标
//   PCLCE-Patch.exe <exe> --check   只检测是否已解锁
//   PCLCE-Patch.exe <exe> --restore 从备份还原
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCLCEPatch
{
    internal sealed class Pe
    {
        public byte[] D;
        public int PeOff, NSec, OptSize, OptOff, SecOff;

        public uint U32(int o) { return (uint)(D[o] | D[o + 1] << 8 | D[o + 2] << 16 | D[o + 3] << 24); }
        public int I32(int o) { return (int)U32(o); }
        public ushort U16(int o) { return (ushort)(D[o] | D[o + 1] << 8); }

        public Pe(byte[] d)
        {
            D = d;
            if (D.Length < 0x100 || D[0] != 'M' || D[1] != 'Z') throw new Exception("不是 PE 文件");
            PeOff = I32(0x3c);
            if (PeOff <= 0 || PeOff + 24 > D.Length || D[PeOff] != 'P' || D[PeOff + 1] != 'E')
                throw new Exception("PE 头无效");
            NSec = U16(PeOff + 6);
            OptSize = U16(PeOff + 20);
            OptOff = PeOff + 24;
            SecOff = OptOff + OptSize;
        }

        public int Opt(int rel) { return OptOff + rel; }
        public int SecHdr(int i) { return SecOff + i * 40; }

        public int RvaToOff(int rva)
        {
            for (int i = 0; i < NSec; i++)
            {
                int h = SecHdr(i);
                uint vs = U32(h + 8), va = U32(h + 12), rs = U32(h + 16), rp = U32(h + 20);
                uint span = vs > rs ? vs : rs;
                if (rva >= va && rva < va + span) return (int)(rp + (rva - va));
            }
            throw new Exception("RVA 0x" + rva.ToString("x") + " 不在任何节内");
        }

        public int Extent()
        {
            int end = 0;
            for (int i = 0; i < NSec; i++)
            {
                int h = SecHdr(i);
                end = Math.Max(end, (int)(U32(h + 20) + U32(h + 16)));
            }
            return end;
        }
    }

    /// <summary>最小化 CLI 元数据解析：只需要 #Strings / #Blob / #~ 与 TypeDef(0x02)、MethodDef(0x06)。</summary>
    internal sealed class Meta
    {
        public byte[] D;
        public Pe Pe;
        public int[] Rows = new int[64];
        public int StrSize, GuidSize, BlobSize;
        public int StrOff, StrLen, BlobOff, BlobLen;
        public int TableOff;
        public int[] TableStart = new int[64];

        public Meta(byte[] d)
        {
            D = d; Pe = new Pe(d);
            int ddBase = Pe.U16(Pe.Opt(0)) == 0x20b ? 112 : 96;
            int clr = Pe.RvaToOff((int)Pe.U32(Pe.Opt(ddBase + 14 * 8)));
            int md = Pe.RvaToOff((int)Pe.U32(clr + 8));
            if (D[md] != 'B' || D[md + 1] != 'S' || D[md + 2] != 'J' || D[md + 3] != 'B')
                throw new Exception("不是 .NET 程序集");

            int verLen = (int)Pe.U32(md + 12);
            int p = (md + 16 + verLen + 3) & ~3;
            int nStream = Pe.U16(p + 2);
            p += 4;
            for (int i = 0; i < nStream; i++)
            {
                int so = (int)Pe.U32(p), ss = (int)Pe.U32(p + 4);
                int j = p + 8;
                while (D[j] != 0) j++;
                string name = Encoding.ASCII.GetString(D, p + 8, j - (p + 8));
                int at = md + so;
                if (name == "#Strings") { StrOff = at; StrLen = ss; }
                else if (name == "#Blob") { BlobOff = at; BlobLen = ss; }
                else if (name == "#~") { TableOff = at; }
                p = (j + 1 + 3) & ~3;
            }
            if (TableOff == 0) throw new Exception("未找到 #~ 表流");

            byte heap = D[TableOff + 6];
            StrSize = (heap & 1) != 0 ? 4 : 2;
            GuidSize = (heap & 2) != 0 ? 4 : 2;
            BlobSize = (heap & 4) != 0 ? 4 : 2;
            ulong valid = (ulong)Pe.U32(TableOff + 8) | ((ulong)Pe.U32(TableOff + 12) << 32);
            int q = TableOff + 24;
            for (int i = 0; i < 64; i++)
                if (((valid >> i) & 1) != 0) { Rows[i] = (int)Pe.U32(q); q += 4; }

            int off = q;
            for (int t = 0; t < 64; t++)
            {
                if (Rows[t] == 0) continue;
                TableStart[t] = off;
                if (t > 0x06) break;
                off += Rows[t] * RowSize(t);
            }
        }

        private int IdxSize(int t) { return Rows[t] >= 0x10000 ? 4 : 2; }
        private int CodedSize(int[] tab, int bits)
        {
            int mx = 0;
            foreach (int t in tab) if (Rows[t] > mx) mx = Rows[t];
            return mx >= (1 << (16 - bits)) ? 4 : 2;
        }

        private static readonly int[] ResolutionScope = { 0x00, 0x1A, 0x23, 0x01 };
        private static readonly int[] TypeDefOrRef = { 0x02, 0x01, 0x1B };

        public int RowSize(int t)
        {
            switch (t)
            {
                case 0x00: return 2 + StrSize + 3 * GuidSize;
                case 0x01: return CodedSize(ResolutionScope, 2) + 2 * StrSize;
                case 0x02: return 4 + 2 * StrSize + CodedSize(TypeDefOrRef, 2) + IdxSize(0x04) + IdxSize(0x06);
                case 0x03: return IdxSize(0x04);
                case 0x04: return 2 + StrSize + BlobSize;
                case 0x05: return IdxSize(0x06);
                case 0x06: return 4 + 2 + 2 + StrSize + BlobSize + IdxSize(0x08);
                case 0x08: return 2 + 2 + StrSize;
                default: throw new Exception("表 0x" + t.ToString("x") + " 未实现");
            }
        }

        public int RowOff(int t, int row1) { return TableStart[t] + (row1 - 1) * RowSize(t); }
        public int ReadIdx(int o, int size) { return size == 4 ? (int)Pe.U32(o) : Pe.U16(o); }

        public string Str(int idx)
        {
            if (idx == 0) return "";
            int e = StrOff + idx;
            while (D[e] != 0) e++;
            return Encoding.UTF8.GetString(D, StrOff + idx, e - (StrOff + idx));
        }

        public string TypeName(int row1)
        {
            int o = RowOff(0x02, row1);
            string n = Str(ReadIdx(o + 4, StrSize));
            string ns = Str(ReadIdx(o + 4 + StrSize, StrSize));
            return ns.Length == 0 ? n : ns + "." + n;
        }

        public int TypeMethodStart(int row1)
        {
            return ReadIdx(RowOff(0x02, row1) + 4 + 2 * StrSize + CodedSize(TypeDefOrRef, 2) + IdxSize(0x04), IdxSize(0x06));
        }

        public string MethodName(int row1) { return Str(ReadIdx(RowOff(0x06, row1) + 8, StrSize)); }
        public int MethodRva(int row1) { return (int)Pe.U32(RowOff(0x06, row1)); }

        public int BodyOff(int row1, out int codeSize, out bool hasEh)
        {
            int ho = Pe.RvaToOff(MethodRva(row1));
            byte b0 = D[ho];                      // tiny 头只有 1 字节，必须按字节判断
            hasEh = false;
            if ((b0 & 3) == 2) { codeSize = b0 >> 2; return ho + 1; }
            ushort h = Pe.U16(ho);
            codeSize = (int)Pe.U32(ho + 4);
            hasEh = (h & 0x8) != 0;
            int sizeInDwords = (h >> 12) & 0xF;   // Flags 高 4 位 = 头大小的双字数
            return ho + sizeInDwords * 4;
        }
    }

    /// <summary>一个待打的补丁点。</summary>
    internal sealed class Site
    {
        public int AsmOffset;   // 内嵌程序集在 exe 中的起始偏移
        public int BodyOffset;  // 方法体（IL 起点）相对程序集的偏移
        public int CodeSize;
        public bool HasEh;
        public bool Patched;
        public string TypeName, MethodName;
        public byte[] Prologue; // 常量桩前缀；null 表示纯 ret
    }

    internal static class Program
    {
        private const string BakSuffix = ".pclcebak";

        // 目标补丁表：类型全名 -> { 方法名 -> 桩 }
        // 桩：null=直接 return；0x16=ldc.i4.0;ret；0x17=ldc.i4.1;ret
        private static readonly string[][] Targets =
        {
            new[] { "PCL.RestrictedFeaturePolicy",  "Check",               "true"  },
            new[] { "PCL.RestrictedFeaturePolicy",  "get_IsRestricted",    "false" },
            new[] { "PCL.RestrictedFeaturePolicy",  "ThrowIfRestricted",   "void"  },
            new[] { "PCL.ProfileUi",                "CanCreateOtherProfile","true" },
            new[] { "PCL.Core.App.Localization.Lang", "get_IsFeaturesUnrestricted", "true" },
            new[] { "PCL.Core.Minecraft.Profile.ProfileService", "get_HasMicrosoftProfile", "true" },
        };

        private static bool _silent;

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            bool restore = false, checkOnly = false;
            string target = null;
            foreach (string a in args)
            {
                if (a == "--restore" || a == "-r") restore = true;
                else if (a == "--check" || a == "-c") checkOnly = true;
                else if (a == "--silent" || a == "-s") _silent = true;
                else if (!a.StartsWith("-")) target = a;
            }
            if (_silent) Console.SetOut(TextWriter.Null);

            if (target == null || Directory.Exists(target))
            {
                string dir = target ?? AppDomain.CurrentDomain.BaseDirectory;
                target = FindCe(dir);
                if (target == null) { Say("[×] 没找到 PCL2 CE 主程序（PCL2_CE_*.exe）。"); return Pause(2); }
            }
            if (!File.Exists(target)) { Say("[×] 文件不存在：" + target); return Pause(2); }
            target = Path.GetFullPath(target);
            Say("[i] 目标：" + Path.GetFileName(target));

            if (restore)
            {
                string bak = target + BakSuffix;
                if (!File.Exists(bak)) { Say("[×] 没有备份文件：" + Path.GetFileName(bak)); return Pause(2); }
                try
                {
                    File.Copy(bak, target, true);
                    Say("[√] 已还原为官方原版。");
                    return Pause(0);
                }
                catch (Exception ex) { Say("[×] 还原失败：" + ex.Message); return Pause(2); }
            }

            byte[] data;
            try { data = File.ReadAllBytes(target); }
            catch (Exception ex) { Say("[×] 读取失败：" + ex.Message + "（CE 可能还开着）"); return Pause(2); }

            List<Site> sites;
            try { sites = LocateAll(data); }
            catch (Exception ex) { Say("[×] 解析失败：" + ex.Message); return Pause(2); }

            if (sites.Count == 0) { Say("[×] 没有找到任何可修补的判定点，版本可能不受支持。"); return Pause(2); }

            if (Environment.GetEnvironmentVariable("PCLCE_DUMP") == "1")
            {
                foreach (Site s in sites)
                    Say(string.Format("  {0}::{1}  asm=0x{2:X} body=0x{3:X} size={4} eh={5} patched={6}",
                        s.TypeName, s.MethodName, s.AsmOffset, s.BodyOffset, s.CodeSize, s.HasEh, s.Patched));
            }

            int done = 0, already = 0;
            foreach (Site s in sites) { if (s.Patched) already++; else done++; }

            Say("[i] 找到 " + sites.Count + " 个判定点：已解锁 " + already + "，待解锁 " + done);
            if (done == 0)
            {
                Say("[√] 这份 CE 已经全部解锁，无需改动。");
                return Pause(0);
            }
            if (checkOnly) { Say("[√] 需要解锁（未改动文件）。"); return Pause(0); }

            try
            {
                if (!File.Exists(target + BakSuffix))
                {
                    File.Copy(target, target + BakSuffix, false);
                    Say("[√] 已备份原版 -> " + Path.GetFileName(target + BakSuffix));
                }

                byte[] work = (byte[])data.Clone();
                foreach (Site s in sites)
                {
                    if (s.Patched) continue;
                    int abs = s.AsmOffset + s.BodyOffset;
                    // 常量桩：prologue + nop 填充 + 结尾 ret（末尾必须是终止指令）
                    int p = 0;
                    if (s.Prologue != null) work[abs + p++] = s.Prologue[0];
                    for (; p < s.CodeSize - 1; p++) work[abs + p] = 0x00;
                    work[abs + s.CodeSize - 1] = 0x2A;
                }

                File.WriteAllBytes(target, work);

                // 回读校验
                List<Site> after = LocateAll(File.ReadAllBytes(target));
                int ok = 0;
                foreach (Site s in after) if (s.Patched) ok++;
                if (ok != after.Count)
                {
                    Say("[×] 写入后复查未通过（" + ok + "/" + after.Count + "），可用 --restore 还原。");
                    return Pause(2);
                }

                Say("");
                foreach (Site s in sites)
                    if (!s.Patched) Say("  [√] " + s.TypeName + "::" + s.MethodName);
                Say("");
                Say("[√] 解锁完成，复查通过（" + ok + "/" + after.Count + "）。");
                Say("    正版验证：已移除（可创建离线 / 第三方档案，启动不再强制登录）");
                Say("    区域限制：已移除（下载 / 补全 / 整合包安装全部放行）");
                return Pause(0);
            }
            catch (Exception ex)
            {
                Say("[×] 写入失败：" + ex.Message);
                Say("    如果 CE 正在运行，请先完全退出。");
                return Pause(2);
            }
        }

        // ------------------------------------------------------------------ 定位
        private static List<Site> LocateAll(byte[] data)
        {
            var result = new List<Site>();
            int i = 0;
            string lastErr = null;
            while (true)
            {
                i = IndexOf(data, new byte[] { (byte)'M', (byte)'Z' }, i);
                if (i < 0) break;
                try
                {
                    if (i + 0x40 < data.Length)
                    {
                        int pe = (int)BitConverter.ToUInt32(data, i + 0x3c);
                        if (pe > 0x40 && pe < 0x400 && i + pe + 4 <= data.Length
                            && data[i + pe] == 'P' && data[i + pe + 1] == 'E')
                        {
                            int extent = ComputeExtent(data, i);
                            if (extent > 0 && i + extent <= data.Length)
                            {
                                var asm = new Meta(Slice(data, i, extent));
                                Collect(asm, i, result);
                            }
                        }
                    }
                }
                catch (Exception ex) { lastErr = ex.Message; }
                i += 1;
            }
            if (result.Count == 0 && lastErr != null) throw new Exception(lastErr);
            return result;
        }

        private static void Collect(Meta asm, int asmOffset, List<Site> outList)
        {
            int typeCount = asm.Rows[0x02];
            for (int r = 1; r <= typeCount; r++)
            {
                string tn;
                try { tn = asm.TypeName(r); } catch { continue; }

                foreach (string[] t in Targets)
                {
                    if (t[0] != tn) continue;
                    int start = asm.TypeMethodStart(r);
                    int end = r < typeCount ? asm.TypeMethodStart(r + 1) : asm.Rows[0x06] + 1;
                    for (int m = start; m < end; m++)
                    {
                        string mn;
                        try { mn = asm.MethodName(m); } catch { continue; }
                        if (mn != t[1]) continue;

                        int code; bool eh;
                        int body;
                        try { body = asm.BodyOff(m, out code, out eh); } catch { continue; }
                        if (eh || code < 2) continue;

                        var s = new Site
                        {
                            AsmOffset = asmOffset,
                            BodyOffset = body,
                            CodeSize = code,
                            HasEh = eh,
                            TypeName = tn,
                            MethodName = mn,
                            Prologue = StubPrologue(t[2]),
                        };
                        s.Patched = IsPatched(asm.D, body, code, s.Prologue);
                        outList.Add(s);
                    }
                }
            }
        }

        private static byte[] StubPrologue(string kind)
        {
            if (kind == "true") return new byte[] { 0x17 };
            if (kind == "false") return new byte[] { 0x16 };
            return null; // void
        }

        private static bool IsPatched(byte[] d, int body, int codeSize, byte[] prologue)
        {
            if (codeSize < 2) return false;
            if (d[body + codeSize - 1] != 0x2A) return false;   // 末尾必须是 ret
            int start = 0;
            if (prologue != null)
            {
                if (d[body] != prologue[0]) return false;
                start = 1;
            }
            for (int i = start; i < codeSize - 1; i++) if (d[body + i] != 0x00) return false;
            return true;
        }

        private static int ComputeExtent(byte[] d, int mz)
        {
            int pe = (int)BitConverter.ToUInt32(d, mz + 0x3c);
            int nsec = BitConverter.ToUInt16(d, mz + pe + 6);
            int optsz = BitConverter.ToUInt16(d, mz + pe + 20);
            int s = mz + pe + 24 + optsz;
            int end = 0;
            for (int k = 0; k < nsec; k++)
            {
                uint rs = BitConverter.ToUInt32(d, s + k * 40 + 16);
                uint rp = BitConverter.ToUInt32(d, s + k * 40 + 20);
                end = Math.Max(end, (int)(rp + rs));
            }
            return end;
        }

        private static string FindCe(string dir)
        {
            string self = "";
            try { self = Path.GetFullPath(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
            var hits = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.exe"))
                {
                    string full = Path.GetFullPath(f);
                    if (string.Equals(full, self, StringComparison.OrdinalIgnoreCase)) continue;
                    long len;
                    try { len = new FileInfo(full).Length; } catch { continue; }
                    if (len < 5000000) continue;
                    try
                    {
                        if (LocateAll(File.ReadAllBytes(full)).Count > 0) hits.Add(full);
                    }
                    catch { }
                }
            }
            catch { return null; }
            if (hits.Count == 0) return null;
            hits.Sort(delegate(string a, string b) { return new FileInfo(b).Length.CompareTo(new FileInfo(a).Length); });
            return hits[0];
        }

        private static byte[] Slice(byte[] d, int off, int len)
        {
            byte[] r = new byte[len];
            Buffer.BlockCopy(d, off, r, 0, len);
            return r;
        }

        private static int IndexOf(byte[] hay, byte[] needle, int start)
        {
            for (int i = start; i <= hay.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++) if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private static void Say(string s) { if (!_silent) Console.WriteLine(s); }

        private static int Pause(int code)
        {
            if (_silent) return code;
            Console.WriteLine();
            Console.WriteLine("按任意键退出…");
            try { Console.ReadKey(true); } catch { }
            return code;
        }
    }
}
