using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HHCL.Core;
using HHCL.Views;

namespace HHCL
{
    public interface IRefreshable
    {
        void OnActivated();
    }

    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private readonly Dictionary<string, UserControl> _cache = new Dictionary<string, UserControl>();
        private string _current = "";

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            // 状态栏版本号跟 AppInfo 走，升级只要改 AppInfo.Version 一处
            StatusVersion.Text = $"{Core.AppInfo.Name} {Core.AppInfo.Version}";

            var cfg = AppState.Current.Config;
            if (cfg.WindowW >= MinWidth && cfg.WindowH >= MinHeight)
            {
                Width = cfg.WindowW;
                Height = cfg.WindowH;
            }

            SourceInitialized += (s, e) => Native.ApplyDarkFrame(this);
            Loaded += MainWindow_Loaded;
            Closing += (s, e) =>
            {
                if (WindowState == WindowState.Normal)
                {
                    AppState.Current.Config.WindowW = Width;
                    AppState.Current.Config.WindowH = Height;
                }
                AppState.Current.Save();
            };
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Navigate("home");
            SetStatus("正在扫描本地版本与 Java…", true);

            await Task.Run(() =>
            {
                AppState.Current.RefreshVersions();
                AppState.Current.RefreshJavas();
            });

            Dispatcher.Invoke(() =>
            {
                UpdateAccountChip();
                var count = AppState.Current.Versions.Count;
                var java = AppState.Current.Javas.FirstOrDefault();
                StatusJava.Text = java != null ? $"Java {java.Version}" : "未检测到 Java";
                SetStatus(count > 0 ? $"已找到 {count} 个本地版本" : "没有找到本地版本，去「下载」页安装一个吧");
                if (PageHost.Content is IRefreshable r) r.OnActivated();
            });
        }

        public void SetStatus(string text, bool busy = false)
        {
            StatusText.Text = text;
            StatusDot.Fill = busy
                ? new SolidColorBrush(Color.FromRgb(0xF2, 0xB4, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
        }

        public void UpdateAccountChip()
        {
            var acc = AppState.Current.Config.CurrentAccount;
            ChipInitial.Text = acc.Initial;
            ChipName.Text = acc.Name;
            ChipType.Text = acc.TypeText;
        }

        public void NavigateTo(string key)
        {
            switch (key)
            {
                case "home": NavHome.IsChecked = true; break;
                case "versions": NavVersions.IsChecked = true; break;
                case "download": NavDownload.IsChecked = true; break;
                case "mods": NavMods.IsChecked = true; break;
                case "modpacks": NavModpacks.IsChecked = true; break;
                case "bedrock": break;   // 基岩版不再是侧栏页面（已并入启动页），这里不再切换侧栏
                case "accounts": NavAccounts.IsChecked = true; break;
                case "settings": NavSettings.IsChecked = true; break;
            }
            Navigate(key);
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ReferenceEquals(sender, NavHome)) Navigate("home");
            else if (ReferenceEquals(sender, NavVersions)) Navigate("versions");
            else if (ReferenceEquals(sender, NavDownload)) Navigate("download");
            else if (ReferenceEquals(sender, NavMods)) Navigate("mods");
            else if (ReferenceEquals(sender, NavModpacks)) Navigate("modpacks");
            else if (ReferenceEquals(sender, NavLan)) Navigate("lan");
            else if (ReferenceEquals(sender, NavAccounts)) Navigate("accounts");
            else if (ReferenceEquals(sender, NavSettings)) Navigate("settings");
        }

        private void Navigate(string key)
        {
            var changed = _current != key;
            _current = key;
            Log.Write("navigate -> " + key);
            _detailStack.Clear();          // 从侧栏切页面 = 退出详情页

            if (!_cache.TryGetValue(key, out var view))
            {
                view = Create(key);
                if (view == null) return;
                _cache[key] = view;
            }

            PageHost.Content = view;

            if (changed)
            {
                view.Opacity = 0;
                var tt = new TranslateTransform(0, 12);
                view.RenderTransform = tt;

                var sb = new Storyboard();
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(fade, view);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                sb.Children.Add(fade);

                var move = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(move, tt);
                Storyboard.SetTargetProperty(move, new PropertyPath("Y"));
                sb.Children.Add(move);

                sb.Begin();
            }

            if (view is IRefreshable r) r.OnActivated();
        }

        private UserControl Create(string key)
        {
            switch (key)
            {
                case "home": return new HomeView();
                case "versions": return new VersionsView();
                case "download": return new DownloadView();
                case "mods": return new ModsView();
                case "modpacks": return new ModpacksView();
                case "bedrock": return new BedrockView();
                case "lan": return new LanView();
                case "accounts": return new AccountsView();
                case "settings": return new SettingsView();
                default: return null;
            }
        }

        // ==================== 详情页（点进模组/整合包） ====================

        private readonly Stack<UserControl> _detailStack = new Stack<UserControl>();

        /// <summary>进入一个详情页（模组详情、整合包详情…），侧栏页面留在栈里可以返回。</summary>
        public void PushDetail(UserControl view)
        {
            if (view == null) return;
            if (PageHost.Content is UserControl cur) _detailStack.Push(cur);
            PageHost.Content = view;
            view.Opacity = 0;
            var tt = new TranslateTransform(0, 12);
            view.RenderTransform = tt;
            var sb = new Storyboard();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fade, view);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
            var move = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(move, tt);
            Storyboard.SetTargetProperty(move, new PropertyPath("Y"));
            sb.Children.Add(move);
            sb.Begin();
        }

        /// <summary>从详情页返回上一层；没有上一层就回到模组页。</summary>
        public void PopDetail()
        {
            if (_detailStack.Count == 0) { Navigate("mods"); return; }
            var prev = _detailStack.Pop();
            PageHost.Content = prev;
            if (prev is IRefreshable r) r.OnActivated();
        }

        private void AccountChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            NavigateTo("accounts");
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnMax_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                BtnMaxGlyph.Text = "\uE923";
                Root.Margin = new Thickness(7);
            }
            else
            {
                BtnMaxGlyph.Text = "\uE922";
                Root.Margin = new Thickness(0);
            }
        }
    }
}
