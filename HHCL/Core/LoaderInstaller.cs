using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    public class LoaderVersionInfo
    {
        public string Version { get; set; } = "";
        public bool Stable { get; set; }
        public string InstallerPath { get; set; } = "";

        public string Display => Stable ? $"{Version} · 稳定版" : Version;

        public override string ToString() => Display;
    }

    public static class LoaderInstaller
    {
        public const string KindVanilla = "vanilla";
        public const string KindFabric = "fabric";
        public const string KindForge = "forge";
        public const string KindNeoForge = "neoforge";

        public static string KindName(string kind) => kind switch
        {
            KindFabric => "Fabric",
            KindForge => "Forge",
            KindNeoForge => "NeoForge",
            _ => "原版"
        };

        /// <summary>某个 MC 版本可用的加载器版本列表。</summary>
        public static async Task<List<LoaderVersionInfo>> GetLoadersAsync(string mc, string kind, CancellationToken ct)
        {
            var list = new List<LoaderVersionInfo>();
            try
            {
                if (kind == KindFabric)
                {
                    // meta.fabricmc.net 在国内（尤其走系统代理时）经常超时，
                    // 优先用 BMCLAPI 的 fabric-meta 镜像，官方地址作为兜底。
                    var text = await MinecraftDownloader.GetTextAsync(
                        $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mc}", ct,
                        altUrl: $"https://meta.fabricmc.net/v2/versions/loader/{mc}");
                    if (JsonNode.Parse(text) is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            var loader = (item as JsonObject)?["loader"] as JsonObject;
                            var v = loader?["version"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(v)) continue;
                            list.Add(new LoaderVersionInfo { Version = v, Stable = loader?["stable"]?.GetValue<bool>() ?? false });
                        }
                    }
                }
                else if (kind == KindForge)
                {
                    var text = await MinecraftDownloader.GetTextAsync("https://bmclapi2.bangbang93.com/forge/promos", ct);
                    if (JsonNode.Parse(text) is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            var o = item as JsonObject;
                            var name = o?["name"]?.GetValue<string>() ?? "";
                            if (!name.StartsWith(mc + "-", StringComparison.OrdinalIgnoreCase)) continue;
                            var ver = o?["build"]?["version"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(ver)) continue;
                            if (list.Any(x => x.Version == ver)) continue;
                            list.Add(new LoaderVersionInfo { Version = ver, Stable = name.EndsWith("-latest") || name.EndsWith("-recommended") });
                        }
                    }
                    list.Reverse();
                }
                else if (kind == KindNeoForge)
                {
                    var text = await MinecraftDownloader.GetTextAsync($"https://bmclapi2.bangbang93.com/neoforge/list/{mc}", ct);
                    if (JsonNode.Parse(text) is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            var o = item as JsonObject;
                            var v = o?["version"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(v)) continue;
                            list.Add(new LoaderVersionInfo
                            {
                                Version = v,
                                Stable = !(v ?? "").Contains("beta") && !(v ?? "").Contains("alpha"),
                                InstallerPath = o?["installerPath"]?.GetValue<string>() ?? ""
                            });
                        }
                        list.Reverse();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"loaders {kind} fail: {ex.Message}");
                throw;
            }

            return list.Take(30).ToList();
        }

        /// <summary>安装加载器，返回新版本的名字。</summary>
        public static async Task<string> InstallAsync(Config cfg, string mc, string kind, string loaderVersion,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            if (kind == KindForge || kind == KindNeoForge)
                return await InstallByInstallerAsync(cfg, mc, kind, loaderVersion, progress, ct);

            var profileUrl = $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mc}/{loaderVersion}/profile/json";
            var profileAlt = $"https://meta.fabricmc.net/v2/versions/loader/{mc}/{loaderVersion}/profile/json";

            progress?.Report(new DlProgress { Stage = "获取加载器配置", Current = loaderVersion });
            var json = await MinecraftDownloader.GetTextAsync(profileUrl, ct, altUrl: profileAlt);

            var name = $"{mc}-{KindName(kind)} {loaderVersion}";
            var dir = Path.Combine(cfg.GameDir, "versions", name);
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, name + ".json");
            File.WriteAllText(jsonPath, json, new UTF8Encoding(false));

            // 补齐加载器自己的库文件
            var entry = new VersionEntry
            {
                Id = name,
                Dir = dir,
                JsonPath = jsonPath,
                McVersion = mc,
                Loader = KindName(kind)
            };
            var plan = LaunchBuilder.Build(cfg, entry, cfg.CurrentAccount, AppState.Current.Javas.FirstOrDefault()?.Path ?? "");
            var libs = plan.MissingFiles.Where(m => m.Path.IndexOf("libraries", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (libs.Count > 0)
            {
                progress?.Report(new DlProgress { Stage = "下载加载器文件", Current = $"{libs.Count} 个文件" });
                await MinecraftDownloader.DownloadFilesAsync(libs.Select(m => (m.Url, m.Path, m.Size)), progress, "下载加载器文件", ct);
            }

            progress?.Report(new DlProgress { Stage = "完成", Current = name, Done = 1, Total = 1, DoneBytes = 1, TotalBytes = 1 });
            return name;
        }

        private static async Task<string> InstallByInstallerAsync(Config cfg, string mc, string kind, string loaderVersion,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            string installerUrl;
            if (kind == KindForge)
            {
                installerUrl = $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{mc}-{loaderVersion}/forge-{mc}-{loaderVersion}-installer.jar";
            }
            else
            {
                var list = await GetLoadersAsync(mc, kind, ct);
                var hit = list.FirstOrDefault(l => l.Version == loaderVersion);
                if (hit == null || string.IsNullOrEmpty(hit.InstallerPath))
                    throw new Exception("找不到 NeoForge 安装器地址");
                installerUrl = "https://bmclapi2.bangbang93.com" + hit.InstallerPath;
            }

            var tmp = Path.Combine(Config.DataDir, kind + "-installer.jar");
            progress?.Report(new DlProgress { Stage = "下载安装器", Current = Path.GetFileName(installerUrl) });
            await MinecraftDownloader.DownloadFilesAsync(new[] { (installerUrl, tmp, 0L) }, progress, "下载安装器", ct);

            // 官方 maven 源（maven.neoforged.net 等）在部分网络下不可达，
            // 先把安装器需要的依赖库从镜像拖到本地 libraries/，安装器就会跳过自己的下载。
            await PrefetchInstallerLibsAsync(tmp, cfg.GameDir, progress, ct);

            EnsureLauncherProfiles(cfg.GameDir);

            // 安装器还需要原版版本已存在（versions/{mc}/{mc}.json + .jar）
            var vanillaJson = Path.Combine(cfg.GameDir, "versions", mc, mc + ".json");
            var vanillaJar = Path.Combine(cfg.GameDir, "versions", mc, mc + ".jar");
            if (!File.Exists(vanillaJson) || !File.Exists(vanillaJar))
                throw new Exception($"缺少原版 {mc}，请先安装原版再安装 {KindName(kind)}。");

            var java = AppState.Current.Javas.FirstOrDefault() ?? JavaScanner.Scan(cfg.JavaPath).FirstOrDefault();
            if (java == null) throw new Exception("没有可用的 Java 来运行安装器");

            progress?.Report(new DlProgress { Stage = "运行安装器", Current = $"{KindName(kind)} {loaderVersion}" });
            Log.Write($"run installer: {java.Path} -jar {tmp} --installClient {cfg.GameDir}");

            var psi = new ProcessStartInfo(java.Path)
            {
                WorkingDirectory = cfg.GameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(tmp);
            psi.ArgumentList.Add("--installClient");
            psi.ArgumentList.Add(cfg.GameDir);

            var versionsDir = Path.Combine(cfg.GameDir, "versions");
            var before = SnapshotVersions(versionsDir);

            using var p = Process.Start(psi);
            if (p == null) throw new Exception("安装器启动失败");
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(ct);
            var stdout = await outTask;
            var stderr = await errTask;

            Log.Write($"installer exit {p.ExitCode}\n{stdout}\n{stderr}");
            try { File.Delete(tmp); } catch { }

            if (p.ExitCode != 0)
                throw new Exception($"{KindName(kind)} 安装器返回 {p.ExitCode}，可能缺少前置的原版版本。\n{Trim(stderr + stdout)}");

            // Forge 生成 1.21.4-forge-54.1.22，NeoForge 生成 neoforge-21.4.161：
            // 名字规则不统一，直接对比安装前后新增的版本目录更可靠。
            var found = DiffVersions(versionsDir, before);
            var fallback = $"{mc}-{kind}-{loaderVersion}";
            var name = found
                       ?? (Directory.Exists(Path.Combine(versionsDir, fallback)) ? fallback : $"{kind}-{loaderVersion}");

            progress?.Report(new DlProgress { Stage = "完成", Current = name, Done = 1, Total = 1, DoneBytes = 1, TotalBytes = 1 });
            return name;
        }

        private static HashSet<string> SnapshotVersions(string versionsDir)
        {
            try
            {
                return Directory.Exists(versionsDir)
                    ? new HashSet<string>(Directory.GetDirectories(versionsDir), StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        private static string DiffVersions(string versionsDir, HashSet<string> before)
        {
            try
            {
                if (!Directory.Exists(versionsDir)) return null;
                var added = Directory.GetDirectories(versionsDir).Where(d => !before.Contains(d)).ToList();
                if (added.Count == 0) return null;
                // 优先选名字里带加载器名的目录
                var pref = added.FirstOrDefault(d => Path.GetFileName(d).IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0
                                                     || Path.GetFileName(d).IndexOf("neoforge", StringComparison.OrdinalIgnoreCase) >= 0);
                return Path.GetFileName(pref ?? added[0]);
            }
            catch { return null; }
        }

        /// <summary>从安装器 jar 里的 version.json / install_profile.json 取出依赖，先用镜像下载到本地。</summary>
        private static async Task PrefetchInstallerLibsAsync(string jarPath, string gameDir,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            var libsDir = Path.Combine(gameDir, "libraries");
            var list = new List<(string Url, string Path, long Size)>();
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
                foreach (var entryName in new[] { "install_profile.json", "version.json" })
                {
                    var e = zip.GetEntry(entryName);
                    if (e == null) continue;
                    using var s = e.Open();
                    using var r = new StreamReader(s, Encoding.UTF8);
                    if (JsonNode.Parse(r.ReadToEnd()) is not JsonObject root) continue;

                    foreach (var node in new[] { root["libraries"], (root["profile"] as JsonObject)?["libraries"] })
                    {
                        if (node is not JsonArray arr) continue;
                        foreach (var item in arr)
                        {
                            var o = item as JsonObject;
                            var art = (o?["downloads"] as JsonObject)?["artifact"] as JsonObject;
                            var rel = art?["path"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(rel)) rel = MavenPath(o?["name"]?.GetValue<string>() ?? "");
                            if (string.IsNullOrEmpty(rel)) continue;

                            // 安装器本地生成的补丁包（forge-xxx-client.jar / -server.jar / -slim.jar）
                            // 在 maven 上根本不存在，预下载必然 404，必须跳过。
                            var lower = rel.ToLowerInvariant();
                            if (lower.EndsWith("-client.jar") || lower.EndsWith("-server.jar")
                                || lower.EndsWith("-slim.jar") || lower.EndsWith("-extra.jar")) continue;
                            // 没有下载地址的也是本地产物
                            if (art != null && string.IsNullOrEmpty(art?["url"]?.GetValue<string>())) continue;

                            rel = rel.Replace('/', Path.DirectorySeparatorChar);
                            var full = Path.Combine(libsDir, rel);
                            if (File.Exists(full)) continue;
                            var size = art?["size"]?.GetValue<long>() ?? 0L;
                            list.Add(("https://bmclapi2.bangbang93.com/maven/" + rel.Replace('\\', '/'), full, size));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("prefetch read fail: " + ex.Message);
                return;
            }

            list = list.GroupBy(x => x.Path).Select(g => g.First()).ToList();
            if (list.Count == 0) return;

            progress?.Report(new DlProgress { Stage = "预下载依赖", Current = $"{list.Count} 个文件" });
            try
            {
                await MinecraftDownloader.DownloadFilesAsync(list, progress, "预下载依赖", ct);
            }
            catch (Exception ex)
            {
                // 预下载只是加速手段，装不上不该中断：剩下的交给安装器自己下。
                Log.Write("prefetch download fail (ignored): " + ex.Message);
            }
        }

        /// <summary>group:artifact:version[:classifier][@ext] → 相对路径。</summary>
        private static string MavenPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var parts = name.Split(':');
            if (parts.Length < 3) return "";
            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var ext = "jar";
            var at = version.IndexOf('@');
            if (at >= 0) { ext = version.Substring(at + 1); version = version.Substring(0, at); }
            var classifier = parts.Length > 3 ? "-" + parts[3] : "";
            return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{ext}";
        }

        private static void EnsureLauncherProfiles(string gameDir)
        {
            try
            {
                Directory.CreateDirectory(gameDir);
                var p = Path.Combine(gameDir, "launcher_profiles.json");
                if (File.Exists(p)) return;
                File.WriteAllText(p, "{\n  \"profiles\": {},\n  \"settings\": {},\n  \"version\": 3\n}",
                    new UTF8Encoding(false));
                Log.Write("created launcher_profiles.json");
            }
            catch (Exception ex) { Log.Write("launcher_profiles.json: " + ex.Message); }
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Trim();
            return s.Length > 400 ? s.Substring(s.Length - 400) : s;
        }
    }
}
