using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>
    /// 基岩版（Minecraft for Windows / Bedrock Edition）的安装引擎。
    /// 基岩版不走 Mojang 版本库，而是走微软商店授权 API：
    ///   DisplayCatalog 取 ContentId → update.xboxlive.com 的 XSTS 令牌 →
    ///   packagespc.xboxlive.com/GetBasePackage 取包文件 → 下载 → Add-AppxPackage 安装。
    /// 需要登录的微软账号拥有基岩版（或试用资格），否则商店会在安装阶段拒绝。
    /// </summary>
    public static class BedrockInstaller
    {
        public const string ProductId = "9NBLGGH2JHXJ";
        public const string ContentId = "7792d9ce-355a-493c-afbd-768f4a77c3b0";
        public const string PackageFamilyName = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";

        private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string GetBasePackageUrl = "https://packagespc.xboxlive.com/GetBasePackage/" + ContentId;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        /// <summary>下载专用：整体不设超时，改由"90 秒无数据"的卡死检测兜底。</summary>
        private static readonly HttpClient HttpDl = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        /// <summary>微软商店商品页 URI，用于「打开商店」兜底安装。</summary>
        public static string StoreUri => $"ms-windows-store://pdp/?ProductId={ProductId}";

        /// <summary>已安装的基岩版数据目录（存档所在）。</summary>
        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", PackageFamilyName, "LocalState", "games", "com.mojang");

        // ============ 安装检测 / 启动 ============

        public static bool IsInstalled(out string version)
        {
            version = "";
            try
            {
                // 注意：Get-AppxPackage 的 -Name 匹配的是包名(Microsoft.MinecraftUWP)，不是包族名，
                // 所以要再按 PackageFamilyName 过滤一次，否则 Beta 版会混进来。
                var outp = RunPs(
                    "$p = Get-AppxPackage -Name 'Microsoft.MinecraftUWP' | " +
                    "Where-Object { $_.PackageFamilyName -eq '" + PackageFamilyName + "' } | Select-Object -First 1; " +
                    "if ($p) { Write-Output ('VER:' + $p.Version) } else { Write-Output 'VER:' }");
                version = ParseVersion(outp);
                return version.Length > 0;
            }
            catch (Exception ex) { Log.Write("bedrock detect: " + ex); }
            return false;
        }

        /// <summary>从 PowerShell 输出里取出 VER: 标记后的版本号（忽略进度流等噪声）。</summary>
        private static string ParseVersion(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                int i = line.IndexOf("VER:", StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                var v = line.Substring(i + 4).Trim();
                // 去掉可能附带的 CR / 其它标记
                v = v.Split(' ')[0].Trim();
                if (v.Length > 0) return v;
            }
            return "";
        }

        /// <summary>通过 shell:AppsFolder 直接启动已安装的基岩版。</summary>
        public static void Launch()
        {
            // 开始菜单里注册的是 !Game（不是 !App），这是实测 Get-StartApps 的结果
            var psi = new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{PackageFamilyName}!Game")
            {
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        /// <summary>商店侧的版本信息（最新版本号、要下载的总大小、文件数）。</summary>
        public class StoreInfo
        {
            public string Version = "";
            public long TotalSize;
            public int FileCount;
            public int OldVersionCount;
            /// <summary>商店随包一起给出的历史版本清单（来自 update-*.xsp 文件名）。</summary>
            public List<string> OldVersions = new List<string>();
        }

        /// <summary>查商店当前提供的基岩版版本与下载体积。</summary>
        public static async Task<StoreInfo> GetStoreInfoAsync(Account acc, CancellationToken ct)
        {
            var auth = await GetXboxUpdateAuthAsync(acc, ct);
            var raw = await GetBasePackageRawAsync(auth, ct);
            var files = ParsePackageFiles(raw);
            var node = JsonNode.Parse(raw) as JsonObject;
            var ver = node?["Version"]?.GetValue<string>() ?? "";
            // 主包版本号形如 1.26.5203.0，Version 字段带一串 guid 后缀
            if (ver.Contains('.'))
            {
                var parts = ver.Split('.');
                if (parts.Length >= 4) ver = string.Join(".", parts.Take(4));
            }
            var info = new StoreInfo
            {
                Version = ver,
                TotalSize = files.Sum(f => f.Size),
                FileCount = files.Count,
                OldVersionCount = files.Count(f => f.FileName.EndsWith(".xsp", StringComparison.OrdinalIgnoreCase))
            };
            // 历史版本号：update-1.26.201.0.<guid>.xsp → 1.26.201.0
            foreach (var f in files.Where(f => f.FileName.EndsWith(".xsp", StringComparison.OrdinalIgnoreCase)))
            {
                var n = f.FileName;
                if (!n.StartsWith("update-", StringComparison.OrdinalIgnoreCase)) continue;
                var rest = n.Substring("update-".Length);
                var dots = 0; var idx = 0;
                for (int i = 0; i < rest.Length && dots < 4; i++)
                {
                    if (rest[i] == '.') dots++;
                    idx = i;
                }
                if (dots >= 4) info.OldVersions.Add(rest.Substring(0, idx));
            }
            info.OldVersions.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal));
            return info;
        }

        // ============ 鉴权：update.xboxlive.com 的 XSTS 令牌 ============

        /// <summary>
        /// 用登录好的微软账号换出 packagespc 需要的 Authorization 头（XBL3.0 x=uhs;token）。
        /// 会按需自动续期 access token。
        /// </summary>
        public static async Task<string> GetXboxUpdateAuthAsync(Account acc, CancellationToken ct)
            => await GetXboxAuthAsync(acc, "http://update.xboxlive.com", ct);

        /// <summary>取账号的 XUID（GDK 版存档目录名 Users\&lt;数字ID&gt; 就是它）。</summary>
        public static async Task<string> GetXuidAsync(Account acc, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(acc.Xuid)) return acc.Xuid;
            // 只有 RP=http://xboxlive.com 的 XSTS 响应里才带 xid(真正的 XUID)，
            // update / api.minecraftservices 这些 RP 只给 uhs(是 XUID 的哈希，取不到)。
            var r = await GetXblAndXstsAsync(acc, "http://xboxlive.com", ct);
            if (!string.IsNullOrEmpty(r.Xid)) acc.Xuid = r.Xid;
            return acc.Xuid ?? "";
        }

        /// <summary>
        /// 用指定 RelyingParty 换 XSTS 令牌。不同商店服务要的 RP 不一样：
        /// 取包用 http://update.xboxlive.com，商店集合/版本清单类用 http://licensing.xboxlive.com。
        /// </summary>
        public static async Task<string> GetXboxAuthAsync(Account acc, string relyingParty, CancellationToken ct)
        {
            var r = await GetXblAndXstsAsync(acc, relyingParty, ct);
            return $"XBL3.0 x={r.Uhs};{r.XstsToken}";
        }

        private struct XboxAuth
        {
            public string Uhs;
            public string XstsToken;
            public string Xid;
        }

        private static async Task<XboxAuth> GetXblAndXstsAsync(Account acc, string relyingParty, CancellationToken ct)
        {
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken))
                throw new Exception("请先在「账号」页登录微软账号，再回来下载基岩版。");

            // 注意：这里必须要「微软身份令牌」，不是 acc.AccessToken（那是 Minecraft 令牌，用它会 401）
            var msToken = await MicrosoftAuth.GetMsTokenAsync(acc, ct);

            // 1) Xbox Live
            var xbl = await PostJson(XblUrl, new
            {
                Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msToken },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            }, ct);
            var xblToken = xbl["Token"]?.GetValue<string>();
            var uhs = xbl["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>();
            if (string.IsNullOrEmpty(xblToken))
                throw new Exception("Xbox 认证失败：" + MicrosoftAuth.DecodeXerr(xbl.ToString()));

            // 2) XSTS
            var xsts = await PostJson(XstsUrl, new
            {
                Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
                RelyingParty = relyingParty,
                TokenType = "JWT"
            }, ct);
            var xstsToken = xsts["Token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(xstsToken))
                throw new Exception("XSTS 授权失败：" + MicrosoftAuth.DecodeXerr(xsts.ToString()));

            var xid = xsts["DisplayClaims"]?["xui"]?[0]?["xid"]?.GetValue<string>() ?? "";
            if (!string.IsNullOrEmpty(xid) && string.IsNullOrEmpty(acc.Xuid)) acc.Xuid = xid;

            return new XboxAuth { Uhs = uhs, XstsToken = xstsToken, Xid = xid };
        }

        // ============ 取包文件清单 ============

        public class BedrockFile
        {
            public string FileName = "";
            public long Size;
            /// <summary>候选下载地址（按优先级）：CDN 根路径 + RelativeUrl。</summary>
            public List<string> Urls = new List<string>();
        }

        /// <summary>
        /// 商店的包版本清单接口：能列出该产品的**所有历史版本**（含每个版本的 PackageUri）。
        /// 这就是实现"像 Java 版一样选版本"的关键数据源。
        /// </summary>
        public static async Task<string> GetVersionManifestRawAsync(string authHeader, CancellationToken ct)
            => await GetVersionManifestRawAsync(authHeader, "market=US&languages=en-US&deviceFamily=Windows.Desktop&AlternateId=Microsoft.MinecraftUWP", ct);

        /// <summary>用自定义查询参数试版本清单接口（参数不对时只会返回类型存根）。</summary>
        public static async Task<string> GetVersionManifestRawAsync(string authHeader, string query, CancellationToken ct)
        {
            var url = "https://storeedgefd.dsx.mp.microsoft.com/v9.0/packageManifests/" + ProductId +
                      (string.IsNullOrEmpty(query) ? "" : "?" + query);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", authHeader);
            req.Headers.Add("User-Agent", AppInfo.UserAgent);
            using var resp = await Http.SendAsync(req, ct);
            var txt = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"版本清单接口 HTTP {(int)resp.StatusCode}：{Truncate(txt)}");
            return txt;
        }

        public static async Task<string> GetBasePackageRawAsync(string authHeader, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GetBasePackageUrl);
            req.Headers.Add("Authorization", authHeader);
            req.Headers.Add("User-Agent", AppInfo.UserAgent);
            using var resp = await Http.SendAsync(req, ct);
            var txt = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"商店返回 HTTP {(int)resp.StatusCode}：{Truncate(txt)}");
            return txt;
        }

        public static async Task<List<BedrockFile>> GetPackageFilesAsync(string authHeader, CancellationToken ct)
        {
            var txt = await GetBasePackageRawAsync(authHeader, ct);
            return ParsePackageFiles(txt);
        }

        /// <summary>
        /// 解析 GetBasePackage 响应。
        /// 下载地址规则：CDN 根路径（如 http://assets1.xboxlive.cn/Z/&lt;guid&gt;/）+ RelativeUrl
        /// （RelativeUrl 里已经含 ContentId 与 VersionId 两段，不能只拼文件名）。
        /// 商店给的是 http 根路径，但 assets2 走 https 是好的，所以每个根都生成 https/http 两个候选。
        /// </summary>
        public static List<BedrockFile> ParsePackageFiles(string txt)
        {
            var files = new List<BedrockFile>();
            var node = JsonNode.Parse(txt) as JsonObject;
            var metaRoots = new List<string>();
            if (node?["PackageMetadata"]?["CdnRoots"] is JsonArray mr)
                foreach (var r in mr)
                {
                    var s = r?.GetValue<string>();
                    if (!string.IsNullOrEmpty(s)) metaRoots.Add(s);
                }

            if (node?["PackageFiles"] is JsonArray arr)
            {
                foreach (var it in arr)
                {
                    var fn = it?["FileName"]?.GetValue<string>() ?? "";
                    if (string.IsNullOrEmpty(fn)) continue;

                    var rel = it?["RelativeUrl"]?.GetValue<string>() ?? "";
                    var size = it?["FileSize"]?.GetValue<long>() ?? 0;
                    var roots = new List<string>();
                    foreach (var key in new[] { "CdnRootPaths", "BackgroundCdnRootPaths" })
                    {
                        if (it?[key] is JsonArray rs)
                            foreach (var r in rs)
                            {
                                var s = r?.GetValue<string>();
                                if (!string.IsNullOrEmpty(s)) roots.Add(s);
                            }
                    }
                    foreach (var r in metaRoots) roots.Add(r);

                    var bf = new BedrockFile { FileName = fn, Size = size };
                    // 旧的字段形态：有直链 URL 就直接用
                    var direct = it?["URL"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(direct)) bf.Urls.Add(direct);
                    if (string.IsNullOrEmpty(rel)) rel = fn;

                    foreach (var root in roots)
                    {
                        var r = root.TrimEnd('/');
                        var path = r.Contains("://") ? r.Substring(r.IndexOf("://") + 3) : r;
                        var schemeIdx = r.IndexOf("://");
                        var scheme = schemeIdx >= 0 ? r.Substring(0, schemeIdx) : "http";
                        // https 优先（assets1 的 https 有问题会自动降级到下一个候选）
                        bf.Urls.Add($"https://{path}/{rel}");
                        if (!string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
                            bf.Urls.Add($"http://{path}/{rel}");
                    }
                    if (bf.Urls.Count > 0) files.Add(bf);
                }
            }
            if (files.Count == 0)
                throw new Exception("商店没有返回可下载的文件。原始响应：" + Truncate(txt) +
                    "——多半是该微软账号没有基岩版的拥有/试用资格，请改用「打开微软商店」安装。");
            return files;
        }

        // ============ 下载 ============

        /// <summary>下载所有包文件，支持断点续传与多 CDN 候选降级。</summary>
        public static async Task DownloadAsync(List<BedrockFile> files, string dir, IProgress<DlProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(dir);
            long totalBytes = files.Sum(f => f.Size);
            long doneBytes = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = files[i];
                var dest = Path.Combine(dir, f.FileName);
                long already = File.Exists(dest) ? new FileInfo(dest).Length : 0;
                if (f.Size > 0 && already >= f.Size) { doneBytes += f.Size; continue; }

                progress.Report(new DlProgress
                {
                    Stage = "下载安装包",
                    Current = f.FileName,
                    Done = i,
                    Total = files.Count,
                    DoneBytes = doneBytes + already,
                    TotalBytes = totalBytes
                });

                string lastErr = null;
                foreach (var url in f.Urls)
                {
                    try
                    {
                        await DownloadOneAsync(url, dest, already, f.Size, ct,
                            (got, total) =>
                            {
                                var sp = sw.Elapsed.TotalSeconds > 0 ? (got - already) / sw.Elapsed.TotalSeconds : 0;
                                progress.Report(new DlProgress
                                {
                                    Stage = "下载安装包",
                                    Current = f.FileName,
                                    Done = i,
                                    Total = files.Count,
                                    DoneBytes = doneBytes + got,
                                    TotalBytes = totalBytes,
                                    SpeedMBps = sp / 1048576.0
                                });
                            });
                        lastErr = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex.Message;
                        Log.Write("bedrock dl candidate fail: " + url + " -> " + ex.Message);
                    }
                }
                if (lastErr != null)
                    throw new Exception($"下载 {f.FileName} 失败：{lastErr}");

                doneBytes += f.Size > 0 ? f.Size : new FileInfo(dest).Length;
            }

            progress.Report(new DlProgress
            {
                Stage = "下载完成",
                Done = files.Count,
                Total = files.Count,
                DoneBytes = totalBytes,
                TotalBytes = totalBytes
            });
        }

        private static async Task DownloadOneAsync(string url, string dest, long resumeFrom, long expectedSize,
            CancellationToken ct, Action<long, long> onProgress)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeFrom > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);

            using var resp = await HttpDl.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // 本地文件已经比服务器还大，直接截断重下
                using (var fs2 = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)) { }
                return;
            }
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)resp.StatusCode}");

            var total = expectedSize > 0 ? expectedSize : (resp.Content.Headers.ContentLength ?? 0) + resumeFrom;
            long got = resumeFrom;

            using (var fs = new FileStream(dest, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                fs.Seek(resumeFrom, SeekOrigin.Begin);
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[1024 * 256];
                while (true)
                {
                    // 卡死检测：90 秒读不到任何数据就判定这条 CDN 线路废了
                    var readTask = stream.ReadAsync(buf.AsMemory(), ct).AsTask();
                    var watchdog = Task.Delay(TimeSpan.FromSeconds(90), ct);
                    var finished = await Task.WhenAny(readTask, watchdog);
                    if (finished != readTask)
                        throw new Exception("下载卡住超过 90 秒");
                    int n = await readTask;
                    if (n <= 0) break;
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    got += n;
                    onProgress(got, total);
                }
                await fs.FlushAsync(ct);
            }
        }

        // ============ 安装 ============

        public static void Install(string dir)
        {
            var main = FindMainPackage(dir);
            if (main == null)
                throw new Exception($"没有找到可安装的包文件（文件已下载到 {dir}），请改用「打开微软商店」安装。");
            // Add-AppxPackage 对 MSIXVC / APPX 直接注册；包内分块会被一并处理
            var script =
                "try { " +
                $"Add-AppxPackage -Path '{main.Replace("'", "''")}' -ForceApplicationShutdown -ErrorAction Stop; " +
                "Write-Output '__OK__' } catch { Write-Output ('__FAIL__' + $_.Exception.Message) }";
            var outp = RunPs(script);
            if (!outp.Contains("__OK__"))
            {
                var msg = outp.Contains("__FAIL__")
                    ? outp.Substring(outp.IndexOf("__FAIL__") + 8).Trim()
                    : outp.Trim();
                throw new Exception("安装被系统拒绝：" + (string.IsNullOrEmpty(msg) ? "(无输出)" : msg));
            }
        }

        private static string FindMainPackage(string dir)
        {
            // 优先主包（MSIXVC 是新基岩版的格式），其次常见打包格式
            var prio = new[] { ".msixvc", ".appx", ".msix", ".msixbundle", ".appxbundle" };
            foreach (var ext in prio)
            {
                var hit = Directory.GetFiles(dir, "*" + ext, SearchOption.TopDirectoryOnly);
                if (hit.Length > 0) return hit[0];
            }
            return null;
        }

        // ============ 工具 ============

        /// <summary>诊断用：把 XSTS 原始响应打出来（看 DisplayClaims 里到底有没有 xid）。</summary>
        public static async Task<string> DebugXstsAsync(Account acc, string relyingParty, CancellationToken ct)
        {
            var msToken = await MicrosoftAuth.GetMsTokenAsync(acc, ct);
            var xbl = await PostJson(XblUrl, new
            {
                Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msToken },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            }, ct);
            var xblToken = xbl["Token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(xblToken)) return "XBL FAIL: " + xbl.ToJsonString();
            var xsts = await PostJson(XstsUrl, new
            {
                Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
                RelyingParty = relyingParty,
                TokenType = "JWT"
            }, ct);
            return xsts.ToJsonString();
        }

        private static async Task<JsonObject> PostJson(string url, object body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req, ct);
            var txt = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)resp.StatusCode}：{Truncate(txt)}");
            try { return JsonNode.Parse(txt) as JsonObject ?? new JsonObject(); }
            catch { return new JsonObject(); }
        }

        private static string Truncate(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 600 ? s.Substring(0, 600) + "…" : s);

        /// <summary>用 -EncodedCommand 跑 PowerShell，避免中文路径在 5.1 下被按 ANSI 解坏。</summary>
        private static string RunPs(string script)
        {
            // $ProgressPreference 屏蔽"正在准备首次使用模块"这类进度流：
            // 它在 stdout 被重定向时会以 CLIXML 形式混进来，把版本号冲成乱码。
            var wrapped = "$ProgressPreference='SilentlyContinue';" +
                          "$OutputEncoding=[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;" + script;
            var bytes = Encoding.Unicode.GetBytes(wrapped);
            var b64 = Convert.ToBase64String(bytes);
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand {b64}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var s = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return s + (string.IsNullOrWhiteSpace(s) ? err : "");
        }
    }
}
