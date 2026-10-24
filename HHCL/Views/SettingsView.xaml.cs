using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;
using Microsoft.Win32;

namespace HHCL.Views
{
    public class JavaOption
    {
        public string Display { get; set; } = "";
        public string Path { get; set; } = "";
        public override string ToString() => Display;
    }

    public partial class SettingsView : UserControl, IRefreshable
    {
        private bool _loading;
        private CancellationTokenSource _javaCts;

        public SettingsView()
        {
            InitializeComponent();
        }

        public void OnActivated() => LoadAll();

        private void LoadAll()
        {
            _loading = true;
            var cfg = AppState.Current.Config;
            DirBox.Text = cfg.GameDir;
            MemMaxBox.Text = cfg.MemoryMax.ToString();
            MemMinBox.Text = cfg.MemoryMin.ToString();
            WidthBox.Text = cfg.WindowWidth.ToString();
            HeightBox.Text = cfg.WindowHeight.ToString();
            JvmBox.Text = cfg.ExtraJvm;
            IsolateCheck.IsChecked = cfg.IsolateVersions;
            MinimizeCheck.IsChecked = cfg.MinimizeOnLaunch;
            ChineseLangCheck.IsChecked = cfg.AutoChineseLang;
            var dark = cfg.Theme != Core.ThemeManager.Light;
            ThemeDark.IsChecked = dark;
            ThemeLight.IsChecked = !dark;
            SourceBox.SelectedIndex = cfg.DownloadSource == "mojang" ? 1 : 0;
            CfKeyBox.Text = cfg.CurseForgeApiKey;
            CfHint.Text = string.IsNullOrWhiteSpace(cfg.CurseForgeApiKey)
                ? "还没填：模组页只能用 Modrinth。"
                : "已填写：模组页的来源下拉里可以切到 CurseForge。";

            var totalMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576);
            MemSuggest.Text = totalMb > 0 ? $"建议 {Math.Min(totalMb / 2, totalMb - 2048):0} MB 左右（本机约 {totalMb / 1024} GB）" : "";

            LoadJavas();
            _loading = false;

            AboutText.Text =
                "HHCL " + Core.AppInfo.Version + " · Minecraft 启动器\n" +
                "界面：.NET 10 / WPF 自绘 WinUI 3 风格 · 启动核心兼容原版与 Fabric、Forge、NeoForge 的版本 JSON\n" +
                "配置文件：" + Config.ConfigPath + "\n" +
                "HHCL 是第三方启动器，与 Mojang / Microsoft 无隶属关系，未获其官方认可或背书。\n" +
                "Minecraft 是 Mojang Studios 的商标；游戏需使用你自己的正版账号游玩。";
        }

        private void LoadJavas()
        {
            var cfg = AppState.Current.Config;
            var options = new System.Collections.Generic.List<JavaOption>
            {
                new JavaOption { Display = "自动选择（推荐）", Path = "" }
            };
            foreach (var j in AppState.Current.Javas)
            {
                var vendor = string.IsNullOrEmpty(j.Vendor) ? "" : j.Vendor + " · ";
                options.Add(new JavaOption { Display = $"{vendor}Java {j.Version} · {j.Path}", Path = j.Path });
            }

            JavaBox.ItemsSource = options;
            var hit = options.FirstOrDefault(o => string.Equals(o.Path, cfg.JavaPath, StringComparison.OrdinalIgnoreCase)) ?? options[0];
            JavaBox.SelectedItem = hit;
        }

        private void Save()
        {
            if (_loading) return;
            AppState.Current.Save();
            MainWindow.Instance?.SetStatus("设置已保存");
        }

        private void DirBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var dir = (DirBox.Text ?? "").Trim().TrimEnd('\\', '/');
            if (!Directory.Exists(dir))
            {
                MessageBox.Show("这个目录不存在。", "HHCL", MessageBoxButton.OK, MessageBoxImage.Warning);
                DirBox.Text = AppState.Current.Config.GameDir;
                return;
            }
            if (AppState.Current.Config.GameDir == dir) return;
            AppState.Current.Config.GameDir = dir;
            AppState.Current.RefreshVersions();
            AppState.Current.Config.SelectedVersion = AppState.Current.Versions.FirstOrDefault()?.Id ?? "";
            Save();
        }

        private void BrowseDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择 Minecraft 游戏目录（.minecraft）" };
            try
            {
                var cur = AppState.Current.Config.GameDir;
                if (Directory.Exists(cur)) dlg.InitialDirectory = cur;
            }
            catch { }

            if (dlg.ShowDialog() == true)
            {
                DirBox.Text = dlg.FolderName;
                DirBox_LostFocus(null, null);
            }
        }

        private void OpenDir_Click(object sender, RoutedEventArgs e) => OpenPath(AppState.Current.Config.GameDir);

        private void MemMax_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (!int.TryParse(MemMaxBox.Text, out var v) || v < 512 || v > 65536)
            {
                MemMaxBox.Text = AppState.Current.Config.MemoryMax.ToString();
                return;
            }
            var cfg = AppState.Current.Config;
            cfg.MemoryMax = v;
            if (cfg.MemoryMin > cfg.MemoryMax) { cfg.MemoryMin = Math.Max(512, cfg.MemoryMax / 2); MemMinBox.Text = cfg.MemoryMin.ToString(); }
            Save();
        }

        private void MemMin_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (!int.TryParse(MemMinBox.Text, out var v) || v < 512)
            {
                MemMinBox.Text = AppState.Current.Config.MemoryMin.ToString();
                return;
            }
            var cfg = AppState.Current.Config;
            cfg.MemoryMin = Math.Min(v, cfg.MemoryMax);
            MemMinBox.Text = cfg.MemoryMin.ToString();
            Save();
        }

        private void Size_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var cfg = AppState.Current.Config;
            if (int.TryParse(WidthBox.Text, out var w) && w >= 640 && w <= 7680) cfg.WindowWidth = w;
            if (int.TryParse(HeightBox.Text, out var h) && h >= 480 && h <= 4320) cfg.WindowHeight = h;
            WidthBox.Text = cfg.WindowWidth.ToString();
            HeightBox.Text = cfg.WindowHeight.ToString();
            Save();
        }

        private void Jvm_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppState.Current.Config.ExtraJvm = JvmBox.Text ?? "";
            Save();
        }

        private void Isolate_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppState.Current.Config.IsolateVersions = IsolateCheck.IsChecked == true;
            Save();
        }

        private void Minimize_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppState.Current.Config.MinimizeOnLaunch = MinimizeCheck.IsChecked == true;
            Save();
        }

        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var theme = ThemeLight.IsChecked == true ? Core.ThemeManager.Light : Core.ThemeManager.Dark;
            AppState.Current.Config.Theme = theme;
            Core.ThemeManager.Apply(theme);
            Save();
        }

        private void ChineseLang_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppState.Current.Config.AutoChineseLang = ChineseLangCheck.IsChecked == true;
            Save();
        }

        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var tag = (SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "bmclapi";
            AppState.Current.Config.DownloadSource = tag;
            Save();
        }

        private void JavaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (!(JavaBox.SelectedItem is JavaOption o)) return;
            AppState.Current.Config.JavaPath = o.Path;
            Save();
            MainWindow.Instance?.SetStatus(string.IsNullOrEmpty(o.Path) ? "Java 使用自动选择" : "已指定 Java：" + o.Path);
        }

        private void RescanJava_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.SetStatus("正在检测 Java…", true);
            _loading = true;
            AppState.Current.RefreshJavas();
            LoadJavas();
            _loading = false;
            MainWindow.Instance?.SetStatus($"检测到 {AppState.Current.Javas.Count} 个 Java");
        }

        private async void InstallJava21_Click(object sender, RoutedEventArgs e) => await InstallJavaAsync(21);
        private async void InstallJava17_Click(object sender, RoutedEventArgs e) => await InstallJavaAsync(17);
        private async void InstallJava25_Click(object sender, RoutedEventArgs e) => await InstallJavaAsync(25);

        private async Task InstallJavaAsync(int major)
        {
            if (_javaCts != null) return;
            _javaCts = new CancellationTokenSource();
            Jdk25Btn.IsEnabled = false;
            Jdk21Btn.IsEnabled = false;
            Jdk17Btn.IsEnabled = false;
            JavaBar.Visibility = Visibility.Visible;
            JavaBar.Value = 0;
            JavaBarText.Text = "准备中…";

            var progress = new Progress<(string stage, double percent, long done, long total)>(p =>
            {
                JavaBar.Value = p.percent;
                JavaBarText.Text = p.total > 0
                    ? $"{p.stage} {p.percent:0}%  ({AppState.FormatSize(p.done)} / {AppState.FormatSize(p.total)})"
                    : p.stage;
            });

            try
            {
                var exe = await JavaInstaller.InstallAsync(major, progress, _javaCts.Token);
                if (!string.IsNullOrEmpty(exe))
                {
                    AppState.Current.Config.JavaPath = exe;
                    AppState.Current.Save();
                    _loading = true;
                    AppState.Current.RefreshJavas();
                    LoadJavas();
                    _loading = false;
                    JavaBarText.Text = "安装完成，已设为默认 Java";
                    MainWindow.Instance?.SetStatus($"Java {major} 安装完成");
                }
                else
                {
                    JavaBarText.Text = "解压完成，但没有找到 javaw.exe";
                }
            }
            catch (OperationCanceledException)
            {
                JavaBarText.Text = "已取消";
            }
            catch (Exception ex)
            {
                JavaBarText.Text = "下载失败：" + ex.Message;
            }
            finally
            {
                Jdk25Btn.IsEnabled = true;
                Jdk21Btn.IsEnabled = true;
                Jdk17Btn.IsEnabled = true;
                _javaCts?.Dispose();
                _javaCts = null;
            }
        }

        private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenPath(Config.DataDir);

        private void OpenCfKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://console.curseforge.com/?#/api-keys") { UseShellExecute = true });
            }
            catch { }
        }

        private void SaveCfKey_Click(object sender, RoutedEventArgs e)
        {
            var key = (CfKeyBox.Text ?? "").Trim();
            AppState.Current.Config.CurseForgeApiKey = key;
            AppState.Current.Save();
            CfHint.Text = string.IsNullOrWhiteSpace(key)
                ? "已清空：模组页只能用 Modrinth。"
                : "已保存：去模组页把来源切成 CurseForge 就能用了。";
            MainWindow.Instance?.SetStatus("已保存 CurseForge API Key");
        }

        private void OpenCrash_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(Config.DataDir, "crash.log");
            if (!File.Exists(path))
            {
                MessageBox.Show("还没有崩溃记录。", "HHCL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true }); } catch { }
        }

        private static void OpenPath(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
