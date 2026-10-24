using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>
    /// 整合包（Modpack）安装引擎。
    /// 支持 Modrinth 的 .mrpack 与 CurseForge 的 .zip 两种格式，双源搜索 + 解析 + 完整安装。
    /// 安装会：① 装好对应的原版 + 加载器；② 建一个独立的版本档案（继承该加载器）；
    /// ③ 把整合包里的模组/配置/资源下载并铺到这个版本隔离目录里。
    /// </summary>
    public class ModpackSummary
    {
        public string Source { get; set; } = "";          // "modrinth" / "curseforge"
        public string ProjectId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public long Downloads { get; set; }

        public string DownloadsText
        {
            get
            {
                if (Downloads >= 100_000_000) return $"{Downloads / 100_000_000.0:0.0} 亿下载";
                if (Downloads >= 10_000) return $"{Downloads / 10_000.0:0.0} 万下载";
                return $"{Downloads} 下载";
            }
        }
        public string SourceText => Source == "curseforge" ? "CurseForge" : "Modrinth";
        public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1).ToUpper();
        public string SubLine => $"{Author} · {DownloadsText}";
    }

    public class ModpackRelease
    {
        public string Source { get; set; } = "";
        public string Id { get; set; } = "";              // Modrinth: version id；CurseForge: fileId
        public string ProjectId { get; set; } = "";        // CurseForge 需要 projectId+fileId 取下载链接
        public string Name { get; set; } = "";
        public string FileName { get; set; } = "";         // .mrpack / .zip
        public string FileUrl { get; set; } = "";          // Modrinth 直接有；CurseForge 需解析
        public string McHint { get; set; } = "";
        /// <summary>Modrinth 的 version_type：release / beta / alpha（整合包自己的版本类型）。</summary>
        public string VersionType { get; set; } = "";
        /// <summary>该整合包版本支持的全部游戏版本（第一个是主要目标）。</summary>
        public string AllGameVersions { get; set; } = "";
        public string LoaderHint { get; set; } = "";
        public long Size { get; set; }
        public string Display => $"{Name} · {McHint} · {LoaderHint}";

        // ComboBox 收起时只能回落到 ToString()（DisplayMemberPath 不会生成 ItemTemplate），
        // 不重写就会显示成 HHCL.Core.ModpackRelease 这种类名
        /// <summary>正式版 / 测试版 / 快照</summary>
        public string KindText => GameVersionKind.OfVersionType(VersionType, (McHint ?? "").Split(',').FirstOrDefault()?.Trim() ?? "");
        public string SizeText => AppState.FormatSize(Size);
        public override string ToString() => Display;
    }

    public class ModpackFile
    {
        // Modrinth 用 path+urls+sha1+env；CurseForge 用 projectId+fileId+type
        public bool IsCurseForge { get; set; }
        public string Path { get; set; } = "";             // mrpack 相对实例目录的路径
        public List<string> Urls { get; set; } = new List<string>();
        public string Sha1 { get; set; } = "";
        public string EnvClient { get; set; } = "";        // required / optional / unsupported（mrpack）
        public long Size { get; set; }
        public int ProjectId { get; set; }
        public int FileId { get; set; }
        public string Type { get; set; } = "mods";         // cf 的文件夹类型
    }

    public class ModpackInfo
    {
        public string Source { get; set; } = "";
        public string Name { get; set; } = "";
        public string VersionId { get; set; } = "";        // 整合包自身版本号（manifest 里的 versionId）
        public string McVersion { get; set; } = "";
        public string Loader { get; set; } = "";           // fabric / forge / neoforge
        public string LoaderVersion { get; set; } = "";     // 具体加载器版本；空串表示用最新
        public string ProfileId { get; set; } = "";        // 新建的版本档案 id
        public List<ModpackFile> Files { get; set; } = new List<ModpackFile>();
        public string OverridesDir { get; set; } = "";     // 解压出来的 overrides 目录（cf）
        public string TempRoot { get; set; } = "";
        public long TotalBytes => Files.Sum(f => f.Size);
    }

    public static class ModpackInstaller
    {
        private static readonly HttpClient Http = CreateClient();
        private const string ModrinthBase = "https://api.modrinth.com/v2";
        private const string CfBase = "https://api.curseforge.com/v1";

        private static HttpClient CreateClient()
        {
            var c = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 16
            }) { Timeout = TimeSpan.FromSeconds(40) };
            c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            return c;
        }

        private static async Task<string> GetJsonAsync(string url, string apiKey, CancellationToken ct, int attempts = 3)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Version = HttpVersion.Version11;
                    req.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                    if (!string.IsNullOrEmpty(apiKey)) req.Headers.Add("x-api-key", apiKey);
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                    if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 401)
                        throw new Exception("CurseForge 拒绝了请求，API Key 可能不对或已失效。");
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { last = ex; Log.Write($"modpack GET fail: {url} :: {ex.Message}"); await Task.Delay(300 * (i + 1), ct); }
            }
            throw last ?? new Exception("整合包请求失败");
        }

        // ---------------- 搜索 ----------------

        public static async Task<List<ModpackSummary>> SearchModrinthAsync(string query, string mc, string loader, CancellationToken ct, int limit = 25, int offset = 0)
        {
            var facets = new JsonArray { new JsonArray { "project_type:modpack" } };
            if (!string.IsNullOrWhiteSpace(loader)) facets.Add(new JsonArray { $"categories:{loader}" });
            if (!string.IsNullOrWhiteSpace(mc)) facets.Add(new JsonArray { $"versions:{mc}" });

            var url = ModrinthBase + "/search?limit=" + limit + "&offset=" + offset +
                      "&index=relevance&query=" + Uri.EscapeDataString(query ?? "") +
                      "&facets=" + Uri.EscapeDataString(facets.ToJsonString());
            var node = JsonNode.Parse(await GetJsonAsync(url, null, ct)) as JsonObject;
            var list = new List<ModpackSummary>();
            if (node?["hits"] is JsonArray hits)
            {
                foreach (var h in hits)
                {
                    var o = h as JsonObject;
                    if (o == null) continue;
                    list.Add(new ModpackSummary
                    {
                        Source = "modrinth",
                        ProjectId = o["project_id"]?.GetValue<string>() ?? o["slug"]?.GetValue<string>() ?? "",
                        Title = o["title"]?.GetValue<string>() ?? "",
                        Description = o["description"]?.GetValue<string>() ?? "",
                        Author = o["author"]?.GetValue<string>() ?? "",
                        IconUrl = o["icon_url"]?.GetValue<string>() ?? "",
                        Downloads = o["downloads"]?.GetValue<long>() ?? 0
                    });
                }
            }
            return list;
        }

        public static async Task<List<ModpackSummary>> SearchCurseForgeAsync(string query, string mc, string loader, string apiKey, CancellationToken ct, int pageSize = 25, int index = 0)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new Exception("还没有填 CurseForge API Key（设置页 → 在线资源）");
            var sb = new StringBuilder();
            sb.Append($"{CfBase}/mods/search?gameId=432&classId=4471");
            sb.Append("&sortField=2&sortOrder=desc");
            sb.Append("&pageSize=").Append(pageSize).Append("&index=").Append(index);
            if (!string.IsNullOrWhiteSpace(query)) sb.Append("&searchFilter=").Append(Uri.EscapeDataString(query));
            if (!string.IsNullOrWhiteSpace(mc)) sb.Append("&gameVersion=").Append(Uri.EscapeDataString(mc));
            var lt = CurseForge.LoaderTypeOf(loader);
            if (lt.HasValue) sb.Append("&modLoaderType=").Append(lt.Value);

            var node = JsonNode.Parse(await GetJsonAsync(sb.ToString(), apiKey, ct)) as JsonObject;
            var list = new List<ModpackSummary>();
            if (node?["data"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var o = it as JsonObject;
                    if (o == null) continue;
                    list.Add(new ModpackSummary
                    {
                        Source = "curseforge",
                        ProjectId = o["id"]?.GetValue<int>().ToString() ?? o["id"]?.ToString() ?? "",
                        Title = o["name"]?.GetValue<string>() ?? "",
                        Description = o["summary"]?.GetValue<string>() ?? "",
                        Author = string.Join(", ", (o["authors"] as JsonArray)?.Select(a => (a as JsonObject)?["name"]?.GetValue<string>() ?? "")
                                                    .Where(s => !string.IsNullOrEmpty(s)).Take(2) ?? Enumerable.Empty<string>()),
                        IconUrl = (o["logo"] as JsonObject)?["url"]?.GetValue<string>() ?? "",
                        Downloads = o["downloadCount"]?.GetValue<long>() ?? 0
                    });
                }
            }
            return list;
        }

        // ---------------- 版本列举 ----------------

        public static async Task<List<ModpackRelease>> GetReleasesAsync(ModpackSummary summary, string mc, string loader, string apiKey, CancellationToken ct)
        {
            if (summary.Source == "curseforge")
                return await GetCfReleasesAsync(summary, mc, apiKey, ct);
            return await GetMrReleasesAsync(summary, mc, loader, ct);
        }

        private static async Task<List<ModpackRelease>> GetMrReleasesAsync(ModpackSummary summary, string mc, string loader, CancellationToken ct)
        {
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(mc)) q.Add("game_versions=" + Uri.EscapeDataString("[\"" + mc + "\"]"));
            if (!string.IsNullOrWhiteSpace(loader)) q.Add("loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]"));
            var url = $"{ModrinthBase}/project/{summary.ProjectId}/version" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
            var arr = JsonNode.Parse(await GetJsonAsync(url, null, ct)) as JsonArray;
            var list = new List<ModpackRelease>();
            if (arr == null) return list;
            foreach (var v in arr)
            {
                var o = v as JsonObject;
                if (o == null) continue;
                var files = o["files"] as JsonArray;
                var file = (files?.Select(f => f as JsonObject).FirstOrDefault(f => (f?["filename"]?.GetValue<string>() ?? "").EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                             ?? files?.FirstOrDefault() as JsonObject);
                if (file == null) continue;
                var gv = o["game_versions"] as JsonArray;
                var ld = o["loaders"] as JsonArray;
                list.Add(new ModpackRelease
                {
                    Source = "modrinth",
                    Id = o["id"]?.GetValue<string>() ?? "",
                    ProjectId = summary.ProjectId,
                    Name = o["name"]?.GetValue<string>() ?? o["version_number"]?.GetValue<string>() ?? "",
                    FileName = file["filename"]?.GetValue<string>() ?? "",
                    FileUrl = file["url"]?.GetValue<string>() ?? "",
                    Size = file["size"]?.GetValue<long>() ?? 0,
                    // 组头只要一个主版本（多个版本号逗号连在一起会很难看），
                    // 全部版本另存一份，供"该版本适配哪些游戏"展示
                    McHint = (gv?.Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>()).FirstOrDefault() ?? "",
                    AllGameVersions = string.Join(", ", (gv?.Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>()).Take(4)),
                    VersionType = o["version_type"]?.GetValue<string>() ?? "",
                    LoaderHint = string.Join("/", (ld?.Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>()).Take(2))
                });
            }
            return list;
        }

        private static async Task<List<ModpackRelease>> GetCfReleasesAsync(ModpackSummary summary, string mc, string apiKey, CancellationToken ct)
        {
            var url = $"{CfBase}/mods/{summary.ProjectId}/files?pageSize=25";
            if (!string.IsNullOrWhiteSpace(mc)) url += "&gameVersion=" + Uri.EscapeDataString(mc);
            var node = JsonNode.Parse(await GetJsonAsync(url, apiKey, ct)) as JsonObject;
            var list = new List<ModpackRelease>();
            if (node?["data"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var o = it as JsonObject;
                    if (o == null) continue;
                    var name = o["displayName"]?.GetValue<string>() ?? o["fileName"]?.GetValue<string>() ?? "";
                    if (!((o["fileName"]?.GetValue<string>() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))) continue;
                    var gv = (o["gameVersions"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new List<string>();
                    var mcHint = gv.FirstOrDefault(g => char.IsDigit(g.FirstOrDefault())) ?? "";
                    var loaderHint = gv.FirstOrDefault(g => g.Contains("Forge") || g.Contains("Fabric") || g.Contains("NeoForge")) ?? "";
                    list.Add(new ModpackRelease
                    {
                        Source = "curseforge",
                        Id = o["id"]?.GetValue<int>().ToString() ?? "",
                        ProjectId = summary.ProjectId,
                        Name = name,
                        FileName = o["fileName"]?.GetValue<string>() ?? "",
                        Size = o["fileLength"]?.GetValue<long>() ?? 0,
                        McHint = mcHint,
                        LoaderHint = loaderHint
                    });
                }
            }
            return list.OrderByDescending(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---------------- 解析清单 ----------------

        public static async Task<ModpackInfo> ParseAsync(ModpackRelease release, CancellationToken ct, string apiKey = "")
        {
            var temp = Path.Combine(Config.DataDir, "modpack-tmp-" + Path.GetRandomFileName());
            Directory.CreateDirectory(temp);
            try
            {
                var packFile = Path.Combine(temp, "pack." + (release.Source == "curseforge" ? "zip" : "mrpack"));
                string url = release.FileUrl;
                if (release.Source == "curseforge")
                {
                    if (string.IsNullOrEmpty(url) && int.TryParse(release.ProjectId, out var pid) && int.TryParse(release.Id, out var fid))
                        url = await ResolveCfUrl(pid, fid, apiKey, ct);
                    if (string.IsNullOrEmpty(url)) throw new Exception("CurseForge 没有给整合包下载链接");
                }
                await MinecraftDownloader.DownloadFilesAsync(new[] { (url, packFile, release.Size) },
                    new Progress<DlProgress>(p => Log.Write($"modpack pack {p.Stage} {p.Done}/{p.Total}")), "下载整合包清单", ct);

                var extractDir = Path.Combine(temp, "x");
                Directory.CreateDirectory(extractDir);
                if (File.Exists(packFile))
                    ZipFile.ExtractToDirectory(packFile, extractDir, true);

                if (release.Source == "curseforge")
                    return ParseCurseForge(extractDir, temp, release, apiKey);
                return ParseModrinth(extractDir, temp, release);
            }
            catch
            {
                try { Directory.Delete(temp, true); } catch { }
                throw;
            }
        }

        private static ModpackInfo ParseModrinth(string extractDir, string temp, ModpackRelease release)
        {
            var index = Path.Combine(extractDir, "modrinth.index.json");
            if (!File.Exists(index)) throw new Exception("不是合法的 .mrpack（缺少 modrinth.index.json）");
            var root = JsonNode.Parse(File.ReadAllText(index)) as JsonObject;
            if (root == null) throw new Exception("modrinth.index.json 解析失败");

            var info = new ModpackInfo
            {
                Source = "modrinth",
                Name = root["name"]?.GetValue<string>() ?? release.ProjectId,
                VersionId = root["versionId"]?.GetValue<string>() ?? "",
                TempRoot = temp
            };

            // MC 版本 + 加载器：v2 在顶层字段，v1 在 dependencies 对象里
            if (root["gameVersion"] is JsonValue gv && !string.IsNullOrEmpty(gv.GetValue<string>()))
                info.McVersion = gv.GetValue<string>();
            if (root["loaders"] is JsonArray ld && ld.Count > 0)
                info.Loader = NormalizeLoader(ld[0]?.GetValue<string>() ?? "");

            if (string.IsNullOrEmpty(info.McVersion) || string.IsNullOrEmpty(info.Loader))
            {
                if (root["dependencies"] is JsonObject deps)
                {
                    foreach (var kv in deps)
                    {
                        var key = kv.Key.ToLowerInvariant();
                        var val = kv.Value?.GetValue<string>() ?? "";
                        if (key == "minecraft" && string.IsNullOrEmpty(info.McVersion)) info.McVersion = val;
                        else if ((key == "fabric-loader" || key == "fabric") && string.IsNullOrEmpty(info.Loader)) { info.Loader = "fabric"; info.LoaderVersion = val; }
                        else if (key == "forge" && string.IsNullOrEmpty(info.Loader)) { info.Loader = "forge"; info.LoaderVersion = val; }
                        else if (key == "neoforge" && string.IsNullOrEmpty(info.Loader)) { info.Loader = "neoforge"; info.LoaderVersion = val; }
                    }
                }
            }

            // 文件列表
            if (root["files"] is JsonArray files)
            {
                foreach (var f in files)
                {
                    var o = f as JsonObject;
                    if (o == null) continue;
                    var path = o["path"]?.GetValue<string>() ?? "";
                    if (string.IsNullOrEmpty(path)) continue;
                    // env.client == unsupported 的客户端不装
                    var envClient = o["env"]?["client"]?.GetValue<string>() ?? "";
                    if (envClient == "unsupported") continue;
                    var urls = (o["downloads"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(u => !string.IsNullOrEmpty(u)).ToList()
                               ?? new List<string>();
                    if (urls.Count == 0) continue;
                    info.Files.Add(new ModpackFile
                    {
                        Path = path.Replace('/', Path.DirectorySeparatorChar),
                        Urls = urls,
                        Sha1 = o["hashes"]?["sha1"]?.GetValue<string>() ?? "",
                        EnvClient = envClient,
                        Size = o["fileSize"]?.GetValue<long>() ?? 0
                    });
                }
            }

            info.ProfileId = MakeProfileId(info);
            return info;
        }

        private static ModpackInfo ParseCurseForge(string extractDir, string temp, ModpackRelease release, string apiKey)
        {
            var manifestPath = Path.Combine(extractDir, "manifest.json");
            if (!File.Exists(manifestPath)) throw new Exception("不是合法的 CurseForge 整合包（缺少 manifest.json）");
            var root = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
            if (root == null) throw new Exception("manifest.json 解析失败");

            var mc = root["minecraft"]?["version"]?.GetValue<string>() ?? "";
            var loaderId = root["minecraft"]?["modLoaders"] is JsonArray mls && mls.Count > 0
                ? (mls[0] as JsonObject)?["id"]?.GetValue<string>() ?? "" : "";
            var (loader, loaderVer) = SplitLoaderId(loaderId);

            var info = new ModpackInfo
            {
                Source = "curseforge",
                Name = root["name"]?.GetValue<string>() ?? release.ProjectId,
                VersionId = root["version"]?.GetValue<string>() ?? "",
                McVersion = mc,
                Loader = loader,
                LoaderVersion = loaderVer,
                TempRoot = temp
            };

            // 文件列表：projectID + fileID，安装时解析下载链接
            if (root["files"] is JsonArray files)
            {
                foreach (var f in files)
                {
                    var o = f as JsonObject;
                    if (o == null) continue;
                    var pid = o["projectID"]?.GetValue<int>() ?? 0;
                    var fid = o["fileID"]?.GetValue<int>() ?? 0;
                    if (pid == 0 || fid == 0) continue;
                    var type = (o["type"]?.GetValue<string>() ?? "mods").ToLowerInvariant();
                    info.Files.Add(new ModpackFile
                    {
                        IsCurseForge = true,
                        ProjectId = pid,
                        FileId = fid,
                        Type = type,
                        Size = 0
                    });
                }
            }

            // overrides 文件夹：拷贝到实例根目录
            var overridesName = root["overrides"]?.GetValue<string>();
            if (string.IsNullOrEmpty(overridesName)) overridesName = "overrides";
            var od = Path.Combine(extractDir, overridesName);
            if (Directory.Exists(od)) info.OverridesDir = od;

            info.ProfileId = MakeProfileId(info);
            return info;
        }

        // ---------------- 安装 ----------------

        public static async Task<string> InstallAsync(ModpackInfo info, Config cfg, IProgress<DlProgress> progress, CancellationToken ct, string apiKey = "")
        {
            var mc = info.McVersion;
            var loader = info.Loader;
            if (string.IsNullOrEmpty(mc) || string.IsNullOrEmpty(loader))
                throw new Exception("整合包里没有识别到 Minecraft 版本或加载器，无法安装。");

            // 1) 原版（加载器依赖它）
            var vanillaJson = Path.Combine(cfg.GameDir, "versions", mc, mc + ".json");
            if (!File.Exists(vanillaJson))
            {
                progress?.Report(new DlProgress { Stage = "安装原版", Current = mc });
                var manifest = await MinecraftDownloader.GetManifestAsync(cfg.DownloadSource, ct);
                var rv = manifest.FirstOrDefault(v => v.Id == mc);
                if (rv == null) throw new Exception($"找不到原版 {mc}（镜像里可能没有这个版本）");
                await MinecraftDownloader.InstallAsync(cfg.GameDir, MinecraftDownloader.LastUsedSource, rv, progress, ct, false);
            }
            else progress?.Report(new DlProgress { Stage = "原版已存在", Current = mc });

            // 2) 加载器（版本为空时取最新）
            string loaderVersion = info.LoaderVersion;
            if (string.IsNullOrEmpty(loaderVersion))
            {
                var loaders = await LoaderInstaller.GetLoadersAsync(mc, loader, ct);
                if (loaders.Count == 0) throw new Exception($"找不到 {loader} 的可用版本");
                loaderVersion = loaders[0].Version;
            }
            progress?.Report(new DlProgress { Stage = "安装加载器", Current = $"{loader} {loaderVersion}" });
            var loaderVersionId = await LoaderInstaller.InstallAsync(cfg, mc, loader, loaderVersion, progress, ct);

            // 3) 建独立版本档案（继承加载器版本）
            var id = info.ProfileId;
            var verDir = Path.Combine(cfg.GameDir, "versions", id);
            Directory.CreateDirectory(verDir);
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var profile = new JsonObject
            {
                ["inheritsFrom"] = loaderVersionId,
                ["id"] = id,
                ["releaseTime"] = now,
                ["time"] = now,
                ["type"] = "release"
            };
            var jsonPath = Path.Combine(verDir, id + ".json");
            if (!File.Exists(jsonPath))
                File.WriteAllText(jsonPath, profile.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

            var packRoot = cfg.IsolateVersions ? verDir : cfg.GameDir;
            Directory.CreateDirectory(packRoot);

            // 4) 下载所有文件
            var dlTasks = new List<(string Url, string Path, long Size)>();
            foreach (var f in info.Files)
            {
                if (f.IsCurseForge)
                {
                    var url = await ResolveCfUrl(f.ProjectId, f.FileId, apiKey, ct);
                    var destDir = Path.Combine(packRoot, MapCfType(f.Type));
                    Directory.CreateDirectory(destDir);
                    dlTasks.Add((url, Path.Combine(destDir, SanitizeFileName(f.FileId + ".jar")), 0));
                }
                else
                {
                    var dest = Path.Combine(packRoot, f.Path.TrimStart(Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    foreach (var u in f.Urls)
                        dlTasks.Add((u, dest, f.Size));
                }
            }
            // 去重（同目标路径只下一遍）
            dlTasks = dlTasks.GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

            if (dlTasks.Count > 0)
            {
                progress?.Report(new DlProgress { Stage = "下载整合包内容", Current = $"{dlTasks.Count} 个文件" });
                await MinecraftDownloader.DownloadFilesAsync(dlTasks, progress, "下载整合包内容", ct);
            }

            // 5) 覆盖 overrides（config / 资源 / 存档等）
            if (!string.IsNullOrEmpty(info.OverridesDir) && Directory.Exists(info.OverridesDir))
            {
                progress?.Report(new DlProgress { Stage = "写入配置与覆盖文件", Current = info.OverridesDir });
                CopyDir(info.OverridesDir, packRoot);
            }

            // 清理临时文件
            if (!string.IsNullOrEmpty(info.TempRoot))
            {
                try { Directory.Delete(info.TempRoot, true); } catch { }
            }

            progress?.Report(new DlProgress { Stage = "完成", Current = id, Done = 1, Total = 1, DoneBytes = 1, TotalBytes = 1 });
            return id;
        }

        // ---------------- 辅助 ----------------

        private static async Task<string> ResolveCfUrl(int projectId, int fileId, string apiKey, CancellationToken ct)
        {
            var url = $"{CfBase}/mods/{projectId}/files/{fileId}/download-url";
            var text = await GetJsonAsync(url, apiKey, ct);
            return (JsonNode.Parse(text) as JsonObject)?["data"]?.GetValue<string>() ?? "";
        }

        private static string NormalizeLoader(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            if (s.Contains("neoforge")) return "neoforge";
            if (s.Contains("fabric")) return "fabric";
            if (s.Contains("forge")) return "forge";
            return s;
        }

        private static (string loader, string version) SplitLoaderId(string id)
        {
            if (string.IsNullOrEmpty(id)) return ("", "");
            var i = id.IndexOf('-');
            if (i <= 0) return (NormalizeLoader(id), "");
            var loader = NormalizeLoader(id.Substring(0, i));
            var version = id.Substring(i + 1).Trim();
            return (loader, version);
        }

        private static string MapCfType(string type)
        {
            type = (type ?? "mods").ToLowerInvariant();
            return type switch
            {
                "mods" => "mods",
                "resourcepacks" => "resourcepacks",
                "shaders" => "shaderpacks",
                "worlds" => "saves",
                _ => type
            };
        }

        private static string MakeProfileId(ModpackInfo info)
        {
            var baseName = string.IsNullOrEmpty(info.Name) ? "Modpack" : info.Name;
            var safe = SanitizeId(baseName);
            if (safe.Length > 40) safe = safe.Substring(0, 40);
            var suffix = info.LoaderVersion.Length > 0 ? info.LoaderVersion : (info.Loader + "-" + (info.McVersion));
            var id = $"{safe}-{info.McVersion}-{info.Loader}-{suffix}";
            // 文件名长度保护
            if (id.Length > 120)
                id = $"{safe}-{info.McVersion}-{info.Loader}";
            return id;
        }

        private static string SanitizeId(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.') sb.Append(c == ' ' ? '-' : c);
                else sb.Append('-');
            }
            var r = sb.ToString().Trim('-');
            return string.IsNullOrEmpty(r) ? "Modpack" : r;
        }

        private static string SanitizeFileName(string s)
        {
            foreach (var bad in Path.GetInvalidFileNameChars()) s = s.Replace(bad, '_');
            return s;
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(f);
                foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
                File.Copy(f, Path.Combine(dst, name), true);
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
