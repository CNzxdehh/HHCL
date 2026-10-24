import io

# 1) 箭头随折叠状态变化
p = 'HHCL/Views/ModDetailView.xaml'
s = io.open(p, encoding='utf-8').read()
old = '''                                            <TextBlock Grid.Column="0" Text="&#x25BE;" FontSize="11"
                                                       Margin="0,1,8,0" VerticalAlignment="Center"
                                                       Foreground="{StaticResource TextSecondaryBrush}" />'''
new = '''                                            <TextBlock Grid.Column="0" FontSize="11"
                                                       Margin="0,1,8,0" VerticalAlignment="Center"
                                                       Foreground="{StaticResource TextSecondaryBrush}">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Text" Value="&#x25BE;" />
                                                        <Style.Triggers>
                                                            <!-- 折叠时换成向右的箭头 -->
                                                            <DataTrigger Binding="{Binding IsCollapsed}" Value="True">
                                                                <Setter Property="Text" Value="&#x25B8;" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>'''
assert old in s, 'chevron anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('箭头状态已加')

# 2) 清理"全部收起/展开"逻辑
p = 'HHCL/Views/ModDetailView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old2 = '''        /// <summary>全部收起 / 全部展开。</summary>
        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            _collapsed.Clear();
            if (_collapsed.Count == 0 && _grouped.Count > 0)
            {
                // 全收：把所有非第一组都折起来？不，全收就是所有组都折
            }
            if (sender is FrameworkElement fe2 && fe2.Tag is bool allCollapsed && allCollapsed)
            {
                foreach (var h in _grouped.Where(x => x.IsHeader)) _collapsed.Add(h.GroupKey);
            }
            RebuildVersionList();
        }'''
new2 = '''        /// <summary>全部收起 / 全部展开（按钮 Tag 里记着当前该做哪个）。</summary>
        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            _collapsed.Clear();
            var wantCollapse = sender is FrameworkElement fe && fe.Tag is bool b && b;
            if (wantCollapse)
            {
                foreach (var h in _grouped.Where(x => x.IsHeader)) _collapsed.Add(h.GroupKey);
            }
            RebuildVersionList();
        }'''
assert old2 in s, 'collapse all anchor'
s = s.replace(old2, new2, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('折叠全部逻辑已清理')
