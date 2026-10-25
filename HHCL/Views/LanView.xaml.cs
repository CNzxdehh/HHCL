using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    /// <summary>
    /// 局域网联机（不需要服务器）：
    /// 主机启动游戏 → 在游戏里"开放到局域网" → 启动器从日志读出房间码/地址；
    /// 朋友填直连地址 → 启动后自动连过去（quickPlayMultiplayer）。
    /// </summary>
    public partial class LanView : UserControl, IRefreshable
    {
        private CancellationTokenSource _cts;
        private VersionEntry _version;
        private readonly LanAuth.Server _authServer = new();
        private readonly LanAuth.Client _authClient = new();

        public LanView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            LoadVersions();
        }

        private void LoadVersions()
        {
            var state = AppState.Current;
            state.RefreshVersions();
            VersionBox.ItemsSource = state.Versions;
            JoinVersionBox.ItemsSource = state.Versions;
            VersionBox.DisplayMemberPath = nameof(VersionEntry.DisplayName);
            JoinVersionBox.DisplayMemberPath = nameof(VersionEntry.DisplayName);
            var cur = state.Versions.FirstOrDefault(v => v.Id == state.Config.SelectedVersion)
                      ?? state.Versions.FirstOrDefault();
            if (cur != null)
            {
                VersionBox.SelectedItem = cur;
                JoinVersionBox.SelectedItem = cur;
            }
        }

        private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _version = VersionBox.SelectedItem as VersionEntry;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            var host = (sender as System.Windows.Controls.Primitives.ToggleButton)?.Name == "TabHost";
            HostPanel.Visibility = host ? Visibility.Visible : Visibility.Collapsed;
            JoinPanel.Visibility = host ? Visibility.Collapsed : Visibility.Visible;
        }

        // ==================== 访问密码 ====================

        private void PasswordCheck_Changed(object sender, RoutedEventArgs e)
        {
            var on = PasswordCheck.IsChecked == true;
            PasswordBox.IsEnabled = on;
            RandomPwdBtn.IsEnabled = on;
            // 不自动填默认密码了，空着让用户自己填（想要随机就点「随机」）
            if (!on) PasswordBox.Clear();
        }

        /// <summary>把框里可能混进来的空白字符去掉，光标停在末尾。</summary>
        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            var raw = tb.Text ?? "";
            var clean = raw.Trim();
            // 中间也不留空白，避免"看着有空格又删不掉"
            clean = clean.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
            if (clean == raw) return;
            var pos = tb.SelectionStart;
            tb.Text = clean;
            tb.SelectionStart = Math.Min(pos, clean.Length);
            tb.SelectionLength = 0;
        }

        private void RandomPwd_Click(object sender, RoutedEventArgs e)
        {
            PasswordBox.Text = RandomPassword();
        }

        private static string RandomPassword()
        {
            var rnd = new Random();
            return rnd.Next(100000, 999999).ToString();
        }

        /// <summary>当前生效的密码（没开密码时返回空）。</summary>
        private string CurrentPassword =>
            (PasswordCheck.IsChecked == true ? (PasswordBox.Text ?? "").Trim() : "");

        // ==================== 主机 ====================

        private async void HostStart_Click(object sender, RoutedEventArgs e)
        {
            var v = VersionBox.SelectedItem as VersionEntry;
            if (v == null) { RoomStateText.Text = "先选一个游戏版本。"; return; }
            if (AppState.Current.IsRunning) { RoomStateText.Text = "游戏已经在运行了。"; return; }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            RoomStateText.Text = "正在启动游戏…";

            var session = AppState.Current.Launch(v, out var err);
            if (session == null)
            {
                RoomStateText.Text = "启动失败：" + err;
                return;
            }
            MainWindow.Instance?.SetStatus("游戏已启动，进世界后按 Esc → 开放到局域网", true);

            // 轮询日志，等玩家在游戏里开放到局域网
            await WatchForRoomAsync(v, _cts.Token);
        }

        private async Task WatchForRoomAsync(VersionEntry v, CancellationToken ct)
        {
            var workDir = LaunchBuilder.GameDirFor(AppState.Current.Config, v);
            for (int i = 0; i < 600 && !ct.IsCancellationRequested; i++)   // 最多等 5 分钟
            {
                ct.ThrowIfCancellationRequested();
                var port = LanMultiplayer.DetectLanPort(workDir);
                if (port.HasValue)
                {
                    // 开了密码 → 在本机起一个带认证的代理端口，朋友只能从代理进
                    var pwd = CurrentPassword;
                    var room = LanMultiplayer.MakeRoom(port.Value);
                    var connectAddr = room.Address;
                    var needProxy = !string.IsNullOrEmpty(pwd);

                    if (needProxy)
                    {
                        if (!_authServer.Start(port.Value, pwd))
                        {
                            Dispatcher.Invoke(() => RoomStateText.Text = "起代理端口失败（可能端口都被占了）。");
                            return;
                        }
                        var ip = LanMultiplayer.BestIPv4();
                        connectAddr = string.IsNullOrEmpty(ip)
                            ? _authServer.Port.ToString()
                            : $"{ip}:{_authServer.Port}";
                        Log.Write($"lan room with password, proxy on {_authServer.Port}");
                    }

                    // 开始应答局域网房间探测，朋友就能在「加入房间」里搜到我们
                    try { LanDiscovery.StartResponder(port.Value); } catch (Exception ex) { Log.Write("lan responder: " + ex.Message); }
                    Dispatcher.Invoke(() => ShowRoom(room, connectAddr, pwd));
                    return;
                }
                await Task.Delay(1000);
            }
            Dispatcher.Invoke(() => RoomStateText.Text = "等了 5 分钟还没检测到房间。确认已在游戏里「开放到局域网」。");
        }

        private void ShowRoom(LanRoom room, string connectAddr, string password)
        {
            RoomBox.Visibility = Visibility.Visible;
            RoomCodeText.Text = room.Code;
            RoomAddressText.Text = connectAddr;

            var withPwd = !string.IsNullOrEmpty(password);
            PasswordRow.Visibility = withPwd ? Visibility.Visible : Visibility.Collapsed;
            CopyPwdBtn.Visibility = withPwd ? Visibility.Visible : Visibility.Collapsed;
            RoomPasswordText.Text = password;

            ShareBox.Visibility = Visibility.Visible;
            if (withPwd)
            {
                ShareText.Text = $"在同一个 WiFi / 局域网下，把这个地址发给你：{connectAddr}，房间密码 {password}。"
                               + "你在「加入房间」里填地址和密码就能进来。";
                RoomStateText.Text = $"房间已开（{room.FoundAt} 检测到），已开启访问密码。";
            }
            else
            {
                ShareText.Text = $"我在局域网里的地址是 {connectAddr}，房间码 {room.Code}。"
                               + "同一个 WiFi 下，把这个地址发给你，你在「加入房间」里填进去就能连上。";
                RoomStateText.Text = $"房间已开（{room.FoundAt} 检测到）。";
            }
            MainWindow.Instance?.SetStatus("局域网房间已就绪");
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RoomCodeText.Text);
                RoomStateText.Text = "房间码已复制。";
            }
            catch (Exception ex) { RoomStateText.Text = "复制失败：" + ex.Message; }
        }

        private void CopyShare_Click(object sender, RoutedEventArgs e)
        {
            var t = ShareText.Text ?? "";
            if (t.Length == 0) return;
            try
            {
                System.Windows.Clipboard.SetText(t);
                MainWindow.Instance?.SetStatus("已复制话术");
            }
            catch (Exception ex) { Log.Write("copy share: " + ex.Message); }
        }

        private void CopyAddr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RoomAddressText.Text);
                RoomStateText.Text = "地址已复制，发给朋友就行。";
            }
            catch (Exception ex) { RoomStateText.Text = "复制失败：" + ex.Message; }
        }

        private void CopyPwd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RoomPasswordText.Text);
                RoomStateText.Text = "密码已复制，发给朋友。";
            }
            catch (Exception ex) { RoomStateText.Text = "复制失败：" + ex.Message; }
        }

        // ==================== 客机 ====================

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                if (t.Length > 0) AddrBox.Text = t;
            }
            catch { }
        }

        private void PastePwd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                if (t.Length > 0) JoinPwdBox.Text = t;
            }
            catch { }
        }

        /// <summary>扫描同一局域网里能连的房间。</summary>
        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            ScanBtn.IsEnabled = false;
            ScanStateText.Text = "正在搜索…";
            RoomList.Visibility = Visibility.Collapsed;
            RoomList.ItemsSource = null;
            try
            {
                var rooms = await LanDiscovery.ScanAsync(1600, _cts?.Token ?? CancellationToken.None);
                if (rooms.Count == 0)
                {
                    ScanStateText.Text = "没搜到";
                    JoinStateText.Text = "附近没搜到房间：对方可能没开「开放到局域网」，或 Windows 防火墙拦了 UDP 探测。"
                                      + "这时还是让对方把地址发给你手动填。";
                }
                else
                {
                    ScanStateText.Text = $"找到 {rooms.Count} 个";
                    RoomList.ItemsSource = rooms;
                    RoomList.Visibility = Visibility.Visible;
                    JoinStateText.Text = "点右边的「加入」直接进，或者复制地址用别的启动器连。";
                }
            }
            catch (Exception ex)
            {
                Log.Write("lan scan: " + ex.Message);
                ScanStateText.Text = "搜索失败";
            }
            finally
            {
                ScanBtn.IsEnabled = true;
            }
        }

        private void RoomList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoomList.SelectedItem is Core.DiscoveredRoom r) AddrBox.Text = r.Address;
        }

        /// <summary>点房间里的「加入」：填地址并直接进入。</summary>
        private void JoinRoom_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string addr) AddrBox.Text = addr;
            Join_Click(sender, e);
        }

        private void Join_Click(object sender, RoutedEventArgs e)
        {
            var v = JoinVersionBox.SelectedItem as VersionEntry;
            if (v == null) { JoinStateText.Text = "先选一个游戏版本。"; return; }
            if (AppState.Current.IsRunning) { JoinStateText.Text = "游戏已经在运行了，先关掉。"; return; }

            var text = (AddrBox.Text ?? "").Trim();
            if (!LanMultiplayer.TryParseAddress(text, out var host, out var port))
            {
                JoinStateText.Text = "地址格式不对，请填 192.168.1.23:51234 这样（IP:端口）。";
                return;
            }
            if (string.IsNullOrEmpty(host))
            {
                JoinStateText.Text = "只填房间码不够，还需要主机 IP（比如 192.168.1.23）。";
                return;
            }

            var pwd = (JoinPwdBox.Text ?? "").Trim();
            var addr = $"{host}:{port}";

            // 有密码 → 走本地代理：先跟主机校验密码，通过了才把游戏流量转过去
            if (!string.IsNullOrEmpty(pwd))
            {
                if (!_authClient.Start(host, port, pwd))
                {
                    JoinStateText.Text = "起本地代理失败。";
                    return;
                }
                addr = $"127.0.0.1:{_authClient.LocalPort}";
            }

            var session = AppState.Current.Launch(v, addr, out var err);
            if (session == null)
            {
                _authClient.Stop();
                JoinStateText.Text = "启动失败：" + err;
                return;
            }

            if (!string.IsNullOrEmpty(pwd))
            {
                JoinStateText.Text = $"已启动，正在用密码连接 {host}:{port}…（密码错的话会连不上）";
                MainWindow.Instance?.SetStatus($"正在用密码连接 {host}:{port}");
                WatchAuthResult();
            }
            else
            {
                JoinStateText.Text = $"已启动，正在连 {addr}…（如果连不上，确认两边在同一个局域网、主机的防火墙放行了该端口）";
                MainWindow.Instance?.SetStatus($"正在连接 {addr}");
            }
        }

        /// <summary>盯一下密码校验结果，好告诉用户到底成没成。</summary>
        private async void WatchAuthResult()
        {
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                var r = _authClient.LastResult;
                if (string.IsNullOrEmpty(r)) continue;
                Dispatcher.Invoke(() =>
                {
                    if (r.Contains("密码不对")) JoinStateText.Text = "密码不对，连不上。跟朋友核对一下密码。";
                    else if (r.Contains("已通过")) JoinStateText.Text = "密码验证通过，正在进入房间…";
                    else JoinStateText.Text = "连接失败：" + r;
                });
                if (r.Contains("密码不对") || r.Contains("已通过")) return;
            }
        }
    }
}
