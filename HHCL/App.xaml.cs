using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HHCL
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                try
                {
                    var path = Path.Combine(Core.Config.DataDir, "crash.log");
                    File.AppendAllText(path, $"[{DateTime.Now}] {ex.ExceptionObject}\r\n");
                }
                catch { }
            };
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var path = Path.Combine(Core.Config.DataDir, "crash.log");
                File.AppendAllText(path, $"[{DateTime.Now}] {e.Exception}\r\n");
            }
            catch { }
            MessageBox.Show("HHCL 遇到一个错误：\n\n" + e.Exception.Message, "HHCL", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            // 主题要在任何界面创建之前应用
            try { Core.ThemeManager.Apply(Core.AppState.Current.Config.Theme); }
            catch (Exception ex) { Core.Log.Write("theme: " + ex.Message); }

            if (e.Args.Length >= 2 && e.Args[0] == "--shot")
            {
                ViewShot.Run(e.Args);
                Shutdown();
                return;
            }

            if (e.Args.Length >= 1 && e.Args[0] == "--plan")
            {
                DumpPlan(e.Args.Length > 1 ? e.Args[1] : null);
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--install")
            {
                // 放到线程池执行，避免在 UI 线程上同步等待 async 造成死锁
                System.Threading.Tasks.Task.Run(() => RunInstallHeadless(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--launch")
            {
                System.Threading.Tasks.Task.Run(() => RunLaunchHeadless(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 1 && e.Args[0] == "--nettest")
            {
                RunNetTest();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--modrinth")
            {
                System.Threading.Tasks.Task.Run(() => RunModrinthTest(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 1 && e.Args[0] == "--javas")
            {
                var sb2 = new System.Text.StringBuilder();
                Core.AppState.Current.RefreshJavas();
                foreach (var j in Core.AppState.Current.Javas) sb2.AppendLine($"{j.Version,-10} {j.Vendor,-14} {(j.Is64 ? 64 : 32)}位  {j.Path}");
                File.WriteAllText(Path.Combine(Core.Config.DataDir, "javas.txt"), sb2.ToString(), new System.Text.UTF8Encoding(false));
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--repair")
            {
                System.Threading.Tasks.Task.Run(() => RunRepair(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 1 && e.Args[0] == "--mstest")
            {
                System.Threading.Tasks.Task.Run(() => RunMsTest()).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--check")
            {
                var sb3 = new System.Text.StringBuilder();
                var args = e.Args;
                try
                {
                    var cfg = Core.AppState.Current.Config;
                    if (args.Contains("--dir"))
                    {
                        var i = Array.IndexOf(args, "--dir");
                        if (i + 1 < args.Length) cfg.GameDir = args[i + 1];
                    }
                    Core.AppState.Current.RefreshVersions();
                    Core.AppState.Current.RefreshJavas();
                    var v = Core.AppState.Current.Versions.FirstOrDefault(x => x.Id == args[1])
                            ?? Core.AppState.Current.Versions.FirstOrDefault();
                    if (v == null) { sb3.AppendLine("NO VERSION"); }
                    else
                    {
                        var (plan, err) = Core.AppState.Current.BuildPlan(v);
                        if (plan == null) sb3.AppendLine("PLAN FAIL: " + err);
                        else
                        {
                            sb3.AppendLine($"version: {v.Id}   ok={plan.Ok}   missing={plan.MissingFiles.Count}");
                            foreach (var m in plan.MissingFiles.Take(12))
                                sb3.AppendLine($"  {m.Display,-28} {m.Size,10}  {m.Url}");
                            sb3.AppendLine("--- Describe ---");
                            sb3.AppendLine(Core.Repair.Describe(plan));
                        }
                    }
                }
                catch (Exception ex) { sb3.AppendLine("FAIL: " + ex); }
                File.WriteAllText(Path.Combine(Core.Config.DataDir, "check.txt"), sb3.ToString(), new System.Text.UTF8Encoding(false));
                Shutdown();
                return;
            }

            if (e.Args.Length >= 4 && e.Args[0] == "--install-mod")
            {
                System.Threading.Tasks.Task.Run(() => RunInstallMod(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--install-java")
            {
                System.Threading.Tasks.Task.Run(() => RunInstallJava(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 3 && e.Args[0] == "--loaders")
            {
                System.Threading.Tasks.Task.Run(() => RunLoadersTest(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 3 && e.Args[0] == "--install-loader")
            {
                System.Threading.Tasks.Task.Run(() => RunInstallLoader(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--modpack")
            {
                System.Threading.Tasks.Task.Run(() => RunModpackTest(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--lan")
            {
                System.Threading.Tasks.Task.Run(() => RunBedrockTest(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--bedrock")
            {
                System.Threading.Tasks.Task.Run(() => RunBedrockTest(e.Args)).GetAwaiter().GetResult();
                Shutdown();
                return;
            }

            var win = new MainWindow();
            MainWindow = win;
            win.Show();
        }

        private class SyncProgress<T> : IProgress<T>        {
            private readonly Action<T> _action;
            public SyncProgress(Action<T> action) => _action = action;
            public void Report(T value) => _action(value);
        }

        /// <summary>无界面安装，用于验证下载引擎：HHCL.exe --install 1.21.4 [--no-assets] [--dir 路径]</summary>
        /// <summary>HHCL.exe --modrinth 关键词 [mod|resourcepack|shader] [mc] [loader]</summary>
        private static void RunModrinthTest(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var query = args[1];
                var type = args.Length > 2 ? args[2] : "mod";
                var mc = args.Length > 3 ? args[3] : "";
                var loader = args.Length > 4 ? args[4] : "";

                var list = Core.Modrinth.SearchAsync(query, type, string.IsNullOrEmpty(mc) ? null : mc,
                    string.IsNullOrEmpty(loader) ? null : loader, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine($"search '{query}' type={type} mc={mc} loader={loader} -> {list.Count} hits");
                foreach (var p in list.Take(5))
                    sb.AppendLine($"  [{p.ProjectId}] {p.Title} · {p.SubLine} · icon={(string.IsNullOrEmpty(p.IconUrl) ? "-" : "yes")}");

                if (list.Count > 0)
                {
                    var versions = Core.Modrinth.GetVersionsAsync(list[0].ProjectId, string.IsNullOrEmpty(mc) ? null : mc,
                        string.IsNullOrEmpty(loader) ? null : loader, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    sb.AppendLine($"versions of {list[0].Title} -> {versions.Count}");
                    foreach (var v in versions.Take(3))
                        sb.AppendLine($"  {v.Display}  {v.FileName}  {v.FileUrl}");
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "modrinth.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>HHCL.exe --install-mod 关键词 mod|resourcepack|shader 1.21.4 fabric [目标目录]</summary>
        private static void RunInstallMod(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var query = args[1];
                var type = args[2];
                var mc = args[3];
                var loader = args.Length > 4 ? args[4] : "";
                var cfg = Core.AppState.Current.Config;
                var target = args.Length > 5 ? args[5]
                    : Path.Combine(cfg.GameDir, type == "resourcepack" ? "resourcepacks" : type == "shader" ? "shaderpacks" : "mods");

                var list = Core.Modrinth.SearchAsync(query, type, mc, loader, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (list.Count == 0) throw new Exception("没有搜索结果");
                sb.AppendLine($"pick: {list[0].Title}");

                var vs = Core.Modrinth.GetVersionsAsync(list[0].ProjectId, mc, loader, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (vs.Count == 0) throw new Exception("没有可用版本");
                sb.AppendLine($"version: {vs[0].Display} -> {vs[0].FileName}");

                var file = Core.Modrinth.InstallAsync(vs[0], target,
                    new SyncProgress<Core.DlProgress>(p => sb.AppendLine($"{p.Stage}\t{p.Done}/{p.Total}\t{p.Current}")),
                    System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine("FILE: " + file);
                sb.AppendLine("EXISTS: " + File.Exists(file));
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex.Message); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "modinstall.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>HHCL.exe --mstest：只验证微软登录接口是否可达（不弹窗）。</summary>
        private static void RunMsTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var (_, userCode, verifyUrl, interval) = Core.MicrosoftAuth
                    .RequestDeviceCodeAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine("OK device code");
                sb.AppendLine("userCode: " + userCode);
                sb.AppendLine("verifyUrl: " + verifyUrl);
                sb.AppendLine("interval: " + interval);
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex.Message); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "mstest.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>HHCL.exe --repair 版本名 [--dir 路径]（无弹窗，直接补齐）</summary>
        private static void RunRepair(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var cfg = Core.AppState.Current.Config;
                var id = args[1];
                if (args.Contains("--dir"))
                {
                    var i = Array.IndexOf(args, "--dir");
                    if (i + 1 < args.Length) cfg.GameDir = args[i + 1];
                }
                Core.AppState.Current.RefreshVersions();
                Core.AppState.Current.RefreshJavas();
                var v = Core.AppState.Current.Versions.FirstOrDefault(x => x.Id == id);
                if (v == null) throw new Exception("找不到版本 " + id);

                var (plan, err) = Core.AppState.Current.BuildPlan(v);
                if (plan == null) throw new Exception(err);
                sb.AppendLine($"before: missing={plan.MissingFiles.Count}");
                if (plan.MissingFiles.Count > 0)
                {
                    var progress = new SyncProgress<Core.DlProgress>(p =>
                        sb.AppendLine($"{p.Stage}\t{p.Done}/{p.Total}\t{p.Percent:0.0}%\t{p.SizeText}\t{p.Current}"));
                    Core.Repair.RunAsync(plan, progress, System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                    var (plan2, _) = Core.AppState.Current.BuildPlan(v);
                    sb.AppendLine("after: missing=" + (plan2?.MissingFiles.Count ?? -1) + "   ok=" + (plan2?.Ok ?? false));
                }
                else sb.AppendLine("already ok");
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex.Message); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "repair.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>HHCL.exe --install-java 25</summary>
        private static void RunInstallJava(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                if (!int.TryParse(args[1], out var major)) major = 25;
                var exe = Core.JavaInstaller.InstallAsync(major,
                    new SyncProgress<(string stage, double percent, long done, long total)>(p =>
                        sb.AppendLine($"{p.stage}\t{p.percent:F1}%\t{p.done / 1048576.0:F1} MB")),
                    System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine("JAVA: " + (exe ?? "(null)"));
                if (!string.IsNullOrEmpty(exe)) sb.AppendLine("EXISTS: " + File.Exists(exe));
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex.Message); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "java.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>HHCL.exe --loaders 1.21.4 forge|neoforge|fabric|quilt</summary>
        private static void RunLoadersTest(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var mc = args[1];
                var kind = args[2];
                var list = Core.LoaderInstaller.GetLoadersAsync(mc, kind, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                sb.AppendLine($"OK {mc} {kind} -> {list.Count}");
                foreach (var l in list.Take(8)) sb.AppendLine($"  {l.Version}  stable={l.Stable}  path={l.InstallerPath}");
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex.Message); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "loaders.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>HHCL.exe --install-loader 1.21.4 fabric [版本|latest] [--dir 路径]</summary>
        private static void RunInstallLoader(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var cfg = Core.AppState.Current.Config;
                var mc = args[1];
                var kind = args[2];
                var version = args.Length > 3 ? args[3] : "latest";
                if (args.Contains("--dir"))
                {
                    var i = Array.IndexOf(args, "--dir");
                    if (i + 1 < args.Length) cfg.GameDir = args[i + 1];
                }
                Core.AppState.Current.RefreshJavas();

                if (version == "latest" || string.IsNullOrEmpty(version))
                {
                    var loaders = Core.LoaderInstaller.GetLoadersAsync(mc, kind, System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                    sb.AppendLine($"loaders: {string.Join(", ", loaders.Take(5).Select(l => l.Version))}");
                    if (loaders.Count == 0) throw new Exception("没有可用的加载器版本");
                    version = loaders[0].Version;
                }

                var progress = new SyncProgress<Core.DlProgress>(p =>
                    sb.AppendLine($"{p.Stage}\t{p.Done}/{p.Total}\t{p.Percent:0.0}%\t{p.SizeText}\t{p.Current}"));
                var name = Core.LoaderInstaller.InstallAsync(cfg, mc, kind, version, progress,
                    System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine("DONE: " + name);
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "loader.log"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>
        /// HHCL.exe --modpack 关键词 [modrinth|curseforge] [--dry|--go] [--dir 路径] [--index N] [--mc 1.21.4] [--loader fabric]
        /// --dry（默认）：搜索 → 解析整合包清单（MC/加载器/文件数/总大小），不下载模组。
        /// --go：在上面基础上真实安装（原版+加载器+模组+配置）。
        /// </summary>
        private static void RunModpackTest(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var query = args[1];
                var source = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "modrinth";
                var go = args.Contains("--go");
                var dry = !go;
                var index = 0;
                var mc = "";
                var loader = "";
                if (args.Contains("--index"))
                {
                    var i = Array.IndexOf(args, "--index");
                    if (i + 1 < args.Length) int.TryParse(args[i + 1], out index);
                }
                if (args.Contains("--mc"))
                {
                    var i = Array.IndexOf(args, "--mc");
                    if (i + 1 < args.Length) mc = args[i + 1];
                }
                if (args.Contains("--loader"))
                {
                    var i = Array.IndexOf(args, "--loader");
                    if (i + 1 < args.Length) loader = args[i + 1];
                }

                var apiKey = Core.AppState.Current.Config.CurseForgeApiKey;
                if (source == "curseforge" && string.IsNullOrWhiteSpace(apiKey))
                    throw new Exception("CurseForge 需要 API Key（设置页 → 在线资源）");

                List<Core.ModpackSummary> list;
                if (source == "curseforge")
                    list = Core.ModpackInstaller.SearchCurseForgeAsync(query, mc, loader, apiKey, System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                else
                    list = Core.ModpackInstaller.SearchModrinthAsync(query, mc, loader, System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                sb.AppendLine($"search '{query}' [{source}] -> {list.Count} hits");
                foreach (var p in list.Take(6))
                    sb.AppendLine($"  [{p.ProjectId}] {p.Title} · {p.SubLine}");
                if (list.Count == 0) throw new Exception("没有搜索结果");
                if (index >= list.Count) index = 0;
                var pick = list[index];
                sb.AppendLine($"pick: {pick.Title}");

                var releases = Core.ModpackInstaller.GetReleasesAsync(pick, mc, loader, apiKey, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                sb.AppendLine($"releases -> {releases.Count}");
                foreach (var r in releases.Take(3))
                    sb.AppendLine($"  {r.Display}  {r.FileName}");
                if (releases.Count == 0) throw new Exception("没有可用版本");

                sb.AppendLine("parsing latest release…");
                var info = Core.ModpackInstaller.ParseAsync(releases[0], System.Threading.CancellationToken.None, apiKey)
                    .GetAwaiter().GetResult();
                sb.AppendLine($"  name={info.Name}");
                sb.AppendLine($"  mc={info.McVersion} loader={info.Loader} loaderVer={info.LoaderVersion}");
                sb.AppendLine($"  files={info.Files.Count}  total={Core.AppState.FormatSize(info.TotalBytes)}");
                sb.AppendLine($"  overrides={info.OverridesDir}");
                sb.AppendLine($"  profileId={info.ProfileId}");
                foreach (var f in info.Files.Take(5))
                    sb.AppendLine($"    file: {(f.IsCurseForge ? $"cf#{f.ProjectId}/{f.FileId}" : f.Path)}  {f.Size}");

                if (dry)
                {
                    sb.AppendLine("DRY OK (未下载模组)");
                }
                else
                {

                var cfg = Core.AppState.Current.Config;
                if (args.Contains("--dir"))
                {
                    var i = Array.IndexOf(args, "--dir");
                    if (i + 1 < args.Length) cfg.GameDir = args[i + 1];
                }
                sb.AppendLine($"install -> gameDir={cfg.GameDir}");
                var progress = new SyncProgress<Core.DlProgress>(p =>
                    sb.AppendLine($"{p.Stage}\t{p.Done}/{p.Total}\t{p.Percent:0.0}%\t{p.SizeText}\t{p.Current}"));
                var id = Core.ModpackInstaller.InstallAsync(info, cfg, progress, System.Threading.CancellationToken.None, apiKey)
                    .GetAwaiter().GetResult();
                sb.AppendLine("INSTALLED: " + id);
            }
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex.Message); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "modpack.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private static void RunNetTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                sb.AppendLine("DefaultProxy: " + (System.Net.Http.HttpClient.DefaultProxy?.GetType().Name ?? "null"));
                try
                {
                    var proxy = System.Net.Http.HttpClient.DefaultProxy?.GetProxy(new Uri("https://piston-meta.mojang.com"));
                    sb.AppendLine("ProxyForMojang: " + (proxy?.ToString() ?? "direct"));
                }
                catch (Exception ex) { sb.AppendLine("proxy probe fail: " + ex.Message); }

                var urls = new[]
                {
                    "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                    "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
                    "https://libraries.minecraft.net/com/mojang/brigadier/1.3.10/brigadier-1.3.10.jar"
                };

                foreach (var url in urls)
                {
                    foreach (var proto in new[] { System.Net.HttpVersion.Version20, System.Net.HttpVersion.Version11 })
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            using var handler = new System.Net.Http.HttpClientHandler();
                            using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url)
                            {
                                Version = proto,
                                VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower
                            };
                            using var resp = client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead)
                                .GetAwaiter().GetResult();
                            sb.AppendLine($"{(int)resp.StatusCode}  {proto,-6} {sw.ElapsedMilliseconds,6} ms  {url}");
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"FAIL {proto,-6} {sw.ElapsedMilliseconds,6} ms  {url}  :: {ex.GetBaseException().Message}");
                        }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("FATAL " + ex); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "nettest.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private static void RunInstallHeadless(string[] args)
        {
            var logPath = Path.Combine(Core.Config.DataDir, "install.log");
            var writer = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            void W(string line) { try { writer.WriteLine(line); } catch { } }
            try
            {
                var id = args[1];
                var gameDir = Core.AppState.Current.Config.GameDir;
                if (args.Contains("--dir"))
                {
                    var i = Array.IndexOf(args, "--dir");
                    if (i + 1 < args.Length) gameDir = args[i + 1];
                }
                bool assets = !args.Contains("--no-assets");
                var source = args.Contains("--mojang") ? "mojang" : Core.AppState.Current.Config.DownloadSource;

                W($"install {id} -> {gameDir}  source={source} assets={assets}");
                var list = Core.MinecraftDownloader.GetManifestAsync(source, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                var rv = list.FirstOrDefault(v => v.Id == id);
                if (rv == null)
                {
                    W("FAIL: version not found");
                }
                else
                {
                    var used = Core.MinecraftDownloader.LastUsedSource;
                    W("manifest ok via " + used);
                    var progress = new SyncProgress<Core.DlProgress>(p =>
                        W($"{p.Stage}\t{p.Done}/{p.Total}\t{p.Percent:0.0}%\t{p.SizeText}\t{p.SpeedText}\t{p.Current}"));
                    Core.MinecraftDownloader.InstallAsync(gameDir, used, rv, progress, System.Threading.CancellationToken.None, assets)
                        .GetAwaiter().GetResult();
                    W("DONE");
                }
            }
            catch (Exception ex)
            {
                W("FAIL: " + ex);
            }
            try { writer.Flush(); writer.Dispose(); } catch { }
        }

        /// <summary>无界面启动，用于验证装好的版本能否真的跑起来：HHCL.exe --launch 1.21.4</summary>
        private static void RunLaunchHeadless(string[] args)
        {
            var gate = new object();
            var writer = new StreamWriter(new FileStream(Path.Combine(Core.Config.DataDir, "launch.log"), FileMode.Create,
                FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            void W(string line) { lock (gate) { try { writer.WriteLine(line); } catch { } } }

            try
            {
                var state = Core.AppState.Current;
                if (args.Contains("--dir"))
                {
                    var di = Array.IndexOf(args, "--dir");
                    if (di + 1 < args.Length) state.Config.GameDir = args[di + 1];
                }
                state.RefreshVersions();
                state.RefreshJavas();
                var ver = state.Versions.FirstOrDefault(v => v.Id == args[1]) ?? state.Versions.FirstOrDefault();
                if (ver == null) { W("ERROR: no version"); return; }

                W("launch: " + ver.Id);
                var session = state.Launch(ver, out var err);
                if (session == null) { W("ERROR: " + err); return; }

                session.Output += line => W(line);
                var exited = new System.Threading.ManualResetEventSlim(false);
                session.Exited += code => { W("EXIT " + code); exited.Set(); };
                if (!exited.Wait(TimeSpan.FromSeconds(50))) W("still running after 50s -> kill");
                session.Kill();
                W("done");
            }
            catch (Exception ex) { W("FAIL: " + ex); }
            try { writer.Flush(); writer.Dispose(); } catch { }
        }

        /// <summary>基岩版链路验证：HHCL.exe --bedrock check|list|go</summary>
        private static void RunBedrockTest(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var mode = args.Length > 1 ? args[1] : "check";
                // --lan 的子命令在 args[1]，但分支判断要认 "lan"
                if (args.Length > 0 && args[0] == "--lan") mode = "lan";
                sb.AppendLine($"mode: {mode}   time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"installed: {Core.BedrockInstaller.IsInstalled(out var ver)}   version={ver}");

                if (mode == "lan")
                {
                    // 用法：--lan ips | --lan detect <游戏目录>   （args[1] 是子命令）
                    var cmd = mode == "lan" && args.Length > 1 ? args[1] : "ips";
                    var lanSb = new System.Text.StringBuilder();
                    lanSb.AppendLine("本机 IPv4: " + string.Join(", ", Core.LanMultiplayer.LocalIPv4()));
                    lanSb.AppendLine("推荐地址: " + Core.LanMultiplayer.BestIPv4());
                    if (cmd == "scan")
                    {
                        // --lan scan → 自测：一边应答一边扫描，看能不能发现自己的房间
                        Core.LanDiscovery.StartResponder(51234);
                        var found = Core.LanDiscovery.ScanAsync(2500).GetAwaiter().GetResult();
                        lanSb.AppendLine($"扫描到 {found.Count} 个房间");
                        foreach (var r in found) lanSb.AppendLine("  " + r.Address + "  房间码 " + r.Code);
                        Core.LanDiscovery.StopResponder();
                    }
                    else if (cmd == "lang")
                    {
                        // --lan lang <游戏目录>  → 把 options.txt 切成中文
                        var dir = args.Length > 2 ? args[2] : Core.AppState.Current.Config.GameDir;
                        var opt = Path.Combine(dir, "options.txt");
                        lanSb.AppendLine("options.txt: " + opt + " 存在=" + File.Exists(opt));
                        if (File.Exists(opt))
                        {
                            var before = File.ReadAllLines(opt).Where(l => l.StartsWith("lang:")).ToArray();
                            lanSb.AppendLine("改之前的 lang: " + (before.Length > 0 ? string.Join(",", before) : "(无)"));
                        }
                        var ok = Core.GameLanguage.EnsureChinese(dir);
                        lanSb.AppendLine("EnsureChinese 返回: " + ok);
                        if (File.Exists(opt))
                        {
                            var after = File.ReadAllLines(opt).Where(l => l.StartsWith("lang:")).ToArray();
                            lanSb.AppendLine("改之后的 lang: " + string.Join(",", after));
                            lanSb.AppendLine("文件总行数: " + File.ReadAllLines(opt).Length);
                        }
                    }
                    else if (cmd == "plan")
                    {
                        // --lan plan <版本> <直连地址>  → 验证直连参数真的进了启动命令行
                        var vid = args.Length > 2 ? args[2] : "";
                        var addr = args.Length > 3 ? args[3] : "192.168.31.183:51234";
                        var state = Core.AppState.Current;
                        state.RefreshVersions();
                        state.RefreshJavas();
                        var ve = state.Versions.FirstOrDefault(x => x.Id == vid) ?? state.Versions.FirstOrDefault();
                        if (ve == null) lanSb.AppendLine("没有可用版本");
                        else
                        {
                            var (pl, err) = state.BuildPlan(ve, addr);
                            if (pl == null) lanSb.AppendLine("构建失败: " + err);
                            else
                            {
                                var joined = string.Join(" ", pl.Args);
                                var has = joined.Contains("--quickPlayMultiplayer");
                                lanSb.AppendLine($"版本 {ve.Id} 参数里是否含 --quickPlayMultiplayer: {has}");
                                var i = joined.IndexOf("--quickPlayMultiplayer");
                                if (i >= 0)
                                {
                                    var seg = joined.Substring(i, Math.Min(60, joined.Length - i));
                                    lanSb.AppendLine("片段: " + seg);
                                }
                                lanSb.AppendLine("直连地址是否出现: " + joined.Contains(addr));
                            }
                        }
                    }
                    else if (cmd == "detect")
                    {
                        var dir = args.Length > 2 ? args[2] : Core.AppState.Current.Config.GameDir;
                        lanSb.AppendLine("日志目录: " + dir);
                        var port = Core.LanMultiplayer.DetectLanPort(dir);
                        if (port.HasValue)
                        {
                            var room = Core.LanMultiplayer.MakeRoom(port.Value);
                            lanSb.AppendLine($"检测到房间: 端口={room.Port} 房间码={room.Code} 地址={room.Address}");
                        }
                        else lanSb.AppendLine("没检测到局域网端口");
                    }
                    var p2 = Path.Combine(Core.Config.DataDir, "lan.txt");
                    File.WriteAllText(p2, lanSb.ToString(), new System.Text.UTF8Encoding(false));
                    Console.WriteLine(lanSb.ToString());
                }
                else if (mode == "addon")
                {
                    // --bedrock addon <本地包路径>
                    var file = args.Length > 2 ? args[2] : "";
                    var acc = Core.AppState.Current.Config.CurrentAccount;
                    string xuid = acc.Xuid ?? "";
                    if (string.IsNullOrEmpty(xuid) && acc.Type == "microsoft")
                    {
                        try { xuid = Core.BedrockInstaller.GetXuidAsync(acc, System.Threading.CancellationToken.None).GetAwaiter().GetResult(); }
                        catch { }
                    }
                    var gdir = Core.BedrockProfiles.FindGameDataDir(out var layout, xuid);
                    sb.AppendLine($"目标目录 ({layout}): {gdir}");
                    try
                    {
                        var r = Core.BedrockAddonInstaller.InstallLocalAsync(file, gdir, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        sb.AppendLine($"结果 ok={r.Ok} 装了: {string.Join("、", r.Installed)}");
                        if (!r.Ok) sb.AppendLine("原因: " + r.Message);
                        // 列出落地结果
                        foreach (var d in new[] { "behavior_packs", "resource_packs", "addons", "worlds" })
                        {
                            var p = System.IO.Path.Combine(gdir, d);
                            if (!System.IO.Directory.Exists(p)) continue;
                            foreach (var sub in System.IO.Directory.GetDirectories(p))
                                sb.AppendLine($"   {d}/{System.IO.Path.GetFileName(sub)}  ({(System.IO.File.Exists(System.IO.Path.Combine(sub, "manifest.json")) ? "有 manifest" : "无 manifest")})");
                        }
                    }
                    catch (Exception ex) { sb.AppendLine("FAIL: " + ex); }
                }
                else if (mode == "vm")
                {
                    // 扫参数组合，找能返回 PackageVersions 的那种
                    var acc = Core.AppState.Current.Config.CurrentAccount;
                    var auth = Core.BedrockInstaller.GetXboxAuthAsync(acc, "http://update.xboxlive.com", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    var queries = new[]
                    {
                        "market=US&languages=en-US&deviceFamily=Windows.Desktop&AlternateId=Microsoft.MinecraftUWP",
                        "market=US&languages=en-US&deviceFamily=Windows.Desktop",
                        "market=US&languages=en-US&AlternateId=Microsoft.MinecraftUWP",
                        "market=US&languages=neutral",
                        "market=CN&languages=zh-CN&deviceFamily=Windows.Desktop&AlternateId=Microsoft.MinecraftUWP",
                        "",
                    };
                    foreach (var q in queries)
                    {
                        try
                        {
                            var v = Core.BedrockInstaller.GetVersionManifestRawAsync(auth, q, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                            var has = v.Contains("PackageVersions");
                            sb.AppendLine($"[{v.Length,7} 字节] PV={has}  q={q}");
                            if (has)
                            {
                                File.WriteAllText(Path.Combine(Core.Config.DataDir, "bedrock_versions.json"), v, new System.Text.UTF8Encoding(false));
                                sb.AppendLine("   >>> 命中，已保存 bedrock_versions.json");
                            }
                        }
                        catch (Exception ex) { sb.AppendLine($"FAIL q={q}: {ex.Message}"); }
                    }
                }
                else if (mode == "xsts")
                {
                    var acc = Core.AppState.Current.Config.CurrentAccount;
                    foreach (var rp in new[] { "http://xboxlive.com", "https://xboxlive.com", "http://licensing.xboxlive.com", "http://auth.xboxlive.com" })
                    {
                        try
                        {
                            var j = Core.BedrockInstaller.DebugXstsAsync(acc, rp, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                            sb.AppendLine($"--- RP {rp} ---");
                            sb.AppendLine(j.Length > 700 ? "…尾部: " + j.Substring(Math.Max(0, j.Length - 700)) : j);
                        }
                        catch (Exception ex) { sb.AppendLine($"RP {rp} FAIL: {ex.Message}"); }
                    }
                }
                else if (mode == "profile")
                {
                    // --bedrock profile list|create|apply|reclaim [名称]
                    var cmd = args.Length > 2 ? args[2] : "list";
                    var pname = args.Length > 3 ? args[3] : "";
                    var acc = Core.AppState.Current.Config.CurrentAccount;
                    string xuid = acc.Xuid ?? "";
                    if (string.IsNullOrEmpty(xuid) && acc.Type == "microsoft")
                    {
                        try { xuid = Core.BedrockInstaller.GetXuidAsync(acc, System.Threading.CancellationToken.None).GetAwaiter().GetResult(); }
                        catch (Exception ex) { sb.AppendLine("xuid 获取失败: " + ex.Message); }
                    }
                    sb.AppendLine("xuid = " + (string.IsNullOrEmpty(xuid) ? "(空)" : xuid));
                    var gdir = Core.BedrockProfiles.FindGameDataDir(out var layout, xuid);
                    sb.AppendLine($"游戏数据目录 ({layout}): {gdir}  存在={System.IO.Directory.Exists(gdir)}");

                    var list = Core.BedrockProfiles.Load();
                    sb.AppendLine($"配置数: {list.Count}");
                    foreach (var pp in list)
                        sb.AppendLine($"  [{pp.Name}] 存档={Core.BedrockProfiles.CountWorlds(pp)} 大小={Core.BedrockProfiles.SizeOf(pp):N0}B 目录={Core.BedrockProfiles.ProfileDir(pp)}");

                    if (cmd == "create")
                    {
                        var np = Core.BedrockProfiles.Create(string.IsNullOrEmpty(pname) ? "测试配置" : pname);
                        sb.AppendLine("已创建: " + np.Name);
                    }
                    else if (cmd == "apply" || cmd == "reclaim")
                    {
                        var target = list.FirstOrDefault(x => x.Name == pname) ?? list.FirstOrDefault();
                        if (target == null) sb.AppendLine("没有可用配置");
                        else
                        {
                            var (ok, msg) = cmd == "apply"
                                ? Core.BedrockProfiles.Apply(target, xuid)
                                : Core.BedrockProfiles.Reclaim(target, xuid);
                            sb.AppendLine($"{cmd} {(ok ? "OK" : "FAIL")}: {msg}");
                            sb.AppendLine($"  配置目录现在: 存档={Core.BedrockProfiles.CountWorlds(target)} 大小={Core.BedrockProfiles.SizeOf(target):N0}B");
                            if (System.IO.Directory.Exists(gdir))
                            {
                                var w = System.IO.Path.Combine(gdir, "minecraftWorlds");
                                sb.AppendLine($"  游戏目录现在: 存档={(System.IO.Directory.Exists(w) ? System.IO.Directory.GetDirectories(w).Length : 0)}");
                            }
                        }
                    }
                }
                else if (mode == "rp")
                {
                    // 扫描不同 RelyingParty，看哪个能拿到真正的版本清单
                    var rps = new[]
                    {
                        "http://update.xboxlive.com",
                        "http://licensing.xboxlive.com",
                        "http://store.xboxlive.com",
                        "https://licensing.xboxlive.com"
                    };
                    var acc = Core.AppState.Current.Config.CurrentAccount;
                    foreach (var rp in rps)
                    {
                        try
                        {
                            var auth = Core.BedrockInstaller.GetXboxAuthAsync(acc, rp, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                            sb.AppendLine($"RP {rp} -> auth OK");
                            try
                            {
                                var vr = Core.BedrockInstaller.GetVersionManifestRawAsync(auth, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                                var has = vr.Contains("PackageVersions") ? "有 PackageVersions!" : "仅类型存根";
                                sb.AppendLine($"   storeedgefd: {vr.Length} 字节, {has}");
                                if (vr.Contains("PackageVersions"))
                                    File.WriteAllText(Path.Combine(Core.Config.DataDir, "bedrock_versions.json"), vr, new System.Text.UTF8Encoding(false));
                            }
                            catch (Exception ex) { sb.AppendLine("   storeedgefd FAIL: " + ex.Message); }
                        }
                        catch (Exception ex) { sb.AppendLine($"RP {rp} -> 失败: {ex.Message}"); }
                    }
                }
                else if (mode != "check" && mode != "lan" && mode != "ip" && mode != "xsts" && mode != "vm" && mode != "rp" && mode != "profile")
                {
                    var acc = Core.AppState.Current.Config.CurrentAccount;
                    sb.AppendLine($"account: {acc.Name}  type={acc.Type}  tokenLen={acc.AccessToken?.Length ?? 0}  expire={acc.TokenExpire}");
                    var auth = Core.BedrockInstaller.GetXboxUpdateAuthAsync(acc, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    sb.AppendLine("auth: OK  " + (auth.Length > 30 ? auth.Substring(0, 30) + "..." : auth));
                    var raw = Core.BedrockInstaller.GetBasePackageRawAsync(auth, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(Core.Config.DataDir, "bedrock_raw.json"), raw, new System.Text.UTF8Encoding(false));
                    sb.AppendLine("raw bytes: " + raw.Length);
                    try
                    {
                        var vraw = Core.BedrockInstaller.GetVersionManifestRawAsync(auth, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        File.WriteAllText(Path.Combine(Core.Config.DataDir, "bedrock_versions.json"), vraw, new System.Text.UTF8Encoding(false));
                        sb.AppendLine("version manifest bytes: " + vraw.Length);
                    }
                    catch (Exception vex) { sb.AppendLine("version manifest FAIL: " + vex.Message); }
                    var files = Core.BedrockInstaller.ParsePackageFiles(raw);
                    sb.AppendLine($"package files: {files.Count}   total={files.Sum(f => f.Size):N0} bytes");
                    foreach (var f in files)
                        sb.AppendLine($"  {f.Size,14:N0}  {f.FileName}   -> {(f.Urls.Count > 0 ? f.Urls[0] : "(无地址)")}");

                    if (mode == "go")
                    {
                        var dir = Path.Combine(Core.Config.DataDir, "bedrock");
                        var prog = new SyncProgress<Core.DlProgress>(p =>
                            sb.AppendLine($"  [{p.Stage}] {p.Done}/{p.Total}  {p.Current}"));
                        Core.BedrockInstaller.DownloadAsync(files, dir, prog, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        sb.AppendLine("download done -> " + dir);
                        Core.BedrockInstaller.Install(dir);
                        sb.AppendLine("install: OK");
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL: " + ex); }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "bedrock.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private static void DumpPlan(string versionId)        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var state = Core.AppState.Current;
                state.RefreshVersions();
                state.RefreshJavas();
                var ver = state.Versions.FirstOrDefault(v => v.Id == versionId) ?? state.Versions.FirstOrDefault();
                sb.AppendLine("version: " + (ver?.Id ?? "<none>"));
                sb.AppendLine("mc: " + (ver?.McVersion ?? "?") + "  loader: " + (ver?.Loader ?? "?") + "  javaReq: " + (ver?.JavaMajor ?? 0));
                sb.AppendLine("javas: " + string.Join(" | ", state.Javas.Select(j => $"Java {j.Major} {j.Path}")));
                var (plan, err) = state.BuildPlan(ver);
                if (plan == null)
                {
                    sb.AppendLine("ERROR: " + err);
                }
                else
                {
                    sb.AppendLine("exe: " + plan.JavaPath);
                    sb.AppendLine("cwd: " + plan.WorkDir);
                    sb.AppendLine("main: " + plan.MainClass);
                    sb.AppendLine("warnings: " + string.Join(" | ", plan.Warnings));
                    sb.AppendLine("args:");
                    foreach (var a in plan.Args) sb.AppendLine("  " + a);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("EXCEPTION: " + ex);
            }
            File.WriteAllText(Path.Combine(Core.Config.DataDir, "plan.txt"), sb.ToString(), System.Text.Encoding.UTF8);
        }
    }
}
