import io

# ============ 1) 模组页：OnActivated 里填充版本下拉 ============
p = 'HHCL/Views/ModsView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old = '''            state.RefreshVersions();
            BuildVersionList(state);'''
new = '''            state.RefreshVersions();
            BuildVersionList(state);
            FillMcBox();'''
assert old in s, 'mods onactivated anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('模组页: OnActivated 已调用 FillMcBox')

# ============ 2) 整合包页：写死的版本列表 → 动态（本地已安装版本） ============
p = 'HHCL/Views/ModpacksView.xaml'
s = io.open(p, encoding='utf-8').read()
old = '''            <ComboBox Grid.Column="2" x:Name="McBox" Style="{StaticResource FluentComboBox}" Width="104" Height="32"
                      Margin="10,0,0,0" SelectedIndex="0" SelectionChanged="McBox_SelectionChanged">
                <ComboBoxItem Content="全部版本" Tag="" />
                <ComboBoxItem Content="1.21.4" Tag="1.21.4" />
                <ComboBoxItem Content="1.21.1" Tag="1.21.1" />
                <ComboBoxItem Content="1.20.1" Tag="1.20.1" />
                <ComboBoxItem Content="1.19.2" Tag="1.19.2" />
                <ComboBoxItem Content="1.18.2" Tag="1.18.2" />
            </ComboBox>'''
new = '''            <ComboBox Grid.Column="2" x:Name="McBox" Style="{StaticResource FluentComboBox}" Width="104" Height="32"
                      Margin="10,0,0,0" SelectedIndex="0" SelectionChanged="McBox_SelectionChanged" />'''
assert old in s, 'modpacks mcbox anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('整合包页: 版本下拉改为动态')

# ============ 3) 整合包页代码：填充版本 ============
p = 'HHCL/Views/ModpacksView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        public void OnActivated()
        {
            // 首次进入直接拉一次热门列表，否则页面空着、必须去动下拉框才出东西
            if (!_firstShown)
            {'''
new = '''        public void OnActivated()
        {
            FillMcBox();
            // 首次进入直接拉一次热门列表，否则页面空着、必须去动下拉框才出东西
            if (!_firstShown)
            {'''
assert old in s, 'modpacks onactivated anchor'
s = s.replace(old, new, 1)

# 加 FillMcBox 方法
anchor = '''        private string Mc => (McBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";'''
new2 = '''        private string Mc => (McBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        /// <summary>版本下拉：本地已安装的游戏版本（去重）+ 全部版本。</summary>
        private void FillMcBox()
        {
            if (McBox == null) return;
            var cur = Mc;
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
            if (cur.Length > 0)
            {
                for (int i = 0; i < McBox.Items.Count; i++)
                    if ((McBox.Items[i] as ComboBoxItem)?.Tag as string == cur) { McBox.SelectedIndex = i; break; }
            }
        }'''
assert anchor in s, 'modpacks mc property anchor'
s = s.replace(anchor, new2, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('整合包页: FillMcBox 已接入')
