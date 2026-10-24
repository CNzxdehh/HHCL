using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    public partial class VersionsView : UserControl, IRefreshable
    {
        public VersionsView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Reload();
        }

        private void Reload()
        {
            var state = AppState.Current;
            state.RefreshVersions();
            VersionList.ItemsSource = state.Versions;
            PathText.Text = state.Config.GameDir;
            CountText.Text = $"{state.Versions.Count} 个版本";
            EmptyText.Visibility = state.Versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SubText.Text = $"共 {state.Versions.Count} 个本地版本 · 版本隔离 {(state.Config.IsolateVersions ? "开启" : "关闭")}";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Reload();
            MainWindow.Instance?.SetStatus("已刷新版本列表");
        }

        private void OpenGameDir_Click(object sender, RoutedEventArgs e) => OpenDir(AppState.Current.Config.GameDir);

        private void OpenOne_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is VersionEntry v) OpenDir(v.Dir);
        }

        private async void LaunchOne_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is VersionEntry v)) return;
            if (AppState.Current.IsRunning)
            {
                MessageBox.Show("已经有一个游戏在运行了，先结束它再启动新的。", "HHCL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppState.Current.Config.SelectedVersion = v.Id;
            AppState.Current.Save();

            var ok = await RepairFlow.EnsureAsync(v, line => MainWindow.Instance?.SetStatus(line.Length > 60 ? line.Substring(0, 60) : line, true));
            if (!ok) return;

            var session = AppState.Current.Launch(v, out var error);
            if (session == null)
            {
                MessageBox.Show(error, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MainWindow.Instance?.NavigateTo("home");
            if (AppState.Current.Config.MinimizeOnLaunch)
                MainWindow.Instance.WindowState = WindowState.Minimized;
            MainWindow.Instance?.SetStatus($"正在运行 {v.Id}", true);
        }

        private static void OpenDir(string dir)
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
