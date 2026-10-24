import io, glob, re

# 1) 主题里剩余的硬编码深色 → 换成主题画刷
p = 'HHCL/Themes/Fluent.xaml'
s = io.open(p, encoding='utf-8').read()
pairs = [
    ('<Setter Property="Background" Value="#FF202026" />',
     '<Setter Property="Background" Value="{DynamicResource CardBrush}" />'),
    ('Value="#1AFFFFFF" />', 'Value="{DynamicResource OverlaySoftBrush}" />'),
    ('Value="#332B88D8" />', 'Value="{DynamicResource AccentTagBrush}" />'),
]
for a, b in pairs:
    n = s.count(a)
    s = s.replace(a, b)
    print('Fluent.xaml 替换', a[:40], '×', n)
io.open(p, 'w', encoding='utf-8').write(s)

# 2) 各页面里剩下的硬编码深色背景/前景
p2 = 'HHCL/Views/HomeView.xaml'
s2 = io.open(p2, encoding='utf-8').read()
s2 = s2.replace('Background="#FF17171B" CornerRadius="6"', 'Background="{DynamicResource StrokeSoftBrush}" CornerRadius="6"')
s2 = s2.replace('Foreground="#FFC8C8D0"', 'Foreground="{DynamicResource TextSecondaryBrush}"')
io.open(p2, 'w', encoding='utf-8').write(s2)
print('HomeView 日志框已主题化')

# 3) 全项目扫一遍剩余硬编码颜色（白底主题下会露馅的）
rest = {}
for path in glob.glob('HHCL/Views/*.xaml') + ['HHCL/MainWindow.xaml', 'HHCL/Themes/Fluent.xaml']:
    t = io.open(path, encoding='utf-8').read()
    for m in re.finditer(r'#[0-9A-Fa-f]{8}\b', t):
        c = m.group(0)
        if c.upper() in ('#FFFFFFFF',):
            continue
        rest.setdefault(c, []).append(path.split('/')[-1])
for c, files in sorted(rest.items()):
    print('剩余硬编码', c, sorted(set(files)))
