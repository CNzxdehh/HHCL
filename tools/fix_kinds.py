import io

# ============ 1) GameVersionKind：支持 version_type（release/beta/alpha） ============
p = 'HHCL/Core/VersionScanner.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        public static string Of(string gameVersion)
        {
            var g = (gameVersion ?? "").Trim();
            if (g.Length == 0) return "";
            var low = g.ToLowerInvariant();
            if (low.Contains("-pre") || low.Contains("-rc") || low.Contains("beta"))
                return "测试版";
            if (g.Any(char.IsLetter)) return "快照";   // 24w14a 这种日期快照
            return "正式版";
        }'''
new = '''        public static string Of(string gameVersion)
        {
            var g = (gameVersion ?? "").Trim();
            if (g.Length == 0) return "";
            var low = g.ToLowerInvariant();
            if (low.Contains("-pre") || low.Contains("-rc") || low.Contains("beta"))
                return "测试版";
            if (g.Any(char.IsLetter)) return "快照";   // 24w14a 这种日期快照
            return "正式版";
        }

        /// <summary>
        /// 按 Modrinth 的 version_type 字段判断（release / beta / alpha），
        /// 这才是"这个模组版本本身"是正式版还是预发布版的依据。
        /// 之前只看游戏版本号，导致整合包 "15.00-beta.2" 被标成"正式版"。
        /// </summary>
        public static string OfVersionType(string versionType, string fallbackGameVersion)
        {
            var t = (versionType ?? "").Trim().ToLowerInvariant();
            switch (t)
            {
                case "release": return "正式版";
                case "beta": return "测试版";
                case "alpha": return "早期版";
                case "snapshot": return "快照";
            }
            return Of(fallbackGameVersion);
        }'''
assert old in s, 'GameVersionKind anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('GameVersionKind: 加了 OfVersionType')

# ============ 2) ModrinthVersion 加 VersionType ============
p = 'HHCL/Core/Modrinth.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        public bool Primary { get; set; }
        public List<string> GameVersions { get; set; } = new List<string>();'''
new = '''        public bool Primary { get; set; }
        /// <summary>Modrinth 的 version_type：release / beta / alpha。</summary>
        public string VersionType { get; set; } = "";
        public List<string> GameVersions { get; set; } = new List<string>();'''
assert old in s, 'ModrinthVersion anchor'
s = s.replace(old, new, 1)

old = '''        public string KindText => GameVersionKind.Of(GameVersions.FirstOrDefault() ?? "");'''
new = '''        public string KindText => GameVersionKind.OfVersionType(VersionType, GameVersions.FirstOrDefault() ?? "");'''
assert old in s, 'KindText anchor'
s = s.replace(old, new, 1)

# 版本列表 API 里取 version_type
old = '''                    Name = o["name"]?.GetValue<string>() ?? "",
                    VersionNumber = o["version_number"]?.GetValue<string>() ?? "",'''
new = '''                    Name = o["name"]?.GetValue<string>() ?? "",
                    VersionNumber = o["version_number"]?.GetValue<string>() ?? "",
                    VersionType = o["version_type"]?.GetValue<string>() ?? "",'''
assert old in s, 'version parse anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('ModrinthVersion: 加了 VersionType 并接入解析')

# ============ 3) ModpackRelease 加 VersionType，McHint 只取第一个游戏版本 ============
p = 'HHCL/Core/ModpackInstaller.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        public string McHint { get; set; } = "";'''
new = '''        public string McHint { get; set; } = "";
        /// <summary>Modrinth 的 version_type：release / beta / alpha（整合包自己的版本类型）。</summary>
        public string VersionType { get; set; } = "";
        /// <summary>该整合包版本支持的全部游戏版本（第一个是主要目标）。</summary>
        public string AllGameVersions { get; set; } = "";'''
assert old in s, 'ModpackRelease anchor'
s = s.replace(old, new, 1)

old = '''        public string KindText => GameVersionKind.Of((McHint ?? "").Split(',').FirstOrDefault()?.Trim() ?? "");'''
new = '''        public string KindText => GameVersionKind.OfVersionType(VersionType, (McHint ?? "").Split(',').FirstOrDefault()?.Trim() ?? "");'''
assert old in s, 'Modpack KindText anchor'
s = s.replace(old, new, 1)

old = '''                    McHint = string.Join(",", (gv?.Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>()).Take(2)),'''
new = '''                    // 组头只要一个主版本（多个版本号逗号连在一起会很难看），
                    // 全部版本另存一份，供"该版本适配哪些游戏"展示
                    McHint = (gv?.Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>()).FirstOrDefault() ?? "",
                    AllGameVersions = string.Join(", ", (gv?.Select(x => x?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>()).Take(4)),
                    VersionType = o["version_type"]?.GetValue<string>() ?? "",'''
assert old in s, 'McHint anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('ModpackRelease: 加了 VersionType / AllGameVersions，McHint 只取首个版本')
