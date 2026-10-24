using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    public class RemoteVersion
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "release";
        public string Url { get; set; } = "";
        public string ReleaseTime { get; set; } = "";
        public string TypeText => Type == "release" ? "正式版" : Type == "snapshot" ? "快照" : "旧版";
        public string SubLine => $"{TypeText} · {ReleaseTime}";
        public string Initial => "M";
    }

    public class DlProgress
    {
        public string Stage { get; set; } = "";
        public string Current { get; set; } = "";
        public int Done { get; set; }
        public int Total { get; set; }
        public long DoneBytes { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedMBps { get; set; }
        public double Percent => Total > 0 ? Math.Min(100, Done * 100.0 / Total) : 0;

        public string SizeText
        {
            get
            {
                string Fmt(long b) => b >= 1073741824 ? $"{b / 1073741824.0:0.00} GB" : b >= 1048576 ? $"{b / 1048576.0:0.0} MB" : $"{b / 1024.0:0.0} KB";
                return $"{Fmt(DoneBytes)} / {Fmt(TotalBytes)}";
            }
        }

        public string SpeedText => SpeedMBps > 0 ? $"{SpeedMBps:0.0} MB/s" : "";
    }

    public static class MinecraftDownloader
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 64
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            return c;
        }

        public static string SourceBase(string source) =>
            source == "mojang" ? "https://piston-meta.mojang.com" : "https://bmclapi2.bangbang93.com";

        /// <summary>资源对象（assets/objects）的下载基地址，和元数据不是同一个域名。</summary>
        public static string AssetsBase(string source) =>
            source == "mojang" ? "https://resources.download.minecraft.net" : "https://bmclapi2.bangbang93.com/assets";

        /// <summary>最近一次成功获取版本列表所用的源（镜像不通时会自动回退到官方）。</summary>
        public static string LastUsedSource { get; private set; } = "bmclapi";

        /// <summary>GET 一个文本资源：强制 HTTP/1.1 + 多次重试，绕开部分 CDN 的握手重置。</summary>
        /// <summary>同一文件的备用地址（官方源 ↔ BMCLAPI 镜像互切），用于重试。</summary>
        /// <summary>接口类请求的备用源：BMCLAPI 主域名互切，其余走 AltMirror。</summary>
        public static string AltApi(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (url.Contains("bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase))
                return url.Replace("bmclapi2.bangbang93.com", "bmclapi.bangbang93.com");
            if (url.Contains("bmclapi.bangbang93.com", StringComparison.OrdinalIgnoreCase))
                return url.Replace("bmclapi.bangbang93.com", "bmclapi2.bangbang93.com");
            return AltMirror(url);
        }

        public static string AltMirror(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var pairs = new (string a, string b)[]
            {
                ("https://meta.fabricmc.net/", "https://bmclapi2.bangbang93.com/fabric-meta/"),
                ("https://maven.fabricmc.net/", "https://bmclapi2.bangbang93.com/maven/"),
                ("https://maven.quiltmc.org/", "https://bmclapi2.bangbang93.com/maven/"),
                ("https://maven.minecraftforge.net/", "https://bmclapi2.bangbang93.com/maven/"),
                ("https://maven.neoforged.net/releases/", "https://bmclapi2.bangbang93.com/maven/"),
                ("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/"),
                ("https://resources.download.minecraft.net/", "https://bmclapi2.bangbang93.com/assets/"),
                ("https://piston-meta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
                ("https://launcher.mojang.com/", "https://bmclapi2.bangbang93.com/")
            };
            foreach (var (a, b) in pairs)
            {
                if (url.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return b + url.Substring(a.Length);
                if (url.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return a + url.Substring(b.Length);
            }
            return null;
        }

        /// <summary>把镜像地址还原成官方地址，用于镜像挂掉时兜底。</summary>
        public static string Unmirror(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return url
                .Replace("https://bmclapi2.bangbang93.com/maven/", "https://libraries.minecraft.net/")
                .Replace("https://bmclapi.bangbang93.com/maven/", "https://libraries.minecraft.net/")
                .Replace("https://bmclapi2.bangbang93.com/fabric-meta/", "https://meta.fabricmc.net/")
                .Replace("https://bmclapi.bangbang93.com/fabric-meta/", "https://meta.fabricmc.net/")
                .Replace("https://bmclapi2.bangbang93.com/assets/", "https://resources.download.minecraft.net/")
                .Replace("https://bmclapi.bangbang93.com/assets/", "https://resources.download.minecraft.net/")
                .Replace("https://bmclapi2.bangbang93.com/", "https://piston-meta.mojang.com/")
                .Replace("https://bmclapi.bangbang93.com/", "https://piston-meta.mojang.com/");
        }

        /// <summary>
        /// 取文本。候选地址按「首选 → 另一个镜像 → 官方 → 官方的镜像」轮换，
        /// 这样 BMCLAPI 抽风（SSL 被拒 / 302）时会自动退回 Mojang 官方。
        /// </summary>
        public static async Task<string> GetTextAsync(string url, CancellationToken ct, int attempts = 6, string altUrl = null)
        {
            var cands = new List<string>();
            void Add(string u)
            {
                if (!string.IsNullOrEmpty(u) && !cands.Any(c => string.Equals(c, u, StringComparison.OrdinalIgnoreCase)))
                    cands.Add(u);
            }
            Add(url);
            Add(altUrl ?? Unmirror(url));
            var official = Unmirror(url);
            Add(official);
            Add(AltMirror(official));
            if (cands.Count == 0) throw new Exception("请求地址为空");

            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                var useUrl = cands[i % cands.Count];
                // 有的 CDN 卡 HTTP/1.1、有的卡 HTTP/2，轮换着试（BMCLAPI 属于前者，Fabric meta 属于后者）
                var proto = (i % 2 == 0) ? HttpVersion.Version11 : HttpVersion.Version20;
                try
                {
                    using var perTry = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    perTry.CancelAfter(TimeSpan.FromSeconds(15));
                    using var req = new HttpRequestMessage(HttpMethod.Get, useUrl)
                    {
                        Version = proto,
                        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                    };
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, perTry.Token);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync(perTry.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    last = new Exception("请求超时");
                    Log.Write($"GET timeout ({i + 1}/{attempts}) {proto} {useUrl}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Write($"GET fail ({i + 1}/{attempts}) {useUrl} :: {ex.Message}");
                }
                if (i < attempts - 1) await Task.Delay(350 * (i + 1), ct);
            }
            throw last ?? new Exception("请求失败");
        }

        public static string Mirror(string url, string source)
        {
            if (string.IsNullOrEmpty(url) || source == "mojang") return url;
            return url
                .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
                .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com")
                .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com")
                .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven")
                .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets");
        }

        public static async Task<List<RemoteVersion>> GetManifestAsync(string source, CancellationToken ct)
        {
            var order = source == "mojang"
                ? new[] { "mojang", "bmclapi" }
                : new[] { "bmclapi", "mojang" };

            Exception last = null;
            foreach (var src in order)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var url = SourceBase(src) + "/mc/game/version_manifest_v2.json";
                        var text = await GetTextAsync(url, ct, 1);
                        var node = JsonNode.Parse(text) as JsonObject;
                        var list = new List<RemoteVersion>();
                        if (node?["versions"] is JsonArray arr)
                        {
                            foreach (var v in arr)
                            {
                                var o = v as JsonObject;
                                if (o == null) continue;
                                list.Add(new RemoteVersion
                                {
                                    Id = o["id"]?.GetValue<string>() ?? "",
                                    Type = o["type"]?.GetValue<string>() ?? "release",
                                    Url = o["url"]?.GetValue<string>() ?? "",
                                    ReleaseTime = (o["releaseTime"]?.GetValue<string>() ?? "").Split('T')[0]
                                });
                            }
                        }
                        if (list.Count == 0) throw new Exception("版本列表为空");
                        LastUsedSource = src;
                        Log.Write($"manifest ok via {src}, {list.Count} versions");
                        return list;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        last = ex;
                        Log.Write($"manifest fail via {src} ({attempt + 1}): {ex.Message}");
                        await Task.Delay(400, ct);
                    }
                }
            }
            throw last ?? new Exception("无法获取版本列表");
        }

        private class Task_ { public string Url; public string Path; public long Size; public string AltUrl; }

        public static Task DownloadFilesAsync(IEnumerable<(string Url, string Path, long Size)> files,
            IProgress<DlProgress> progress, string stage, CancellationToken ct)
        {
            var list = files.Where(f => !string.IsNullOrEmpty(f.Url) && !string.IsNullOrEmpty(f.Path))
                .Select(f => new Task_ { Url = f.Url, Path = f.Path, Size = f.Size, AltUrl = AltMirror(f.Url) }).ToList();
            return DownloadAll(list, progress, string.IsNullOrEmpty(stage) ? "补齐文件" : stage, 0, ct);
        }

        public static async Task InstallAsync(string gameDir, string source, RemoteVersion rv, IProgress<DlProgress> progress, CancellationToken ct, bool includeAssets = true)
        {
            var st = new Stopwatch();
            var tasks = new List<Task_>();
            Directory.CreateDirectory(Path.Combine(gameDir, "versions", rv.Id));

            progress?.Report(new DlProgress { Stage = "获取版本信息", Current = rv.Id });
            var jsonUrl = Mirror(rv.Url, source);
            var jsonText = await GetTextAsync(jsonUrl, ct);
            var versionDir = Path.Combine(gameDir, "versions", rv.Id);
            var jsonPath = Path.Combine(versionDir, rv.Id + ".json");
            File.WriteAllText(jsonPath, jsonText);

            var node = JsonNode.Parse(jsonText) as JsonObject;
            if (node == null) throw new Exception("版本 JSON 解析失败");

            // 客户端 jar
            var client = node["downloads"]?["client"] as JsonObject;
            if (client != null)
            {
                tasks.Add(new Task_
                {
                    Url = Mirror(client["url"]?.GetValue<string>(), source),
                    Path = Path.Combine(versionDir, rv.Id + ".jar"),
                    Size = client["size"]?.GetValue<long>() ?? 0,
                    AltUrl = AltMirror(Mirror(client["url"]?.GetValue<string>(), source))
                });
            }

            // 库文件
            var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (node["libraries"] is JsonArray libs)
            {
                foreach (var l in libs)
                {
                    var lo = l as JsonObject;
                    if (lo == null) continue;
                    var rules = lo["rules"] as JsonArray;
                    if (rules != null && !LaunchBuilder.RulesAllow(rules, features)) continue;

                    var art = lo["downloads"]?["artifact"] as JsonObject;
                    var name = lo["name"]?.GetValue<string>() ?? "";
                    string rel, url; long size = 0;
                    if (art != null)
                    {
                        rel = art["path"]?.GetValue<string>();
                        url = Mirror(art["url"]?.GetValue<string>(), source);
                        size = art["size"]?.GetValue<long>() ?? 0;
                    }
                    else
                    {
                        rel = LaunchBuilder.PathFromName(name);
                        url = Mirror("https://libraries.minecraft.net/" + rel, source);
                    }
                    if (string.IsNullOrEmpty(rel)) continue;
                    tasks.Add(new Task_
                    {
                        Url = url,
                        Path = Path.Combine(gameDir, "libraries", rel.Replace('/', Path.DirectorySeparatorChar)),
                        Size = size,
                        AltUrl = AltMirror(url)
                    });
                }
            }

            // 资源索引
            var idx = node["assetIndex"] as JsonObject;
            string indexId = idx?["id"]?.GetValue<string>() ?? "legacy";
            if (idx != null)
            {
                tasks.Add(new Task_
                {
                    Url = Mirror(idx["url"]?.GetValue<string>(), source),
                    Path = Path.Combine(gameDir, "assets", "indexes", indexId + ".json"),
                    Size = idx["size"]?.GetValue<long>() ?? 0,
                    AltUrl = AltMirror(Mirror(idx["url"]?.GetValue<string>(), source))
                });
            }

            st.Start();
            await DownloadAll(tasks, progress, null, 0, ct);

            // 资源对象
            var indexPath = Path.Combine(gameDir, "assets", "indexes", indexId + ".json");
            if (includeAssets && File.Exists(indexPath))
            {
                var assetTasks = new List<Task_>();
                try
                {
                    var idxNode = JsonNode.Parse(File.ReadAllText(indexPath)) as JsonObject;
                    if (idxNode?["objects"] is JsonObject objs)
                    {
                        foreach (var kv in objs)
                        {
                            var hash = kv.Value?["hash"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(hash)) continue;
                            var size = kv.Value?["size"]?.GetValue<long>() ?? 0;
                            var sub = hash.Substring(0, 2);
                            var assetUrl = $"{AssetsBase(source)}/{sub}/{hash}";
                            assetTasks.Add(new Task_
                            {
                                Url = assetUrl,
                                Path = Path.Combine(gameDir, "assets", "objects", sub, hash),
                                Size = size,
                                AltUrl = AltMirror(assetUrl)
                            });
                        }
                    }
                }
                catch { }

                var pending = assetTasks.Where(t => !File.Exists(t.Path) || new FileInfo(t.Path).Length != t.Size).ToList();
                if (pending.Count > 0)
                    await DownloadAll(assetTasks, progress, "下载资源文件", 0, ct);
            }

            progress?.Report(new DlProgress { Stage = "完成", Current = rv.Id, Done = 1, Total = 1, DoneBytes = 1, TotalBytes = 1 });
        }

        private static async Task DownloadAll(List<Task_> all, IProgress<DlProgress> progress, string stage, long baseBytes, CancellationToken ct)
        {
            var unique = new Dictionary<string, Task_>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in all)
            {
                if (string.IsNullOrEmpty(t.Path)) continue;
                unique[t.Path] = t;
            }

            var pending = unique.Values.Where(t =>
            {
                try
                {
                    if (!File.Exists(t.Path)) return true;
                    var len = new FileInfo(t.Path).Length;
                    // 已知大小就比对大小；未知大小时 0 字节的残留文件也要重下
                    return t.Size > 0 ? len != t.Size : len == 0;
                }
                catch { return true; }
            }).ToList();

            long totalBytes = unique.Values.Sum(t => t.Size);
            long doneBytes = totalBytes - pending.Sum(t => t.Size);
            int total = unique.Count;
            int done = total - pending.Count;

            var sw = Stopwatch.StartNew();
            long lastBytes = doneBytes;
            var lastReport = 0L;
            var sync = new object();

            void Report(string current)
            {
                lock (sync)
                {
                    var el = sw.Elapsed.TotalSeconds;
                    var speed = el > 0.5 ? (doneBytes - lastBytes) / 1048576.0 / Math.Max(0.001, el) : 0;
                    if (sw.ElapsedMilliseconds - lastReport < 120 && done < total) return;
                    lastReport = sw.ElapsedMilliseconds;
                    sw.Restart();
                    lastBytes = doneBytes;
                    progress?.Report(new DlProgress
                    {
                        Stage = stage ?? "下载文件",
                        Current = current,
                        Done = Volatile.Read(ref done),
                        Total = total,
                        DoneBytes = Interlocked.Read(ref doneBytes),
                        TotalBytes = totalBytes,
                        SpeedMBps = speed
                    });
                }
            }

            Report("准备中…");

            // 并发太高会把镜像的 TLS 握手打爆（实测 24 并发稳定出 SSL 失败），10 个足够快也稳
            var errors = new List<string>();
            var failed = new List<Task_>();

            async Task RunBatch(List<Task_> list, int concurrency)
            {
                using var sem = new SemaphoreSlim(concurrency);
                var jobs = list.Select(async t =>
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(t.Path));
                        var tmp = t.Path + ".hhcltmp";

                        // 候选：主源 → 镜像↔官方互换（AltMirror）→ 强制官方源（Unmirror）。
                        // 注意：bmclapi.bangbang93.com 这个「次域名」实测经常 60s 超时（见 AltApi 的旧映射），
                        // 用它兜底只会白白等满超时，所以这里改用官方源 libraries.minecraft.net 兜底，
                        // 它对未缓存的新版库（如 MC 26.3 的 log4j/guava/netty）是直连 200、最稳的源。
                        var cands = new List<string> { t.Url };
                        void AddCand(string u)
                        {
                            if (!string.IsNullOrEmpty(u) && !cands.Any(c => string.Equals(c, u, StringComparison.OrdinalIgnoreCase)))
                                cands.Add(u);
                        }
                        AddCand(t.AltUrl);
                        AddCand(Unmirror(t.Url));

                        bool ok = false;
                        for (int attempt = 0; attempt < 6 && !ok; attempt++)
                        {
                            var useUrl = cands[attempt % cands.Count];
                            try
                            {
                                // 未缓存的新版库（MC 26.3 的 log4j/guava/netty 等）从镜像回源很慢，
                                // 卡死判定放宽到 120s，避免把「慢但活着」的下载误杀成失败。
                                using var stall = new CancellationTokenSource();
                                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stall.Token);
                                stall.CancelAfter(TimeSpan.FromSeconds(120));

                                using var req = new HttpRequestMessage(HttpMethod.Get, useUrl)
                                {
                                    Version = HttpVersion.Version11,
                                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                                };
                                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                                resp.EnsureSuccessStatusCode();
                                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                                {
                                    await using var src = await resp.Content.ReadAsStreamAsync(linked.Token);
                                    var buf = new byte[81920];
                                    int r;
                                    while ((r = await src.ReadAsync(buf, linked.Token)) > 0)
                                    {
                                        stall.CancelAfter(TimeSpan.FromSeconds(120));
                                        await fs.WriteAsync(buf.AsMemory(0, r), linked.Token);
                                        Interlocked.Add(ref doneBytes, r);
                                        Report(Path.GetFileName(t.Path));
                                    }
                                }

                                var got = new FileInfo(tmp).Length;
                                if (t.Size > 0 && got != t.Size) throw new Exception($"大小不符（{got}/{t.Size}）");
                                if (got == 0 && t.Size != 0) throw new Exception("下载到 0 字节");

                                File.Move(tmp, t.Path, true);
                                Interlocked.Increment(ref done);
                                Report(Path.GetFileName(t.Path));
                                ok = true;
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                if (attempt == 3) break;
                                await Task.Delay(600 * (attempt + 1), ct);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch
                            {
                                if (attempt == 3) break;
                                await Task.Delay(600 * (attempt + 1), ct);
                            }
                            finally
                            {
                                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                            }
                        }

                        if (!ok) lock (failed) failed.Add(t);
                    }
                    finally { sem.Release(); }
                }).ToList();

                await Task.WhenAll(jobs);
            }

            await RunBatch(pending, 10);

            // 偶发的 SSL 握手失败再来一轮：低并发 + 先歇一下，基本都能补上
            if (failed.Count > 0 && !ct.IsCancellationRequested)
            {
                var retry = failed.ToList();
                failed.Clear();
                Log.Write($"retry {retry.Count} files with low concurrency");
                await Task.Delay(1500, ct);
                await RunBatch(retry, 3);
            }

            if (failed.Count > 0)
            {
                foreach (var t in failed) errors.Add(Path.GetFileName(t.Path));
                throw new Exception($"有 {errors.Count} 个文件下载失败（已重试多次）：{string.Join("、", errors.Take(3))}");
            }
        }
    }
}
