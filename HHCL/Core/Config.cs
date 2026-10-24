using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HHCL.Core
{
    public class Account
    {
        public string Name { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string Type { get; set; } = "offline";
        public long Created { get; set; }

        // 微软正版登录用到：AccessToken 给游戏做在线验证，RefreshToken 用于下次免登录续期
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string Xuid { get; set; } = "";
        /// <summary>access token 的过期时间（Unix 秒）。</summary>
        public long TokenExpire { get; set; }

        /// <summary>
        /// 微软身份令牌（audience 是 user.auth.xboxlive.com），用于 Xbox/XBL 鉴权。
        /// 注意与 <see cref="AccessToken"/> 区分：那个是 Minecraft 服务令牌，喂给游戏的 auth_access_token，
        /// 两者不可混用（混用会导致 401 或游戏内在线认证失败）。
        /// </summary>
        public string MsAccessToken { get; set; } = "";
        /// <summary>微软身份令牌的过期时间（Unix 秒）。</summary>
        public long MsTokenExpire { get; set; }

        [JsonIgnore]
        public string TypeText => Type == "offline" ? "离线登录" : "微软登录";

        [JsonIgnore]
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpper();

        public static string OfflineUuid(string name)
        {
            // 与 Java UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF8)) 一致
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant
            var sb = new StringBuilder(32);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public string FormattedUuid =>
            Uuid.Length == 32
                ? $"{Uuid.Substring(0, 8)}-{Uuid.Substring(8, 4)}-{Uuid.Substring(12, 4)}-{Uuid.Substring(16, 4)}-{Uuid.Substring(20, 12)}"
                : Uuid;
    }

    public class Config
    {
        /// <summary>游戏目录。留空时首次运行会自动探测（别写死本机路径，发给别人用会出错）。</summary>
        public string GameDir { get; set; } = "";
        public string JavaPath { get; set; } = "";
        public int MemoryMin { get; set; } = 1024;
        public int MemoryMax { get; set; } = 8192;
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;
        public string ExtraJvm { get; set; } = "";
        public bool IsolateVersions { get; set; } = true;
        public bool MinimizeOnLaunch { get; set; } = false;
        public bool CloseOnLaunch { get; set; } = false;
        /// <summary>启动前自动把游戏语言切成简体中文（改 options.txt 的 lang 项）。</summary>
        public bool AutoChineseLang { get; set; } = true;
        /// <summary>界面主题：dark（默认，黑）/ light（白）。</summary>
        public string Theme { get; set; } = "dark";
        public string DownloadSource { get; set; } = "bmclapi";
        public string SelectedVersion { get; set; } = "";
        public string SelectedAccount { get; set; } = "";
        public List<Account> Accounts { get; set; } = new List<Account>();
        public double WindowW { get; set; } = 1180;
        public double WindowH { get; set; } = 760;

        /// <summary>传给游戏的 --clientId，MC 26.x 起必填，缺失会直接崩。生成一个稳定的随机值即可。</summary>
        public string ClientId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>CurseForge API Key，留空则模组页只用 Modrinth。</summary>
        public string CurseForgeApiKey { get; set; } = "";

        /// <summary>基岩版存档方案列表（每套一个独立数据目录）。</summary>
        public List<BedrockProfile> BedrockProfiles { get; set; } = new List<BedrockProfile>();

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string DataDir
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HHCL");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ConfigPath => Path.Combine(DataDir, "config.json");

        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath, Encoding.UTF8), Options);
                    if (cfg != null)
                    {
                        if (cfg.Accounts == null) cfg.Accounts = new List<Account>();
                        if (string.IsNullOrWhiteSpace(cfg.GameDir)) cfg.GameDir = DetectGameDir();
                        return cfg;
                    }
                }
            }
            catch { }
            var fresh = new Config();
            fresh.GameDir = DetectGameDir();
            return fresh;
        }

        /// <summary>
        /// 自动探测游戏目录：优先用 Minecraft 启动器（官方）留下的目录，
        /// 否则用 %APPDATA%\.minecraft。写死路径的话，发给别人用就会指到不存在的盘。
        /// </summary>
        private static string DetectGameDir()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(appdata))
            {
                candidates.Add(Path.Combine(appdata, ".minecraft"));
                // 官方启动器有时放在 OneDrive 里
                var one = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(one))
                    candidates.Add(Path.Combine(one, "OneDrive", ".minecraft"));
            }
            candidates.Add(Path.Combine(Path.GetTempPath(), ".minecraft"));

            foreach (var c in candidates)
                if (Directory.Exists(c)) return c;

            var fallback = Path.Combine(appdata, ".minecraft");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        public void Save()
        {
            try
            {
                var utf8 = new UTF8Encoding(false);
                var tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options), utf8);
                File.Copy(tmp, ConfigPath, true);
                File.Delete(tmp);
            }
            catch { }
        }

        public Account CurrentAccount
        {
            get
            {
                if (Accounts.Count == 0)
                {
                    var acc = new Account { Name = "Steve", Uuid = Account.OfflineUuid("Steve"), Type = "offline", Created = DateTimeOffset.Now.ToUnixTimeSeconds() };
                    Accounts.Add(acc);
                    SelectedAccount = acc.Name;
                    return acc;
                }
                var cur = Accounts.Find(a => a.Name == SelectedAccount);
                if (cur == null)
                {
                    cur = Accounts[0];
                    SelectedAccount = cur.Name;
                }
                return cur;
            }
        }
    }
}
