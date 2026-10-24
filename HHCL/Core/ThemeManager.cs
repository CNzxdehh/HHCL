using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace HHCL.Core
{
    /// <summary>
    /// 主题：深色（默认）/ 浅色。
    /// 实现方式是把 Fluent.xaml 里那一组 SolidColorBrush 的 Color 直接换掉，
    /// 这样所有页面不用改任何 XAML 就跟着变。
    /// </summary>
    public static class ThemeManager
    {
        public const string Dark = "dark";
        public const string Light = "light";

        /// <summary>画刷 key → 深色值 / 浅色值。</summary>
        private static readonly (string Key, string Dark, string Light)[] Palette =
        {
            ("BgBrush",              "#FF1B1B1F", "#FFF3F3F3"),
            ("NavBrush",             "#FF1F1F25", "#FFEBEBEE"),
            ("CardBrush",            "#FF242429", "#FFFFFFFF"),
            ("CardHoverBrush",       "#FF2B2B33", "#FFF5F5F7"),
            ("CardActiveBrush",      "#FF30303A", "#FFE8E8EC"),
            ("StrokeBrush",          "#FF3A3A42", "#FFD6D6DC"),
            ("StrokeSoftBrush",      "#FF2E2E36", "#FFE4E4E9"),
            ("TextPrimaryBrush",     "#FFFFFFFF", "#FF16161A"),
            ("TextSecondaryBrush",   "#FFC3C3CC", "#FF5A5A66"),
            ("TextTertiaryBrush",    "#FF8B8B96", "#FF8A8A94"),
            ("AccentBrush",          "#FF0078D4", "#FF0067C0"),
            ("AccentHoverBrush",     "#FF1585DE", "#FF1A86E0"),
            ("AccentPressedBrush",   "#FF0069BC", "#FF00539E"),
            ("AccentLightBrush",     "#FF60CDFF", "#FF0067C0"),
            ("AccentSoftBrush",      "#332B88D8", "#1F0067C0"),
            ("DangerBrush",          "#FFFF99A4", "#FFC42B1C"),
            ("CloseHoverBrush",      "#FFC42B1C", "#FFE81123"),
            ("OverlayHoverBrush",    "#18FFFFFF", "#0A000000"),
            ("OverlayPressedBrush",  "#0FFFFFFF", "#14000000"),
            // 新增的语义画刷（原来在 XAML 里写死的半透明白）
            ("OverlaySoftBrush",     "#14FFFFFF", "#14000000"),
            ("OverlaySoft2Brush",    "#16FFFFFF", "#0F000000"),
            ("AccentPanelBrush",     "#142B88D8", "#120067C0"),
            ("AccentIconBrush",      "#1F2B88D8", "#1F0067C0"),
            ("AccentTagBrush",       "#332B88D8", "#260067C0"),
            ("StatusDotBrush",       "#FF4CC2FF", "#FF0067C0"),
            ("FooterBrush",          "#FF191A1E", "#FFE8E8EC"),
            // 分段控件选中态：浅色下必须是浅底，否则深字压深底看不清
            ("SegmentSelectedBrush", "#FF3A3A45", "#FFFFFFFF"),
        };

        /// <summary>渐变画刷 key → 深色各色标 / 浅色各色标。</summary>
        private static readonly (string Key, string[] Dark, string[] Light)[] Gradients =
        {
            // 窗口背景（主内容区那层）：浅色下要是浅灰渐变，不能留深色
            ("WindowBgBrush",
                new[] { "#FF20242F", "#FF1C1C22", "#FF191A1E" },
                new[] { "#FFF7F7F9", "#FFF2F2F5", "#FFEDEDEF" }),
            // 首页英雄卡
            ("HeroGradientBrush",
                new[] { "#FF1F3350", "#FF232636", "#FF242429" },
                new[] { "#FFDCE9F7", "#FFEAF1F9", "#FFF4F7FB" }),
            // 强调渐变（头像圆、logo 底、指示条）
            ("AccentGradientBrush",
                new[] { "#FF2B9BE8", "#FF0F6CBD" },
                new[] { "#FF3BA0EE", "#FF0A5AA8" }),
            // logo 渐变：两套主题保持一致
            ("LogoGradientBrush",
                new[] { "#FF5BC0F8", "#FF0078D4", "#FF5A4FD4" },
                new[] { "#FF5BC0F8", "#FF0078D4", "#FF5A4FD4" }),
        };

        public static string Current { get; private set; } = Dark;

        /// <summary>应用主题（dark / light）。</summary>
        public static void Apply(string theme)
        {
            Current = theme == Light ? Light : Dark;
            var res = Application.Current?.Resources;
            if (res == null) return;

            foreach (var (key, dark, light) in Palette)
            {
                // XAML 里定义的画刷会被 WPF 冻结（改不了属性），
                // 所以换主题时新建一个不冻结的画刷替换资源项；
                // 界面里用的是 DynamicResource，会自动跟着变。
                var hex = Current == Light ? light : dark;
                res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }

            // 渐变画刷同样是冻结的，要整个换掉
            foreach (var (key, dark, light) in Gradients)
            {
                var stops = Current == Light ? light : dark;
                var grad = new LinearGradientBrush();
                grad.StartPoint = new Point(0, 0);
                grad.EndPoint = new Point(1, 1);
                for (int i = 0; i < stops.Length; i++)
                    grad.GradientStops.Add(new GradientStop(
                        (Color)ColorConverter.ConvertFromString(stops[i]),
                        stops.Length == 1 ? 0 : (double)i / (stops.Length - 1)));
                res[key] = grad;
            }

            Log.Write("theme -> " + Current);
        }
    }
}
