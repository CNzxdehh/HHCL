using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HHCL.Core;

namespace HHCL.Views
{
    public partial class BedrockView : UserControl, IRefreshable
    {
        private bool _busy;
        private CancellationTokenSource _cts;
        private List<BedrockProfile> _profiles = new List<BedrockProfile>();
        private bool _loadingProfiles;
        private bool _gameRunning;

        public BedrockView()
        {
            InitializeComponent();
            LoadProfiles();
        }

        public void OnActivated()
        {
            RefreshState();
            LoadProfiles();
            _ = RefreshStoreInfoAsync();
        }

        // ==================== 存档配置 ====================

        private BedrockProfile CurrentProfile =>
            _profiles.FirstOrDefault(p => p.Name == (ProfileBox.SelectedItem as string));

        private void LoadProfiles()
        {
            _profiles = BedrockProfiles.Load();
            _loadingProfiles = true;
            // 自定义的 FluentComboBox 模板不吃 DisplayMemberPath，直接用名字字符串做项
            ProfileBox.ItemsSource = null;
            ProfileBox.ItemsSource = _profiles.Select(p => p.Name).ToList();
            if (_profiles.Count > 0) ProfileBox.SelectedIndex = 0;
            _loadingProfiles = false;
            UpdateProfileInfo();
        }

        private void SelectProfile(string name)
        {
            var i = ProfileBox.Items.IndexOf(name);
            if (i >= 0) ProfileBox.SelectedIndex = i;
        }

        private void UpdateProfileInfo()
        {
            var p = CurrentProfile;
            if (p == null) { ProfileInfo.Text = ""; return; }
            var worlds = BedrockProfiles.CountWorlds(p);
            var size = AppState.FormatSize(BedrockProfiles.SizeOf(p));
            var last = p.LastUsed > 0
                ? DateTimeOffset.FromUnixTimeSeconds(p.LastUsed).ToLocalTime().ToString("MM-dd HH:mm")
                : "还没用过";
            ProfileInfo.Text = $"{worlds} 个存档 · 占用 {size} · 上次使用 {last}";
        }

        private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingProfiles) return;
            UpdateProfileInfo();
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = TextInputWindow.Ask(Window.GetWindow(this), "新建存档配置",
                "给这套存档起个名字，比如「和朋友联机」「自己单机」。", "配置 " + (_profiles.Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            var p = BedrockProfiles.Create(name);
            LoadProfiles();
            SelectProfile(p.Name);
            Hint.Text = $"已创建配置「{p.Name}」。启动基岩版时会自动套用这套存档。";
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var p = CurrentProfile;
            if (p == null) return;
            var name = TextInputWindow.Ask(Window.GetWindow(this), "重命名配置", "改成什么名字？", p.Name);
            if (string.IsNullOrEmpty(name) || name == p.Name) return;
            BedrockProfiles.Rename(p, name);
            LoadProfiles();
            SelectProfile(p.Name);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var p = CurrentProfile;
            if (p == null) return;
            if (MessageBox.Show($"删除配置「{p.Name}」？\n\n这套配置里的存档会一起删掉，删了就找不回来了。",
                "HHCL", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            BedrockProfiles.Delete(p);
            LoadProfiles();
            Hint.Text = $"已删除配置「{p.Name}」。";
        }

        /// <summary>取账号 XUID（GDK 存档目录名就是它），取不到就退回自动识别。</summary>
        private static async Task<string> ResolveXuidAsync()
        {
            var acc = AppState.Current.Config.CurrentAccount;
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken)) return acc.Xuid ?? "";
            if (!string.IsNullOrEmpty(acc.Xuid)) return acc.Xuid;
            try { return await BedrockInstaller.GetXuidAsync(acc, CancellationToken.None); }
            catch (Exception ex) { Log.Write("bedrock xuid: " + ex.Message); return ""; }
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            var p = CurrentProfile;
            if (p == null) return;
            if (_gameRunning) { Hint.Text = "游戏正在运行，先退出游戏再切换配置。"; return; }
            Hint.Text = $"正在把配置「{p.Name}」应用到存档目录…";
            var xuid = await ResolveXuidAsync();
            var (ok, msg) = await Task.Run(() => BedrockProfiles.Apply(p, xuid));
            Hint.Text = ok ? msg + "。" : msg;
            UpdateProfileInfo();
        }

        // ==================== 启动 / 回收 ====================

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (_gameRunning) { Hint.Text = "游戏已经在运行了。"; return; }

            var p = CurrentProfile;
            var xuid = await ResolveXuidAsync();
            if (p != null)
            {
                Hint.Text = $"正在应用配置「{p.Name}」…";
                var (ok, msg) = await Task.Run(() => BedrockProfiles.Apply(p, xuid));
                if (!ok) { Hint.Text = msg; return; }
            }

            try
            {
                BedrockInstaller.Launch();
                Hint.Text = p != null
                    ? $"已用配置「{p.Name}」启动。游戏退出后会自动把改动收回这套配置。"
                    : "游戏已启动。";
                _ = WatchGameAsync(p, xuid);
            }
            catch (Exception ex)
            {
                Hint.Text = "启动失败：" + ex.Message;
            }
        }

        /// <summary>等游戏进程起来再等它退出，退出后把存档改动收回配置。</summary>
        private async Task WatchGameAsync(BedrockProfile p, string xuid)
        {
            _gameRunning = true;
            try
            {
                // 等游戏进程出现（最多 90 秒）
                bool appeared = false;
                for (int i = 0; i < 180 && !appeared; i++)
                {
                    await Task.Delay(500);
                    if (IsGameRunning()) { appeared = true; break; }
                }
                if (!appeared) return;

                // 等它退出
                while (IsGameRunning()) await Task.Delay(2000);
                await Task.Delay(2000);

                if (p != null)
                {
                    var (ok, msg) = await Task.Run(() => BedrockProfiles.Reclaim(p, xuid));
                    Dispatcher.Invoke(() =>
                    {
                        Hint.Text = ok ? msg + "，存档已归位。" : msg;
                        UpdateProfileInfo();
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Write("watch bedrock game: " + ex);
            }
            finally
            {
                _gameRunning = false;
            }
        }

        /// <summary>基岩版游戏进程名（Minecraft.Windows，Preview 是 Minecraft.WindowsBeta）。</summary>
        private static bool IsGameRunning()
        {
            var procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.ProcessName.StartsWith("Minecraft.Windows", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in procs) { try { p.Dispose(); } catch { } }
            }
            return false;
        }

        // ==================== 基岩版 addon 商店 ====================

        private CfBedrockGame _bedrockGame;
        private CfProject _addonSelected;
        private List<CfFile> _addonFiles = new List<CfFile>();
        private bool _addonBusy;

        private string AddonType => (AddonTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "addons";

        private async void AddonType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _addonSelected = null;
            _addonFiles.Clear();
            AddonDetailRow.Visibility = Visibility.Collapsed;
            AddonList.Visibility = Visibility.Collapsed;
            AddonStateText.Text = "";
        }

        private void AddonSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { AddonSearch_Click(sender, e); e.Handled = true; }
        }

        private async void AddonSearch_Click(object sender, RoutedEventArgs e)
        {
            if (_addonBusy) return;
            var key = AppState.Current.Config.CurseForgeApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                AddonStateText.Text = "搜基岩版 addon 需要 CurseForge API Key：到「设置 → 在线资源」里填一个免费的 Key，再回来搜。";
                AddonList.Visibility = Visibility.Collapsed;
                return;
            }

            _addonBusy = true;
            AddonSearchBtn.IsEnabled = false;
            AddonStateText.Text = "正在搜索…";
            AddonList.Visibility = Visibility.Collapsed;
            try
            {
                _bedrockGame ??= await CurseForgeBedrock.GetGameAsync(key, CancellationToken.None);
                var query = (AddonSearchBox.Text ?? "").Trim();
                var list = await CurseForgeBedrock.SearchAsync(query, AddonType, _bedrockGame, key,
                    CancellationToken.None, 50, 0);
                AddonList.ItemsSource = list;
                AddonList.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                AddonStateText.Text = list.Count > 0
                    ? $"找到 {list.Count} 个结果（分区：{_bedrockGame.Name}）"
                    : "没找到匹配的 addon，换个关键词试试。";
            }
            catch (Exception ex)
            {
                Log.Write("bedrock addon search: " + ex);
                AddonStateText.Text = "搜索失败：" + ex.Message;
            }
            finally
            {
                _addonBusy = false;
                AddonSearchBtn.IsEnabled = true;
            }
        }

        private async void AddonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(AddonList.SelectedItem is CfProject p)) return;
            _addonSelected = p;
            _addonFiles.Clear();
            AddonDetailRow.Visibility = Visibility.Visible;
            AddonDetailText.Text = $"已选：{p.Title}";
            AddonFileText.Text = "正在获取可安装文件…";
            AddonInstallBtn.IsEnabled = false;
            try
            {
                var key = AppState.Current.Config.CurseForgeApiKey;
                _addonFiles = await CurseForgeBedrock.GetFilesAsync(p.Id, key, CancellationToken.None);
                if (_addonFiles.Count > 0)
                {
                    var f = _addonFiles[0];
                    AddonFileText.Text = $"最新文件：{f.FileName}　{AppState.FormatSize(f.Size)}" +
                        (string.IsNullOrEmpty(f.GameVersions) ? "" : $"　适用 {f.GameVersions}");
                    AddonInstallBtn.IsEnabled = true;
                }
                else
                {
                    AddonFileText.Text = "这个项目暂时没有可下载的文件。";
                }
            }
            catch (Exception ex)
            {
                Log.Write("bedrock addon files: " + ex);
                AddonFileText.Text = "获取文件列表失败：" + ex.Message;
            }
        }

        private async void AddonInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_addonBusy || _addonSelected == null || _addonFiles.Count == 0) return;
            var key = AppState.Current.Config.CurseForgeApiKey;
            var file = _addonFiles[0];
            _addonBusy = true;
            AddonInstallBtn.IsEnabled = false;
            AddonBar.Visibility = Visibility.Visible;
            AddonBar.Value = 0;

            var progress = new Progress<DlProgress>(p =>
            {
                if (p.TotalBytes > 0) AddonBar.Value = Math.Min(100, p.DoneBytes * 100.0 / p.TotalBytes);
                else if (p.Total > 0) AddonBar.Value = Math.Min(100, p.Done * 100.0 / p.Total);
                AddonStateText.Text = $"{p.Stage}　{p.Current}";
            });

            try
            {
                var xuid = await ResolveXuidAsync();
                var gameDir = BedrockProfiles.FindGameDataDir(out var layout, xuid);
                var res = await BedrockAddonInstaller.InstallAsync(file, key, gameDir, progress, CancellationToken.None);
                if (res.Ok)
                {
                    AddonStateText.Text = $"装好了：{string.Join("、", res.Installed)}。进游戏的世界设置里勾选一下就会生效。";
                    AddonBar.Value = 100;
                }
                else
                {
                    AddonStateText.Text = "安装失败：" + res.Message;
                }
            }
            catch (Exception ex)
            {
                Log.Write("bedrock addon install: " + ex);
                AddonStateText.Text = "安装失败：" + ex.Message;
            }
            finally
            {
                _addonBusy = false;
                AddonInstallBtn.IsEnabled = true;
            }
        }

        /// <summary>从本地文件安装 addon（不需要 API Key）。</summary>
        private async void AddonLocal_Click(object sender, RoutedEventArgs e)
        {
            if (_addonBusy) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择基岩版 addon 包",
                Filter = "基岩版插件包 (*.mcaddon;*.mcpack;*.zip)|*.mcaddon;*.mcpack;*.zip|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            _addonBusy = true;
            AddonLocalBtn.IsEnabled = false;
            AddonBar.Visibility = Visibility.Visible;
            AddonBar.Value = 0;
            var progress = new Progress<DlProgress>(p =>
            {
                if (p.TotalBytes > 0) AddonBar.Value = Math.Min(100, p.DoneBytes * 100.0 / p.TotalBytes);
                AddonStateText.Text = $"{p.Stage}　{p.Current}";
            });
            try
            {
                var xuid = await ResolveXuidAsync();
                var gameDir = BedrockProfiles.FindGameDataDir(out _, xuid);
                var res = await BedrockAddonInstaller.InstallLocalAsync(dlg.FileName, gameDir, progress, CancellationToken.None);
                AddonStateText.Text = res.Ok
                    ? $"装好了：{string.Join("、", res.Installed)}。进游戏的世界设置里勾选一下就会生效。"
                    : "安装失败：" + res.Message;
                if (res.Ok) AddonBar.Value = 100;
            }
            catch (Exception ex)
            {
                Log.Write("bedrock addon local install: " + ex);
                AddonStateText.Text = "安装失败：" + ex.Message;
            }
            finally
            {
                _addonBusy = false;
                AddonLocalBtn.IsEnabled = true;
            }
        }

        // ==================== 商店信息 ====================

        /// <summary>后台查一下商店当前版本和下载体积（失败不影响使用，静默即可）。</summary>
        private async Task RefreshStoreInfoAsync()
        {
            try
            {
                var acc = AppState.Current.Config.CurrentAccount;
                if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken))
                {
                    StoreInfoText.Text = "未登录微软账号，无法查询商店版本（登录后可在此下载基岩版）。";
                    return;
                }
                StoreInfoText.Text = "正在查询商店版本…";
                var info = await BedrockInstaller.GetStoreInfoAsync(acc, CancellationToken.None);
                var size = AppState.FormatSize(info.TotalSize);
                StoreInfoText.Text = $"商店最新版本 {info.Version}　·　可下载约 {size}";
                if (info.OldVersions.Count > 0)
                {
                    var shown = string.Join("、", info.OldVersions.Take(8));
                    VersionListText.Text =
                        $"商店侧还有 {info.OldVersions.Count} 个历史版本清单：{shown}" +
                        (info.OldVersions.Count > 8 ? " 等" : "") +
                        "。这些是更新清单（XPFM），内容要靠微软更新引擎流式拉取，" +
                        "加上 Windows 同一包族只能注册一个版本，所以只能升级到最新版、不能单独装旧版。";
                    VersionListText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Log.Write("bedrock store info: " + ex.Message);
                StoreInfoText.Text = "查询商店版本失败：" + ex.Message;
            }
        }

        private void RefreshState()
        {
            try
            {
                if (BedrockInstaller.IsInstalled(out var ver))
                {
                    // 这里必须用 Segoe MDL2 的码位，直接写 ✔ 会变成豆腐块（该字体没有这个字形）
                    StateIcon.Text = "";           // Completed（对勾，Segoe MDL2 码位）
                    StateIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x6C, 0xD0, 0x7C));
                    StateText.Text = "已安装" + (string.IsNullOrEmpty(ver) ? "" : "（版本 " + ver + "）");
                    LaunchBtn.Visibility = Visibility.Visible;
                    StoreBtnTop.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StateIcon.Text = "";           // Info（Segoe MDL2 码位）
                    StateIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentLightBrush");
                    StateText.Text = "尚未安装基岩版";
                    LaunchBtn.Visibility = Visibility.Collapsed;
                    StoreBtnTop.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                StateText.Text = "检测失败：" + ex.Message;
            }
        }

        // ==================== 下载安装 ====================

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var acc = AppState.Current.Config.CurrentAccount;
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken))
            {
                Hint.Text = "请先在「账号」页登录微软账号，再回来下载基岩版。";
                return;
            }

            _busy = true;
            DownloadBtn.IsEnabled = false;
            StoreBtn.IsEnabled = false;
            CancelBtn_Show(true);
            Bar.Visibility = Visibility.Visible;
            ProgressRow.Visibility = Visibility.Visible;
            Bar.Value = 0;
            Hint.Text = "";
            _cts = new CancellationTokenSource();

            var progress = new Progress<DlProgress>(p =>
            {
                // 基岩版主包就有 1.9GB，按字节算进度才准（按文件数会一直卡在 78%）
                if (p.TotalBytes > 0) Bar.Value = Math.Min(100, p.DoneBytes * 100.0 / p.TotalBytes);
                else Bar.Value = p.Percent;
                StageText.Text = $"{p.Stage}  {p.Done}/{p.Total}".TrimEnd();
                RightText.Text = p.TotalBytes > 0 ? $"{p.SizeText}  {p.SpeedText}" : "";
            });

            try
            {
                MainWindow.Instance?.SetStatus("正在准备基岩版下载…", true);

                // 1) 换商店授权令牌
                StageText.Text = "正在获取商店授权…";
                var auth = await BedrockInstaller.GetXboxUpdateAuthAsync(acc, _cts.Token);

                // 2) 取包文件清单
                StageText.Text = "正在查询安装包…";
                var files = await BedrockInstaller.GetPackageFilesAsync(auth, _cts.Token);

                // 3) 下载
                var dir = Path.Combine(Config.DataDir, "bedrock");
                await BedrockInstaller.DownloadAsync(files, dir, progress, _cts.Token);

                // 4) 安装
                StageText.Text = "正在安装…";
                Bar.Value = 100;
                BedrockInstaller.Install(dir);

                StageText.Text = "安装完成";
                Hint.Text = "基岩版已安装，点「启动基岩版」或下方按钮开玩。";
                RefreshState();
                MainWindow.Instance?.SetStatus("基岩版安装完成");
            }
            catch (OperationCanceledException)
            {
                Hint.Text = "已取消。已下载的文件保留在缓存目录，再次点击可继续。";
                StageText.Text = "已取消";
                MainWindow.Instance?.SetStatus("已取消下载");
            }
            catch (Exception ex)
            {
                Hint.Text = "下载/安装失败：" + ex.Message + "\n若提示未拥有或授权被拒，请改用「打开微软商店」走官方渠道。";
                StageText.Text = "失败";
                MainWindow.Instance?.SetStatus("基岩版安装失败");
            }
            finally
            {
                _busy = false;
                DownloadBtn.IsEnabled = true;
                StoreBtn.IsEnabled = true;
                CancelBtn_Show(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void Store_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(BedrockInstaller.StoreUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Hint.Text = "打不开商店链接：" + ex.Message + "\n请手动在微软商店搜索 Minecraft for Windows 安装。";
            }
        }

        private void CancelBtn_Show(bool show)
        {
            if (show && CancelBtn.Visibility != Visibility.Visible)
                CancelBtn.Visibility = Visibility.Visible;
            else if (!show)
                CancelBtn.Visibility = Visibility.Collapsed;
        }
    }
}
