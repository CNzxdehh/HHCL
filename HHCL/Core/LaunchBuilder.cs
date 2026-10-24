using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HHCL.Core
{
    public class MissingFile
    {
        public string Display { get; set; } = "";
        public string Path { get; set; } = "";
        public string Url { get; set; } = "";
        public long Size { get; set; }
    }

    public class LaunchPlan
    {
        public string JavaPath { get; set; } = "";
        public List<string> Args { get; set; } = new List<string>();
        public string WorkDir { get; set; } = "";
        public string GameDir { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string MainClass { get; set; } = "";
        public int RequiredJava { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<MissingFile> MissingFiles { get; set; } = new List<MissingFile>();
        public List<string> Missing => MissingFiles.Select(f => f.Display).ToList();
        public bool Ok => MissingFiles.Count == 0;

        public string CommandLine
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append('"').Append(JavaPath).Append('"');
                foreach (var a in Args)
                {
                    sb.Append(' ');
                    if (a.Contains(' ')) sb.Append('"').Append(a).Append('"');
                    else sb.Append(a);
                }
                return sb.ToString();
            }
        }
    }

    internal class MergedVersion
    {
        public string MainClass;
        public string AssetsIndex;
        public string McVersion;
        public int JavaMajor;
        public string Type;
        public string JarId;
        public readonly List<JsonObject> Libraries = new List<JsonObject>();
        public readonly List<JsonNode> JvmArgs = new List<JsonNode>();
        public readonly List<JsonNode> GameArgs = new List<JsonNode>();
        public string LegacyArgs;
        public JsonObject Root;
    }

    public static class LaunchBuilder
    {
        public const string LauncherName = "HHCL";
        public const string LauncherVersion = "1.0.0";

        public static string GameDirFor(Config cfg, VersionEntry v) =>
            cfg.IsolateVersions ? v.Dir : cfg.GameDir;

        /// <param name="quickPlayMultiplayer">非空时用 quickPlayMultiplayer 变量覆盖，用于"直连"局域网房间（IP:端口）。</param>
        public static LaunchPlan Build(Config cfg, VersionEntry version, Account account, string javaPath, Action<string> log = null,
            string quickPlayMultiplayer = "")
        {
            var plan = new LaunchPlan
            {
                VersionId = version.Id,
                JavaPath = javaPath,
                GameDir = GameDirFor(cfg, version)
            };
            plan.WorkDir = plan.GameDir;

            var merged = Collect(version, plan);
            plan.MainClass = merged.MainClass;
            plan.RequiredJava = merged.JavaMajor;

            var assetsDir = Path.Combine(cfg.GameDir, "assets");
            var nativesDir = Path.Combine(version.Dir, version.Id + "-natives");
            Directory.CreateDirectory(nativesDir);

            var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["is_demo_user"] = false,
                ["has_custom_resolution"] = false,
                ["has_quick_plays_support"] = false,
                ["is_quick_play_singleplayer"] = false,
                ["is_quick_play_multiplayer"] = false,
                ["is_quick_play_realms"] = false
            };

            // 直连局域网房间时，必须打开 quick play 的 feature 门控，
            // 否则 --quickPlayMultiplayer 这个参数会被 rules 过滤掉（等于没传）。
            if (!string.IsNullOrWhiteSpace(quickPlayMultiplayer))
            {
                features["has_quick_plays_support"] = true;
                features["is_quick_play_multiplayer"] = true;
            }

            // ---------- 库 ----------
            var classpath = new List<string>();
            var nativesJars = new List<string>();
            var missing = new List<MissingFile>();
            // Forge / NeoForge 的安装器会生成一个打过补丁的客户端 jar（...:client），
            // 那才是真正要跑的 MC。它和原版 jar 导出同名的包，NeoForge 走模块系统时会直接
            // 报 "Modules minecraft and _1._21._4 export package ..." 崩掉，所以有它就别加原版 jar。
            bool hasPatchedClient = false;
            var source = string.IsNullOrEmpty(cfg.DownloadSource) ? "bmclapi" : cfg.DownloadSource;

            foreach (var lib in merged.Libraries)
            {
                var name = lib["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;

                var rules = lib["rules"] as JsonArray;
                if (rules != null && !RulesAllow(rules, features)) continue;

                bool isNative = name.Contains("natives-");
                var ns = lib["natives"] as JsonObject;
                string classifier = null;
                if (ns != null)
                {
                    classifier = ns["windows"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(classifier)) continue;
                    isNative = true;
                }

                string rel;
                string url = null;
                long size = 0;

                var art = lib["downloads"]?["artifact"] as JsonObject;
                var classArt = classifier != null ? lib["downloads"]?["classifiers"]?[classifier] as JsonObject : null;
                var chosen = classArt ?? art;

                if (chosen != null)
                {
                    rel = chosen["path"]?.GetValue<string>();
                    url = chosen["url"]?.GetValue<string>();
                    size = chosen["size"]?.GetValue<long>() ?? 0;
                }
                else
                {
                    rel = PathFromName(name, classifier);
                }

                if (string.IsNullOrEmpty(rel)) continue;
                var full = Path.Combine(cfg.GameDir, "libraries", rel.Replace('/', Path.DirectorySeparatorChar));

                if (isNative) nativesJars.Add(full);
                else
                {
                    classpath.Add(full);
                    // 判断要用 artifact 的相对路径：NeoForge/Forge 的 json 里这一项的 name
                    // 不带 :client 分类器，只有 path 里写着 xxx-client.jar。
                    if (File.Exists(full)
                        && rel.EndsWith("-client.jar", StringComparison.OrdinalIgnoreCase)
                        && rel.StartsWith("net/neoforged/", StringComparison.OrdinalIgnoreCase))
                        hasPatchedClient = true;
                }

                if (!File.Exists(full))
                {
                    if (string.IsNullOrEmpty(url)) url = "https://libraries.minecraft.net/" + rel;
                    missing.Add(new MissingFile
                    {
                        Display = rel.Replace('/', '\\'),
                        Path = full,
                        Url = MinecraftDownloader.Mirror(url, source),
                        Size = size
                    });
                }
            }

            // NeoForge 的 version json 里干脆不列 client jar（BootstrapLauncher 自己去找），
            // 所以再按目录补一刀：classpath 里有 forge / neoforge 的库，
            // 且它旁边就躺着 xxx-client.jar，说明补丁客户端是自带的。
            if (!hasPatchedClient)
            {
                foreach (var cp in classpath)
                {
                    var dir = Path.GetDirectoryName(cp);
                    if (string.IsNullOrEmpty(dir)) continue;
                    if (dir.IndexOf("minecraftforge", StringComparison.OrdinalIgnoreCase) < 0
                        && dir.IndexOf("neoforged", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        if (Directory.GetFiles(dir, "*-client.jar").Length > 0) { hasPatchedClient = true; break; }
                    }
                    catch { }
                }
            }

            // NeoForge 更彻底：version json 里连自己的 jar 都不列，全靠 BootstrapLauncher 去找，
            // 所以只看 mainClass 是不是它，再去 libraries 下翻有没有 neoforge 的补丁客户端。
            if (!hasPatchedClient
                && (merged.MainClass ?? "").IndexOf("bootstraplauncher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var nfRoot = Path.Combine(cfg.GameDir, "libraries", "net", "neoforged", "neoforge");
                try
                {
                    if (Directory.Exists(nfRoot)
                        && Directory.GetFiles(nfRoot, "*-client.jar", SearchOption.AllDirectories).Length > 0)
                        hasPatchedClient = true;
                }
                catch { }
            }

            // 客户端 jar
            string jarId = !string.IsNullOrEmpty(merged.JarId) ? merged.JarId : version.Id;
            if (hasPatchedClient)
            {
                Log.Write($"skip vanilla jar ({jarId}.jar): patched client jar already on classpath");
            }
            else
            {
                Log.Write($"use jar: {jarId}.jar (hasPatchedClient={hasPatchedClient})");
                var clientJar = Path.Combine(cfg.GameDir, "versions", jarId, jarId + ".jar");
                if (!File.Exists(clientJar)) clientJar = Path.Combine(version.Dir, jarId + ".jar");
                if (!File.Exists(clientJar) && File.Exists(version.JarPath)) clientJar = version.JarPath;
                if (File.Exists(clientJar)) classpath.Add(clientJar);
                else
                {
                    var dl = merged.Root?["downloads"]?["client"] as JsonObject;
                    var clientUrl = dl?["url"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(clientUrl))
                        clientUrl = $"{MinecraftDownloader.SourceBase(source)}/version/{jarId}/client";
                    missing.Add(new MissingFile
                    {
                        Display = jarId + ".jar",
                        Path = clientJar,
                        Url = MinecraftDownloader.Mirror(clientUrl, source),
                        Size = dl?["size"]?.GetValue<long>() ?? 0
                    });
                }
            }

            // ---------- 解压 natives ----------
            // MC 26.x 的 natives jar 内部是 windows/x64/org/lwjgl/*.dll 这种带平台前缀的结构，
            // 而 java.library.path 指向 {natives}/java 且不递归查找 —— 必须把 dll 平铺出来，
            // 否则会报 "Failed to locate library: lwjgl.dll" 直接崩溃。
            bool isArm64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                           == System.Runtime.InteropServices.Architecture.Arm64;

            foreach (var jar in nativesJars)
            {
                if (!File.Exists(jar)) continue;
                var jn = Path.GetFileName(jar).ToLowerInvariant();
                if (!isArm64 && jn.Contains("arm64")) continue; // x64 机器上别让 arm64 的 dll 覆盖过来
                try
                {
                    using var zip = ZipFile.OpenRead(jar);
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        if (e.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;

                        // 原样解压一份（老版本 MC 的 library.path 直接指向 natives 根）
                        WriteNative(Path.Combine(nativesDir, e.FullName.Replace('/', Path.DirectorySeparatorChar)), e, false);

                        var ext = Path.GetExtension(e.Name).ToLowerInvariant();
                        if (ext is ".dll" or ".so" or ".dylib" or ".jnilib")
                        {
                            WriteNative(Path.Combine(nativesDir, e.Name), e, true);
                            WriteNative(Path.Combine(nativesDir, "java", e.Name), e, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    plan.Warnings.Add($"解压 native 失败：{Path.GetFileName(jar)} ({ex.Message})");
                }
            }

            // ---------- 资源索引 ----------
            var indexId = merged.AssetsIndex ?? "legacy";
            var indexPath = Path.Combine(assetsDir, "indexes", indexId + ".json");
            if (!File.Exists(indexPath))
            {
                plan.Warnings.Add($"缺少资源索引 {indexId}.json，游戏可能无法加载音效与语言");
                var idxNode0 = merged.Root?["assetIndex"] as JsonObject;
                var idxUrl = idxNode0?["url"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(idxUrl))
                    missing.Add(new MissingFile
                    {
                        Display = $"资源索引 {indexId}.json",
                        Path = indexPath,
                        Url = MinecraftDownloader.Mirror(idxUrl, source),
                        Size = idxNode0?["size"]?.GetValue<long>() ?? 0
                    });
            }
            else
            {
                // 校验资源对象（音效、贴图、语言文件）
                try
                {
                    var idxNode = JsonNode.Parse(File.ReadAllText(indexPath)) as JsonObject;
                    if (idxNode?["objects"] is JsonObject objs)
                    {
                        foreach (var kv in objs)
                        {
                            var hash = kv.Value?["hash"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(hash)) continue;
                            var objPath = Path.Combine(assetsDir, "objects", hash.Substring(0, 2), hash);
                            var objSize = kv.Value?["size"]?.GetValue<long>() ?? 0;
                            if (File.Exists(objPath) && (objSize <= 0 || new FileInfo(objPath).Length == objSize)) continue;
                            missing.Add(new MissingFile
                            {
                                Display = "资源 " + Path.GetFileName(kv.Key.Replace('/', '\\')),
                                Path = objPath,
                                Url = $"{MinecraftDownloader.AssetsBase(source)}/{hash.Substring(0, 2)}/{hash}",
                                Size = objSize
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    plan.Warnings.Add("资源索引解析失败：" + ex.Message);
                }
            }

            // Collect 阶段也可能登记缺失项（例如版本 json 本身不存在），这里一起合并，不要覆盖
            plan.MissingFiles = plan.MissingFiles
                .Concat(missing)
                .Where(m => !string.IsNullOrEmpty(m.Path) || !string.IsNullOrEmpty(m.Url))
                .DistinctBy(m => m.Path)
                .ToList();

            // ---------- 变量替换 ----------
            var acc = account ?? new Account { Name = "Steve", Uuid = Account.OfflineUuid("Steve") };
            var subs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["auth_player_name"] = acc.Name,
                ["version_name"] = version.Id,
                ["game_directory"] = plan.GameDir,
                ["assets_root"] = assetsDir,
                ["game_assets"] = Path.Combine(assetsDir, "virtual", indexId),
                ["assets_index_name"] = indexId,
                ["auth_uuid"] = acc.FormattedUuid,
                ["auth_access_token"] = acc.Type == "offline" ? "0" : (acc.AccessToken ?? "0"),
                ["auth_session"] = "",
                // MC 26.x 起 --clientId / --xuid 是必填参数，传空串会让游戏启动即崩
                ["clientid"] = string.IsNullOrWhiteSpace(cfg.ClientId) ? Guid.NewGuid().ToString("N") : cfg.ClientId,
                ["auth_xuid"] = acc.Type == "offline" ? "0" : (acc.Xuid ?? "0"),
                ["user_type"] = "legacy",
                ["user_properties"] = "{}",
                ["version_type"] = LauncherName,
                ["natives_directory"] = nativesDir,
                ["launcher_name"] = LauncherName,
                ["launcher_version"] = LauncherVersion,
                ["classpath"] = string.Join(";", classpath),
                ["resolution_width"] = cfg.WindowWidth.ToString(),
                ["resolution_height"] = cfg.WindowHeight.ToString(),
                ["quickPlayPath"] = "",
                ["quickPlaySingleplayer"] = "",
                ["quickPlayMultiplayer"] = quickPlayMultiplayer ?? "",
                ["quickPlayRealms"] = "",
                ["library_directory"] = Path.Combine(cfg.GameDir, "libraries"),
                ["classpath_separator"] = ";"
            };

            string Sub(string s) => Regex.Replace(s ?? "", @"\$\{([^}]+)\}", m =>
            {
                var key = m.Groups[1].Value;
                if (subs.TryGetValue(key, out var val)) return val;
                plan.Warnings.Add($"未知变量 ${{{key}}}");
                return "";
            });

            // ---------- JVM 参数 ----------
            var jvm = new List<string>();
            jvm.Add($"-Xms{cfg.MemoryMin}M");
            jvm.Add($"-Xmx{cfg.MemoryMax}M");

            if (merged.JvmArgs.Count == 0)
            {
                jvm.Add($"-Djava.library.path={nativesDir}");
                jvm.Add("-cp");
                jvm.Add(string.Join(";", classpath));
            }
            else
            {
                foreach (var node in merged.JvmArgs)
                    CollectArg(node, jvm, features, Sub);
            }

            if (!jvm.Any(a => a.StartsWith("-Djava.library.path", StringComparison.OrdinalIgnoreCase)))
                jvm.Insert(0, $"-Djava.library.path={nativesDir}");

            // JVM 默认 locale：游戏界面语言由 options.txt 决定，但这个能让
            // 文件对话框/报错等系统组件也是中文
            if (!jvm.Any(a => a.StartsWith("-Duser.language", StringComparison.OrdinalIgnoreCase)))
            {
                jvm.Add("-Duser.language=zh");
                jvm.Add("-Duser.country=CN");
            }

            if (!jvm.Any(a => a.StartsWith("-Dminecraft.launcher.brand", StringComparison.OrdinalIgnoreCase)))
                jvm.Add("-Dminecraft.launcher.brand=" + LauncherName);
            if (!jvm.Any(a => a.StartsWith("-Dminecraft.launcher.version", StringComparison.OrdinalIgnoreCase)))
                jvm.Add("-Dminecraft.launcher.version=" + LauncherVersion);

            foreach (var extra in SplitArgs(cfg.ExtraJvm))
                jvm.Add(extra);

            jvm.Add(merged.MainClass);

            // ---------- 游戏参数 ----------
            var game = new List<string>();
            if (merged.GameArgs.Count > 0)
            {
                foreach (var node in merged.GameArgs)
                    CollectArg(node, game, features, Sub);
            }
            else if (!string.IsNullOrEmpty(merged.LegacyArgs))
            {
                foreach (var a in SplitArgs(merged.LegacyArgs)) game.Add(Sub(a));
            }

            if (!game.Any(a => a == "--width"))
            {
                game.Add("--width");
                game.Add(cfg.WindowWidth.ToString());
                game.Add("--height");
                game.Add(cfg.WindowHeight.ToString());
            }

            plan.Args.AddRange(jvm);
            plan.Args.AddRange(game);
            return plan;
        }

        private static void CollectArg(JsonNode node, List<string> output, Dictionary<string, bool> features, Func<string, string> sub)
        {
            if (node == null) return;
            if (node is JsonValue)
            {
                var s = node.GetValue<string>();
                if (!string.IsNullOrEmpty(s)) output.Add(sub(s));
                return;
            }
            if (node is JsonArray arr)
            {
                foreach (var item in arr) CollectArg(item, output, features, sub);
                return;
            }
            var obj = node as JsonObject;
            if (obj == null) return;

            var rules = obj["rules"] as JsonArray;
            if (rules != null && !RulesAllow(rules, features)) return;

            var value = obj["value"];
            if (value != null) CollectArg(value, output, features, sub);
        }

        private static void WriteNative(string target, System.IO.Compression.ZipArchiveEntry e, bool keepFirst)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                {
                    // 同名 dll 可能来自 arm64 / x64 两份 jar，保留先解压的那份
                    if (keepFirst || new FileInfo(target).Length == e.Length) return;
                }
                e.ExtractToFile(target, true);
            }
            catch { }
        }

        public static bool RulesAllow(JsonArray rules, Dictionary<string, bool> features)
        {
            bool allow = false;
            bool matched = false;
            foreach (var r in rules)
            {
                var rule = r as JsonObject;
                if (rule == null) continue;
                if (!RuleMatches(rule, features)) continue;
                matched = true;
                allow = (rule["action"]?.GetValue<string>() ?? "allow") == "allow";
            }
            return matched && allow;
        }

        private static bool RuleMatches(JsonObject rule, Dictionary<string, bool> features)
        {
            var os = rule["os"] as JsonObject;
            if (os != null)
            {
                var name = os["name"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(name) && name != "windows") return false;

                var arch = os["arch"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(arch))
                {
                    bool is64 = Environment.Is64BitOperatingSystem;
                    if (arch == "x86" && is64) return false;
                    if ((arch == "x86_64" || arch == "amd64") && !is64) return false;
                    if (arch == "arm64") return false;
                }

                var ver = os["version"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(ver))
                {
                    try
                    {
                        var v = Environment.OSVersion.Version.ToString();
                        if (!Regex.IsMatch(v, ver)) return false;
                    }
                    catch { }
                }
            }

            var feat = rule["features"] as JsonObject;
            if (feat != null)
            {
                foreach (var kv in feat)
                {
                    bool want = kv.Value?.GetValue<bool>() ?? false;
                    bool have = features.TryGetValue(kv.Key, out var b) && b;
                    if (want != have) return false;
                }
            }
            return true;
        }

        /// <summary>比较两个库版本号，a 是否比 b 新（够用即可，不做完整的 Maven 版本语义）。</summary>
        public static bool VersionGreater(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return false;
            if (string.IsNullOrEmpty(b)) return true;
            if (a == b) return false;

            var pa = a.Split('.', '-', '+', '_');
            var pb = b.Split('.', '-', '+', '_');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                var x = i < pa.Length ? pa[i] : "0";
                var y = i < pb.Length ? pb[i] : "0";
                if (int.TryParse(x, out var xi) && int.TryParse(y, out var yi))
                {
                    if (xi != yi) return xi > yi;
                }
                else
                {
                    var cmp = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp > 0;
                }
            }
            return false;
        }

        public static string PathFromName(string name, string classifier = null)        {
            try
            {
                var ext = "jar";
                var at = name.IndexOf('@');
                if (at >= 0)
                {
                    ext = name.Substring(at + 1);
                    name = name.Substring(0, at);
                }
                var parts = name.Split(':');
                if (parts.Length < 3) return null;
                var group = parts[0];
                var artifact = parts[1];
                var ver = parts[2];
                var cls = classifier ?? (parts.Length > 3 ? parts[3] : null);
                var file = $"{artifact}-{ver}" + (string.IsNullOrEmpty(cls) ? "" : "-" + cls) + "." + ext;
                return $"{group.Replace('.', '/')}/{artifact}/{ver}/{file}";
            }
            catch { return null; }
        }

        public static IEnumerable<string> SplitArgs(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var sb = new StringBuilder();
            bool inQuote = false;
            foreach (var ch in text)
            {
                if (ch == '"') { inQuote = !inQuote; continue; }
                if (char.IsWhiteSpace(ch) && !inQuote)
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                    continue;
                }
                sb.Append(ch);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private static MergedVersion Collect(VersionEntry version, LaunchPlan plan)
        {
            var merged = new MergedVersion();
            var chain = new List<JsonObject>();
            var chainIds = new List<string>();

            string cur = version.Id;
            int guard = 0;
            while (!string.IsNullOrEmpty(cur) && guard++ < 16)
            {
                var path = Path.Combine(version.Dir, "..", cur, cur + ".json");
                var normalized = Path.GetFullPath(path);
                if (!File.Exists(normalized)) break;
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(normalized)) as JsonObject;
                    if (node == null) break;
                    chain.Add(node);
                    chainIds.Add(cur);
                    cur = node["inheritsFrom"]?.GetValue<string>();
                }
                catch { break; }
            }
            if (chain.Count == 0)
            {
                plan.MissingFiles.Add(new MissingFile { Display = version.Id + ".json", Path = "", Url = "" });
                merged.MainClass = "net.minecraft.client.main.Main";
                return merged;
            }

            merged.Root = chain[0];

            foreach (var o in chain)
            {
                if (merged.MainClass == null) merged.MainClass = o["mainClass"]?.GetValue<string>();
                if (merged.AssetsIndex == null)
                    merged.AssetsIndex = o["assetIndex"]?["id"]?.GetValue<string>() ?? o["assets"]?.GetValue<string>();
                if (merged.McVersion == null) merged.McVersion = o["clientVersion"]?.GetValue<string>();
                if (merged.Type == null) merged.Type = o["type"]?.GetValue<string>();
                if (merged.JavaMajor == 0) merged.JavaMajor = o["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? 0;
                if (merged.JarId == null) merged.JarId = o["jar"]?.GetValue<string>();
                if (merged.LegacyArgs == null) merged.LegacyArgs = o["minecraftArguments"]?.GetValue<string>();
            }

            // Fabric / Forge / NeoForge 的版本 json 一般不带 jar 字段，
            // 它们没有自己的客户端 jar，必须沿 inheritsFrom 链用最底层那个原版 jar。
            if (string.IsNullOrEmpty(merged.JarId))
                merged.JarId = chainIds.Count > 0 ? chainIds[chainIds.Count - 1] : version.Id;
            if (merged.JavaMajor == 0) merged.JavaMajor = 8;

            // 库：父在前，子覆盖同名；同一 group:artifact 只保留版本更高的（Fabric 会带新版 ASM 等）
            var libMap = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            var libOrder = new List<string>();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (!(chain[i]["libraries"] is JsonArray libs)) continue;
                foreach (var l in libs)
                {
                    var lo = l as JsonObject;
                    var name = lo?["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name)) continue;

                    var parts = name.Split('@')[0].Split(':');
                    if (parts.Length < 3) continue;
                    var key = parts[0] + ":" + parts[1] + (parts.Length > 3 ? ":" + parts[3] : "");
                    var libVersion = parts[2];

                    if (!libMap.TryGetValue(key, out var existing))
                    {
                        libMap[key] = lo;
                        libOrder.Add(key);
                    }
                    else
                    {
                        var existingVersion = existing["name"]?.GetValue<string>()?.Split('@')[0].Split(':')
                            .ElementAtOrDefault(2) ?? "0";
                        if (VersionGreater(libVersion, existingVersion)) libMap[key] = lo;
                    }
                }
            }
            foreach (var n in libOrder) merged.Libraries.Add(libMap[n]);

            // 参数：父在前
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i]["arguments"]?["jvm"] is JsonArray jvm) foreach (var a in jvm) merged.JvmArgs.Add(a?.DeepClone());
                if (chain[i]["arguments"]?["game"] is JsonArray game) foreach (var a in game) merged.GameArgs.Add(a?.DeepClone());
            }

            return merged;
        }
    }
}
