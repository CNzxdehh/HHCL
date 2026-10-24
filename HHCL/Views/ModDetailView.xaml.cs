using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    /// <summary>详情页里一个版本条目的统一包装（模组/整合包、不同来源都能塞进来）。</summary>
    public class DetailVersion
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Loader { get; set; } = "";
        public string Mc { get; set; } = "";
        public string Size { get; set; } = "";
        public string Date { get; set; } = "";
        public object Raw { get; set; } = null!;
        /// <summary>true 表示这是"游戏版本"组头（26.3 / 1.21.4 那种标题行），不是可安装的项。</summary>
        public bool IsHeader { get; set; }
        /// <summary>组键（基础版本号），折叠时用。</summary>
        public string GroupKey { get; set; } = "";
        /// <summary>该组当前是否已折叠。</summary>
        public bool IsCollapsed { get; set; }
    }

    /// <summary>
    /// 模组 / 整合包的详情页：点进一个项目就跳到这里，
    /// 列出它的全部版本（每个版本带加载器、正式版·测试版·快照），选一个直接安装。
    /// </summary>
    public partial class ModDetailView : UserControl
    {
        private readonly string _source;      // modrinth / curseforge / bedrock
        private readonly bool _isModpack;
        private readonly string _projectId;
        private readonly string _targetDir;   // 安装目标（mods 目录 / 基岩版数据目录）
        private readonly string _apiKey;
        private CancellationTokenSource _cts;
        private bool _busy;
        private List<DetailVersion> _grouped = new List<DetailVersion>();   // 完整分组结果（含组头）
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 模组
        public ModDetailView(ModrinthProject p, string source, string targetDir)
        {
            _source = source; _projectId = p.ProjectId; _targetDir = targetDir;
            _apiKey = AppState.Current.Config.CurseForgeApiKey;
            InitializeComponent();
            TitleText.Text = p.Title;
            SubText.Text = $"{p.Author} · {p.DownloadsText} · 模组";
            DescText.Text = p.Description;
            SafeIcon(p.IconUrl);
            ApplyTarget(targetDir);
            VersionHeader.Text = "版本";
            Loaded += async (s, e) => await LoadVersionsAsync();
        }

        public ModDetailView(CfProject p, string source, string targetDir)
        {
            _source = source; _projectId = p.Id; _targetDir = targetDir;
            _apiKey = AppState.Current.Config.CurseForgeApiKey;
            InitializeComponent();
            TitleText.Text = p.Title;
            SubText.Text = $"{p.Author} · {p.DownloadsText} · 模组";
            DescText.Text = p.Description;
            SafeIcon(p.IconUrl);
            ApplyTarget(targetDir);
            VersionHeader.Text = "版本";
            Loaded += async (s, e) => await LoadVersionsAsync();
        }

        // 整合包
        public ModDetailView(ModpackSummary p, string targetDir)
        {
            _source = p.Source; _projectId = p.ProjectId; _targetDir = targetDir;
            _isModpack = true;
            _apiKey = AppState.Current.Config.CurseForgeApiKey;
            InitializeComponent();
            TitleText.Text = p.Title;
            SubText.Text = $"{p.Author} · {p.DownloadsText} · 整合包";
            DescText.Text = p.Description;
            SafeIcon(p.IconUrl);
            ApplyTarget("");
            OpenDirBtn.Visibility = Visibility.Collapsed;   // 整合包装到新建的版本目录里，这里打开没意义
            VersionHeader.Text = "整合包版本";
            Loaded += async (s, e) => await LoadVersionsAsync();
        }

        /// <summary>
        /// 图标要安全加载：URL 为空/不合法/网络不通时不能让整个页面挂掉，
        /// 否则点进详情页直接崩，就进不去了。
        /// <summary>安装目录：顶栏只显示末两段（完整路径放进"打开安装目录"按钮的悬停提示）。</summary>
        private void ApplyTarget(string targetDir)
        {
            if (string.IsNullOrEmpty(targetDir))
            {
                TargetText.Text = "整合包会新建一个独立版本档案";
                OpenDirBtn.ToolTip = null;
                return;
            }
            var parts = targetDir.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
            TargetText.Text = parts.Length >= 2 ? parts[parts.Length - 2] + "\\" + parts[parts.Length - 1] : targetDir;
            OpenDirBtn.ToolTip = "安装到：" + targetDir;
        }

        private void SafeIcon(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                var uri = new Uri(url);
                if (!uri.IsAbsoluteUri) return;
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnDemand;
                img.EndInit();
                IconImg.Source = img;
            }
            catch (Exception ex)
            {
                Log.Write("detail icon: " + ex.Message);   // 图标失败就算了，不影响页面
            }
        }

        private async Task LoadVersionsAsync()
        {
            StateText.Text = "正在获取全部版本…";
            VersionList.ItemsSource = null;
            try
            {
                var list = new List<DetailVersion>();
                if (_isModpack)
                {
                    var summary = new ModpackSummary { Source = _source, ProjectId = _projectId };
                    var releases = await ModpackInstaller.GetReleasesAsync(summary, "", "", _apiKey, CancellationToken.None);
                    foreach (var r in releases)
                        list.Add(new DetailVersion
                        {
                            Name = r.Name,
                            Kind = r.KindText,
                            Loader = r.LoaderHint,
                            Mc = r.McHint,
                            Size = r.SizeText,
                            Raw = r
                        });
                }
                else if (_source == "modrinth")
                {
                    var versions = await Modrinth.GetVersionsAsync(_projectId, "", "", CancellationToken.None);
                    foreach (var v in versions)
                        list.Add(new DetailVersion
                        {
                            Name = v.Name,
                            Kind = v.KindText,
                            Loader = v.LoaderText,
                            Mc = v.GameVersionText,
                            Size = v.SizeText,
                            Raw = v
                        });
                }
                else if (_source == "bedrock")
                {
                    var files = await CurseForgeBedrock.GetFilesAsync(_projectId, _apiKey, CancellationToken.None);
                    foreach (var f in files)
                        list.Add(new DetailVersion
                        {
                            Name = f.FileName,
                            Kind = f.KindText,
                            Loader = f.LoaderText,
                            Mc = f.GameVersionText,
                            Size = f.SizeText,
                            Raw = f
                        });
                }
                else
                {
                    var files = await CurseForge.GetFilesAsync(_projectId, "", "", _apiKey, CancellationToken.None);
                    foreach (var f in files)
                        list.Add(new DetailVersion
                        {
                            Name = f.FileName,
                            Kind = f.KindText,
                            Loader = f.LoaderText,
                            Mc = f.GameVersionText,
                            Size = f.SizeText,
                            Raw = f
                        });
                }

                // 按"游戏版本"分组：先 26.3 组头，再排它下面的各个文件，然后 26.2、1.21.4…
                _grouped = GroupByGameVersion(list);
                // 默认全部折叠：1216 个文件铺开根本没法看，先收起来让用户按需展开
                _collapsed.Clear();
                foreach (var h in _grouped.Where(x => x.IsHeader)) _collapsed.Add(h.GroupKey);
                var real = _grouped.Count(x => !x.IsHeader);
                var groups = _grouped.Count(x => x.IsHeader);
                RebuildVersionList();
                VersionCountText.Text = real > 0 ? $"{groups} 个版本 · {real} 个文件" : "";
                StateText.Text = real > 0
                    ? "按游戏版本分组，选中某个文件后点上面「下载并安装」。"
                    : "这个项目暂时没有可用文件。";
                // 默认选中第一个真实条目（跳过组头）
                var firstReal = VersionList.Items.Cast<DetailVersion>().ToList().FindIndex(x => !x.IsHeader);
                if (firstReal >= 0) VersionList.SelectedIndex = firstReal;
            }
            catch (Exception ex)
            {
                Log.Write("detail versions: " + ex);
                StateText.Text = "获取版本失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 把版本列表按"游戏版本"分组：组头（26.3 / 1.21.4 …）+ 组内文件。
        /// 组按版本号从新到旧排（26.3 &gt; 1.21.4 &gt; 1.20.1），组内保持接口返回的顺序（新的在前）。
        /// </summary>
        private static List<DetailVersion> GroupByGameVersion(List<DetailVersion> list)
        {
            var result = new List<DetailVersion>();
            var groups = new List<(string Key, List<DetailVersion> Items)>();
            var index = new Dictionary<string, List<DetailVersion>>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in list)
            {
                // 按"基础版本号"归并：26.1-snapshot-8 / 26.1-pre-2 都算 26.1 这一组，
                // 否则 Fabric API 会分出 300 多个组，根本没法看
                var key = BaseGameVersion(v.Mc);
                if (!index.TryGetValue(key, out var items))
                {
                    items = new List<DetailVersion>();
                    index[key] = items;
                    groups.Add((key, items));
                }
                // 子项也要带组键，否则折叠时判断不到（点组头会没反应）
                v.GroupKey = key;
                items.Add(v);
            }

            foreach (var g in groups.OrderByDescending(g => g.Key, VersionComparer.Instance))
            {
                result.Add(new DetailVersion
                {
                    Name = g.Key,
                    Size = $"{g.Items.Count} 个文件",
                    IsHeader = true,
                    GroupKey = g.Key
                });
                result.AddRange(g.Items);
            }
            return result;
        }

        /// <summary>取基础版本号：26.1-snapshot-8 → 26.1，26.2-pre-1 → 26.2，1.21.4 → 1.21.4。</summary>
        private static string BaseGameVersion(string mc)
        {
            if (string.IsNullOrWhiteSpace(mc)) return "其它";
            var first = mc.Split('/').FirstOrDefault()?.Trim() ?? "";
            if (first.Length == 0) return "其它";
            var cut = first.IndexOfAny(new[] { '-', '_' });
            var baseVer = cut > 0 ? first.Substring(0, cut) : first;
            return baseVer.Length > 0 ? baseVer : first;
        }

        /// <summary>版本号比较：按数字段逐段比，26.3 要排在 1.21.4 前面。</summary>
        private class VersionComparer : IComparer<string>
        {
            public static readonly VersionComparer Instance = new VersionComparer();
            public int Compare(string a, string b)
            {
                var pa = (a ?? "").Split('.');
                var pb = (b ?? "").Split('.');
                for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
                {
                    var na = i < pa.Length && int.TryParse(pa[i], out var x) ? x : -1;
                    var nb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : -1;
                    if (na != nb) return na.CompareTo(nb);        // 版本号大的更大，配合 OrderByDescending 就是从新到旧
                }
                return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>按折叠状态重建列表（折叠的组只留组头）。</summary>
        private void RebuildVersionList()
        {
            var outp = new List<DetailVersion>();
            foreach (var item in _grouped)
            {
                if (item.IsHeader)
                {
                    item.IsCollapsed = _collapsed.Contains(item.GroupKey);
                    outp.Add(item);
                    continue;
                }
                if (_collapsed.Contains(item.GroupKey)) continue;
                outp.Add(item);
            }
            VersionList.ItemsSource = outp;
            UpdateCollapseAllButton();
        }

        /// <summary>点组头：折叠 / 展开这一组。</summary>
        private void Header_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            if (!(fe.DataContext is DetailVersion dv) || !dv.IsHeader) return;
            if (_collapsed.Contains(dv.GroupKey)) _collapsed.Remove(dv.GroupKey);
            else _collapsed.Add(dv.GroupKey);
            RebuildVersionList();
        }

        /// <summary>全部收起 / 全部展开（按钮 Tag 里记着当前该做哪个）。</summary>
        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            _collapsed.Clear();
            var wantCollapse = sender is FrameworkElement fe && fe.Tag is bool b && b;
            if (wantCollapse)
            {
                foreach (var h in _grouped.Where(x => x.IsHeader)) _collapsed.Add(h.GroupKey);
            }
            RebuildVersionList();
        }

        private void UpdateCollapseAllButton()
        {
            var total = _grouped.Count(x => x.IsHeader);
            var collapsed = _collapsed.Count;
            if (CollapseAllBtn == null) return;
            CollapseAllBtn.Content = collapsed >= total && total > 0 ? "全部展开" : "全部收起";
            CollapseAllBtn.Tag = !(collapsed >= total && total > 0);
        }

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(VersionList.SelectedItem is DetailVersion dv)) return;
            if (dv.IsHeader) { InstallBtn.IsEnabled = false; return; }   // 组头不能装
            InstallBtn.IsEnabled = !_busy;
            PickText.Text = $"已选：{dv.Name}　{dv.Kind}　{dv.Loader}　{dv.Size}";
            PickText.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (!(VersionList.SelectedItem is DetailVersion dv)) { PickText.Text = "先选一个版本。"; return; }

            _busy = true;
            InstallBtn.IsEnabled = false;
            CancelBtn.Visibility = Visibility.Visible;
                ProgText.Visibility = Visibility.Visible;
            _cts = new CancellationTokenSource();
            Bar.Visibility = Visibility.Visible;
            Bar.Value = 0;
            ProgText.Text = "";

            var progress = new Progress<DlProgress>(p =>
            {
                if (p.TotalBytes > 0) Bar.Value = Math.Min(100, p.DoneBytes * 100.0 / p.TotalBytes);
                else Bar.Value = p.Percent;
                ProgText.Text = $"{p.Stage} {p.Current} {p.SizeText} {p.SpeedText}";
            });

            try
            {
                MainWindow.Instance?.SetStatus("正在安装 " + dv.Name, true);
                string done;
                if (_isModpack)
                {
                    var release = (ModpackRelease)dv.Raw;
                    var info = await ModpackInstaller.ParseAsync(release, _cts.Token, _apiKey);
                    var id = await ModpackInstaller.InstallAsync(info, AppState.Current.Config, progress, _cts.Token, _apiKey);
                    done = $"整合包装好了：{id}。去「启动」页就能开玩。";
                    AppState.Current.RefreshVersions();
                    var hit = AppState.Current.Versions.FirstOrDefault(v => v.Id == id);
                    if (hit != null) { AppState.Current.Config.SelectedVersion = id; AppState.Current.Save(); }
                }
                else if (dv.Raw is ModrinthVersion mv)
                {
                    var path = await Modrinth.InstallAsync(mv, _targetDir, progress, _cts.Token);
                    done = $"装好了：{Path.GetFileName(path)}";
                }
                else if (dv.Raw is CfFile cf)
                {
                    if (_source == "bedrock")
                    {
                        var res = await BedrockAddonInstaller.InstallAsync(cf, _apiKey, _targetDir, progress, _cts.Token);
                        done = res.Ok
                            ? $"装进基岩版了：{string.Join("、", res.Installed)}。进游戏的世界设置里勾选一下就生效。"
                            : "安装失败：" + res.Message;
                    }
                    else
                    {
                        var path = await CurseForge.InstallAsync(cf, _targetDir, progress, _cts.Token, _apiKey);
                        done = $"装好了：{Path.GetFileName(path)}";
                    }
                }
                else done = "这个版本类型不支持安装。";

                Bar.Value = 100;
                ProgText.Text = done;
                MainWindow.Instance?.SetStatus("安装完成");
            }
            catch (OperationCanceledException)
            {
                ProgText.Text = "已取消。";
                MainWindow.Instance?.SetStatus("已取消");
            }
            catch (Exception ex)
            {
                Log.Write("detail install: " + ex);
                ProgText.Text = "安装失败：" + ex.Message;
                MainWindow.Instance?.SetStatus("安装失败");
            }
            finally
            {
                _busy = false;
                CancelBtn.Visibility = Visibility.Collapsed;
                ProgText.Visibility = Visibility.Collapsed;
                InstallBtn.IsEnabled = VersionList.SelectedItem is DetailVersion s2 && !s2.IsHeader;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OpenDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_targetDir))
                {
                    Directory.CreateDirectory(_targetDir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_targetDir) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { ProgText.Text = "打不开目录：" + ex.Message; }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.PopDetail();
    }
}
