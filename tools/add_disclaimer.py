import io

# 1) 关于文本加免责声明
p = 'HHCL/Views/SettingsView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
old = '''            AboutText.Text =
                "HHCL 1.0.0 · Minecraft 启动器\\n" +
                "界面：.NET 10 / WPF 自绘 WinUI 3 风格 · 启动核心兼容原版与 Fabric、Forge、NeoForge 的版本 JSON\\n" +
                "配置文件：" + Config.ConfigPath;'''
new = '''            AboutText.Text =
                "HHCL " + Core.AppInfo.Version + " · Minecraft 启动器\\n" +
                "界面：.NET 10 / WPF 自绘 WinUI 3 风格 · 启动核心兼容原版与 Fabric、Forge、NeoForge 的版本 JSON\\n" +
                "配置文件：" + Config.ConfigPath + "\\n" +
                "HHCL 是第三方启动器，与 Mojang / Microsoft 无隶属关系，未获其官方认可或背书。\\n" +
                "Minecraft 是 Mojang Studios 的商标；游戏需使用你自己的正版账号游玩。";'''
assert old in s, 'about text anchor'
s = s.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('关于文本：免责声明已加')

# 2) 关于卡片加赞助按钮（链接待填）
p = 'HHCL/Views/SettingsView.xaml'
s = io.open(p, encoding='utf-8').read()
old2 = '''                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Content="打开配置目录" Style="{StaticResource SubtleButton}" Height="30" Click="OpenConfig_Click" />
                        <Button Content="查看崩溃日志" Style="{StaticResource GhostButton}" Height="30" Margin="8,0,0,0" Click="OpenCrash_Click" />
                    </StackPanel>'''
new2 = '''                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Content="打开配置目录" Style="{StaticResource SubtleButton}" Height="30" Click="OpenConfig_Click" />
                        <Button Content="查看崩溃日志" Style="{StaticResource GhostButton}" Height="30" Margin="8,0,0,0" Click="OpenCrash_Click" />
                        <Button x:Name="SponsorBtn" Content="赞助一下" Style="{StaticResource GhostButton}" Height="30"
                                Margin="8,0,0,0" Click="Sponsor_Click" />
                    </StackPanel>
                    <TextBlock x:Name="SponsorHint" Text="HHCL 永久免费，赞助仅用于支持开发成本（服务器、域名等），不影响任何功能。"
                               Style="{StaticResource Label}" Margin="0,10,0,0" TextWrapping="Wrap" Visibility="Collapsed" />'''
assert old2 in s, 'about buttons anchor'
s = s.replace(old2, new2, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('关于卡片：赞助按钮已加')

# 3) 赞助点击处理
p = 'HHCL/Views/SettingsView.xaml.cs'
s = io.open(p, encoding='utf-8').read()
anchor = '''        private void LoadJavas()'''
new3 = '''        /// <summary>赞助链接：填上你的爱发电主页地址即可（留空则按钮隐藏）。</summary>
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

        private void LoadJavas()'''
assert anchor in s, 'load javas anchor'
s = s.replace(anchor, new3, 1)

# 链接为空时隐藏按钮
old4 = '''            AboutText.Text ='''
new4 = '''            if (SponsorBtn != null) SponsorBtn.Visibility = string.IsNullOrWhiteSpace(SponsorUrl)
                ? Visibility.Collapsed : Visibility.Visible;

            AboutText.Text ='''
assert old4 in s
s = s.replace(old4, new4, 1)
io.open(p, 'w', encoding='utf-8').write(s)
print('赞助点击已接线（链接待填）')
