import io

# 1) Config 加 Theme
p = 'HHCL/Core/Config.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        /// <summary>启动前自动把游戏语言切成简体中文（改 options.txt 的 lang 项）。</summary>
        public bool AutoChineseLang { get; set; } = true;'''
new = '''        /// <summary>启动前自动把游戏语言切成简体中文（改 options.txt 的 lang 项）。</summary>
        public bool AutoChineseLang { get; set; } = true;
        /// <summary>界面主题：dark（默认，黑）/ light（白）。</summary>
        public string Theme { get; set; } = "dark";'''
assert old in s, 'config anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('Config: 已加 Theme')

# 2) App 启动时应用主题
p = 'HHCL/App.xaml.cs'
s = io.open(p, encoding='utf-8').read()
anchor = '''            if (e.Args.Length >= 2 && e.Args[0] == "--shot")
            {'''
new = '''            // 主题要在任何界面创建之前应用
            try { Core.ThemeManager.Apply(Core.AppState.Current.Config.Theme); }
            catch (Exception ex) { Log.Write("theme: " + ex.Message); }

            if (e.Args.Length >= 2 && e.Args[0] == "--shot")
            {'''
assert anchor in s, 'app anchor'
s = s.replace(anchor, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('App: 启动时应用主题')
