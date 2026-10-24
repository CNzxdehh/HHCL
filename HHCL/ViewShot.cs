using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HHCL
{
    /// <summary>
    /// 把某个界面直接渲染成 PNG（不依赖鼠标点击，避免坐标漂移）。
    /// 用法：HHCL.exe --shot mods|modpacks|bedrock|home [宽] [高]
    /// </summary>
    public static class ViewShot
    {
        /// <summary>空转等待，让界面/异步搜索把事情做完（不能直接 Thread.Sleep 阻塞 UI 线程）。</summary>
        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(ms),
                DispatcherPriority.Normal,
                (s, e) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        public static int Run(string[] args)
        {
            // --shot <page> <w> <h> [light]
            // --shot window <page> <w> <h> [light]   渲染整个主窗口（能看到侧边栏/标题栏/窗口渐变）
            bool fullWindow = args.Length > 1 && args[1].ToLowerInvariant() == "window";
            int idx = fullWindow ? 2 : 1;
            var which = args.Length > idx ? args[idx].ToLowerInvariant() : "mods";
            int w = args.Length > idx + 1 ? int.Parse(args[idx + 1]) : 1200;
            int h = args.Length > idx + 2 ? int.Parse(args[idx + 2]) : 800;

            int code = 0;
            // 在 STA 线程上创建 WPF 元素并渲染
            var t = new Thread(() =>
            {
                // App.xaml 已经创建过 Application，这里只能复用，不能再 new
                var app = Application.Current ?? new Application();
                // 主题资源如果没加载就补上，样式才能生效
                try
                {
                    if (app.Resources.MergedDictionaries.Count == 0)
                    {
                        var uri = new Uri("pack://application:,,,/Themes/Fluent.xaml", UriKind.Absolute);
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
                    }
                }
                catch (Exception ex) { Console.WriteLine("主题加载失败: " + ex.Message); }

                // 主题必须在视图创建之前应用（画刷在元素创建时就绑定了）
                var themeArg = args.Length > idx + 3 ? args[idx + 3] : "";
                if (themeArg == "light") Core.ThemeManager.Apply(Core.ThemeManager.Light);
                else if (themeArg == "dark") Core.ThemeManager.Apply(Core.ThemeManager.Dark);

                UserControl view = null;
                    if (which == "detail")
                    {
                        try
                        {
                            var proj = new Core.ModrinthProject
                            {
                                ProjectId = "P7dR8mSH",
                                Title = "Fabric API",
                                Description = "Lightweight and modular API providing common hooks and intercompatibility measures utilized by mods using the Fabric toolchain.",
                                Author = "modmuss50",
                                Downloads = 287178000,
                                IconUrl = "https://cdn.modrinth.com/data/P7dR8mSH/icon.png"
                            };
                            var dv = new Views.ModDetailView(proj, "modrinth", @"E:\.minecraftersions.3\mods");
                            var demo = new System.Collections.Generic.List<Views.DetailVersion>
                            {
                                new Views.DetailVersion { Name = "Sodium 0.9.3-beta.1 for Fabric", Kind = "正式版", Loader = "fabric", Mc = "26.3", Size = "1.8 MB", Date = "10-23" },
                                new Views.DetailVersion { Name = "Sodium 0.9.3-beta.1 for NeoForge", Kind = "正式版", Loader = "neoforge", Mc = "26.3", Size = "1.8 MB", Date = "10-23" },
                                new Views.DetailVersion { Name = "Sodium 0.9.2 for Fabric", Kind = "正式版", Loader = "fabric", Mc = "26.2", Size = "1.7 MB", Date = "10-18" },
                                new Views.DetailVersion { Name = "Sodium 0.8.15-beta.1 for Fabric", Kind = "测试版", Loader = "fabric", Mc = "26.1", Size = "1.7 MB", Date = "10-11" },
                                new Views.DetailVersion { Name = "Sodium 0.6.4 for Fabric", Kind = "正式版", Loader = "fabric", Mc = "1.21.4", Size = "1.7 MB", Date = "09-02" },
                                new Views.DetailVersion { Name = "Sodium 0.5.8+mc1.21.1 for Fabric", Kind = "正式版", Loader = "fabric", Mc = "1.21.1", Size = "1.6 MB", Date = "08-20" },
                                new Views.DetailVersion { Name = "Sodium 0.4.10 for Fabric", Kind = "正式版", Loader = "fabric", Mc = "1.20.1", Size = "1.6 MB", Date = "07-15" },
                            };
                            var vt = typeof(Views.ModDetailView);
                            var vlist = vt.GetField("VersionList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(dv) as ListBox;
                            if (vlist != null) { vlist.ItemsSource = demo; vlist.SelectedIndex = 1; }
                            // 用反射调用分组逻辑（真实代码路径）
                            var grp = vt.GetMethod("GroupByGameVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                            if (grp != null && vlist != null)
                            {
                                var grouped = grp.Invoke(null, new object[] { demo }) as System.Collections.IEnumerable;
                                if (grouped != null)
                                {
                                    var gl = new System.Collections.Generic.List<Views.DetailVersion>();
                                    foreach (var one in grouped) gl.Add((Views.DetailVersion)one);
                                    vlist.ItemsSource = gl;
                                    vlist.SelectedIndex = 1;
                                    var vc2 = vt.GetField("VersionCountText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(dv) as TextBlock;
                                    if (vc2 != null) vc2.Text = gl.Count(x => !x.IsHeader) + " 个文件";
                                }
                            }
                            var vc = vt.GetField("VersionCountText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(dv) as TextBlock;
                            if (vc != null) vc.Text = demo.Count + " 个";
                            var st = vt.GetField("StateText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(dv) as TextBlock;
                            if (st != null) st.Text = "选一个版本（不同加载器 / 正式版·测试版·快照都在里面），然后点下面安装。";
                            view = dv;
                        }
                        catch (Exception ex) { Console.WriteLine("detail build fail: " + ex.Message); }
                    }

                try
                {
                    if (view == null) view = which switch
                    {
                        "mods" => new Views.ModsView(),
                        "modpacks" => new Views.ModpacksView(),
                        "bedrock" => new Views.BedrockView(),
                        "download" => new Views.DownloadView(),
                        "lan" => new Views.LanView(),
                        "home" => new Views.HomeView(),
                        "versions" => new Views.VersionsView(),
                        "accounts" => new Views.AccountsView(),
                        "settings" => new Views.SettingsView(),
                        _ => new Views.ModsView()
                    };
                    if (view is Views.ModsView mv) { mv.OnActivated(); }
                    else if (view is Views.ModpacksView pv) pv.OnActivated();
                    else if (view is Views.BedrockView bv) bv.OnActivated();
                    else if (view is Views.DownloadView dv) dv.OnActivated();
                    else if (view is Views.LanView lv) lv.OnActivated();
                    else if (view is Views.HomeView hv) hv.OnActivated();
                    else if (view is Views.VersionsView vv) vv.OnActivated();
                    else if (view is Views.AccountsView av) av.OnActivated();
                    else if (view is Views.SettingsView sv) sv.OnActivated();

                    // 反射驱动：填关键词 → 触发搜索 → 选中第一个结果 → 让右侧版本列表装载
                    if (view is Views.ModsView mv2 && which == "modsonline")
                    {
                        var t = view.GetType();
                        var searchBox = (TextBox)t.GetField("SearchBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view);
                        var sourceBox = (ComboBox)t.GetField("SourceBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view);
                        var onlineList = (ListBox)t.GetField("OnlineList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view);
                        // 切到"在线下载"页签，否则来源/类型下拉是隐藏的看不到
                        var tabOnline = t.GetField("TabOnline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view) as System.Windows.Controls.Primitives.ToggleButton;
                        if (tabOnline != null) tabOnline.IsChecked = true;
                        // 直接切可见性（Tab_Checked 的效果），确保渲染出来的是在线下载那条工具栏
                        foreach (var (fname, vis) in new[] { ("BarOnline", true), ("PanelOnline", true), ("BarInstalled", false), ("PanelInstalled", false) })
                        {
                            var el = t.GetField(fname, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view) as FrameworkElement;
                            if (el != null) el.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                        }
                        Pump(800);
                        if (searchBox != null) searchBox.Text = "fabric";
                        // 直接调用私有的 SearchAsync(bool)，比等 SelectionChanged 可靠
                        var m = t.GetMethod("SearchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (m != null)
                        {
                            // 不能 task.Wait（会阻塞 UI 线程造成死锁），要一边转调度器一边等
                            m.Invoke(mv2, new object[] { true });
                            Pump(45000);
                        }
                        if (onlineList != null && onlineList.Items.Count > 0)
                        {
                            onlineList.SelectedIndex = 0;                     // 触发 → 右侧装载文件列表
                            Pump(6000);
                            Console.WriteLine("RESULT_COUNT:" + onlineList.Items.Count);
                        }
                        else Console.WriteLine("RESULT_COUNT:0");
                        var stateText = t.GetField("OnlineStateText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view) as TextBlock;
                        var countText = t.GetField("ResultCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view) as TextBlock;
                        var verList = t.GetField("OnlineVerBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view) as ListBox;
                        // 渲染验证：直接塞真实结构的数据（不依赖网络），检查列表项的排版
                        if (verList != null)
                        {
                            var demo = new System.Collections.Generic.List<Core.ModrinthVersion>
                            {
                                new Core.ModrinthVersion { Name = "Sodium 0.9.3-beta.1 for Fabric", Loaders = new List<string>{"fabric"}, GameVersions = new List<string>{"26.3"}, FileSize = 1800000 },
                                new Core.ModrinthVersion { Name = "Sodium 0.9.3-beta.1 for NeoForge", Loaders = new List<string>{"neoforge"}, GameVersions = new List<string>{"26.3"}, FileSize = 1810000 },
                                new Core.ModrinthVersion { Name = "Sodium 0.9.2 for Fabric", Loaders = new List<string>{"fabric"}, GameVersions = new List<string>{"26.2"}, FileSize = 1790000 },
                                new Core.ModrinthVersion { Name = "Sodium 0.8.15-beta.1 for Fabric", Loaders = new List<string>{"fabric"}, GameVersions = new List<string>{"26.1"}, FileSize = 1780000 },
                                new Core.ModrinthVersion { Name = "Sodium 0.6.4 for Fabric", Loaders = new List<string>{"fabric"}, GameVersions = new List<string>{"1.21.4"}, FileSize = 1700000 },
                                new Core.ModrinthVersion { Name = "Sodium 0.5.8 for Fabric", Loaders = new List<string>{"fabric"}, GameVersions = new List<string>{"1.21.1"}, FileSize = 1650000 },
                                new Core.ModrinthVersion { Name = "Sodium 0.4.10 for Fabric", Loaders = new List<string>{"fabric"}, GameVersions = new List<string>{"1.20.1"}, FileSize = 1600000 },
                            };
                            verList.ItemsSource = demo;
                            verList.SelectedIndex = 1;
                        }
                        Console.WriteLine("STATE:" + (stateText?.Text ?? "<null>"));
                        Console.WriteLine("COUNT:" + (countText?.Text ?? "<null>"));
                        Console.WriteLine("VERLIST_TYPE:" + (verList?.GetType().Name ?? "<null>") + " ITEMS:" + (verList?.Items.Count.ToString() ?? "-"));
                    }

                    // 先按目标尺寸布局，再渲染
                    view.Width = w;
                    view.Height = h;
                    view.Measure(new Size(w, h));
                    view.Arrange(new Rect(0, 0, w, h));
                    view.UpdateLayout();

                    // 等一帧，让模板/虚拟化完成
                    Pump(600);

                    // 页面背景是透明的，渲染时垫一层应用底色，否则浅色文字在白底上看不见
                    // 背板跟着主题走（之前写死深色，浅色主题下页面会看成深色）
                    // 用 WindowBgBrush（窗口那层渐变）当背板，这样浅色主题下能看出渐变有没有换色
                    var bg = Application.Current?.Resources["WindowBgBrush"] as System.Windows.Media.Brush;
                    var host = new Border
                    {
                        Background = bg ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x1F)),
                        Padding = new Thickness(24, 16, 24, 16),
                        Child = view
                    };
                    host.Measure(new Size(w, h));
                    host.Arrange(new Rect(0, 0, w, h));
                    host.UpdateLayout();

                    // VERIFY_THEME: 可用 --shot <page> <w> <h> light 切浅色主题
                    var theme = args.Length > idx + 3 ? args[idx + 3] : "";
                    if (theme == "light") Core.ThemeManager.Apply(Core.ThemeManager.Light);

                    // SHOT_WINDOW: 渲染整个主窗口（能看到侧边栏/标题栏/窗口背景渐变）
                    System.Windows.FrameworkElement target = host;
                    if (fullWindow)
                    {
                        try
                        {
                            var win = new MainWindow();
                            win.Width = w; win.Height = h;
                            win.PageHost.Content = view;
                            win.Measure(new Size(w, h));
                            win.Arrange(new Rect(0, 0, w, h));
                            win.UpdateLayout();
                            Pump(500);
                            win.UpdateLayout();
                            target = win;
                        }
                        catch (Exception ex) { Console.WriteLine("SHOT_WINDOW_FAIL: " + ex.Message); }
                    }

                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(target);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    var path = Path.Combine(Core.Config.DataDir, $"shot_{which}.png");
                    using (var fs = File.Create(path)) enc.Save(fs);
                    Console.WriteLine("SHOT:" + path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SHOT_FAIL: " + ex);
                    code = 1;
                }
                finally
                {
                    // 不调 Shutdown：整个进程马上就要退出，且 App 是全局单例
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return code;
        }
    }
}