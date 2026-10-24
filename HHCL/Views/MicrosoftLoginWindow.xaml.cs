using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HHCL.Core;

namespace HHCL.Views
{
    public partial class MicrosoftLoginWindow : Window
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private string _verifyUrl = "";

        /// <summary>登录成功后的账号；失败或取消为 null。</summary>
        public Account Result { get; private set; }

        public MicrosoftLoginWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => _ = StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                var acc = await MicrosoftAuth.LoginAsync(st =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!string.IsNullOrEmpty(st.UserCode) && UserCodeText.Text == "······")
                        {
                            UserCodeText.Text = st.UserCode;
                            _verifyUrl = st.VerifyUrl;
                            VerifyUrlText.Text = st.VerifyUrl;
                            try { Clipboard.SetText(st.UserCode); } catch { }
                            OpenBrowser();
                        }
                        StateText.Text = st.Message;
                    });
                }, _cts.Token);

                Result = acc;
                StateIcon.Text = "\uE73E";
                StateText.Text = $"登录成功：{acc.Name}";
                Bar.IsIndeterminate = false;
                Bar.Value = 100;
                CancelBtn.Content = "完成";
                await Task.Delay(700);
                Close();
            }
            catch (OperationCanceledException)
            {
                StateText.Text = "已取消";
            }
            catch (Exception ex)
            {
                Core.Log.Write("MS login window fail: " + ex);
                StateIcon.Text = "\uE7BA";
                StateText.Text = "登录失败：" + ex.Message;
                Bar.IsIndeterminate = false;
                Bar.Value = 0;
                CancelBtn.Content = "关闭";
                MessageBox.Show(ex.Message, "微软登录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenBrowser()
        {
            if (string.IsNullOrEmpty(_verifyUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_verifyUrl) { UseShellExecute = true });
            }
            catch { }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(UserCodeText.Text); } catch { }
            OpenBrowser();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            Close();
        }
    }
}
