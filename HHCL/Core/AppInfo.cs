namespace HHCL.Core
{
    /// <summary>
    /// 应用信息。这里集中放 User-Agent 等对外标识。
    ///
    /// Modrinth 的 API 规范建议 User-Agent 唯一标识 + 带联系方式，
    /// 这样他们想联系你（而不是直接封 IP）时找得到人。
    /// 建议把 Contact 改成你的真实联系方式（邮箱 / QQ / GitHub 主页都行）。
    /// </summary>
    public static class AppInfo
    {
        public const string Name = "HHCL";
        public const string Version = "1.0";

        /// <summary>联系方式。留空则不带在 User-Agent 里（建议填上）。</summary>
        public const string Contact = "";

        /// <summary>对外请求用的 User-Agent。</summary>
        public static string UserAgent
        {
            get
            {
                var ua = $"{Name}/{Version} (Windows; Minecraft Launcher)";
                if (!string.IsNullOrWhiteSpace(Contact)) ua += $"; {Contact}";
                return ua;
            }
        }
    }
}
