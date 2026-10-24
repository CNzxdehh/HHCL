using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    public class CfProject
    {
        public string Id { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public long Downloads { get; set; }
        public string ProjectType { get; set; } = "mod";

        public string TypeText => ProjectType == "resourcepack" ? "资源包"
            : ProjectType == "shader" ? "光影" : "模组";

        public string DownloadsText
        {
            get
            {
                if (Downloads >= 100_000_000) return $"{Downloads / 100_000_000.0:0.0} 亿下载";
                if (Downloads >= 10_000) return $"{Downloads / 10_000.0:0.0} 万下载";
                return $"{Downloads} 下载";
            }
        }

        public string SubLine => $"{Author} · {DownloadsText}";
    }

    public class CfFile
    {
        public string Id { get; set; } = "";
        public string ModId { get; set; } = "";
        public string Name { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Url { get; set; } = "";
        public long Size { get; set; }
        public string GameVersions { get; set; } = "";
        public string Loaders { get; set; } = "";
        public DateTime Released { get; set; }

        public string Display => $"{Name} · {Loaders} · {AppState.FormatSize(Size)}";
        public string SizeText => AppState.FormatSize(Size);
        public string LoaderText => string.IsNullOrEmpty(Loaders) ? "—" : Loaders;
        public string GameVersionText => GameVersions ?? "";

        /// <summary>正式版 / 测试版 / 快照</summary>
        public string KindText => GameVersionKind.Of((GameVersions ?? "").Split(',').FirstOrDefault()?.Trim() ?? "");
        public override string ToString() => Display;
    }

    public static class CurseForge
    {
        private const string Base = "https://api.curseforge.com/v1";
        private const int GameId = 432; // Minecraft

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>CurseForge 的 classId：模组 6、资源包 12、光影 6552。</summary>
        public static int ClassIdOf(string projectType) =>
            projectType == "resourcepack" ? 12 : projectType == "shader" ? 6552 : 6;

        /// <summary>CurseForge 的 modLoaderType：1=Forge 4=Fabric 5=Quilt 6=NeoForge。</summary>
        public static int? LoaderTypeOf(string loader)
        {
            var l = (loader ?? "").ToLowerInvariant();
            if (l.Contains("fabric")) return 4;
            if (l.Contains("neoforge")) return 6;
            if (l.Contains("forge")) return 1;
            if (l.Contains("quilt")) return 5;
            return null;
        }

        private static HttpRequestMessage Req(string url, string apiKey)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            r.Headers.Add("x-api-key", apiKey ?? "");
            r.Headers.Add("Accept", "application/json");
            r.Headers.Add("User-Agent", AppInfo.UserAgent);
            return r;
        }

        public static async Task<List<CfProject>> SearchAsync(string query, string projectType, string mcVersion, string loader,
            string apiKey, CancellationToken ct, int pageSize = 50, int index = 0)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new Exception("还没有填 CurseForge API Key（设置页 → 在线资源）");

            var sb = new System.Text.StringBuilder();
            sb.Append($"{Base}/mods/search?gameId={GameId}&classId={ClassIdOf(projectType)}");
            sb.Append("&sortField=2&sortOrder=desc"); // 按下载量排序
            sb.Append("&pageSize=").Append(pageSize).Append("&index=").Append(index);
            if (!string.IsNullOrWhiteSpace(query)) sb.Append("&searchFilter=").Append(Uri.EscapeDataString(query));
            if (!string.IsNullOrWhiteSpace(mcVersion)) sb.Append("&gameVersion=").Append(Uri.EscapeDataString(mcVersion));
            var lt = LoaderTypeOf(loader);
            if (lt.HasValue) sb.Append("&modLoaderType=").Append(lt.Value);

            using var req = Req(sb.ToString(), apiKey);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 401)
                throw new Exception("CurseForge 拒绝了请求，API Key 可能不对或已失效。");
            resp.EnsureSuccessStatusCode();

            var list = new List<CfProject>();
            if (JsonNode.Parse(text) is JsonObject root && root["data"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var o = it as JsonObject;
                    if (o == null) continue;
                    list.Add(new CfProject
                    {
                        Id = o["id"]?.GetValue<int>().ToString() ?? o["id"]?.ToString() ?? "",
                        Slug = o["slug"]?.GetValue<string>() ?? "",
                        Title = o["name"]?.GetValue<string>() ?? "",
                        Description = o["summary"]?.GetValue<string>() ?? "",
                        Author = string.Join(", ", (o["authors"] as JsonArray)?
                                                   .Select(a => (a as JsonObject)?["name"]?.GetValue<string>() ?? "")
                                                   .Where(s => s.Length > 0).Take(2) ?? Enumerable.Empty<string>()),
                        IconUrl = (o["logo"] as JsonObject)?["url"]?.GetValue<string>() ?? "",
                        Downloads = o["downloadCount"]?.GetValue<long>() ?? 0,
                        ProjectType = projectType
                    });
                }
            }
            return list;
        }

        public static async Task<List<CfFile>> GetFilesAsync(string modId, string mcVersion, string loader,
            string apiKey, CancellationToken ct, int pageSize = 50)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new Exception("还没有填 CurseForge API Key");
            var lt = LoaderTypeOf(loader);

            async Task<List<CfFile>> Fetch(bool strict)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"{Base}/mods/{modId}/files?pageSize=").Append(pageSize);
                if (strict && !string.IsNullOrWhiteSpace(mcVersion)) sb.Append("&gameVersion=").Append(Uri.EscapeDataString(mcVersion));
                if (strict && lt.HasValue) sb.Append("&modLoaderType=").Append(lt.Value);

                using var req = Req(sb.ToString(), apiKey);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);
                resp.EnsureSuccessStatusCode();
                return ParseFiles(text, modId);
            }

            // 先按当前版本/加载器精确筛；一个都没有就放宽，避免"明明有模组却选不了版本"
            var list = await Fetch(true);
            if (list.Count == 0) list = await Fetch(false);
            return list.OrderByDescending(f => f.Released).ToList();
        }

        private static List<CfFile> ParseFiles(string text, string modId)
        {
            var list = new List<CfFile>();
            if (JsonNode.Parse(text) is JsonObject root && root["data"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var o = it as JsonObject;
                    if (o == null) continue;
                    var gv = (o["gameVersions"] as JsonArray)?
                             .Select(x => x?.GetValue<string>() ?? "")
                             .Where(s => !s.Contains("Forge") && !s.Contains("Fabric") && !s.Contains("NeoForge") && !s.Contains("Quilt"))
                             .Take(3) ?? Enumerable.Empty<string>();
                    var ld = (o["gameVersions"] as JsonArray)?
                             .Select(x => x?.GetValue<string>() ?? "")
                             .Where(s => s.Contains("Forge") || s.Contains("Fabric") || s.Contains("NeoForge") || s.Contains("Quilt"))
                             .Take(2) ?? Enumerable.Empty<string>();

                    list.Add(new CfFile
                    {
                        Id = o["id"]?.ToString() ?? "",
                        ModId = modId,
                        Name = o["displayName"]?.GetValue<string>() ?? o["fileName"]?.GetValue<string>() ?? "",
                        FileName = o["fileName"]?.GetValue<string>() ?? "",
                        Url = o["downloadUrl"]?.GetValue<string>() ?? "",
                        Size = o["fileLength"]?.GetValue<long>() ?? 0,
                        GameVersions = string.Join(", ", gv),
                        Loaders = string.Join("/", ld),
                        Released = ParseDate(o["fileDate"]?.GetValue<string>())
                    });
                }
            }
            return list.OrderByDescending(f => f.Released).ToList();
        }

        /// <summary>下载某个文件到目标目录（游戏目录里的 mods / resourcepacks / shaderpacks）。</summary>
        public static async Task<string> InstallAsync(CfFile file, string targetDir, IProgress<DlProgress> progress,
            CancellationToken ct, string apiKey = "")
        {
            Directory.CreateDirectory(targetDir);
            var name = string.IsNullOrWhiteSpace(file.FileName) ? file.Name : file.FileName;
            foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            var target = Path.Combine(targetDir, name);

            var url = file.Url;
            // 有些文件不直接给 downloadUrl，需要单独取一次
            if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(file.ModId) && !string.IsNullOrWhiteSpace(file.Id))
            {
                try
                {
                    using var req = Req($"{Base}/mods/{file.ModId}/files/{file.Id}/download-url", apiKey);
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                    var t = await resp.Content.ReadAsStringAsync(ct);
                    url = (JsonNode.Parse(t) as JsonObject)?["data"]?.GetValue<string>() ?? "";
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(url)) throw new Exception("CurseForge 没有给下载链接");

            await MinecraftDownloader.DownloadFilesAsync(
                new[] { (Url: url, Path: target, Size: file.Size) }, progress, "安装中", ct);
            return target;
        }

        private static DateTime ParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                                             | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
                ? d : DateTime.MinValue;
        }
    }
}
