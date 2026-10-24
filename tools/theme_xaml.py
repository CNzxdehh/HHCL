import io, re, glob

# 1) Fluent.xaml 里补上新增的语义画刷
p = 'HHCL/Themes/Fluent.xaml'
s = io.open(p, encoding='utf-8').read()
anchor = '''    <SolidColorBrush x:Key="OverlayPressedBrush" Color="#0FFFFFFF" />'''
add = anchor + '''
    <!-- 语义画刷：浅色主题下由 ThemeManager 换色 -->
    <SolidColorBrush x:Key="OverlaySoftBrush" Color="#14FFFFFF" />
    <SolidColorBrush x:Key="OverlaySoft2Brush" Color="#16FFFFFF" />
    <SolidColorBrush x:Key="AccentPanelBrush" Color="#142B88D8" />
    <SolidColorBrush x:Key="AccentIconBrush" Color="#1F2B88D8" />
    <SolidColorBrush x:Key="AccentTagBrush" Color="#332B88D8" />
    <SolidColorBrush x:Key="StatusDotBrush" Color="#FF4CC2FF" />
    <SolidColorBrush x:Key="FooterBrush" Color="#FF191A1E" />'''
assert anchor in s, 'brush anchor'
s = s.replace(anchor, add, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('Fluent.xaml: 新增 7 个语义画刷')

# 2) 把 XAML 里的硬编码颜色换成资源画刷
repl = [
    ('#14FFFFFF', '{StaticResource OverlaySoftBrush}'),
    ('#16FFFFFF', '{StaticResource OverlaySoft2Brush}'),
    ('#12FFFFFF', '{StaticResource OverlaySoft2Brush}'),
    ('#142B88D8', '{StaticResource AccentPanelBrush}'),
    ('#1F2B88D8', '{StaticResource AccentIconBrush}'),
    ('#332B88D8', '{StaticResource AccentTagBrush}'),
]
total = 0
for path in glob.glob('HHCL/Views/*.xaml') + ['HHCL/MainWindow.xaml']:
    s = io.open(path, encoding='utf-8').read()
    orig = s
    for a, b in repl:
        n = s.count(a)
        if n:
            s = s.replace(a, b)
            total += n
    if s != orig:
        io.open(path, 'w', encoding='utf-8').write(s)
print('替换硬编码颜色处数:', total)

# 3) MainWindow 的窗口背景 / 底栏 / 状态点
p = 'HHCL/MainWindow.xaml'
s = io.open(p, encoding='utf-8').read()
s = s.replace('WindowStyle="None" Background="#FF1B1B1F"', 'WindowStyle="None" Background="{StaticResource BgBrush}"')
s = s.replace('Background="#FF191A1E" BorderBrush="{StaticResource StrokeSoftBrush}"',
              'Background="{StaticResource FooterBrush}" BorderBrush="{StaticResource StrokeSoftBrush}"')
s = s.replace('Fill="#FF4CC2FF"', 'Fill="{StaticResource StatusDotBrush}"')
io.open(p, 'w', encoding='utf-8').write(s)
print('MainWindow 硬编码已换')
