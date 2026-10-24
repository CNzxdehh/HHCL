using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace HHCL.Core
{
    public class GameSession
    {
        private Process _process;
        private StreamWriter _logWriter;
        public event Action<string> Output;
        public event Action<int> Exited;

        public bool IsRunning
        {
            get
            {
                try { return _process != null && !_process.HasExited; }
                catch { return false; }
            }
        }

        public string LogFile { get; private set; }

        public void Start(LaunchPlan plan)
        {
            var psi = new ProcessStartInfo(plan.JavaPath)
            {
                WorkingDirectory = plan.WorkDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in plan.Args) psi.ArgumentList.Add(a);

            var logDir = Path.Combine(Config.DataDir, "logs");
            Directory.CreateDirectory(logDir);
            LogFile = Path.Combine(logDir, $"launch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logWriter = new StreamWriter(new FileStream(LogFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };

            _logWriter.WriteLine("=== HHCL 启动日志 ===");
            _logWriter.WriteLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _logWriter.WriteLine("版本: " + plan.VersionId);
            _logWriter.WriteLine("主类: " + plan.MainClass);
            _logWriter.WriteLine("游戏目录: " + plan.WorkDir);
            _logWriter.WriteLine("命令: " + plan.CommandLine);
            _logWriter.WriteLine("=====================");

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => Emit(e.Data);
            _process.ErrorDataReceived += (s, e) => Emit(e.Data);
            _process.Exited += (s, e) =>
            {
                try { Emit($"[HHCL] 游戏进程已退出，退出码 {_process.ExitCode}"); } catch { }
                try { _logWriter?.Flush(); _logWriter?.Dispose(); } catch { }
                Exited?.Invoke(SafeExitCode());
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private int SafeExitCode()
        {
            try { return _process.ExitCode; } catch { return -1; }
        }

        private void Emit(string line)
        {
            if (line == null) return;
            try { _logWriter?.WriteLine(line); } catch { }
            Output?.Invoke(line);
        }

        public void Kill()
        {
            try
            {
                if (_process != null && !_process.HasExited) _process.Kill(true);
            }
            catch { }
        }
    }

    public static class JavaInstaller
    {
        /// <summary>下载并解压 Microsoft OpenJDK 到 HHCL 数据目录，返回 javaw.exe 路径。</summary>
        public static async System.Threading.Tasks.Task<string> InstallAsync(int major, IProgress<(string stage, double percent, long done, long total)> progress, System.Threading.CancellationToken ct)
        {
            var url = $"https://aka.ms/download-jdk/microsoft-jdk-{major}-windows-x64.zip";
            var dir = Path.Combine(Config.DataDir, "jdk");
            Directory.CreateDirectory(dir);
            var zipPath = Path.Combine(dir, $"jdk-{major}.zip");

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using (var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1;
                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(("下载 Java 运行时", total > 0 ? done * 100.0 / total : 0, done, total));
                }
            }

            var extractDir = Path.Combine(dir, "jdk-" + major);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            await System.Threading.Tasks.Task.Run(() =>
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, true);
                progress?.Report(("解压 Java 运行时", 100, 1, 1));
            }, ct);

            try { File.Delete(zipPath); } catch { }

            string exe = null;
            foreach (var f in Directory.GetFiles(extractDir, "javaw.exe", SearchOption.AllDirectories))
            {
                exe = f;
                break;
            }
            return exe;
        }
    }
}
