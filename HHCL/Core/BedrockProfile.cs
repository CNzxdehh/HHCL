using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace HHCL.Core
{
    /// <summary>一套基岩版存档方案（独立的数据目录）。</summary>
    public class BedrockProfile
    {
        public string Name { get; set; } = "";
        public long Created { get; set; }
        public long LastUsed { get; set; }

        [JsonIgnore]
        public string Display => Name;
    }

    /// <summary>
    /// 基岩版"配置"管理：每套配置一个独立目录，启动前把配置应用到游戏数据目录，
    /// 游戏退出后再把改动收回配置里。这样多套存档互不干扰。
    ///
    /// 数据目录有两种布局（2025 年起微软把基岩版从 UWP 换成了 GDK，路径变了）：
    ///   GDK 新版(1.21.120+)：%APPDATA%\Minecraft Bedrock\Users\[用户ID]\games\com.mojang
    ///   UWP 老版：%LOCALAPPDATA%\Packages\Microsoft.MinecraftUWP_8wekyb3d8bbwe\LocalState\games\com.mojang
    /// </summary>
    public static class BedrockProfiles
    {
        public const string PackageFamilyName = BedrockInstaller.PackageFamilyName;

        /// <summary>所有配置的根目录。</summary>
        public static string Root => Path.Combine(Config.DataDir, "bedrock_profiles");

        // ---------------- 目录定位 ----------------

        /// <summary>
        /// 找到当前这套基岩版实际使用的 com.mojang 数据目录（找不到就按 GDK 新版建）。
        /// xuid 用于拼 GDK 的 Users\&lt;数字ID&gt; 目录；传空则退化到 shared。
        /// </summary>
        public static string FindGameDataDir(out string layout, string xuid = null)
        {
            // 1) GDK 新版：%APPDATA%\Minecraft Bedrock\Users\<数字ID>\games\com.mojang
            var gdkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Bedrock");
            var users = Path.Combine(gdkRoot, "Users");
            if (Directory.Exists(users))
            {
                // 优先用账号对应的 XUID 目录，其次任意数字 ID 目录，最后 shared
                if (!string.IsNullOrEmpty(xuid))
                {
                    var mine = Path.Combine(users, xuid, "games", "com.mojang");
                    if (Directory.Exists(mine)) { layout = "GDK"; return mine; }
                }
                var hit = Directory.GetDirectories(users)
                    .Where(d => !string.Equals(Path.GetFileName(d), "shared", StringComparison.OrdinalIgnoreCase))
                    .Select(d => Path.Combine(d, "games", "com.mojang"))
                    .FirstOrDefault(Directory.Exists);
                if (hit != null) { layout = "GDK"; return hit; }
                var shared = Path.Combine(users, "shared", "games", "com.mojang");
                if (Directory.Exists(shared)) { layout = "GDK"; return shared; }
            }
            if (Directory.Exists(gdkRoot))
            {
                layout = "GDK";
                return Path.Combine(users, string.IsNullOrEmpty(xuid) ? "shared" : xuid, "games", "com.mojang");
            }

            // 2) UWP 老版
            var uwp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", PackageFamilyName, "LocalState", "games", "com.mojang");
            if (Directory.Exists(uwp)) { layout = "UWP"; return uwp; }

            layout = "GDK";
            return Path.Combine(users, string.IsNullOrEmpty(xuid) ? "shared" : xuid, "games", "com.mojang");
        }

        public static string FindGameDataDir(out string layout) => FindGameDataDir(out layout, null);

        /// <summary>某套配置自己的目录。</summary>
        public static string ProfileDir(BedrockProfile p) =>
            Path.Combine(Root, Sanitize(p.Name));

        private static string Sanitize(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string((name ?? "").Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrEmpty(s) ? "默认" : s;
        }

        // ---------------- 配置的增删改 ----------------

        public static List<BedrockProfile> Load()
        {
            var list = AppState.Current.Config.BedrockProfiles ?? new List<BedrockProfile>();
            if (list.Count == 0)
            {
                list.Add(new BedrockProfile { Name = "默认", Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                AppState.Current.Config.BedrockProfiles = list;
            }
            return list;
        }

        public static void Save(List<BedrockProfile> list)
        {
            AppState.Current.Config.BedrockProfiles = list;
            AppState.Current.Save();
        }

        public static BedrockProfile Create(string name)
        {
            var list = Load();
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name)) name = "配置 " + (list.Count + 1);
            var p = new BedrockProfile
            {
                Name = name,
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            list.Add(p);
            Save(list);
            Directory.CreateDirectory(ProfileDir(p));
            return p;
        }

        public static void Delete(BedrockProfile p)
        {
            var list = Load();
            list.RemoveAll(x => x.Name == p.Name);
            if (list.Count == 0)
                list.Add(new BedrockProfile { Name = "默认", Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            Save(list);
            try { if (Directory.Exists(ProfileDir(p))) Directory.Delete(ProfileDir(p), true); }
            catch (Exception ex) { Log.Write("delete bedrock profile: " + ex.Message); }
        }

        public static void Rename(BedrockProfile p, string newName)
        {
            newName = (newName ?? "").Trim();
            if (string.IsNullOrEmpty(newName) || newName == p.Name) return;
            var old = ProfileDir(p);
            var list = Load();
            if (list.Any(x => x.Name == newName)) return;
            var target = Path.Combine(Root, Sanitize(newName));
            try { if (Directory.Exists(old)) Directory.Move(old, target); } catch { }
            p.Name = newName;
            Save(list);
        }

        // ---------------- 应用 / 回收 ----------------

        /// <summary>把某套配置应用到游戏数据目录（配置 → 游戏）。</summary>
        public static (bool ok, string message) Apply(BedrockProfile p, string xuid = null)
        {
            var gameDir = FindGameDataDir(out var layout, xuid);
            var src = ProfileDir(p);
            try
            {
                Directory.CreateDirectory(gameDir);
                SyncDir(src, gameDir, deleteExtra: true);
                p.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Save(Load());
                return (true, $"已应用配置「{p.Name}」（{layout} 布局）");
            }
            catch (Exception ex)
            {
                Log.Write("apply bedrock profile: " + ex);
                return (false, "应用配置失败：" + ex.Message);
            }
        }

        /// <summary>游戏退出后，把游戏目录里的改动收回配置（游戏 → 配置）。</summary>
        public static (bool ok, string message) Reclaim(BedrockProfile p, string xuid = null)
        {
            var gameDir = FindGameDataDir(out _, xuid);
            var dst = ProfileDir(p);
            try
            {
                Directory.CreateDirectory(dst);
                SyncDir(gameDir, dst, deleteExtra: true);
                p.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Save(Load());
                return (true, $"已回收配置「{p.Name}」");
            }
            catch (Exception ex)
            {
                Log.Write("reclaim bedrock profile: " + ex);
                return (false, "回收配置失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 目录增量同步：只复制新增/变化的文件（按大小+修改时间判断），可选删掉目标里多余的文件。
        /// 存档动辄几 GB，全量复制会很慢，增量才有可用性。
        /// </summary>
        public static void SyncDir(string src, string dst, bool deleteExtra)
        {
            if (!Directory.Exists(src))
            {
                Directory.CreateDirectory(dst);
                return;
            }
            Directory.CreateDirectory(dst);

            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel;
                try { rel = Path.GetRelativePath(src, file); } catch { continue; }
                var target = Path.Combine(dst, rel);
                try
                {
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    var fi = new FileInfo(file);
                    if (File.Exists(target))
                    {
                        var ti = new FileInfo(target);
                        if (ti.Length == fi.Length && ti.LastWriteTimeUtc == fi.LastWriteTimeUtc)
                            continue;
                        // 游戏没关的时候文件可能被占用，删不掉就直接覆盖
                        try { File.Copy(file, target, true); }
                        catch (IOException) { File.Copy(file, target, true); }
                    }
                    else
                    {
                        File.Copy(file, target, true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"sync file fail {rel}: {ex.Message}");
                }
            }

            if (!deleteExtra) return;
            foreach (var file in Directory.EnumerateFiles(dst, "*", SearchOption.AllDirectories))
            {
                string rel;
                try { rel = Path.GetRelativePath(dst, file); } catch { continue; }
                if (File.Exists(Path.Combine(src, rel))) continue;
                try { File.Delete(file); } catch { }
            }
            // 清理空目录
            try
            {
                foreach (var d in Directory.GetDirectories(dst, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(x => x.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                }
            }
            catch { }
        }

        // ---------------- 统计 ----------------

        public static int CountWorlds(BedrockProfile p)
        {
            try
            {
                var w = Path.Combine(ProfileDir(p), "minecraftWorlds");
                return Directory.Exists(w) ? Directory.GetDirectories(w).Length : 0;
            }
            catch { return 0; }
        }

        public static long SizeOf(BedrockProfile p)
        {
            try
            {
                var d = ProfileDir(p);
                if (!Directory.Exists(d)) return 0;
                return Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f =>
                {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                });
            }
            catch { return 0; }
        }
    }
}
