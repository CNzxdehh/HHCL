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

        public LanView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            LoadVersions();
            UpdateLocalIpHint();
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

        private void UpdateLocalIpHint()
        {
            var ips = LanMultiplayer.LocalIPv4();
            var best = LanMultiplayer.BestIPv4();
            if (string.IsNullOrEmpty(best)) return;
            ShareText.Text = $"我在局域网里的地址是 {best}。同一个 WiFi 下，把上面复制到的直连地址发给我就行。";
        }

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
                    var room = LanMultiplayer.MakeRoom(port.Value);
                    // 开始应答局域网房间探测，朋友就能在「加入房间」里搜到我们
                    try { LanDiscovery.StartResponder(port.Value); } catch (Exception ex) { Log.Write("lan responder: " + ex.Message); }
                    Dispatcher.Invoke(() => ShowRoom(room));
                    return;
                }
                await Task.Delay(1000);
            }
            Dispatcher.Invoke(() => RoomStateText.Text = "等了 5 分钟还没检测到房间。确认已在游戏里「开放到局域网」。");
        }

        private void ShowRoom(LanRoom room)
        {
            RoomBox.Visibility = Visibility.Visible;
            RoomCodeText.Text = room.Code;
            RoomAddressText.Text = room.Address;
            ShareText.Text = $"我在局域网里的地址是 {room.Address}，房间码 {room.Code}。"
                           + "同一个 WiFi 下，把这个地址发给你，你在「加入房间」里填进去就能连上。";
            RoomStateText.Text = $"房间已开（{room.FoundAt} 检测到）。";
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

            var addr = $"{host}:{port}";
            var session = AppState.Current.Launch(v, addr, out var err);
            if (session == null)
            {
                JoinStateText.Text = "启动失败：" + err;
                return;
            }
            JoinStateText.Text = $"已启动，正在连 {addr}…（如果连不上，确认两边在同一个局域网、主机的防火墙放行了该端口）";
            MainWindow.Instance?.SetStatus($"正在连接 {addr}");
        }
    }
}
