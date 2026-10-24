using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>微软设备码登录的进度回调。</summary>
    public class MsLoginState
    {
        /// <summary>需要用户在网页上输入的一次性代码。</summary>
        public string UserCode { get; set; } = "";
        /// <summary>用户要打开的网址。</summary>
        public string VerifyUrl { get; set; } = "";
        /// <summary>给界面显示的当前状态。</summary>
        public string Message { get; set; } = "";
        public bool Done { get; set; }
        public Account Result { get; set; }
    }

    public static class MicrosoftAuth
    {
        // 与 PCL2 一致：走设备码流程，client_id 就用它注册的那个
        private const string ClientId = "fe72edc2-3a6f-4280-90e8-e2beb64ce7e1";
        private const string Scope = "XboxLive.signin offline_access";
        private const string DevicecodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string LoginWithXboxUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
        private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
        private const string EntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

        /// <summary>
        /// 完整的设备码登录流程。onState 用于把 user_code 和进度抛给界面，
        /// 返回登录好的账号（含 access/refresh token、uuid、xuid）。
        /// </summary>
        public static async Task<Account> LoginAsync(Action<MsLoginState> onState, CancellationToken ct)
        {
            var st = new MsLoginState();

            // 1) 申请设备码
            st.Message = "正在向微软申请登录码…";
            onState?.Invoke(st);

            var (deviceCode, userCode, verifyUrl, interval) = await RequestDeviceCodeAsync(ct);
            st.UserCode = userCode;
            st.VerifyUrl = verifyUrl;

            if (string.IsNullOrEmpty(deviceCode)) throw new Exception("微软没有返回登录码");

            st.Message = $"请在网页里输入 {st.UserCode}，然后等待自动完成…";
            onState?.Invoke(st);

            // 2) 轮询换 token
            string msToken = null, refresh = null;
            long msTokenExpire = 0;
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline) throw new Exception("登录超时，请重试");

                await Task.Delay(TimeSpan.FromSeconds(interval), ct);

                var req2 = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["client_id"] = ClientId,
                        ["device_code"] = deviceCode
                    })
                };
                var r2 = await SendJson(req2, ct);
                var err = r2["error"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(err))
                {
                    if (err == "authorization_pending") continue;
                    // 微软让我们放慢轮询，按规范加 5 秒再继续
                    if (err == "slow_down") { interval += 5; continue; }
                    if (err == "expired_token") throw new Exception("登录码已过期，请重新点一次登录。");
                    throw new Exception(r2["error_description"]?.GetValue<string>() ?? err);
                }
                msToken = r2["access_token"]?.GetValue<string>();
                refresh = r2["refresh_token"]?.GetValue<string>();
                if (string.IsNullOrEmpty(msToken)) throw new Exception("没有拿到微软令牌");
                msTokenExpire = DateTimeOffset.UtcNow.AddSeconds(r2["expires_in"]?.GetValue<int>() ?? 3600).ToUnixTimeSeconds();
                break;
            }

            st.Message = "微软登录成功，正在换取 Xbox 令牌…";
            onState?.Invoke(st);

            // 3) Xbox Live
            var xblReq = new HttpRequestMessage(HttpMethod.Post, XblUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    Properties = new
                    {
                        AuthMethod = "RPS",
                        SiteName = "user.auth.xboxlive.com",
                        RpsTicket = "d=" + msToken
                    },
                    RelyingParty = "http://auth.xboxlive.com",
                    TokenType = "JWT"
                }), Encoding.UTF8, "application/json")
            };
            var xbl = await SendJson(xblReq, ct);
            var xblToken = xbl["Token"]?.GetValue<string>();
            var uhs = xbl["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>();
            if (string.IsNullOrEmpty(xblToken))
            {
                var xerr = xbl["XErr"]?.GetValue<long>() ?? 0;
                throw new Exception("Xbox 认证失败：" + (xerr != 0 ? DecodeXerr($"\"XErr\":{xerr}") : "这个微软账号可能没有 Xbox 档案，或没有完成 Xbox 验证。"));
            }

            // 4) XSTS
            var xstsReq = new HttpRequestMessage(HttpMethod.Post, XstsUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    Properties = new
                    {
                        SandboxId = "RETAIL",
                        UserTokens = new[] { xblToken }
                    },
                    RelyingParty = "rp://api.minecraftservices.com/",
                    TokenType = "JWT"
                }), Encoding.UTF8, "application/json")
            };
            JsonObject xsts;
            try { xsts = await SendJson(xstsReq, ct); }
            catch (Exception ex)
            {
                throw new Exception("XSTS 授权失败：" + DecodeXerr(ex.Message));
            }
            var xstsToken = xsts["Token"]?.GetValue<string>();
            var xuid = xsts["DisplayClaims"]?["xui"]?[0]?["xid"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(xstsToken))
            {
                var xerr = xsts["XErr"]?.GetValue<long>() ?? 0;
                throw new Exception("XSTS 授权失败：" + (xerr != 0 ? DecodeXerr($"\"XErr\":{xerr}") : "没有拿到 XSTS 令牌。"));
            }

            // 顺带用 xboxlive.com 这个 RP 再换一次：只有它的响应里带 xid(真正的 XUID)，
            // 上面的 minecraftservices RP 只给 uhs(哈希)。基岩版存档目录名要用 xid。
            if (string.IsNullOrEmpty(xuid))
            {
                try
                {
                    var xl = await PostJsonAsync(XstsUrl, new
                    {
                        Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
                        RelyingParty = "http://xboxlive.com",
                        TokenType = "JWT"
                    }, ct);
                    xuid = xl["DisplayClaims"]?["xui"]?[0]?["xid"]?.GetValue<string>() ?? "";
                }
                catch { }
            }

            st.Message = "正在登录 Minecraft…";
            onState?.Invoke(st);

            // 5) 换 MC token
            var mcReq = new HttpRequestMessage(HttpMethod.Post, LoginWithXboxUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    identityToken = $"XBL3.0 x={uhs};{xstsToken}"
                }), Encoding.UTF8, "application/json")
            };
            var mc = await SendJson(mcReq, ct);
            var accessToken = mc["access_token"]?.GetValue<string>();
            var expires = mc["expires_in"]?.GetValue<int>() ?? 86400;
            if (string.IsNullOrEmpty(accessToken)) throw new Exception("Minecraft 登录失败");

            // 6) 拿角色信息
            var profReq = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
            profReq.Headers.Add("Authorization", "Bearer " + accessToken);
            var prof = await SendJson(profReq, ct);
            var name = prof["name"]?.GetValue<string>();
            var uuid = prof["id"]?.GetValue<string>();

            bool owns = false;
            try
            {
                var entReq = new HttpRequestMessage(HttpMethod.Get, EntitlementsUrl);
                entReq.Headers.Add("Authorization", "Bearer " + accessToken);
                var ent = await SendJson(entReq, ct);
                if (ent["items"] is JsonArray items)
                    foreach (var it in items)
                        if ((it?["name"]?.GetValue<string>() ?? "").Contains("game_minecraft")) owns = true;
            }
            catch { }

            if (string.IsNullOrEmpty(name))
                throw new Exception(!owns
                    ? "这个微软账号没有购买 Minecraft（Java 版），无法登录。"
                    : "能登录但没读到角色档案，可能还没创建游戏角色。");

            var acc = new Account
            {
                Name = name,
                Uuid = uuid ?? "",
                Type = "microsoft",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AccessToken = accessToken,
                RefreshToken = refresh ?? "",
                Xuid = xuid,
                TokenExpire = DateTimeOffset.UtcNow.AddSeconds(expires).ToUnixTimeSeconds(),
                // 微软身份令牌要单独留着：XBL/XSTS 鉴权（以及基岩版商店授权）用的是它，
                // 不能拿 Minecraft 令牌去顶替，否则一律 401。
                MsAccessToken = msToken ?? "",
                MsTokenExpire = msTokenExpire
            };

            st.Message = "登录完成";
            st.Done = true;
            st.Result = acc;
            onState?.Invoke(st);
            return acc;
        }

        /// <summary>只申请设备码，不轮询。返回 (deviceCode, userCode, verifyUrl, interval秒)。</summary>
        public static async Task<(string deviceCode, string userCode, string verifyUrl, int interval)> RequestDeviceCodeAsync(CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, DevicecodeUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"] = Scope
                })
            };
            var r = await SendJson(req, ct);
            var deviceCode = r["device_code"]?.GetValue<string>();
            if (string.IsNullOrEmpty(deviceCode))
                throw new Exception(r["error_description"]?.GetValue<string>() ?? "微软没有返回登录码");
            return (
                deviceCode,
                r["user_code"]?.GetValue<string>() ?? "",
                r["verification_uri"]?.GetValue<string>() ?? "https://www.microsoft.com/link",
                r["interval"]?.GetValue<int>() ?? 5
            );
        }

        /// <summary>
        /// 取一个可用的微软身份令牌（audience=user.auth.xboxlive.com），用于 Xbox/XBL 鉴权。
        /// 账号里没有或快过期时，用 refresh token 换一个新的并写回账号。
        /// </summary>
        public static async Task<string> GetMsTokenAsync(Account acc, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(acc.RefreshToken))
                throw new Exception("账号里没有微软 refresh token，请重新登录微软账号。");

            if (!string.IsNullOrEmpty(acc.MsAccessToken) &&
                acc.MsTokenExpire > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds())
                return acc.MsAccessToken;

            var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["refresh_token"] = acc.RefreshToken,
                    ["grant_type"] = "refresh_token",
                    ["scope"] = Scope
                })
            };
            var r = await SendJson(req, ct);
            var at = r["access_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(at))
                throw new Exception("续期微软令牌失败：" + (r["error_description"]?.GetValue<string>() ?? r["error"]?.GetValue<string>() ?? "未知错误"));
            acc.MsAccessToken = at;
            acc.MsTokenExpire = DateTimeOffset.UtcNow.AddSeconds(r["expires_in"]?.GetValue<int>() ?? 3600).ToUnixTimeSeconds();
            var newRefresh = r["refresh_token"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(newRefresh)) acc.RefreshToken = newRefresh;
            return at;
        }

        /// <summary>
        /// 续期账号的 Minecraft 令牌。
        /// 注意：refresh token 换来的是微软身份令牌，必须再走一遍 XBL → XSTS → login_with_xbox
        /// 才能拿到游戏真正需要的 Minecraft 令牌。旧实现直接把微软身份令牌写进 AccessToken，
        /// 会导致刷新后游戏在线认证失败。
        /// </summary>
        public static async Task<bool> RefreshAsync(Account acc, CancellationToken ct)
        {
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken)) return false;
            try
            {
                // 1) 微软身份令牌
                var msToken = await GetMsTokenAsync(acc, ct);

                // 2) Xbox Live
                var xbl = await PostJsonAsync(XblUrl, new
                {
                    Properties = new
                    {
                        AuthMethod = "RPS",
                        SiteName = "user.auth.xboxlive.com",
                        RpsTicket = "d=" + msToken
                    },
                    RelyingParty = "http://auth.xboxlive.com",
                    TokenType = "JWT"
                }, ct);
                var xblToken = xbl["Token"]?.GetValue<string>();
                var uhs = xbl["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>();
                if (string.IsNullOrEmpty(xblToken)) return false;

                // 3) XSTS
                var xsts = await PostJsonAsync(XstsUrl, new
                {
                    Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
                    RelyingParty = "rp://api.minecraftservices.com/",
                    TokenType = "JWT"
                }, ct);
                var xstsToken = xsts["Token"]?.GetValue<string>();
                if (string.IsNullOrEmpty(xstsToken)) return false;

                // 4) 换 Minecraft 令牌
                var mc = await PostJsonAsync(LoginWithXboxUrl, new
                {
                    identityToken = $"XBL3.0 x={uhs};{xstsToken}"
                }, ct);
                var accessToken = mc["access_token"]?.GetValue<string>();
                if (string.IsNullOrEmpty(accessToken)) return false;
                acc.AccessToken = accessToken;
                acc.TokenExpire = DateTimeOffset.UtcNow.AddSeconds(mc["expires_in"]?.GetValue<int>() ?? 86400).ToUnixTimeSeconds();
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("refresh ms token fail: " + ex.Message);
                return false;
            }
        }

        private static async Task<JsonObject> PostJsonAsync(string url, object body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            return await SendJson(req, ct);
        }

        private static async Task<JsonObject> SendJson(HttpRequestMessage req, CancellationToken ct)
        {
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
            try { return JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
            catch
            {
                if ((int)resp.StatusCode >= 400) throw new Exception($"HTTP {(int)resp.StatusCode}：{Truncate(text)}");
                return new JsonObject();
            }
        }

        private static string Truncate(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 600 ? s.Substring(0, 600) + "…" : s);

        /// <summary>从 Xbox / Minecraft 返回的错误体里抠 XErr 码，给出可读的中文原因。</summary>
        internal static string DecodeXerr(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return "（微软没给具体原因）";
            var i = msg.IndexOf("\"XErr\"", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                var j = msg.IndexOf(':', i);
                var k = msg.IndexOfAny(new[] { ',', '}', '"' }, i);
                var end = (j >= 0 && (k < 0 || j < k)) ? j : k;
                if (end > i)
                {
                    var numStr = msg.Substring(i, end - i)
                        .Replace("\"XErr\"", "", StringComparison.OrdinalIgnoreCase)
                        .Replace(":", "").Replace(" ", "").Trim();
                    if (long.TryParse(numStr, out var code) && code != 0)
                    {
                        return code switch
                        {
                            2148916233 => "这个微软账号还没有 Xbox 档案——先去 xbox.com 用这个账号登录一次、接受 Xbox 服务条款，再回来登录。",
                            2148916238 => "账号所在地区不支持，或被家长控制限制（未成年账号需由成人微软账号授权后才能玩）。",
                            2148916242 => "需要先完成成人验证才能登录 Xbox。",
                            2148916235 => "该微软账号已被禁止登录 Xbox Live。",
                            _ => $"XErr={code}（多半是账号没有 Xbox 档案，或被家长控制限制）"
                        };
                    }
                }
            }
            return msg;
        }
    }
}
