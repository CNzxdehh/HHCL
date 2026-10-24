import io

# 1) XAML：去掉赞助按钮和提示文字
p = 'HHCL/Views/SettingsView.xaml'
s = io.open(p, encoding='utf-8').read()
old = '''                        <Button x:Name="SponsorBtn" Content="赞助一下" Style="{StaticResource GhostButton}" Height="30"
                                Margin="8,0,0,0" Click="Sponsor_Click" />
                    </StackPanel>
                    <TextBlock x:Name="SponsorHint" Text="HHCL 永久免费，赞助仅用于支持开发成本（服务器、域名等），不影响任何功能。"
                               Style="{StaticResource Label}" Margin="0,10,0,0" TextWrapping="Wrap" Visibility="Collapsed" />'''
new = '''                    </StackPanel>'''
assert old in s, 'sponsor button anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('XAML: 赞助按钮与提示已删')

# 2) 代码：去掉 SponsorUrl / Sponsor_Click / 隐藏逻辑
p = 'HHCL/Views/SettingsView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old2 = '''        /// <summary>赞助链接：填上你的爱发电主页地址即可（留空则按钮隐藏）。</summary>
        private const string SponsorUrl = "";

        private void Sponsor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SponsorUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SponsorUrl) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("sponsor: " + ex.Message); }
        }

'''
assert old2 in s, 'sponsor code anchor'
s = s.replace(old2, '', 1)

old3 = '''            if (SponsorBtn != null) SponsorBtn.Visibility = string.IsNullOrWhiteSpace(SponsorUrl)
                ? Visibility.Collapsed : Visibility.Visible;

'''
assert old3 in s, 'sponsor visibility anchor'
s = s.replace(old3, '', 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('代码: 赞助相关已删')
