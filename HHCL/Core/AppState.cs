using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HHCL.Core
{
    public class AppState
    {
        public static AppState Current { get; } = new AppState();

        public Config Config { get; private set; }
        public List<VersionEntry> Versions { get; private set; } = new List<VersionEntry>();
        public List<JavaInfo> Javas { get; private set; } = new List<JavaInfo>();
        public GameSession Session { get; private set; }
        public List<RemoteVersion> RemoteVersions { get; set; } = new List<RemoteVersion>();

        public event Action VersionsChanged;
        public event Action JavasChanged;
        public event Action<GameSession> SessionStarted;

        private AppState()
        {
            Config = Config.Load();
            if (string.IsNullOrEmpty(Config.GameDir) || !Directory.Exists(Config.GameDir))
            {
                var guess = new[] { @"E:\.minecraft", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") }
                    .FirstOrDefault(Directory.Exists);
                if (guess != null) Config.GameDir = guess;
            }
        }

        public void Save() => Config.Save();

        public void RefreshVersions()
        {
            Versions = VersionScanner.Scan(Config.GameDir);
            if (string.IsNullOrEmpty(Config.SelectedVersion) || Versions.All(v => v.Id != Config.SelectedVersion))
                Config.SelectedVersion = Versions.FirstOrDefault()?.Id ?? "";
            VersionsChanged?.Invoke();
        }

        public void RefreshJavas()
        {
            Javas = JavaScanner.Scan(Config.JavaPath);
            JavasChanged?.Invoke();
        }

        public VersionEntry SelectedVersion =>
            Versions.FirstOrDefault(v => v.Id == Config.SelectedVersion) ?? Versions.FirstOrDefault();

        public JavaInfo JavaFor(VersionEntry v)
        {
            if (v == null) return Javas.FirstOrDefault();
            return JavaScanner.PickFor(Javas, v.JavaMajor, Config.JavaPath);
        }

        public JavaInfo CurrentJava => JavaScanner.PickFor(Javas, 0, Config.JavaPath);

        public bool IsRunning => Session != null && Session.IsRunning;

        public (LaunchPlan plan, string error) BuildPlan(VersionEntry version, string directAddress = "")
        {
            if (version == null) return (null, "还没有选择版本");
            var java = JavaFor(version);
            if (java == null) return (null, "没有找到可用的 Java 运行时");
            if (version.JavaMajor > 0 && java.Major < version.JavaMajor)
                return (null, $"该版本需要 Java {version.JavaMajor} 或更高，当前只找到 Java {java.Major}。请到「设置」里下载或指定 Java。");

            try
            {
                // 缺少文件不算致命错误：把 plan 返回给上层，由它决定要不要一键补齐
                var plan = LaunchBuilder.Build(Config, version, Config.CurrentAccount, java.Path, null, directAddress);
                return (plan, null);
            }
            catch (Exception ex)
            {
                return (null, "构建启动参数失败：" + ex.Message);
            }
        }

        public GameSession Launch(VersionEntry version, out string error) => Launch(version, "", out error);

        /// <param name="directAddress">直连地址 "IP:端口"，非空则启动后自动连过去（局域网联机用）。</param>
        public GameSession Launch(VersionEntry version, string directAddress, out string error)
        {
            error = null;
            var (plan, err) = BuildPlan(version, directAddress);
            if (plan == null) { error = err; return null; }
            if (!plan.Ok)
            {
                error = "缺少必要文件：\n" + string.Join("\n", plan.Missing.Take(6))
                        + (plan.MissingFiles.Count > 6 ? $"\n… 共 {plan.MissingFiles.Count} 个文件" : "");
                return null;
            }

            // 启动前把游戏语言切成简体中文（options.txt 的 lang 项）
            if (Config.AutoChineseLang)
            {
                try { GameLanguage.EnsureChinese(plan.GameDir); }
                catch (Exception ex) { Log.Write("lang switch: " + ex.Message); }
            }

            var session = new GameSession();
            try
            {
                session.Start(plan);
            }
            catch (Exception ex)
            {
                error = "启动进程失败：" + ex.Message;
                return null;
            }
            Session = session;
            SessionStarted?.Invoke(session);
            return session;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:0.00} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return bytes + " B";
        }
    }
}
