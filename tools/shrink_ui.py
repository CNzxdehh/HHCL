import io

# ============ 1) 详情页：安装区整体缩小（用户点名"下载那个按钮"） ============
p = 'HHCL/Views/ModDetailView.xaml'
s = io.open(p, encoding='utf-8').read()
reps = [
    ('<Border Background="{DynamicResource AccentPanelBrush}" CornerRadius="8" Padding="14,12" Margin="0,12,0,0">',
     '<Border Background="{DynamicResource AccentPanelBrush}" CornerRadius="7" Padding="12,9" Margin="0,10,0,0">'),
    ('''<Button x:Name="InstallBtn" Content="下载并安装" Style="{StaticResource AccentButton}"
                                        Height="40" Width="180" Click="Install_Click" IsEnabled="False" />''',
     '''<Button x:Name="InstallBtn" Content="下载并安装" Style="{StaticResource AccentButton}"
                                        Height="34" Width="146" FontSize="13" Click="Install_Click" IsEnabled="False" />'''),
    ('''<Button x:Name="OpenDirBtn" Content="打开安装目录" Style="{StaticResource GhostButton}"
                                        Height="40" Margin="10,0,0,0" Click="OpenDir_Click" />''',
     '''<Button x:Name="OpenDirBtn" Content="打开安装目录" Style="{StaticResource GhostButton}"
                                        Height="34" FontSize="13" Margin="8,0,0,0" Click="OpenDir_Click" />'''),
    ('''<Button x:Name="CancelBtn" Content="取消" Style="{StaticResource GhostButton}" Height="40"
                                        Width="90" Margin="10,0,0,0" Visibility="Collapsed" Click="Cancel_Click" />''',
     '''<Button x:Name="CancelBtn" Content="取消" Style="{StaticResource GhostButton}" Height="34"
                                        Width="76" FontSize="13" Margin="8,0,0,0" Visibility="Collapsed" Click="Cancel_Click" />'''),
    ('<StackPanel Orientation="Horizontal" Margin="0,12,0,0">',
     '<StackPanel Orientation="Horizontal" Margin="0,10,0,0">'),
    # 已选项文字也别太大
    ('<TextBlock x:Name="PickText" Text="在下面选一个版本" Style="{DynamicResource Body}" TextWrapping="Wrap" />',
     '<TextBlock x:Name="PickText" Text="在下面选一个版本" Style="{DynamicResource Body}" FontSize="12.5" TextWrapping="Wrap" />'),
]
for a, b in reps:
    if a not in s:
        print('跳过(未找到):', a[:48].replace('\n', ' '))
        continue
    s = s.replace(a, b, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('详情页安装区已缩小')

# ============ 2) 下载页按钮 ============
p = 'HHCL/Views/DownloadView.xaml'
s = io.open(p, encoding='utf-8').read()
s = s.replace('<Button x:Name="InstallBtn" Style="{StaticResource AccentButton}" Height="42" Content="下载并安装"',
              '<Button x:Name="InstallBtn" Style="{StaticResource AccentButton}" Height="34" FontSize="13" Content="下载并安装"')
io.open(p, 'w', encoding='utf-8').write(s)
print('下载页按钮已缩小')

# ============ 3) 启动页：基岩版按钮 + 启动游戏按钮 ============
p = 'HHCL/Views/HomeView.xaml'
s = io.open(p, encoding='utf-8').read()
reps = [
    ('<Button x:Name="BedrockPrimaryBtn" Style="{StaticResource AccentButton}" Height="40" Width="168"',
     '<Button x:Name="BedrockPrimaryBtn" Style="{StaticResource AccentButton}" Height="34" Width="142" FontSize="13"'),
    ('<Button x:Name="BedrockSecondaryBtn" Style="{StaticResource GhostButton}" Height="40" Width="150"',
     '<Button x:Name="BedrockSecondaryBtn" Style="{StaticResource GhostButton}" Height="34" Width="132" FontSize="13"'),
    ('<Button x:Name="BedrockManageBtn" Style="{StaticResource GhostButton}" Height="40" Width="150"',
     '<Button x:Name="BedrockManageBtn" Style="{StaticResource GhostButton}" Height="34" Width="132" FontSize="13"'),
    ('<Button x:Name="BedrockCancelBtn" Style="{StaticResource GhostButton}" Height="40" Width="96"',
     '<Button x:Name="BedrockCancelBtn" Style="{StaticResource GhostButton}" Height="34" Width="82" FontSize="13"'),
    ('<Button x:Name="PlayBtn" Style="{StaticResource AccentButton}" Width="190" Height="46"',
     '<Button x:Name="PlayBtn" Style="{StaticResource AccentButton}" Width="166" Height="40"'),
]
for a, b in reps:
    if a not in s:
        print('跳过:', a[:48])
        continue
    s = s.replace(a, b, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('启动页按钮已缩小')
