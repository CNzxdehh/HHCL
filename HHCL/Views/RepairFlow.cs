using System;
using System.Threading;
using System.Threading.Tasks;
using HHCL.Core;
using System.Windows;

namespace HHCL.Views
{
    public static class RepairFlow
    {
        /// <summary>启动前检查：缺文件就弹「是否一键补齐」，补齐成功返回 true。</summary>
        public static async Task<bool> EnsureAsync(VersionEntry version, Action<string> log, Action<bool> busy = null)
        {
            var state = AppState.Current;

            // 微软账号的 token 会过期，启动前先静默续一下
            var acc = state.Config.CurrentAccount;
            if (acc != null && acc.Type == "microsoft" && !string.IsNullOrEmpty(acc.RefreshToken))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (acc.TokenExpire <= 0 || acc.TokenExpire - now < 3600)
                {
                    log?.Invoke("[HHCL] 正在刷新微软登录状态…");
                    if (await MicrosoftAuth.RefreshAsync(acc, CancellationToken.None))
                    {
                        state.Save();
                        log?.Invoke("[HHCL] 登录状态已刷新");
                    }
                    else
                    {
                        log?.Invoke("[HHCL] 登录状态已失效，请在账号页重新登录微软账号");
                    }
                }
            }

            var (plan, err) = state.BuildPlan(version);
            if (plan == null)
            {
                log?.Invoke("[HHCL] 无法启动：" + err);
                MessageBox.Show(err, "HHCL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (plan.Ok) return true;

            if (!Repair.Confirm(plan)) return false;

            busy?.Invoke(true);
            var progress = new Progress<DlProgress>(p =>
            {
                MainWindow.Instance?.SetStatus($"补齐文件 {p.Done}/{p.Total} {p.SizeText} {p.SpeedText}", true);
                log?.Invoke($"[HHCL] 补齐 {p.Done}/{p.Total} {p.SizeText} {p.SpeedText}  {p.Current}");
            });

            try
            {
                await Repair.RunAsync(plan, progress, CancellationToken.None);
                log?.Invoke("[HHCL] 文件补齐完成");
                MainWindow.Instance?.SetStatus("文件补齐完成");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("[HHCL] 补齐失败：" + ex.Message);
                MessageBox.Show("补齐失败：" + ex.Message, "HHCL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                busy?.Invoke(false);
            }
        }
    }
}
