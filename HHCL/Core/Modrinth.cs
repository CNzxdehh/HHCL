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
    public class ModrinthProject
    {
        public string ProjectId { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string Author { get; set; } = "";
        public string ProjectType { get; set; } = "mod";
        public long Downloads { get; set; }
        public string Categories { get; set; } = "";

        public string TypeText => ProjectType == "mod" ? "模组"
            : ProjectType == "resourcepack" ? "资源包"
            : ProjectType == "shader" ? "光影" : ProjectType;

        public string DownloadsText => Downloads >= 10000 ? $"{Downloads / 10000.0:0.0} 万下载" : $"{Downloads} 下载";
        public string SubLine => $"{Author} · {DownloadsText}";
        public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1).ToUpper();
    }

    public class ModrinthVersion
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FileUrl { get; set; } = "";
        public string FileHash { get; set; } = "";
        public long FileSize { get; set; }
        public bool Primary { get; set; }
        /// <summary>Modrinth 的 version_type：release / beta / alpha。</summary>
        public string VersionType { get; set; } = "";
        public List<string> GameVersions { get; set; } = new List<string>();
        public List<string> Loaders { get; set; } = new List<string>();

        public string Display => $"{Name ?? VersionNumber} · {string.Join("/", Loaders.Take(2))} · {AppState.FormatSize(FileSize)}";
        public string SizeText => AppState.FormatSize(FileSize);
        public string LoaderText => Loaders.Count > 0 ? string.Join(" / ", Loaders.Take(3)) : "—";
        public string GameVersionText => GameVersions.Count > 0 ? string.Join(" / ", GameVersions.Take(3)) : "";

        /// <summary>正式版 / 测试版 / 快照</summary>
        public string KindText => GameVersionKind.OfVersionType(VersionType, GameVersions.FirstOrDefault() ?? "");
        public override string ToString() => Display;
    }

    public static class Modrinth
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 16
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            // Modrinth 要求自定义 UA，否则拒绝请求
            c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            return c;
        }

        private static async Task<string> GetAsync(string url, CancellationToken ct, int attempts = 3)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url)
                    {
                        Version = HttpVersion.Version11,
                        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                    };
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Write($"modrinth GET fail: {url} :: {ex.Message}");
                    if (i < attempts - 1) await Task.Delay(300 * (i + 1), ct);
                }
            }
            throw last ?? new Exception("Modrinth 请求失败");
        }

        /// <summary>projectType: mod / resourcepack / shader</summary>
        public static async Task<List<ModrinthProject>> SearchAsync(string query, string projectType, string mcVersion, string loader,
            CancellationToken ct, int limit = 50, int offset = 0, bool matchVersion = false)
        {
            var facets = new JsonArray { new JsonArray { $"project_type:{projectType}" } };
            if (!string.IsNullOrWhiteSpace(loader))
                facets.Add(new JsonArray { $"categories:{loader}" });
            // 只有勾选"只看兼容版本"时才按游戏版本过滤，否则结果会少一大截
            if (matchVersion && !string.IsNullOrWhiteSpace(mcVersion))
                facets.Add(new JsonArray { $"versions:{mcVersion}" });

            var url = "https://api.modrinth.com/v2/search?limit=" + limit +
                      "&offset=" + offset +
                      "&index=relevance&query=" + Uri.EscapeDataString(query ?? "") +
                      "&facets=" + Uri.EscapeDataString(facets.ToJsonString());

            var text = await GetAsync(url, ct);
            var node = JsonNode.Parse(text) as JsonObject;
            var list = new List<ModrinthProject>();
            if (node?["hits"] is JsonArray hits)
            {
                foreach (var h in hits)
                {
                    var o = h as JsonObject;
                    if (o == null) continue;
                    list.Add(new ModrinthProject
                    {
                        ProjectId = o["project_id"]?.GetValue<string>() ?? o["slug"]?.GetValue<string>() ?? "",
                        Slug = o["slug"]?.GetValue<string>() ?? "",
                        Title = o["title"]?.GetValue<string>() ?? "",
                        Description = o["description"]?.GetValue<string>() ?? "",
                        IconUrl = o["icon_url"]?.GetValue<string>() ?? "",
                        Author = o["author"]?.GetValue<string>() ?? "",
                        ProjectType = o["project_type"]?.GetValue<string>() ?? projectType,
                        Downloads = o["downloads"]?.GetValue<long>() ?? 0,
                        Categories = string.Join(", ", (o["display_categories"] as JsonArray)?.Take(3)
                            .Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>())
                    });
                }
            }
            return list;
        }

        public static async Task<List<ModrinthVersion>> GetVersionsAsync(string projectId, string mcVersion, string loader, CancellationToken ct)
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(mcVersion)) query.Add("game_versions=" + Uri.EscapeDataString($"[\"{mcVersion}\"]"));
            if (!string.IsNullOrWhiteSpace(loader)) query.Add("loaders=" + Uri.EscapeDataString($"[\"{loader}\"]"));
            var url = $"https://api.modrinth.com/v2/project/{projectId}/version";
            if (query.Count > 0) url += "?" + string.Join("&", query);

            var text = await GetAsync(url, ct);
            var arr = JsonNode.Parse(text) as JsonArray;
            var list = new List<ModrinthVersion>();
            if (arr == null) return list;

            foreach (var v in arr)
            {
                var o = v as JsonObject;
                if (o == null) continue;

                var files = o["files"] as JsonArray;
                var file = (files?.Select(f => f as JsonObject)
                                .FirstOrDefault(f => f?["primary"]?.GetValue<bool>() == true) ?? files?.FirstOrDefault() as JsonObject);
                if (file == null) continue;

                list.Add(new ModrinthVersion
                {
                    Id = o["id"]?.GetValue<string>() ?? "",
                    Name = o["name"]?.GetValue<string>() ?? "",
                    VersionNumber = o["version_number"]?.GetValue<string>() ?? "",
                    VersionType = o["version_type"]?.GetValue<string>() ?? "",
                    FileName = file["filename"]?.GetValue<string>() ?? "",
                    FileUrl = file["url"]?.GetValue<string>() ?? "",
                    FileHash = file["hashes"]?["sha1"]?.GetValue<string>() ?? "",
                    FileSize = file["size"]?.GetValue<long>() ?? 0,
                    Primary = file["primary"]?.GetValue<bool>() ?? false,
                    GameVersions = ((o["game_versions"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "")
                        .Where(s => s.Length > 0).ToList()) ?? new List<string>(),
                    Loaders = ((o["loaders"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "")
                        .Where(s => s.Length > 0).ToList()) ?? new List<string>()
                });
            }
            return list;
        }

        /// <summary>下载一个 Modrinth 文件到指定目录（模组/资源包/光影目录）。</summary>
        public static async Task<string> InstallAsync(ModrinthVersion ver, string targetDir, IProgress<DlProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(targetDir);
            var path = Path.Combine(targetDir, ver.FileName);
            await MinecraftDownloader.DownloadFilesAsync(new[] { (ver.FileUrl, path, ver.FileSize) }, progress, "安装中", ct);
            return path;
        }
    }
}
