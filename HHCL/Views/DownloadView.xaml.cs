using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    public partial class DownloadView : UserControl, IRefreshable
    {
        private bool _loaded;
        private bool _busy;
        private CancellationTokenSource _cts;
        private List<RemoteVersion> _all = new List<RemoteVersion>();

        public DownloadView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                SourceBox.SelectedIndex = AppState.Current.Config.DownloadSource == "mojang" ? 1 : 0;
            };
        }

        public void OnActivated()
        {
            if (!_loaded && !_busy) _ = LoadAsync();
        }

        private string Source => _activeSource ?? AppState.Current.Config.DownloadSource;
        private string _activeSource;

        private async Task LoadAsync()
        {
            _busy = true;
            ShowState("正在获取版本列表…");
            try
            {
                _all = await MinecraftDownloader.GetManifestAsync(AppState.Current.Config.DownloadSource, CancellationToken.None);
                _activeSource = MinecraftDownloader.LastUsedSource;
                _loaded = true;
                ApplyFilter();
                HideState();
                var fallback = _activeSource != AppState.Current.Config.DownloadSource;
                MainWindow.Instance?.SetStatus(fallback
                    ? $"镜像不可用，已切换到另外的源获取到 {_all.Count} 个版本"
                    : $"已获取 {_all.Count} 个可下载版本");
            }
            catch (Exception ex)
            {
                Log.Write("manifest failed: " + ex);
                ShowState("获取失败：" + ex.Message);
                MainWindow.Instance?.SetStatus("版本列表获取失败");
            }
            finally { _busy = false; }
        }

        private void ApplyFilter()
        {
            var q = (SearchBox.Text ?? "").Trim();
            var showSnapshot = SnapshotCheck.IsChecked == true;
            var list = _all.Where(v =>
                (showSnapshot || v.Type == "release") &&
                (q.Length == 0 || v.Id.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Take(400).ToList();
            RemoteList.ItemsSource = list;
            if (list.Count > 0) RemoteList.SelectedIndex = 0;
            else if (_loaded) ShowState(q.Length > 0 ? "没找到匹配的版本" : "列表为空");
        }

        private void ShowState(string text)
        {
            ListStateText.Text = text;
            ListState.Visibility = Visibility.Visible;
        }

        private void HideState() => ListState.Visibility = Visibility.Collapsed;

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _loaded = false;
            _ = LoadAsync();
        }

        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            var tag = (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "bmclapi";
            if (AppState.Current.Config.DownloadSource == tag) return;
            AppState.Current.Config.DownloadSource = tag;
            AppState.Current.Save();
            _activeSource = null;
            if (_loaded) { _loaded = false; _ = LoadAsync(); }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loaded) ApplyFilter();
        }

        private void Snapshot_Changed(object sender, RoutedEventArgs e)
        {
            if (_loaded) ApplyFilter();
        }

        private void RemoteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(RemoteList.SelectedItem is RemoteVersion rv)) return;
            DetailName.Text = "Minecraft " + rv.Id;
            DetailType.Text = rv.TypeText;
            DetailDate.Text = rv.ReleaseTime;
            DetailBody.Text = rv.Type == "release"
                ? "正式版，包含完整的客户端、运行库与资源文件。下载体积通常 300 MB ～ 500 MB。"
                : "快照/预览版本，内容和正式版一致，但可能不稳定。";
            InstallHint.Text = "";
            InstallBtn.Content = InstalledExists(rv.Id) ? "重新安装" : "下载并安装";
            RefreshLoaderVersions();
        }

        private string LoaderKind => (LoaderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "vanilla";
        private string LoaderVersion => LoaderVerBox.SelectedItem as string ?? LoaderVerBox.Text;
        private string _loaderKey = "";

        private void LoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _loaderKey = "";
            RefreshLoaderVersions();
        }

        private async void RefreshLoaderVersions()
        {
            if (!IsLoaded || !(RemoteList.SelectedItem is RemoteVersion rv)) return;
            var kind = LoaderKind;

            if (kind == "vanilla")
            {
                LoaderVerBox.ItemsSource = null;
                LoaderHint.Text = "不装加载器：模组需要 Fabric / Forge / NeoForge 之类的加载器才能跑。";
                return;
            }

            var key = rv.Id + "|" + kind;
            if (_loaderKey == key) return;
            _loaderKey = key;

            LoaderVerBox.ItemsSource = null;
            LoaderHint.Text = "正在获取加载器版本…";
            try
            {
                var list = await LoaderInstaller.GetLoadersAsync(rv.Id, kind, CancellationToken.None);
                if (_loaderKey != key) return;
                LoaderVerBox.ItemsSource = list;
                if (list.Count > 0) LoaderVerBox.SelectedIndex = 0;
                LoaderHint.Text = kind == "forge" || kind == "neoforge"
                    ? "Forge / NeoForge 会先装好原版，再运行官方安装器（需要几十秒到几分钟）。"
                    : $"会先装好原版 {rv.Id}，再叠加 {LoaderInstaller.KindName(kind)}。模组要装到加载器对应的版本目录里。";
            }
            catch (Exception ex)
            {
                if (_loaderKey != key) return;
                LoaderHint.Text = "获取加载器版本失败：" + ex.Message;
            }
        }

        private bool InstalledExists(string id) =>
            AppState.Current.Versions.Any(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (!(RemoteList.SelectedItem is RemoteVersion rv))
            {
                InstallHint.Text = "先在左边选一个版本。";
                return;
            }

            _busy = true;
            InstallBtn.IsEnabled = false;
            CancelBtn.Visibility = Visibility.Visible;
            Bar.Value = 0;
            InstallHint.Text = "";
            _cts = new CancellationTokenSource();

            var progress = new Progress<DlProgress>(p =>
            {
                Bar.Value = p.Percent;
                ProgStage.Text = $"{p.Stage} {p.Done}/{p.Total}";
                ProgRight.Text = p.TotalBytes > 0 ? $"{p.SizeText}  {p.SpeedText}" : "";
            });

            try
            {
                var cfg = AppState.Current.Config;
                MainWindow.Instance?.SetStatus($"正在安装 {rv.Id}", true);

                // 1) 原版（加载器依赖于它）
                var vanillaJson = Path.Combine(cfg.GameDir, "versions", rv.Id, rv.Id + ".json");
                if (!File.Exists(vanillaJson))
                {
                    ProgStage.Text = "安装原版";
                    await MinecraftDownloader.InstallAsync(cfg.GameDir, Source, rv, progress, _cts.Token);
                }
                else
                {
                    ProgStage.Text = "原版已存在，跳过";
                }

                string installed = rv.Id;

                // 2) 加载器
                var kind = LoaderKind;
                if (kind != "vanilla")
                {
                    var loaderVer = (LoaderVerBox.SelectedItem as LoaderVersionInfo)?.Version;
                    if (string.IsNullOrEmpty(loaderVer))
                    {
                        InstallHint.Text = "加载器版本还没加载好，等一下再点。";
                        return;
                    }
                    installed = await LoaderInstaller.InstallAsync(cfg, rv.Id, kind, loaderVer, progress, _cts.Token);
                }

                Bar.Value = 100;
                ProgStage.Text = "安装完成";
                ProgRight.Text = "";
                InstallHint.Text = "装好了，可以直接去「启动」页开玩。";
                InstallBtn.Content = "重新安装";

                AppState.Current.RefreshVersions();
                var hit = AppState.Current.Versions.FirstOrDefault(v => v.Id == installed)
                          ?? AppState.Current.Versions.FirstOrDefault(v => v.Id.StartsWith(rv.Id));
                if (hit != null)
                {
                    AppState.Current.Config.SelectedVersion = hit.Id;
                    AppState.Current.Save();
                    InstallHint.Text = $"装好了：{hit.Id}，可以直接去「启动」页开玩。";
                }
                MainWindow.Instance?.SetStatus($"{installed} 安装完成");
            }
            catch (OperationCanceledException)
            {
                InstallHint.Text = "已取消下载，已下载的文件会保留，再次点击可继续。";
                ProgStage.Text = "已取消";
                MainWindow.Instance?.SetStatus("已取消下载");
            }
            catch (Exception ex)
            {
                InstallHint.Text = "安装失败：" + ex.Message;
                ProgStage.Text = "失败";
                MainWindow.Instance?.SetStatus("安装失败");
            }
            finally
            {
                _busy = false;
                InstallBtn.IsEnabled = true;
                CancelBtn.Visibility = Visibility.Collapsed;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }
    }
}
