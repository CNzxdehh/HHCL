import io

p = 'HHCL/Core/Config.cs'
s = io.open(p, encoding='utf-8').read()

# 1) GameDir 默认值不再写死本机路径
old = '''        public string GameDir { get; set; } = @"E:\\.minecraft";'''
new = '''        /// <summary>游戏目录。留空时首次运行会自动探测（别写死本机路径，发给别人用会出错）。</summary>
        public string GameDir { get; set; } = "";'''
assert old in s, 'GameDir anchor'
s = s.replace(old, new, 1)

# 2) 加载时自动探测
old2 = '''                    if (cfg != null)
                    {
                        if (cfg.Accounts == null) cfg.Accounts = new List<Account>();
                        return cfg;
                    }'''
new2 = '''                    if (cfg != null)
                    {
                        if (cfg.Accounts == null) cfg.Accounts = new List<Account>();
                        if (string.IsNullOrWhiteSpace(cfg.GameDir)) cfg.GameDir = DetectGameDir();
                        return cfg;
                    }'''
assert old2 in s, 'load anchor'
s = s.replace(old2, new2, 1)

old3 = '''            return new Config();
        }'''
new3 = '''            var fresh = new Config();
            fresh.GameDir = DetectGameDir();
            return fresh;
        }

        /// <summary>
        /// 自动探测游戏目录：优先用 Minecraft 启动器（官方）留下的目录，
        /// 否则用 %APPDATA%\\.minecraft。写死路径的话，发给别人用就会指到不存在的盘。
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
        }'''
assert old3 in s, 'new config anchor'
s = s.replace(old3, new3, 1)

# 需要 using System.Collections.Generic / System.IO
if 'using System.Collections.Generic;' not in s:
    s = s.replace('using System.Text.Json.Serialization;', 'using System.Collections.Generic;\nusing System.IO;\nusing System.Text.Json.Serialization;', 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('Config: GameDir 改为动态探测')
