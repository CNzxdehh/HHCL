import io, re, glob

# 1) ThemeManager：加渐变画刷支持
p = 'HHCL/Core/ThemeManager.cs'
s = io.open(p, encoding='utf-8').read()

old = '''        public static string Current { get; private set; } = Dark;'''
new = '''        /// <summary>渐变画刷 key → 深色各色标 / 浅色各色标。</summary>
        private static readonly (string Key, string[] Dark, string[] Light)[] Gradients =
        {
            // 窗口背景（主内容区那层）：浅色下要是浅灰渐变，不能留深色
            ("WindowBgBrush",
                new[] { "#FF20242F", "#FF1C1C22", "#FF191A1E" },
                new[] { "#FFF7F7F9", "#FFF2F2F5", "#FFEDEDEF" }),
            // 首页英雄卡
            ("HeroGradientBrush",
                new[] { "#FF1F3350", "#FF232636", "#FF242429" },
                new[] { "#FFDCE9F7", "#FFEAF1F9", "#FFF4F7FB" }),
            // 强调渐变（头像圆、logo 底、指示条）
            ("AccentGradientBrush",
                new[] { "#FF2B9BE8", "#FF0F6CBD" },
                new[] { "#FF3BA0EE", "#FF0A5AA8" }),
            // logo 渐变：两套主题保持一致
            ("LogoGradientBrush",
                new[] { "#FF5BC0F8", "#FF0078D4", "#FF5A4FD4" },
                new[] { "#FF5BC0F8", "#FF0078D4", "#FF5A4FD4" }),
        };

        public static string Current { get; private set; } = Dark;'''
assert old in s, 'current anchor'
s = s.replace(old, new, 1)

# 2) 用统一的渐变处理替换原来只处理 HeroGradientBrush 的那段
old2 = '''            // 首页的渐变卡片（同样是冻结的，要整个换掉）
            var grad = new LinearGradientBrush();
            grad.StartPoint = new Point(0, 0);
            grad.EndPoint = new Point(1, 1);
            if (Current == Light)
            {
                grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFDCE9F7"), 0));
                grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFEAF1F9"), 0.55));
                grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFF4F7FB"), 1));
            }
            else
            {
                grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF1F3350"), 0));
                grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF232636"), 0.55));
                grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF242429"), 1));
            }
            res["HeroGradientBrush"] = grad;'''
new2 = '''            // 渐变画刷同样是冻结的，要整个换掉
            foreach (var (key, dark, light) in Gradients)
            {
                var stops = Current == Light ? light : dark;
                var grad = new LinearGradientBrush();
                grad.StartPoint = new Point(0, 0);
                grad.EndPoint = new Point(1, 1);
                for (int i = 0; i < stops.Length; i++)
                    grad.GradientStops.Add(new GradientStop(
                        (Color)ColorConverter.ConvertFromString(stops[i]),
                        stops.Length == 1 ? 0 : (double)i / (stops.Length - 1)));
                res[key] = grad;
            }'''
assert old2 in s, 'hero gradient anchor'
s = s.replace(old2, new2, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('ThemeManager: 渐变画刷已统一处理')

# 3) 所有 StaticResource 画刷引用改成 DynamicResource
n = 0
for path in glob.glob('HHCL/Views/*.xaml') + ['HHCL/MainWindow.xaml', 'HHCL/Themes/Fluent.xaml']:
    s = io.open(path, encoding='utf-8').read()
    orig = s
    for k in ['WindowBgBrush', 'AccentGradientBrush', 'LogoGradientBrush']:
        c = s.count('{StaticResource %s}' % k)
        if c:
            s = s.replace('{StaticResource %s}' % k, '{DynamicResource %s}' % k)
            n += c
    if s != orig:
        io.open(path, 'w', encoding='utf-8').write(s)
print('改成 DynamicResource 的引用数:', n)
