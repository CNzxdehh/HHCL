using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using HHCL.Core;

namespace HHCL.Views
{
    public class AccountRow
    {
        public string Name { get; set; } = "";
        public string Initial { get; set; } = "";
        public string SubLine { get; set; } = "";
        public bool IsCurrent { get; set; }
        public Account Source { get; set; }
    }

    public partial class AccountsView : UserControl, IRefreshable
    {
        public AccountsView()
        {
            InitializeComponent();
        }

        public void OnActivated() => Reload();

        private void Reload()
        {
            var cfg = AppState.Current.Config;
            var cur = cfg.CurrentAccount;
            var rows = cfg.Accounts.Select(a => new AccountRow
            {
                Name = a.Name,
                Initial = a.Initial,
                IsCurrent = a.Name == cur.Name,
                Source = a,
                SubLine = $"{a.TypeText} · {a.FormattedUuid}"
            }).ToList();

            AccountList.ItemsSource = rows;
            AccountList.SelectedItem = rows.FirstOrDefault(r => r.IsCurrent);
            CountText.Text = $"{rows.Count} 个";
            MainWindow.Instance?.UpdateAccountChip();
        }

        private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountList.SelectedItem is AccountRow row && !row.IsCurrent)
            {
                AppState.Current.Config.SelectedAccount = row.Name;
                AppState.Current.Save();
                Reload();
            }
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is AccountRow row)) return;
            AppState.Current.Config.SelectedAccount = row.Name;
            AppState.Current.Save();
            Reload();
            MainWindow.Instance?.SetStatus("已切换账号：" + row.Name);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is AccountRow row)) return;
            var cfg = AppState.Current.Config;
            if (cfg.Accounts.Count <= 1)
            {
                MessageBox.Show("至少要保留一个账号。", "HHCL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"删除账号「{row.Name}」？", "HHCL", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            cfg.Accounts.RemoveAll(a => a.Name == row.Name);
            if (cfg.SelectedAccount == row.Name) cfg.SelectedAccount = cfg.Accounts[0].Name;
            cfg.Save();
            Reload();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? "").Trim();
            var cfg = AppState.Current.Config;

            if (name.Length < 2 || name.Length > 16 || !Regex.IsMatch(name, "^[A-Za-z0-9_]+$"))
            {
                ShowError("名字需要 2~16 位，只能包含字母、数字、下划线。");
                return;
            }
            if (cfg.Accounts.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError("已经有同名账号了。");
                return;
            }

            cfg.Accounts.Add(new Account
            {
                Name = name,
                Uuid = Account.OfflineUuid(name),
                Type = "offline",
                Created = DateTimeOffset.Now.ToUnixTimeSeconds()
            });
            cfg.SelectedAccount = name;
            cfg.Save();
            NameBox.Text = "";
            ErrText.Visibility = Visibility.Collapsed;
            Reload();
            MainWindow.Instance?.SetStatus("已添加离线账号 " + name);
        }

        private void ShowError(string text)
        {
            ErrText.Text = text;
            ErrText.Visibility = Visibility.Visible;
        }

        // ---------------- 微软正版登录 ----------------

        private async void MsLogin_Click(object sender, RoutedEventArgs e)
        {
            var cfg = AppState.Current.Config;
            MsLoginBtn.IsEnabled = false;
            MsText.Visibility = Visibility.Collapsed;

            try
            {
                var win = new MicrosoftLoginWindow { Owner = MainWindow.Instance };
                win.ShowDialog();

                if (win.Result == null) return;

                var acc = win.Result;
                var old = cfg.Accounts.FirstOrDefault(a => a.Type == "microsoft" && a.Uuid == acc.Uuid);
                if (old != null) cfg.Accounts.Remove(old);
                cfg.Accounts.Add(acc);
                cfg.SelectedAccount = acc.Name;
                cfg.Save();
                Reload();
                MainWindow.Instance?.SetStatus("已登录微软账号 " + acc.Name);
                ShowMs("已登录：" + acc.Name + "（登录状态会自动续期）", false);
            }
            catch (Exception ex)
            {
                Core.Log.Write("MS login top-level fail: " + ex);
                ShowMs("登录出错：" + ex.Message, true);
            }
            finally
            {
                MsLoginBtn.IsEnabled = true;
            }
        }

        private async void MsRefresh_Click(object sender, RoutedEventArgs e)
        {
            var cfg = AppState.Current.Config;
            var ms = cfg.Accounts.Where(a => a.Type == "microsoft" && !string.IsNullOrEmpty(a.RefreshToken)).ToList();
            if (ms.Count == 0)
            {
                ShowMs("还没有微软账号，先点上面的按钮登录。", false);
                return;
            }

            MsRefreshBtn.IsEnabled = false;
            var ok = 0;
            foreach (var a in ms)
                if (await MicrosoftAuth.RefreshAsync(a, System.Threading.CancellationToken.None)) ok++;

            cfg.Save();
            MsRefreshBtn.IsEnabled = true;
            ShowMs(ok > 0 ? $"已刷新 {ok}/{ms.Count} 个账号的登录状态。" : "刷新失败，可能需要重新登录。", ok == 0);
        }

        private void ShowMs(string text, bool warn)
        {
            MsText.Text = text;
            MsText.Foreground = warn
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
            MsText.Visibility = Visibility.Visible;
        }
    }
}
