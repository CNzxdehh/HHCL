using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>
    /// 基岩版 addon（.mcpack / .mcaddon）安装器。
    ///
    /// 基岩版的包结构：
    ///   .mcpack  = 一个包（行为包或资源包），本质是 zip，根目录有 manifest.json
    ///   .mcaddon = 多个 .mcpack（可能还带一个世界）打包在一起的 zip
    /// 装好后要进世界设置里勾选才会生效（这一步只能在游戏里做）。
    ///
    /// 落盘位置在 com.mojang 下：
    ///   behavior_packs/ 行为包   resource_packs/ 资源包   worlds/ 世界
    /// </summary>
    public static class BedrockAddonInstaller
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        public class Result
        {
            public bool Ok;
            public string Message = "";
            public List<string> Installed = new List<string>();
        }

        /// <summary>下载并安装一个 CF 上的基岩版 addon 文件。</summary>
        public static async Task<Result> InstallAsync(CfFile file, string apiKey, string dataDir,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            var res = new Result();
            var tmp = Path.Combine(Path.GetTempPath(), "hhcl-bedrock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                // 1) 下载
                var url = CurseForgeBedrock.ResolveDownloadUrl(file, apiKey);
                var downloadPath = Path.Combine(tmp, file.FileName);
                progress?.Report(new DlProgress { Stage = "下载 addon", Current = file.FileName, Done = 0, Total = 1, TotalBytes = file.Size });
                await DownloadAsync(url, downloadPath, file.Size, progress, ct);
                progress?.Report(new DlProgress { Stage = "下载 addon", Done = 1, Total = 1, DoneBytes = file.Size, TotalBytes = file.Size });

                // 2) 解包并按类型落到正确目录
                Directory.CreateDirectory(dataDir);
                var unpack = Path.Combine(tmp, "unpack");
                Directory.CreateDirectory(unpack);
                await ExtractAndPlaceAsync(downloadPath, file.FileName, dataDir, res, progress, ct);
                return res;
            }
            catch (Exception ex)
            {
                res.Message = ex.Message;
                return res;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        /// <summary>
        /// 直接从本地文件安装 addon（.mcaddon / .mcpack / .zip）。
        /// 不需要任何 API Key——从哪儿下的包都能直接装进基岩版。
        /// </summary>
        public static async Task<Result> InstallLocalAsync(string filePath, string dataDir,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            var res = new Result();
            try
            {
                if (!File.Exists(filePath)) { res.Message = "文件不存在：" + filePath; return res; }
                Directory.CreateDirectory(dataDir);
                var tmp = Path.Combine(Path.GetTempPath(), "hhcl-bedrock-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                try
                {
                    progress?.Report(new DlProgress { Stage = "安装 addon", Current = Path.GetFileName(filePath) });
                    await ExtractAndPlaceAsync(filePath, Path.GetFileName(filePath), dataDir, res, progress, ct);
                }
                finally
                {
                    try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                }
                return res;
            }
            catch (Exception ex)
            {
                res.Message = ex.Message;
                return res;
            }
        }

        /// <summary>解压 + 按类型归位（下载与本地安装共用）。</summary>
        private static async Task ExtractAndPlaceAsync(string archivePath, string fileName, string dataDir,
            Result res, IProgress<DlProgress> progress, CancellationToken ct)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "hhcl-bedrock-" + Guid.NewGuid().ToString("N"));
            var unpack = Path.Combine(tmp, "unpack");
            Directory.CreateDirectory(unpack);
            try
            {
                var lower = (fileName ?? "").ToLowerInvariant();
                if (lower.EndsWith(".mcpack") || lower.EndsWith(".mcaddon") || lower.EndsWith(".zip"))
                {
                    try
                    {
                        ZipFile.ExtractToDirectory(archivePath, unpack, true);
                    }
                    catch (InvalidDataException)
                    {
                        res.Message = "这个文件不是有效的压缩包（.mcaddon/.mcpack 应该是 zip 格式）。";
                        return;
                    }
                    await PlaceAsync(unpack, dataDir, res, progress, ct);
                }
                else
                {
                    // 其它格式直接丢进 addons 目录（老版本基岩版的通用位置）
                    var dir = Path.Combine(dataDir, "addons");
                    Directory.CreateDirectory(dir);
                    File.Copy(archivePath, Path.Combine(dir, Path.GetFileName(archivePath)), true);
                    res.Installed.Add(Path.GetFileName(archivePath));
                }
                res.Ok = res.Installed.Count > 0;
                if (!res.Ok && string.IsNullOrEmpty(res.Message))
                    res.Message = "包里没找到可安装的内容（既没有 manifest.json 也不是有效的包）。";
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        /// <summary>把解出来的内容按 manifest 类型分门别类放进对应目录。</summary>
        private static async Task PlaceAsync(string unpackDir, string dataDir, Result res,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            // 1) 本目录自己就是个包（有 manifest.json）
            var selfManifest = Path.Combine(unpackDir, "manifest.json");
            if (File.Exists(selfManifest))
                InstallPack(selfManifest, unpackDir, dataDir, res, progress);

            // 2) 嵌套的 .mcpack/.mcaddon —— 解压到 unpackDir **外面**，
            //    这样外层扫描不会把它们再扫一遍（否则会重复安装）
            var parent = Path.GetDirectoryName(unpackDir);
            foreach (var nested in Directory.GetFiles(unpackDir, "*", SearchOption.AllDirectories)
                                           .Where(f => f.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase)
                                                    || f.EndsWith(".mcaddon", StringComparison.OrdinalIgnoreCase)))
            {
                ct.ThrowIfCancellationRequested();
                var sub = Path.Combine(parent ?? unpackDir, "sub-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    Directory.CreateDirectory(sub);
                    ZipFile.ExtractToDirectory(nested, sub, true);
                    await PlaceAsync(sub, dataDir, res, progress, ct);
                }
                catch (Exception ex) { Log.Write("bedrock addon nested fail: " + ex.Message); }
            }

            // 3) 子目录里的包
            foreach (var dir in Directory.GetDirectories(unpackDir))
            {
                ct.ThrowIfCancellationRequested();
                var m = Path.Combine(dir, "manifest.json");
                if (File.Exists(m)) InstallPack(m, dir, dataDir, res, progress);
            }

            // 4) 世界（.mcaddon 常带一个存档）
            foreach (var levelDat in Directory.GetFiles(unpackDir, "level.dat", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var worldDir = Path.GetDirectoryName(levelDat);
                if (worldDir == null) continue;
                var destRoot = Path.Combine(dataDir, "worlds");
                Directory.CreateDirectory(destRoot);
                // 优先用 levelname.txt（玩家在世界里看到的名字），拿不到再用目录名
                var name = ReadWorldName(worldDir) ?? Path.GetFileName(worldDir);
                if (string.IsNullOrEmpty(name) || name == "unpack" || name.StartsWith("sub-"))
                    name = "addon-world";
                var dest = Path.Combine(destRoot, name);
                try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { }
                CopyDir(worldDir, dest);
                res.Installed.Add($"worlds/{name}");
            }
        }

        /// <summary>按 manifest 的模块类型把一个包目录装进 behavior_packs / resource_packs。</summary>
        private static void InstallPack(string manifestPath, string packDir, string dataDir,
            Result res, IProgress<DlProgress> progress)
        {
            var type = ReadPackType(manifestPath);   // data=行为包 resource=资源包
            var folder = type == "data" ? "behavior_packs" : "resource_packs";
            var destRoot = Path.Combine(dataDir, folder);
            Directory.CreateDirectory(destRoot);

            // 名字优先取 manifest 里的 header.name（游戏里显示的就是它）
            var name = ReadPackName(manifestPath);
            if (string.IsNullOrEmpty(name)) name = Path.GetFileName(packDir);
            if (string.IsNullOrEmpty(name) || name.StartsWith("sub-")) name = "addon";
            var dest = Path.Combine(destRoot, name);

            try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { }
            CopyDir(packDir, dest);
            res.Installed.Add($"{folder}/{name}");
            progress?.Report(new DlProgress { Stage = "安装", Current = name, Done = res.Installed.Count, Total = res.Installed.Count });
        }

        /// <summary>读 manifest.json 的 modules[].type：data=行为包，resource=资源包。</summary>
        private static string ReadPackType(string manifestPath)
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
                var mods = node?["modules"] as JsonArray;
                if (mods != null)
                {
                    foreach (var m in mods)
                    {
                        var t = m?["type"]?.GetValue<string>() ?? "";
                        if (t == "data" || t == "script") return "data";
                        if (t == "resource") return "resource";
                    }
                }
            }
            catch { }
            return "resource";
        }

        private static string ReadPackName(string manifestPath)
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
                // 名字在 header.name 里（不是顶层 name）
                var n = node?["header"]?["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(n)) n = node?["name"]?.GetValue<string>();
                return string.IsNullOrEmpty(n) ? null : n;
            }
            catch { return null; }
        }

        private static string ReadWorldName(string worldDir)
        {
            try
            {
                var f = Path.Combine(worldDir, "levelname.txt");
                if (File.Exists(f))
                {
                    var t = File.ReadAllText(f).Trim();
                    if (t.Length > 0) return t;
                }
            }
            catch { }
            return null;
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
            }
        }

        private static async Task DownloadAsync(string url, string dest, long expected,
            IProgress<DlProgress> progress, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", AppInfo.UserAgent);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = expected > 0 ? expected : (resp.Content.Headers.ContentLength ?? 0);
            long got = 0;
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[256 * 1024];
                while (true)
                {
                    var readTask = stream.ReadAsync(buf.AsMemory(), ct).AsTask();
                    var watchdog = Task.Delay(TimeSpan.FromSeconds(90), ct);
                    if (await Task.WhenAny(readTask, watchdog) != readTask)
                        throw new Exception("下载卡住超过 90 秒");
                    int n = await readTask;
                    if (n <= 0) break;
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    got += n;
                    progress?.Report(new DlProgress
                    {
                        Stage = "下载 addon",
                        DoneBytes = got,
                        TotalBytes = total
                    });
                }
            }
        }
    }
}
