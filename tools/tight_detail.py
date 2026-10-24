import io

p = 'HHCL/Views/ModDetailView.xaml'
s = io.open(p, encoding='utf-8').read()

reps = [
    # 卡片内边距收一点
    ('<Border Grid.Row="1" Style="{StaticResource Card}" Padding="18,16" Margin="0,14,0,0">',
     '<Border Grid.Row="1" Style="{StaticResource Card}" Padding="16,13" Margin="0,12,0,0">'),
    ('<Border Grid.Row="2" Style="{StaticResource Card}" Padding="18,16" Margin="0,14,0,16">',
     '<Border Grid.Row="2" Style="{StaticResource Card}" Padding="16,13" Margin="0,12,0,14">'),
    # 项目信息内部的间距
    ('<TextBlock x:Name="SubText" Text="" Style="{StaticResource Label}" Margin="0,8,0,0" />',
     '<TextBlock x:Name="SubText" Text="" Style="{StaticResource Label}" Margin="0,6,0,0" />'),
    ('<TextBlock x:Name="DescText" Text="" Style="{StaticResource Body}" Margin="0,12,0,0"',
     '<TextBlock x:Name="DescText" Text="" Style="{StaticResource Body}" Margin="0,8,0,0"'),
    # 状态文字
    ('<TextBlock x:Name="StateText" Text="" Style="{StaticResource Label}" Margin="0,10,0,0" TextWrapping="Wrap" />',
     '<TextBlock x:Name="StateText" Text="" Style="{StaticResource Label}" Margin="0,8,0,0" TextWrapping="Wrap" />'),
    # 安装区：更紧
    ('<Border Background="{DynamicResource AccentPanelBrush}" CornerRadius="7" Padding="12,9" Margin="0,10,0,0">',
     '<Border Background="{DynamicResource AccentPanelBrush}" CornerRadius="7" Padding="11,8" Margin="0,8,0,0">'),
    # 提示语默认隐藏（选中版本后才显示），省掉一整行
    ('<TextBlock x:Name="PickText" Text="在下面选一个版本" Style="{DynamicResource Body}" TextWrapping="Wrap" />',
     '<TextBlock x:Name="PickText" Text="" Style="{DynamicResource Body}" FontSize="12.5" TextWrapping="Wrap"\n                                       Visibility="Collapsed" />'),
    # 按钮行
    ('<StackPanel Orientation="Horizontal" Margin="0,10,0,0">',
     '<StackPanel Orientation="Horizontal" Margin="0,8,0,0">'),
    # 进度条上边距
    ('Margin="0,14,0,0" Visibility="Collapsed" />',
     'Margin="0,10,0,0" Visibility="Collapsed" />'),
    # 进度文字：空的时候不占高度
    ('<TextBlock x:Name="ProgText" Text="" Style="{StaticResource Label}" Margin="0,8,0,0" TextWrapping="Wrap" />',
     '<TextBlock x:Name="ProgText" Text="" Style="{StaticResource Label}" Margin="0,6,0,0" TextWrapping="Wrap"\n                                       Visibility="Collapsed" />'),
    # 列表上边距
    ('<ListBox x:Name="VersionList" Style="{StaticResource FluentListBox}" Margin="0,14,0,0"',
     '<ListBox x:Name="VersionList" Style="{StaticResource FluentListBox}" Margin="0,10,0,0"'),
    # 组头上下间距
    ('<Setter Property="Margin" Value="2,10,2,3" />',
     '<Setter Property="Margin" Value="2,6,2,2" />'),
]
for a, b in reps:
    if a not in s:
        print('跳过:', a[:56].replace('\n', ' '))
        continue
    s = s.replace(a, b, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('详情页间距已收紧')
