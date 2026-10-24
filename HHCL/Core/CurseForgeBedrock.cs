using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>CurseForge 里"我的世界 基岩版"这个游戏的 id 与各分类(classId)。</summary>
    public class CfBedrockGame
    {
        public int GameId;
        public string Slug = "";
        public string Name = "";
        /// <summary>分类 slug → classId，例如 addons / texture-packs / shaders / worlds。</summary>
        public Dictionary<string, int> Classes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int ClassOf(string slug)
        {
            if (Classes.TryGetValue(slug, out var v)) return v;
            // 兜底：名称里含关键字的分类
            foreach (var kv in Classes)
                if (kv.Key.Contains(slug, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return 0;
        }
    }

    /// <summary>
    /// CurseForge 的基岩版（minecraft-bedrock）支持。
    /// 基岩版的 addon 全在 CF 的 minecraft-bedrock 区（两万多项目），Modrinth 那边一个都没有。
    /// 需要 CF API Key（设置 → 在线资源里填）。
    /// </summary>
    public static class CurseForgeBedrock
    {
        private const string Base = "https://api.curseforge.com/v1";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

        private static HttpRequestMessage Req(string url, string apiKey)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            r.Headers.Add("x-api-key", apiKey ?? "");
            r.Headers.Add("Accept", "application/json");
            r.Headers.Add("User-Agent", AppInfo.UserAgent);
            return r;
        }

        private static async Task<JsonObject> GetJson(string url, string apiKey, CancellationToken ct)
        {
            using var req = Req(url, apiKey);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                throw new Exception("CurseForge 拒绝了请求，API Key 可能不对或已失效。");
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"CurseForge HTTP {(int)resp.StatusCode}：{Trim(text)}");
            try { return JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
            catch { return new JsonObject(); }
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 400 ? s.Substring(0, 400) + "…" : s);

        /// <summary>
        /// 动态发现基岩版的 gameId 与各分类 classId（不写死数字，CF 调整编号也不会坏）。
        /// </summary>
        public static async Task<CfBedrockGame> GetGameAsync(string apiKey, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new Exception("还没有填 CurseForge API Key");
            var games = await GetJson($"{Base}/games", apiKey, ct);
            var list = new List<CfBedrockGame>();
            if (games["data"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var o = it as JsonObject;
                    if (o == null) continue;
                    list.Add(new CfBedrockGame
                    {
                        GameId = o["id"]?.GetValue<int>() ?? 0,
                        Slug = o["slug"]?.GetValue<string>() ?? "",
                        Name = o["name"]?.GetValue<string>() ?? ""
                    });
                }
            }
            var game = list.FirstOrDefault(g => g.Slug == "minecraft-bedrock")
                     ?? list.FirstOrDefault(g => g.Name.Contains("Bedrock", StringComparison.OrdinalIgnoreCase)
                                             && g.Name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase));
            if (game == null || game.GameId == 0)
                throw new Exception("在 CurseForge 里没找到「我的世界 基岩版」这个游戏分区。");

            // 分类树：根节点就是 classId
            var detail = await GetJson($"{Base}/games/{game.GameId}", apiKey, ct);
            if (detail["categories"] is JsonArray cats)
            {
                foreach (var c in cats)
                {
                    var o = c as JsonObject;
                    if (o == null) continue;
                    var parent = o["parentId"];
                    // parentId 为 null 的就是 class（addons / texture-packs / shaders…）
                    if (parent != null && parent.GetValue<int>() != 0) continue;
                    var slug = o["slug"]?.GetValue<string>() ?? "";
                    var id = o["id"]?.GetValue<int>() ?? 0;
                    if (slug.Length > 0 && id != 0) game.Classes[slug] = id;
                }
            }
            return game;
        }

        /// <summary>搜索基岩版项目。classSlug 传 addons / texture-packs / shaders 等，空则不过滤。</summary>
        public static async Task<List<CfProject>> SearchAsync(string query, string classSlug, CfBedrockGame game,
            string apiKey, CancellationToken ct, int pageSize = 50, int index = 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Base}/mods/search?gameId={game.GameId}&sortField=2&sortOrder=desc");
            sb.Append("&pageSize=").Append(pageSize).Append("&index=").Append(index);
            if (!string.IsNullOrWhiteSpace(query)) sb.Append("&searchFilter=").Append(Uri.EscapeDataString(query));
            var cid = game.ClassOf(classSlug);
            if (cid != 0) sb.Append("&classId=").Append(cid);

            var root = await GetJson(sb.ToString(), apiKey, ct);
            var list = new List<CfProject>();
            if (root["data"] is JsonArray arr)
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
                        ProjectType = classSlug ?? "addons"
                    });
                }
            }
            return list;
        }

        /// <summary>取某个基岩版项目的所有可下载文件。</summary>
        public static async Task<List<CfFile>> GetFilesAsync(string modId, string apiKey, CancellationToken ct, int pageSize = 50)
        {
            var root = await GetJson($"{Base}/mods/{modId}/files?pageSize={pageSize}", apiKey, ct);
            var list = new List<CfFile>();
            if (root["data"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var o = it as JsonObject;
                    if (o == null) continue;
                    list.Add(new CfFile
                    {
                        Id = o["id"]?.GetValue<int>().ToString() ?? o["id"]?.ToString() ?? "",
                        ModId = modId,
                        FileName = o["fileName"]?.GetValue<string>() ?? "",
                        Url = o["downloadUrl"]?.GetValue<string>() ?? "",
                        Size = o["fileLength"]?.GetValue<long>() ?? 0,
                        Released = o["fileDate"]?.GetValue<DateTime>().ToLocalTime() ?? DateTime.MinValue,
                        GameVersions = string.Join(", ", (o["gameVersions"] as JsonArray)?
                            .Select(x => x?.GetValue<string>() ?? "")
                            .Where(s => s.Length > 0).Take(6) ?? Enumerable.Empty<string>())
                    });
                }
            }
            return list.OrderByDescending(f => f.Released).ToList();
        }

        /// <summary>CF 文件的真实下载地址（官方 downloadUrl 带 Key；没有就按 forgecdn 规则拼）。</summary>
        public static string ResolveDownloadUrl(CfFile file, string apiKey)
        {
            if (!string.IsNullOrEmpty(file.Url)) return file.Url;
            return $"https://mediafilez.forgecdn.net/files/{file.Id}/{file.Id}/{file.FileName}";
        }
    }
}
