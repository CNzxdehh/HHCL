import io

# ============ 1) 模组页：工具栏加「版本」下拉 ============
p = 'HHCL/Views/ModsView.xaml'
s = io.open(p, encoding='utf-8').read()
old = '''                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBox x:Name="SearchBox" Style="{StaticResource FluentTextBox}" Height="32" Tag="搜索模组、资源包、光影…"'''
new = '''                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBox x:Name="SearchBox" Style="{StaticResource FluentTextBox}" Height="32" Tag="搜索模组、资源包、光影…"'''
assert old in s, 'mods grid anchor'
s = s.replace(old, new, 1)

# 在类型下拉后面插入版本下拉，搜索按钮挪到第 4 列
old2 = '''            <Button Grid.Column="3" x:Name="SearchBtn" Content="搜索" Style="{StaticResource AccentButton}" Height="32"'''
new2 = '''            <ComboBox Grid.Column="3" x:Name="McBox" Style="{StaticResource FluentComboBox}" Width="104" Height="32"
                      Margin="10,0,0,0" SelectedIndex="0" SelectionChanged="McBox_SelectionChanged" />
            <Button Grid.Column="4" x:Name="SearchBtn" Content="搜索" Style="{StaticResource AccentButton}" Height="32"'''
assert old2 in s, 'search button anchor'
s = s.replace(old2, new2, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('模组页: 已加版本下拉')

# ============ 2) 模组页代码：填充版本、搜索时按版本过滤 ============
p = 'HHCL/Views/ModsView.xaml.cs'
s = io.open(p, encoding='utf-8').read()

# 属性
old3 = '''        private void McBox_SelectionChanged'''
new3 = '''        private void McBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _ = SearchAsync(true);
        }

        private void McBox_SelectionChanged'''
if old3 in s:
    print('已有 McBox_SelectionChanged，跳过')

# 填充版本下拉的方法 + 过滤逻辑
anchor = '''        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)'''
new4 = '''        /// <summary>版本下拉：本地已安装的游戏版本（去重）+ 全部版本。</summary>
        private void FillMcBox()
        {
            if (McBox == null) return;
            _loading = true;
            McBox.Items.Clear();
            McBox.Items.Add(new ComboBoxItem { Content = "全部版本", Tag = "" });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in AppState.Current.Versions)
            {
                var mv = (v.McVersion ?? "").Trim();
                if (mv.Length == 0 || !seen.Add(mv)) continue;
                McBox.Items.Add(new ComboBoxItem { Content = mv, Tag = mv });
            }
            McBox.SelectedIndex = 0;
            _loading = false;
        }

        /// <summary>版本下拉选中的版本（空 = 不按版本过滤）。</summary>
        private string McFilter => (McBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)'''
assert anchor in s, 'source changed anchor'
s = s.replace(anchor, new4, 1)

# 搜索时用下拉的版本覆盖 mc
old5 = '''                var mc = v?.McVersion;
                var loader = (v?.Loader ?? "原版").ToLowerInvariant();
                if (loader == "原版" || loader == "vanilla") loader = "";'''
new5 = '''                var mc = v?.McVersion;
                var loader = (v?.Loader ?? "原版").ToLowerInvariant();
                if (loader == "原版" || loader == "vanilla") loader = "";
                // 版本下拉选了具体版本就按它过滤（优先于上方的游戏版本选择器）
                var mcFilter = McFilter;
                var byMc = mcFilter.Length > 0;
                if (byMc) mc = mcFilter;'''
assert old5 in s, 'mc anchor'
s = s.replace(old5, new5, 1)

# Modrinth 搜索要传 matchVersion=true 才按版本过滤
old6 = '''                        var list = await Modrinth.SearchAsync(query, ProjectType, mc, loader,
                            CancellationToken.None, 50, _offset);'''
new6 = '''                        var list = await Modrinth.SearchAsync(query, ProjectType, mc, loader,
                            CancellationToken.None, 50, _offset, byMc);'''
assert old6 in s, 'modrinth search anchor'
s = s.replace(old6, new6, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('模组页代码: 版本筛选已接入')
