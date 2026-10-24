using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HHCL.Core
{
    public class VersionEntry
    {
        public string Id { get; set; } = "";
        public string Dir { get; set; } = "";
        public string JsonPath { get; set; } = "";
        public string JarPath { get; set; } = "";
        public string McVersion { get; set; } = "";
        public string Loader { get; set; } = "原版";
        public string Type { get; set; } = "release";
        public string ReleaseTime { get; set; } = "";
        public int JavaMajor { get; set; }
        public long JarSize { get; set; }
        public bool HasMods { get; set; }

        public string ModsDir => Path.Combine(Dir, "mods");
        public string PackDir => Path.Combine(Dir, "resourcepacks");

        public string SizeText => JarSize > 0 ? $"{JarSize / 1024.0 / 1024.0:0.#} MB" : "无 jar";
        public string TypeText => Type == "release" ? "正式版" : Type == "snapshot" ? "快照" : Type;
        public string JavaText => JavaMajor > 0 ? $"Java {JavaMajor}+" : "Java 8+";
        public string Initial => string.IsNullOrEmpty(Loader) || Loader == "原版" ? "M" : Loader.Substring(0, 1).ToUpper();
        public string SubLine => $"{McVersion} · {Loader} · {JavaText} · {SizeText}";
        public string ModCountText => HasMods ? "" : "无模组目录";

        /// <summary>
        /// 版本分组键：同一"版本族"归一组。
        /// 1.21.4 / 1.21.4-rc1 / 1.21.4-pre1 → 1.21.4；而 1.21 是另一组。
        /// </summary>
        public string GroupKey
        {
            get
            {
                var id = Id ?? "";
                var i = id.IndexOfAny(new[] { '-', ' ', '_' });
                var key = i > 0 ? id.Substring(0, i) : id;
                return string.IsNullOrEmpty(key) ? id : key;
            }
        }

        /// <summary>下拉里显示的文本：版本号 + 类型 + 加载器。</summary>
        public string DisplayName
        {
            get
            {
                var t = TypeText;
                var loader = string.IsNullOrEmpty(Loader) || Loader == "原版" ? "" : $"　{Loader}";
                return $"{Id}{(string.IsNullOrEmpty(t) || t == "正式版" ? "" : $"　{t}")}{loader}";
            }
        }

        public override string ToString() => Id;
    }

    /// <summary>判断一个游戏版本号属于正式版 / 测试版 / 快照。</summary>
    public static class GameVersionKind
    {
        public static string Of(string gameVersion)
        {
            var g = (gameVersion ?? "").Trim();
            if (g.Length == 0) return "";
            var low = g.ToLowerInvariant();
            if (low.Contains("-pre") || low.Contains("-rc") || low.Contains("beta"))
                return "测试版";
            if (g.Any(char.IsLetter)) return "快照";   // 24w14a 这种日期快照
            return "正式版";
        }

        /// <summary>
        /// 按 Modrinth 的 version_type 字段判断（release / beta / alpha），
        /// 这才是"这个模组版本本身"是正式版还是预发布版的依据。
        /// 之前只看游戏版本号，导致整合包 "15.00-beta.2" 被标成"正式版"。
        /// </summary>
        public static string OfVersionType(string versionType, string fallbackGameVersion)
        {
            var t = (versionType ?? "").Trim().ToLowerInvariant();
            switch (t)
            {
                case "release": return "正式版";
                case "beta": return "测试版";
                case "alpha": return "早期版";
                case "snapshot": return "快照";
            }
            return Of(fallbackGameVersion);
        }
    }

    public static class VersionScanner
    {
        public static List<VersionEntry> Scan(string gameDir)
        {
            var list = new List<VersionEntry>();
            try
            {
                var vdir = Path.Combine(gameDir, "versions");
                if (!Directory.Exists(vdir)) return list;

                foreach (var dir in Directory.GetDirectories(vdir))
                {
                    var id = Path.GetFileName(dir);
                    var json = Path.Combine(dir, id + ".json");
                    if (!File.Exists(json)) continue;

                    var entry = new VersionEntry { Id = id, Dir = dir, JsonPath = json };
                    var jar = Path.Combine(dir, id + ".jar");
                    entry.JarPath = File.Exists(jar) ? jar : "";

                    try
                    {
                        var node = JsonNode.Parse(File.ReadAllText(json)) as JsonObject;
                        if (node != null)
                        {
                            entry.McVersion = node["clientVersion"]?.GetValue<string>() ?? "";
                            entry.Type = node["type"]?.GetValue<string>() ?? "release";
                            entry.JavaMajor = node["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? 0;
                            var rt = node["releaseTime"]?.GetValue<string>();
                            if (!string.IsNullOrEmpty(rt) && DateTime.TryParse(rt, out var dt))
                                entry.ReleaseTime = dt.ToString("yyyy-MM-dd");

                            var inherited = node["inheritsFrom"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(entry.McVersion) && !string.IsNullOrEmpty(inherited))
                                entry.McVersion = inherited;

                            entry.Loader = DetectLoader(id, node);
                            if (string.IsNullOrEmpty(entry.McVersion))
                                entry.McVersion = GuessMcVersion(id) ?? "?";
                            if (entry.JavaMajor <= 0) entry.JavaMajor = GuessJavaMajor(entry.McVersion);
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(entry.McVersion)) entry.McVersion = GuessMcVersion(id) ?? "?";
                    if (entry.Loader == "原版" && entry.Id.Contains("fabric", StringComparison.OrdinalIgnoreCase)) entry.Loader = "Fabric";
                    entry.HasMods = Directory.Exists(entry.ModsDir);

                    try { if (File.Exists(jar)) entry.JarSize = new FileInfo(jar).Length; } catch { }

                    list.Add(entry);
                }
            }
            catch { }
            return list.OrderByDescending(v => v.ReleaseTime).ThenByDescending(v => v.Id).ToList();
        }

        private static string DetectLoader(string id, JsonObject node)
        {
            var text = id.ToLowerInvariant() + " " + (node["mainClass"]?.GetValue<string>() ?? "").ToLowerInvariant();
            var libs = node["libraries"] as JsonArray;
            var libNames = new StringBuilder();
            if (libs != null)
            {
                foreach (var l in libs)
                {
                    var n = l?["name"]?.GetValue<string>();
                    if (n != null) libNames.Append(n.ToLowerInvariant()).Append(' ');
                }
            }
            var all = text + " " + libNames.ToString();

            if (all.Contains("neoforge")) return "NeoForge";
            if (all.Contains("fabric")) return "Fabric";
            if (all.Contains("quilt")) return "Quilt";
            if (all.Contains("forge")) return "Forge";
            if (all.Contains("optifine")) return "OptiFine";
            if (all.Contains("liteloader")) return "LiteLoader";
            return "原版";
        }

        private static string GuessMcVersion(string id)
        {
            // 老格式 1.21.4，新版（25wxx / 26.3 这种）是 两位年份.序号，都要能认出来
            var m = Regex.Match(id, @"\b(\d+\.\d+(?:\.\d+)?)\b");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static int GuessJavaMajor(string mc)
        {
            try
            {
                if (string.IsNullOrEmpty(mc) || !mc.StartsWith("1.")) return 8;
                var parts = mc.Split('.');
                int minor = int.Parse(parts[1]);
                int patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                if (minor >= 21) return 21;
                if (minor == 20 && patch >= 5) return 21;
                if (minor >= 18) return 17;
                if (minor == 17) return 16;
                return 8;
            }
            catch { return 8; }
        }
    }
}
