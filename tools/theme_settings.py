import io

# ==================== 1) 设置页加"外观"卡片 ====================
p = 'HHCL/Views/SettingsView.xaml'
s = io.open(p, encoding='utf-8').read()
anchor = '''            <!-- 在线资源 -->'''
card = '''            <!-- 外观 -->
            <Border Style="{StaticResource Card}" Margin="0,14,0,0" Padding="20,16">
                <StackPanel>
                    <TextBlock Text="外观" Style="{StaticResource H2}" FontSize="14" />
                    <TextBlock Text="界面配色，选完立刻生效。" Style="{StaticResource Label}" Margin="0,6,0,0" TextWrapping="Wrap" />

                    <Grid Margin="0,12,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="150" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="主题" Style="{StaticResource Body}" VerticalAlignment="Center" />
                        <StackPanel Grid.Column="1" Orientation="Horizontal">
                            <RadioButton x:Name="ThemeDark" Style="{StaticResource SegmentItem}" GroupName="Theme"
                                         Content="深色（黑）" MinWidth="130" Checked="Theme_Changed" />
                            <RadioButton x:Name="ThemeLight" Style="{StaticResource SegmentItem}" GroupName="Theme"
                                         Content="浅色（白）" MinWidth="130" Margin="14,0,0,0" Checked="Theme_Changed" />
                        </StackPanel>
                    </Grid>
                </StackPanel>
            </Border>

            <!-- 在线资源 -->'''
assert anchor in s, 'settings anchor'
s = s.replace(anchor, card, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('设置页: 已加外观卡片')

# ==================== 2) 设置页代码接线 ====================
p = 'HHCL/Views/SettingsView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old = '''            ChineseLangCheck.IsChecked = cfg.AutoChineseLang;'''
new = '''            ChineseLangCheck.IsChecked = cfg.AutoChineseLang;
            var dark = cfg.Theme != Core.ThemeManager.Light;
            ThemeDark.IsChecked = dark;
            ThemeLight.IsChecked = !dark;'''
assert old in s, 'init anchor'
s = s.replace(old, new, 1)

old2 = '''        private void ChineseLang_Changed(object sender, RoutedEventArgs e)'''
new2 = '''        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var theme = ThemeLight.IsChecked == true ? Core.ThemeManager.Light : Core.ThemeManager.Dark;
            AppState.Current.Config.Theme = theme;
            Core.ThemeManager.Apply(theme);
            Save();
        }

        private void ChineseLang_Changed(object sender, RoutedEventArgs e)'''
assert old2 in s, 'changed anchor'
s = s.replace(old2, new2, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('设置页: 主题切换已接线')

# ==================== 3) 详情页默认全折叠 ====================
p = 'HHCL/Views/ModDetailView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old3 = '''                _grouped = GroupByGameVersion(list);'''
new3 = '''                _grouped = GroupByGameVersion(list);
                // 默认全部折叠：1216 个文件铺开根本没法看，先收起来让用户按需展开
                _collapsed.Clear();
                foreach (var h in _grouped.Where(x => x.IsHeader)) _collapsed.Add(h.GroupKey);'''
assert old3 in s, 'grouped anchor'
s = s.replace(old3, new3, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('详情页: 默认全折叠')
