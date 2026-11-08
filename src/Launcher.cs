// PCLCE-Launcher —— PCL2 CE 离线解锁启动器
//
// 做的事（按顺序）：
//   1. 找同目录的 PCL2_CE_*.exe；没有就提示用户从官方下载并放到本目录
//   2. 检查 .NET 10 桌面运行时；缺了自动下载安装（也可用同目录的离线安装包）
//   3. 检查 CE 是否已解锁；没解锁就调用 PCLCE-Patch.exe 打补丁（自动备份原版）
//   4. 启动 PCL2 CE
//
// 本程序不含 PCL2 CE 的任何代码或二进制，用户需自行从官方获取。
//
// 目标框架 .NET Framework 4.x（Windows 10/11 自带），本身零依赖。
// 编译：build.bat
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace PCLCEOfflineLauncher
{
    internal static class Program
    {
        private const string DesktopRuntimeName = "Microsoft.WindowsDesktop.App";
        private const int RequiredMajor = 10;

        // 官方 aka.ms 短链，自己会跳到当前最新的 10.0.x
        private const string RuntimeUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";
        private const string OfficialReleaseUrl = "https://github.com/PCL-Community/PCL-CE/releases";

        private static string _dir;

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            _dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            Console.Title = "PCL2 CE 离线解锁启动器";
            Banner();

            // ---------- 1. 找 CE ----------
            string ce = FindCe();
            if (ce == null)
            {
                Console.WriteLine("[!] 本目录没有找到 PCL2_CE_*.exe");
                Console.WriteLine();
                Console.WriteLine("    请从官方发布页下载 PCL2 CE，放到本目录后再运行：");
                Console.WriteLine("    " + OfficialReleaseUrl);
                Console.WriteLine();
                Console.WriteLine("    文件名形如 PCL2_CE_Release_x64.exe");
                Console.WriteLine();
                Console.WriteLine("    （本工具不含 PCL2 CE 本体，需你自行从官方获取）");
                Pause();
                return 2;
            }
            Info("启动器：" + Path.GetFileName(ce));

            // ---------- 2. 运行时 ----------
            if (!HasDesktopRuntime())
            {
                if (!EnsureRuntime())
                {
                    Fail("运行时未能就绪，无法启动。");
                    Pause();
                    return 2;
                }
            }
            else
            {
                Info(".NET " + RequiredMajor + " 桌面运行时已就绪。");
            }

            // ---------- 3. 补丁 ----------
            string patcher = Path.Combine(_dir, "PCLCE-Patch.exe");
            if (!File.Exists(patcher))
            {
                Warn("未找到 PCLCE-Patch.exe，跳过解锁检查，直接启动。");
            }
            else
            {
                int rc = RunPatcher(patcher, ce);
                if (rc == 0) Info("解锁状态：已确认。");
                else Warn("补丁步骤返回 " + rc + "，仍继续启动（若功能受限可手动运行 PCLCE-Patch.exe）。");
            }

            // ---------- 4. 启动 ----------
            try
            {
                var psi = new ProcessStartInfo(ce) { UseShellExecute = true, WorkingDirectory = _dir };
                foreach (string a in args) psi.Arguments = (psi.Arguments + " " + Quote(a)).Trim();
                Process.Start(psi);
                Info("已启动，本窗口 " + 4 + " 秒后自动关闭。");
                Thread.Sleep(4000);
                return 0;
            }
            catch (Exception ex)
            {
                Fail("启动失败：" + ex.Message);
                Pause();
                return 3;
            }
        }

        // ------------------------------------------------------------ 运行时
        private static bool EnsureRuntime()
        {
            // 优先用同目录的离线安装包（没有就下载）
            string local = FindLocalRuntimeInstaller();
            if (local != null)
            {
                Info("使用本目录安装包：" + Path.GetFileName(local));
                if (RunInstaller(local)) return true;
            }

            Info(".NET " + RequiredMajor + " 桌面运行时未安装，正在下载…");
            string downloaded = DownloadRuntime();
            if (downloaded == null)
            {
                Fail("下载失败。请手动安装 .NET " + RequiredMajor + " 桌面运行时后再试：");
                Console.WriteLine("    " + RuntimeUrl);
                return false;
            }
            if (RunInstaller(downloaded)) return true;

            Fail("安装未成功。可手动运行：" + downloaded);
            return false;
        }

        private static string FindLocalRuntimeInstaller()
        {
            try
            {
                return Directory.GetFiles(_dir, "windowsdesktop-runtime-*.exe")
                    .OrderByDescending(f => f)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static string DownloadRuntime()
        {
            string dest = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-win-x64.exe");
            try
            {
                // 老框架默认不启用 TLS 1.2，显式打开
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "PCLCE-Offline-Launcher");
                    wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        Console.Write("\r    下载中 " + e.ProgressPercentage + "%   ");
                    };
                    wc.DownloadFileTaskAsync(new Uri(RuntimeUrl), dest).Wait();
                    Console.WriteLine();
                }
                Info("下载完成：" + dest);
                return dest;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Warn("下载出错：" + ex.Message);
                return null;
            }
        }

        private static bool RunInstaller(string exe)
        {
            Info("正在安装（会弹一次权限确认）…");
            try
            {
                var psi = new ProcessStartInfo(exe, "/install /quiet /norestart")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Warn("静默安装失败（" + ex.Message + "），改用交互安装…");
                try
                {
                    var psi = new ProcessStartInfo(exe, "/install /passive /norestart")
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using (var p = Process.Start(psi)) p.WaitForExit();
                }
                catch { }
            }

            if (HasDesktopRuntime()) { Info("运行时安装完成。"); return true; }
            return false;
        }

        private static bool HasDesktopRuntime()
        {
            try
            {
                string dotnet = FindDotnet();
                if (dotnet != null)
                {
                    var psi = new ProcessStartInfo(dotnet, "--list-runtimes")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        string outp = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(15000);
                        foreach (string line in outp.Split('\n'))
                        {
                            if (!line.StartsWith(DesktopRuntimeName, StringComparison.OrdinalIgnoreCase)) continue;
                            if (ParseMajor(line) >= RequiredMajor) return true;
                        }
                    }
                }
            }
            catch { }

            foreach (string root in new[]
            {
                @"C:\Program Files\dotnet\shared\" + DesktopRuntimeName,
                @"C:\Program Files (x86)\dotnet\shared\" + DesktopRuntimeName
            })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string d in Directory.GetDirectories(root))
                    {
                        int v;
                        if (int.TryParse(Path.GetFileName(d).Split('.')[0], out v) && v >= RequiredMajor) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static int ParseMajor(string line)
        {
            int i = line.IndexOf(DesktopRuntimeName, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            string rest = line.Substring(i + DesktopRuntimeName.Length).Trim();
            int sp = rest.IndexOf(' ');
            if (sp > 0) rest = rest.Substring(0, sp);
            int major;
            return int.TryParse(rest.Split('.')[0], out major) ? major : 0;
        }

        private static string FindDotnet()
        {
            string cand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"dotnet\dotnet.exe");
            if (File.Exists(cand)) return cand;
            cand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"dotnet\dotnet.exe");
            if (File.Exists(cand)) return cand;
            return null;
        }

        // ------------------------------------------------------------ 补丁
        private static int RunPatcher(string patcher, string ce)
        {
            Info("正在检查解锁状态…");
            try
            {
                var psi = new ProcessStartInfo(patcher, Quote(ce))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = _dir
                };
                using (var p = Process.Start(psi))
                {
                    p.StandardInput.WriteLine();     // 吃掉"按任意键退出"
                    string outp = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    foreach (string line in outp.Split('\n'))
                    {
                        string t = line.TrimEnd('\r');
                        if (t.Length == 0) continue;
                        if (t.Contains("按任意键")) continue;
                        if (t.Contains("==========")) continue;
                        if (t.Contains("PCL2 CE 解锁工具")) continue;
                        if (t.Contains("正版验证 + 区域限制")) continue;
                        Console.WriteLine("    " + t);
                    }
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                Warn("调用补丁器失败：" + ex.Message);
                return -1;
            }
        }

        // ------------------------------------------------------------ 查找
        private static string FindCe()
        {
            try
            {
                var files = Directory.GetFiles(_dir, "PCL2_CE_*.exe")
                    .Where(f => new FileInfo(f).Length > 5000000)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToArray();
                if (files.Length > 0) return files[0];
            }
            catch { }
            return null;
        }

        private static string Quote(string s) { return s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s; }

        // ------------------------------------------------------------ 输出
        private static void Banner()
        {
            Console.WriteLine("==========================================================");
            Console.WriteLine("  PCL2 CE 离线解锁启动器");
            Console.WriteLine("  仅供本机使用 · 不含 PCL2 CE 本体");
            Console.WriteLine("==========================================================");
            Console.WriteLine();
        }

        private static void Info(string s) { Console.WriteLine("[i] " + s); }
        private static void Warn(string s) { Console.WriteLine("[!] " + s); }
        private static void Fail(string s) { Console.WriteLine("[×] " + s); }

        private static void Pause()
        {
            Console.WriteLine();
            Console.WriteLine("按任意键退出…");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
