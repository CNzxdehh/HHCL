import io, glob

KEYS = [
    'BgBrush', 'NavBrush', 'CardBrush', 'CardHoverBrush', 'CardActiveBrush',
    'StrokeBrush', 'StrokeSoftBrush', 'TextPrimaryBrush', 'TextSecondaryBrush',
    'TextTertiaryBrush', 'AccentBrush', 'AccentHoverBrush', 'AccentPressedBrush',
    'AccentLightBrush', 'AccentSoftBrush', 'DangerBrush', 'CloseHoverBrush',
    'OverlayHoverBrush', 'OverlayPressedBrush',
    'OverlaySoftBrush', 'OverlaySoft2Brush', 'AccentPanelBrush', 'AccentIconBrush',
    'AccentTagBrush', 'StatusDotBrush', 'FooterBrush',
]

total = 0
for path in glob.glob('HHCL/Views/*.xaml') + ['HHCL/MainWindow.xaml']:
    s = io.open(path, encoding='utf-8').read()
    orig = s
    for k in KEYS:
        # 只替换 StaticResource，不动已经写成 DynamicResource 的
        s = s.replace('{StaticResource %s}' % k, '{DynamicResource %s}' % k)
    if s != orig:
        n = sum(orig.count('{StaticResource %s}' % k) for k in KEYS)
        total += n
        io.open(path, 'w', encoding='utf-8').write(s)
print('改成 DynamicResource 的引用数:', total)

# 主题里的"定义"本身保持 StaticResource 引用没问题
p = 'HHCL/Themes/Fluent.xaml'
s = io.open(p, encoding='utf-8').read()
print('Fluent.xaml 里 DynamicResource 数量:', s.count('DynamicResource'))
