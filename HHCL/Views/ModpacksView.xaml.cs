using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    public partial class ModpacksView : UserControl, IRefreshable
    {
        private bool _busy;
        private ModpackSummary _selected;

        // 分页：往下滚自动加载更多（和下载/模组页一致），避免一次只给 25 条显得“搜到这么少”
        private int _offset;
        private bool _hasMore = true;
        private List<ModpackSummary> _results = new List<ModpackSummary>();

        public ModpacksView()
        {
            InitializeComponent();
        }

        private bool _firstShown;

        public void OnActivated()
        {
            FillMcBox();
            // 首次进入直接拉一次热门列表，否则页面空着、必须去动下拉框才出东西
            if (!_firstShown)
            {
                _firstShown = true;
                _ = SearchAsync(true);
                return;
            }
            if (_results.Count == 0 && !_busy) ShowState("输入关键词开始搜索整合包");
        }

        private string Source => (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";
        private string Mc => (McBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

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
        }
        private string Loader => (LoaderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        // ---------------- 搜索 ----------------

        private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) _ = SearchAsync(true);
        }

        private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(true);

        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) _ = SearchAsync(true);
        }

        private void McBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) _ = SearchAsync(true);
        }

        private void LoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) _ = SearchAsync(true);
        }

        private async Task SearchAsync(bool reset)
        {
            var query = (SearchBox.Text ?? "").Trim();
            var source = Source;
            var apiKey = AppState.Current.Config.CurseForgeApiKey;

            if (_busy) return;
            _busy = true;
            SearchBtn.IsEnabled = false;

            if (reset)
            {
                _offset = 0;
                _hasMore = true;
                _results = new List<ModpackSummary>();
                _selected = null;
                PackList.ItemsSource = null;
                if (source == "curseforge" && string.IsNullOrWhiteSpace(apiKey))
                {
                    ShowState("用 CurseForge 需要先在「设置 → 在线资源」里填一个免费的 API Key。\n这次先用 Modrinth 帮你搜。");
                    source = "modrinth";
                }
                else
                {
                    ShowState("正在搜索整合包…");
                }
            }

            try
            {
                List<ModpackSummary> page;
                if (source == "curseforge")
                    page = await ModpackInstaller.SearchCurseForgeAsync(query, Mc, Loader, apiKey, CancellationToken.None, 50, _offset);
                else
                    page = await ModpackInstaller.SearchModrinthAsync(query, Mc, Loader, CancellationToken.None, 50, _offset);

                foreach (var s in page)
                    if (!_results.Any(r => r.ProjectId == s.ProjectId)) _results.Add(s);

                _offset += page.Count;
                _hasMore = page.Count >= 50;

                PackList.ItemsSource = null;
                PackList.ItemsSource = _results;
                ResultCount.Text = _hasMore
                    ? $"{_results.Count} 个（往下滚自动加载）"
                    : $"{_results.Count} 个";
                if (_results.Count == 0)
                    ShowState(query.Length == 0 ? "换个关键词试试" : "没有找到，换个关键词或去掉版本筛选试试");
                else
                    HideState();
            }
            catch (Exception ex)
            {
                ShowState("搜索失败：" + ex.Message);
            }
            finally
            {
                _busy = false;
                SearchBtn.IsEnabled = true;
            }
        }

        /// <summary>滚动到接近底部时自动加载下一页（和下载/模组页一致）。</summary>
        private void PackList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!_hasMore || _busy || _results.Count == 0) return;
            if (!(e.OriginalSource is ScrollViewer sv)) return;
            if (sv.ScrollableHeight <= 0) return;
            if (e.VerticalOffset + e.ViewportHeight < sv.ScrollableHeight - 40) return;
            _ = SearchAsync(false);
        }

        private void ShowState(string text)
        {
            StateText.Text = text;
            StatePanel.Visibility = Visibility.Visible;
        }

        private void HideState() => StatePanel.Visibility = Visibility.Collapsed;

        // ---------------- 选择整合包 ----------------

        private void PackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(PackList.SelectedItem is ModpackSummary s)) return;
            _selected = s;

            // 点一下就跳到这个整合包自己的界面（详情页），那边列全部版本并能直接安装
            MainWindow.Instance?.PushDetail(new ModDetailView(s, AppState.Current.Config.GameDir));
        }

    }
}
