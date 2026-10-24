import io

# ============ 1) 整合包页：删掉 Row2 里遗留的两列（右栏删了但列定义还在 → 右侧空 354px） ============
p = 'HHCL/Views/ModpacksView.xaml'
s = io.open(p, encoding='utf-8').read()
old = '''        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="14" />
                <ColumnDefinition Width="340" />
            </Grid.ColumnDefinitions>

            <Border Style="{StaticResource Card}" Padding="14,12">'''
new = '''        <Grid Grid.Row="2">
            <Border Style="{StaticResource Card}" Padding="14,12">'''
assert old in s, 'modpacks grid anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('整合包页: 右侧空列已删')

# ============ 2) 组头：紧凑化（版本号太大、竖条和箭头之间空太多） ============
p = 'HHCL/Views/ModDetailView.xaml'
s = io.open(p, encoding='utf-8').read()
reps = [
    # 竖条：16 高 → 13 高，间距 9 → 7
    ('''<Border Grid.Column="0" Width="3" Height="16" CornerRadius="2"
                                                    Background="{DynamicResource AccentBrush}" Margin="0,0,9,0"
                                                    VerticalAlignment="Center" />''',
     '''<Border Grid.Column="0" Width="3" Height="13" CornerRadius="2"
                                                    Background="{DynamicResource AccentBrush}" Margin="0,0,7,0"
                                                    VerticalAlignment="Center" />'''),
    # 箭头：10 号字、宽 12、边距 6 → 9 号字、宽 10、边距 4
    ('''<TextBlock Grid.Column="1" FontSize="10" Width="12"
                                                       VerticalAlignment="Center" Margin="0,0,6,0"''',
     '''<TextBlock Grid.Column="1" FontSize="9" Width="10"
                                                       VerticalAlignment="Center" Margin="0,0,4,0"'''),
    # 版本号：13.5 → 12.5
    ('''<TextBlock Grid.Column="2" Text="{Binding Name}" FontSize="13.5"
                                                       FontWeight="SemiBold" VerticalAlignment="Center"''',
     '''<TextBlock Grid.Column="2" Text="{Binding Name}" FontSize="12.5"
                                                       FontWeight="SemiBold" VerticalAlignment="Center"'''),
    # 文件数：11.5 → 11，边距 12 → 8
    ('''<TextBlock Grid.Column="3" Text="{Binding Size}" FontSize="11.5"
                                                       VerticalAlignment="Center" Margin="12,0,0,0"''',
     '''<TextBlock Grid.Column="3" Text="{Binding Size}" FontSize="11"
                                                       VerticalAlignment="Center" Margin="8,0,0,0"'''),
    # 内边距：8,7 → 6,5
    ('<Setter Property="Padding" Value="8,7" />', '<Setter Property="Padding" Value="6,5" />'),
    # 组头上下间距：16 → 10
    ('<Setter Property="Margin" Value="2,16,2,5" />', '<Setter Property="Margin" Value="2,10,2,3" />'),
]
for a, b in reps:
    assert a in s, a[:50]
    s = s.replace(a, b, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('组头: 已紧凑化')
