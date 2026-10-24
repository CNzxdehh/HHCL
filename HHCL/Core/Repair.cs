using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HHCL.Core
{
    public static class Repair
    {
        /// <summary>缺少文件的文字描述，用于确认弹窗。</summary>
        public static string Describe(LaunchPlan plan)
        {
            if (plan == null || plan.Ok) return "";
            var count = plan.MissingFiles.Count;
            var bytes = plan.MissingFiles.Sum(f => f.Size);
            var sizeText = bytes > 0 ? $"（约 {AppState.FormatSize(bytes)}）" : "";
            var sample = string.Join("\n", plan.Missing.Take(3));
            return $"检测到缺少 {count} 个文件{sizeText}。\n\n{sample}"
                   + (count > 3 ? $"\n…" : "")
                   + "\n\n是否一键补齐？补齐完成后会自动启动游戏。";
        }

        /// <summary>下载补齐所有缺失文件。</summary>
        public static Task RunAsync(LaunchPlan plan, IProgress<DlProgress> progress, CancellationToken ct)
        {
            var files = plan.MissingFiles
                .Where(m => !string.IsNullOrEmpty(m.Url) && !string.IsNullOrEmpty(m.Path))
                .Select(m => (m.Url, m.Path, m.Size))
                .ToList();
            return MinecraftDownloader.DownloadFilesAsync(files, progress, "一键补齐", ct);
        }

        /// <summary>统一的一键补齐确认弹窗。返回 true 表示用户同意。</summary>
        public static bool Confirm(LaunchPlan plan)
        {
            return MessageBox.Show(Describe(plan), "HHCL · 一键补齐",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
    }
}
