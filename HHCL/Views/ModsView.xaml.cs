using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HHCL.Core;

namespace HHCL.Views
{
    public class FileItem
    {
        public string Path { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public long Size { get; set; }
        public bool Enabled { get; set; }
        public bool IsPack { get; set; }

        public string SizeText => AppState.FormatSize(Size) + (Enabled ? "" : " · 已禁用");
    }

    public partial class ModsView : UserControl, IRefreshable
    {
        private bool _onlineBusy;
        private int _offset;          // 当前已经取了多少条（用于"加载更多"）
        private bool _hasMore = true;
        private List<object> _results = new List<object>();
        private object _selected;     // 当前选中的项目（ModrinthProject 或 CfProject）

        public ModsView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            var state = AppState.Current;
            state.RefreshVersions();
            BuildVersionList(state);
            FillMcBox();
            VersionBox.SelectedItem = state.Versions.FirstOrDefault(v => v.Id == state.Config.SelectedVersion) ?? state.Versions.FirstOrDefault();
            if (VersionBox.SelectedItem == null)
            {
                SubText.Text = "还没有安装任何版本。";
                PathText.Text = "";
                ModList.ItemsSource = null;
                PackList.ItemsSource = null;
                ModEmpty.Visibility = Visibility.Visible;
                PackEmpty.Visibility = Visibility.Visible;
                return;
            }

            LoadFiles();
            UpdateFilterText();
        }

        /// <summary>
        /// 构建版本下拉列表：按"版本族"插入组头（1.21.4 / 1.21.4-rc1 / 1.21.4-pre1 归一组）。
        /// 组头用 ComboBoxItem 直接作为项（WPF 会把 UIElement 项直接当容器用），
        /// 禁用且不可点，所以选不中。之前试过 GroupStyle + 自定义模板，结果整个下拉渲染为空，别再用。
        /// </summary>
        private void BuildVersionList(AppState state)
        {
            var items = new List<object>();
            string lastGroup = null;
            foreach (var v in state.Versions)
            {
                var g = v.GroupKey;
                if (!string.IsNullOrEmpty(g) && g != lastGroup)
                {
                    var head = new ComboBoxItem
                    {
                        Content = g,
                        IsHitTestVisible = false,
                        Focusable = false,
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Padding = new Thickness(14, 9, 0, 4)
                    };
                    if (Application.Current?.TryFindResource("AccentLightBrush") is System.Windows.Media.Brush b)
                        head.Foreground = b;
                    items.Add(head);
                    lastGroup = g;
                }
                items.Add(v);
            }
            VersionBox.ItemsSource = items;
        }

        private VersionEntry Current => VersionBox.SelectedItem as VersionEntry;

        /// <summary>按类型算出安装目标目录（mods / resourcepacks / shaderpacks）。</summary>
        private string TargetDirFor(string projectType)
        {
            var folder = projectType == "mod" ? "mods"
                : projectType == "resourcepack" ? "resourcepacks" : "shaderpacks";
            return Path.Combine(RootDir, folder);
        }

        private string RootDir
        {
            get
            {
                var v = Current;
                return v == null ? AppState.Current.Config.GameDir : LaunchBuilder.GameDirFor(AppState.Current.Config, v);
            }
        }

        // ---------------- 顶部切换 ----------------

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool online = TabOnline.IsChecked == true;
            PanelInstalled.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
            PanelOnline.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
            BarInstalled.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
            BarOnline.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
            if (!online) LoadFiles();
            else if (_results.Count == 0) _ = SearchAsync(true); // 切到在线时直接拉一批热门，不用先输入关键词
        }

        private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Current == null) return;
            AppState.Current.Config.SelectedVersion = Current.Id;
            AppState.Current.Save();
            LoadFiles();
            UpdateFilterText();
        }

        private void RefreshLocal_Click(object sender, RoutedEventArgs e) => LoadFiles();

        // ---------------- 已安装 ----------------

        private void LoadFiles()
        {
            var modsDir = Path.Combine(RootDir, "mods");
            var packsDir = Path.Combine(RootDir, "resourcepacks");
            PathText.Text = RootDir;

            var mods = ScanDir(modsDir, new[] { ".jar" }, false);
            var packs = ScanDir(packsDir, new[] { ".zip" }, true);

            ModList.ItemsSource = mods;
            PackList.ItemsSource = packs;
            ModEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PackEmpty.Visibility = packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ModCount.Text = $"{mods.Count} 个";
            PackCount.Text = $"{packs.Count} 个";
            SubText.Text = $"当前版本 {Current?.Id} 的模组与资源包（版本隔离 {(AppState.Current.Config.IsolateVersions ? "开启" : "关闭")}）";
        }

        private static List<FileItem> ScanDir(string dir, string[] exts, bool isPack)
        {
            var result = new List<FileItem>();
            try
            {
                if (!Directory.Exists(dir)) return result;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(f);
                    bool disabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var baseName = disabled ? name.Substring(0, name.Length - 9) : name;
                    if (!exts.Any(ext => baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;

                    result.Add(new FileItem
                    {
                        Path = f,
                        DisplayName = baseName,
                        Enabled = !disabled,
                        IsPack = isPack,
                        Size = new FileInfo(f).Length
                    });
                }
            }
            catch { }
            return result.OrderByDescending(x => x.Enabled).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void File_Toggled(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox cb) || !(cb.Tag is FileItem item)) return;
            bool want = cb.IsChecked == true;
            if (want == item.Enabled) return;

            try
            {
                if (want)
                {
                    var target = item.Path.Substring(0, item.Path.Length - 9);
                    if (!File.Exists(target)) File.Move(item.Path, target);
                }
                else
                {
                    if (!item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        File.Move(item.Path, item.Path + ".disabled");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("改名失败：" + ex.Message, "HHCL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            LoadFiles();
        }

        private void File_Delete(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is FileItem item)) return;
            if (MessageBox.Show($"把「{item.DisplayName}」移到回收站？\n\n文件会被移动到游戏目录下的 HHCL-回收站，可以手动找回。",
                    "HHCL", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                var bin = Path.Combine(RootDir, "HHCL-回收站");
                Directory.CreateDirectory(bin);
                var dest = Path.Combine(bin, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(item.Path)}");
                File.Move(item.Path, dest);
                MainWindow.Instance?.SetStatus("已移到 HHCL-回收站");
            }
            catch (Exception ex)
            {
                MessageBox.Show("移动失败：" + ex.Message, "HHCL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            LoadFiles();
        }

        private void OpenMods_Click(object sender, RoutedEventArgs e) => OpenDir(Path.Combine(RootDir, "mods"));

        private void OpenPacks_Click(object sender, RoutedEventArgs e) => OpenDir(Path.Combine(RootDir, "resourcepacks"));

        private static void OpenDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        // ---------------- 在线下载（Modrinth） ----------------

        private string ProjectType => (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";

        private string TargetFolder => ProjectType == "mod" ? "mods"
            : ProjectType == "resourcepack" ? "resourcepacks" : "shaderpacks";

        private string SourceKind => (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";

        private bool UseCurseForge => SourceKind == "curseforge"
                                      && !string.IsNullOrWhiteSpace(AppState.Current.Config.CurseForgeApiKey);

        /// <summary>基岩版模式：数据源是 CurseForge 的 minecraft-bedrock 分区。</summary>
        private bool IsBedrock => SourceKind == "bedrock";
        private bool BedrockReady => IsBedrock && !string.IsNullOrWhiteSpace(AppState.Current.Config.CurseForgeApiKey);

        /// <summary>Java 的分类 → 基岩版的 CF 分类 slug。</summary>
        private static string BedrockClassOf(string projectType) =>
            projectType == "resourcepack" ? "texture-packs" : projectType == "shader" ? "shaders" : "addons";

        private CfBedrockGame _bedrockGame;

        /// <summary>版本下拉：本地已安装的游戏版本（去重）+ 全部版本。</summary>
        private void FillMcBox()
        {
            if (McBox == null) return;
            var keep = McBox.SelectedIndex;
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
        }

        /// <summary>版本下拉选中的版本（空 = 不按版本过滤）。</summary>
        private string McFilter => (McBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void McBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _ = SearchAsync(true);
        }

        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _ = SearchAsync(true);
        }

        private void UpdateFilterText()
        {
            var v = Current;
            var src = IsBedrock ? "基岩版 addon（CurseForge）" : UseCurseForge ? "CurseForge" : "Modrinth";
            FilterText.Text = v == null ? src : $"{v.McVersion} · {v.Loader} · {src}";
        }

        private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _ = SearchAsync(true);
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = SearchAsync(true);
        }

        private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(true);

        /// <summary>滚动到接近底部时自动加载下一页（替代原来浮在列表中间的“加载更多”按钮）。</summary>
        private void OnlineList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!_hasMore || _onlineBusy) return;
            if (!(e.OriginalSource is ScrollViewer sv)) return;
            if (sv.ScrollableHeight <= 0) return;
            if (e.VerticalOffset + e.ViewportHeight < sv.ScrollableHeight - 40) return;
            _ = SearchAsync(false);
        }

        private async Task SearchAsync(bool reset)
        {
            var query = (SearchBox.Text ?? "").Trim();
            // 空关键词也允许搜：等价于按热度浏览榜单，不用先想关键词
            var browsing = query.Length == 0;
            if (_onlineBusy) return;
            _onlineBusy = true;
            SearchBtn.IsEnabled = false;

            if (reset)
            {
                _offset = 0;
                _hasMore = true;
                _results = new List<object>();
                OnlineList.ItemsSource = null;
                OnlineState.Visibility = Visibility.Visible;
                OnlineStateText.Text = IsBedrock ? "正在从 CurseForge 载入基岩版 addon…"
                    : UseCurseForge ? "正在从 CurseForge 载入…" : "正在从 Modrinth 载入…";
                ResultCount.Text = "";

                if (IsBedrock && !BedrockReady)
                {
                    OnlineStateText.Text = "搜基岩版 addon 需要 CurseForge API Key：到「设置 → 在线资源」里填一个免费的 Key，再回来搜。";
                }
                else if (SourceKind == "curseforge" && !UseCurseForge)
                {
                    OnlineStateText.Text = "用 CurseForge 需要先在「设置 → 在线资源」里填一个免费的 API Key。\n这次先用 Modrinth 帮你搜。";
                }
            }

            try
            {
                int got = 0;
                var v = Current;
                var mc = v?.McVersion;
                var loader = (v?.Loader ?? "原版").ToLowerInvariant();
                if (loader == "原版" || loader == "vanilla") loader = "";
                // 版本下拉选了具体版本就按它过滤（优先于上方的游戏版本选择器）
                var mcFilter = McFilter;
                var byMc = mcFilter.Length > 0;
                if (byMc) mc = mcFilter;

                if (IsBedrock)
                {
                    if (!BedrockReady) return;
                    _bedrockGame ??= await CurseForgeBedrock.GetGameAsync(
                        AppState.Current.Config.CurseForgeApiKey, CancellationToken.None);
                    var bList = await CurseForgeBedrock.SearchAsync(query, BedrockClassOf(ProjectType),
                        _bedrockGame, AppState.Current.Config.CurseForgeApiKey, CancellationToken.None, 50, _offset);
                    _results.AddRange(bList);
                    got = bList.Count;
                }
                else
                {
                    bool cf = UseCurseForge;
                    if (cf)
                    {
                        var list = await CurseForge.SearchAsync(query, ProjectType, mc, loader,
                            AppState.Current.Config.CurseForgeApiKey, CancellationToken.None, 50, _offset);
                        _results.AddRange(list);
                        got = list.Count;
                    }
                    else
                    {
                        var list = await Modrinth.SearchAsync(query, ProjectType, mc, loader,
                            CancellationToken.None, 50, _offset, byMc);
                        _results.AddRange(list);
                        got = list.Count;

                        // 中文搜索增强：Modrinth 上项目标题基本都是英文，
                        // 中文命中太少时用英文同义词补搜一批（结果去重合并，中文的排前面）
                        if (got < 5 && reset && SearchTerms.HasChinese(query))
                        {
                            foreach (var en in SearchTerms.Expand(query))
                            {
                                var more = await Modrinth.SearchAsync(en, ProjectType, mc, loader,
                                    CancellationToken.None, 20, 0);
                                var seen = new HashSet<string>(_results.Select(x => (x as ModrinthProject)?.ProjectId ?? ""));
                                foreach (var m in more)
                                    if (seen.Add(m.ProjectId)) _results.Add(m);
                            }
                        }
                    }
                }

                _offset += got;
                _hasMore = got >= 50;

                OnlineList.ItemsSource = null;
                OnlineList.ItemsSource = _results;
                ResultCount.Text = _hasMore
                    ? $"{_results.Count} 个结果（往下滚自动加载）"
                    : $"{_results.Count} 个结果";
                OnlineState.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_results.Count == 0)
                    OnlineStateText.Text = browsing || string.IsNullOrEmpty(query)
                        ? "没有找到。换个类型，或者取消版本/加载器筛选试试。"
                        : "没有找到，换个关键词、或者取消版本筛选试试";
                UpdateFilterText();
            }
            catch (Exception ex)
            {
                OnlineStateText.Text = "搜索失败：" + ex.Message;
                OnlineState.Visibility = Visibility.Visible;
            }
            finally
            {
                _onlineBusy = false;
                SearchBtn.IsEnabled = true;
            }
        }

        private async void OnlineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OnlineList.SelectedItem == null) return;
            _selected = OnlineList.SelectedItem;

            // 点一下就跳到这个模组自己的界面（详情页），不是填右栏
            if (_selected is ModrinthProject mp0)
            {
                MainWindow.Instance?.PushDetail(new ModDetailView(mp0, "modrinth", TargetDirFor(ProjectType)));
                return;
            }
            if (_selected is CfProject cf0)
            {
                var src = IsBedrock ? "bedrock" : "curseforge";
                string dir;
                if (IsBedrock)
                {
                    // 基岩版要按账号 XUID 找存档目录，多账号机器上不能随便挑一个
                    var xuid = await ResolveXuidAsync();
                    dir = BedrockProfiles.FindGameDataDir(out _, xuid);
                }
                else dir = TargetDirFor(ProjectType);
                MainWindow.Instance?.PushDetail(new ModDetailView(cf0, src, dir));
                return;
            }

            // 其余情况（理论上不会到）直接忽略
        }

        /// <summary>拿当前微软账号的 XUID（基岩版存档目录按它区分）。</summary>
        private static async Task<string> ResolveXuidAsync()
        {
            var acc = AppState.Current.Config.CurrentAccount;
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken)) return acc.Xuid ?? "";
            if (!string.IsNullOrEmpty(acc.Xuid)) return acc.Xuid;
            try { return await BedrockInstaller.GetXuidAsync(acc, CancellationToken.None); }
            catch (Exception ex) { Log.Write("mods xuid: " + ex.Message); return ""; }
        }

    }
}
