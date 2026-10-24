using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    public partial class HomeView : UserControl, IRefreshable
    {
        private GameSession _hooked;

        public HomeView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            RefreshVersions();
            RefreshStats();
            SyncRunningState();
            RefreshBedrock();
        }

        private void RefreshVersions()
        {
            var state = AppState.Current;
            var versions = state.Versions;
            var selectedId = state.Config.SelectedVersion;

            VersionBox.ItemsSource = null;
            VersionBox.ItemsSource = versions;
            VersionBox.DisplayMemberPath = "Id";

            var hit = versions.FirstOrDefault(v => v.Id == selectedId) ?? versions.FirstOrDefault();
            VersionBox.SelectedItem = hit;

            if (hit == null)
            {
                MetaMc.Text = "无版本";
                MetaLoader.Text = "—";
                MetaJava.Text = "—";
                Hint("本地还没有任何版本，去左边「下载」页安装一个吧。");
            }
        }

        private void RefreshStats()
        {
            var state = AppState.Current;
            var java = state.SelectedVersion != null ? state.JavaFor(state.SelectedVersion) : state.CurrentJava;

            JavaValue.Text = java != null ? $"Java {java.Version}" : "未检测到";
            JavaSub.Text = java != null
                ? $"{(string.IsNullOrEmpty(java.Vendor) ? "OpenJDK" : java.Vendor)} · {(java.Is64 ? "64 位" : "32 位")}"
                : "去「设置」页指定";

            MemValue.Text = $"{state.Config.MemoryMax} MB";
            MemSub.Text = $"最小 {state.Config.MemoryMin} MB";

            VerValue.Text = $"{state.Versions.Count} 个";
            VerSub.Text = state.Versions.Count > 0
                ? $"版本隔离 {(state.Config.IsolateVersions ? "开启" : "关闭")}"
                : "还没有安装";
        }

        private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionBox.SelectedItem is VersionEntry v)
            {
                AppState.Current.Config.SelectedVersion = v.Id;
                AppState.Current.Save();

                MetaMc.Text = v.McVersion;
                MetaLoader.Text = v.Loader;
                var java = AppState.Current.JavaFor(v);
                MetaJava.Text = java != null ? $"Java {java.Major}" : "Java ?";
                HideHint();

                if (java != null && v.JavaMajor > 0 && java.Major < v.JavaMajor)
                    Hint($"这个版本需要 Java {v.JavaMajor} 或更高，当前选中的是 Java {java.Major}。可以到「设置」页一键下载 Java {v.JavaMajor}。");
                else
                    HideHint();

                RefreshStats();
            }
        }

        private void Hint(string text)
        {
            HintText.Text = text;
            HintText.Visibility = Visibility.Visible;
        }

        private void HideHint()
        {
            HintText.Visibility = Visibility.Collapsed;
        }

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            var state = AppState.Current;

            if (state.IsRunning)
            {
                if (MessageBox.Show("游戏正在运行，确定要强制结束吗？", "HHCL", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    state.Session.Kill();
                    AppendLog("[HHCL] 已请求结束游戏进程");
                }
                return;
            }

            if (!(VersionBox.SelectedItem is VersionEntry version))
            {
                Hint("还没有选择版本，先去「下载」页安装一个。");
                return;
            }

            PlayBtn.IsEnabled = false;
            PlayText.Text = "检查中";
            PlaySub.Text = "";
            HideHint();

            var ok = await RepairFlow.EnsureAsync(version, AppendLog, busy =>
            {
                PlayText.Text = busy ? "补齐文件中" : "启动游戏";
            });
            PlayBtn.IsEnabled = true;
            if (!ok)
            {
                PlayText.Text = "启动游戏";
                return;
            }

            DoLaunch(version);
        }

        private void DoLaunch(VersionEntry version)
        {
            var state = AppState.Current;
            PlayBtn.IsEnabled = false;
            PlayText.Text = "正在启动";
            PlaySub.Text = "";

            var session = state.Launch(version, out var error);
            if (session == null)
            {
                PlayBtn.IsEnabled = true;
                PlayText.Text = "启动游戏";
                Hint(error);
                AppendLog("[HHCL] 启动失败：" + error);
                MainWindow.Instance?.SetStatus("启动失败");
                return;
            }

            AppendLog($"=== 启动 {version.Id} ===");
            AppendLog("游戏目录: " + LaunchBuilder.GameDirFor(state.Config, version));
            HookSession(session);
            MainWindow.Instance?.SetStatus($"正在运行 {version.Id}", true);
            LogState.Text = "运行中";
            if (AppState.Current.Config.MinimizeOnLaunch)
                MainWindow.Instance.WindowState = WindowState.Minimized;
        }

        private void HookSession(GameSession session)
        {
            if (ReferenceEquals(_hooked, session)) return;
            _hooked = session;

            session.Output += line => Dispatcher.BeginInvoke(new Action(() => AppendLog(line)));
            session.Exited += code => Dispatcher.BeginInvoke(new Action(() =>
            {
                PlayBtn.IsEnabled = true;
                PlayText.Text = "启动游戏";
                PlayGlyph.Visibility = Visibility.Visible;
                PlaySub.Text = "";
                PlayBtn.SetResourceReference(BackgroundProperty, "AccentBrush");
                LogState.Text = code == 0 ? "已退出" : $"异常退出 ({code})";
                MainWindow.Instance?.SetStatus(code == 0 ? "游戏已退出" : $"游戏异常退出，代码 {code}");
            }));

            SyncRunningState();
        }

        private void SyncRunningState()
        {
            var state = AppState.Current;
            if (state.IsRunning)
            {
                HookSession(state.Session);
                PlayText.Text = "结束游戏";
                PlayGlyph.Visibility = Visibility.Collapsed;
                PlaySub.Text = "游戏正在运行";
                PlayBtn.SetResourceReference(BackgroundProperty, "CardHoverBrush");
                LogState.Text = "运行中";
            }
            else
            {
                PlayText.Text = "启动游戏";
                PlayGlyph.Visibility = Visibility.Visible;
                PlaySub.Text = "";
                PlayBtn.SetResourceReference(BackgroundProperty, "AccentBrush");
            }
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (LogBox.Text.Length > 240000)
                LogBox.Text = LogBox.Text.Substring(120000);

            LogBox.AppendText(line + "\r\n");
            LogBox.ScrollToEnd();
        }

        // ==================== 基岩版（已从独立侧栏页面并到启动页） ====================

        private bool _bedrockInstalled;
        private bool _bedrockBusy;
        private CancellationTokenSource _bedrockCts;

        /// <summary>检测基岩版安装状态（要跑一次 PowerShell 查包，必须放后台，别卡住界面）。</summary>
        private async void RefreshBedrock()
        {
            BedrockStateText.Text = "检测中";
            try
            {
                // IsInstalled 要跑一次 PowerShell 查包，必须丢到后台线程
                var (ok, ver) = await Task.Run(() =>
                {
                    var installed = BedrockInstaller.IsInstalled(out var v);
                    return (installed, v ?? "");
                });

                // await 之后不一定还在 UI 线程（没有 WPF 同步上下文时会被切到线程池），
                // 显式 Dispatcher 回 UI 线程再动界面
                await Dispatcher.InvokeAsync(() =>
                {
                    _bedrockInstalled = ok;
                    if (_bedrockInstalled)
                    {
                        BedrockStateText.Text = "已安装";
                        BedrockVersionText.Text = string.IsNullOrEmpty(ver) ? "已安装" : $"版本 {ver}";
                        BedrockSecondaryBtn.Content = "重新安装 / 更新";
                        BedrockHint.Text = "启动前会自动应用你选中的配置；游戏退出后改动会自动收回那套配置。";
                    }
                    else
                    {
                        BedrockStateText.Text = "未安装";
                        BedrockVersionText.Text = "";
                        BedrockSecondaryBtn.Content = "下载并安装";
                        BedrockHint.Text = "需要先登录微软账号并拥有基岩版（或试用资格），约 2 GB，从 Xbox 商店下载。";
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write("bedrock refresh: " + ex.Message);
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        BedrockStateText.Text = "检测失败";
                        BedrockHint.Text = "检测基岩版安装状态失败：" + ex.Message;
                    });
                }
                catch { }
            }
        }

        /// <summary>基岩版游戏进程名（Minecraft.Windows，Preview 是 Minecraft.WindowsBeta）。</summary>
        private static bool BedrockGameRunning()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.ProcessName.StartsWith("Minecraft.Windows", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        private static async Task<string> ResolveXuidAsync()
        {
            var acc = AppState.Current.Config.CurrentAccount;
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken)) return acc.Xuid ?? "";
            if (!string.IsNullOrEmpty(acc.Xuid)) return acc.Xuid;
            try { return await BedrockInstaller.GetXuidAsync(acc, CancellationToken.None); }
            catch (Exception ex) { Log.Write("home xuid: " + ex.Message); return ""; }
        }

        private async void BedrockPrimary_Click(object sender, RoutedEventArgs e)
        {
            if (!_bedrockInstalled) { BedrockHint.Text = "还没安装基岩版，先点「下载并安装」。"; return; }
            if (BedrockGameRunning()) { BedrockHint.Text = "基岩版已经在运行了。"; return; }

            try
            {
                var profile = BedrockProfiles.Load().FirstOrDefault();
                if (profile != null)
                {
                    BedrockHint.Text = $"正在应用配置「{profile.Name}」…";
                    var xuid = await ResolveXuidAsync();
                    var (ok, msg) = await Task.Run(() => BedrockProfiles.Apply(profile, xuid));
                    if (!ok) { BedrockHint.Text = msg; return; }
                    BedrockHint.Text = $"已用配置「{profile.Name}」启动。游戏退出后会自动把改动收回这套配置。";
                }
                else BedrockHint.Text = "游戏已启动。";

                BedrockInstaller.Launch();
                MainWindow.Instance?.SetStatus("基岩版已启动", true);
            }
            catch (Exception ex)
            {
                Log.Write("bedrock launch: " + ex);
                BedrockHint.Text = "启动失败：" + ex.Message;
            }
        }

        private async void BedrockInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_bedrockBusy) return;
            var acc = AppState.Current.Config.CurrentAccount;
            if (acc.Type != "microsoft" || string.IsNullOrEmpty(acc.RefreshToken))
            {
                BedrockHint.Text = "请先在「账号」页登录微软账号，再回来安装基岩版。";
                return;
            }

            _bedrockBusy = true;
            _bedrockCts = new CancellationTokenSource();
            BedrockPrimaryBtn.IsEnabled = false;
            BedrockSecondaryBtn.IsEnabled = false;
            BedrockManageBtn.IsEnabled = false;
            BedrockCancelBtn.Visibility = Visibility.Visible;
            BedrockBar.Visibility = Visibility.Visible;
            BedrockBar.Value = 0;
            BedrockHint.Text = "";

            var progress = new Progress<DlProgress>(p =>
            {
                // 主包 1.9GB，按字节算进度才准（按文件数会一直卡在中途）
                if (p.TotalBytes > 0) BedrockBar.Value = Math.Min(100, p.DoneBytes * 100.0 / p.TotalBytes);
                else BedrockBar.Value = p.Percent;
                BedrockProgressText.Text = $"{p.Stage}  {p.Current}  {p.SizeText}  {p.SpeedText}".TrimEnd();
            });

            try
            {
                MainWindow.Instance?.SetStatus("正在准备基岩版下载…", true);
                BedrockProgressText.Text = "正在获取商店授权…";
                var auth = await BedrockInstaller.GetXboxUpdateAuthAsync(acc, _bedrockCts.Token);

                BedrockProgressText.Text = "正在查询安装包…";
                var files = await BedrockInstaller.GetPackageFilesAsync(auth, _bedrockCts.Token);

                var dir = Path.Combine(Config.DataDir, "bedrock");
                await BedrockInstaller.DownloadAsync(files, dir, progress, _bedrockCts.Token);

                BedrockProgressText.Text = "正在安装…";
                BedrockBar.Value = 100;
                BedrockInstaller.Install(dir);

                BedrockHint.Text = "基岩版装好了，点「启动基岩版」即可开玩。";
                MainWindow.Instance?.SetStatus("基岩版安装完成");
                RefreshBedrock();
            }
            catch (OperationCanceledException)
            {
                BedrockHint.Text = "已取消下载。";
                MainWindow.Instance?.SetStatus("已取消");
            }
            catch (Exception ex)
            {
                Log.Write("bedrock install: " + ex);
                BedrockHint.Text = "安装失败：" + ex.Message;
                MainWindow.Instance?.SetStatus("基岩版安装失败");
            }
            finally
            {
                _bedrockBusy = false;
                BedrockPrimaryBtn.IsEnabled = true;
                BedrockSecondaryBtn.IsEnabled = true;
                BedrockManageBtn.IsEnabled = true;
                BedrockCancelBtn.Visibility = Visibility.Collapsed;
                _bedrockCts?.Dispose();
                _bedrockCts = null;
            }
        }

        private void BedrockCancel_Click(object sender, RoutedEventArgs e)
        {
            try { _bedrockCts?.Cancel(); } catch { }
        }

        /// <summary>打开基岩版的完整管理界面（配置切换、模组与资源包、商店版本信息）。</summary>
        private void BedrockManage_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.PushDetail(new BedrockView());
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Text = "";

        private void OpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(Config.DataDir, "logs");
            Directory.CreateDirectory(dir);
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
        }
    }
}
