import io

p = 'HHCL/Themes/Fluent.xaml'
s = io.open(p, encoding='utf-8').read()

# 样式里的硬编码 → 主题画刷（浅色主题下这些深色/半透明白会露馅）
repl = [
    # 半透明白的 hover / overlay：浅色下等于没颜色
    ('Value="#14FFFFFF" />', 'Value="{DynamicResource OverlaySoftBrush}" />'),
    ('Value="#10FFFFFF" />', 'Value="{DynamicResource OverlaySoft2Brush}" />'),
    ('Value="#18FFFFFF" />', 'Value="{DynamicResource OverlayHoverBrush}" />'),
    ('Value="#22FFFFFF" />', 'Value="{DynamicResource OverlayHoverBrush}" />'),
    # 边框
    ('Value="#FF4A4A55" />', 'Value="{DynamicResource StrokeBrush}" />'),
    # 深色小控件
    ('Background="#FF2A2A31"', 'Background="{DynamicResource StrokeSoftBrush}"'),
    ('Background="#FF2E2E36"', 'Background="{DynamicResource StrokeSoftBrush}"'),
    ('Background="#FF303038"', 'Background="{DynamicResource StrokeSoftBrush}"'),
    ('Background="#FF4E4E59"', 'Background="{DynamicResource TextTertiaryBrush}"'),
    ('Fill="#FFB6B6C0"', 'Fill="{DynamicResource TextTertiaryBrush}"'),
    ('Value="#FF7A7A88" />', 'Value="{DynamicResource TextTertiaryBrush}" />'),
]
for a, b in repl:
    n = s.count(a)
    if n:
        s = s.replace(a, b)
    print(f'  {a[:34]:<36} × {n}')

io.open(p, 'w', encoding='utf-8').write(s)
print('硬编码已替换')
