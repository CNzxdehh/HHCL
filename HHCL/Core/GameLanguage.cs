using System;
using System.IO;
using System.Text;

namespace HHCL.Core
{
    /// <summary>
    /// 游戏语言：启动前把 options.txt 里的 lang 改成简体中文。
    ///
    /// 游戏的界面语言是 options.txt 里的 lang 项（默认 en_us）。
    /// 语言文件（minecraft/lang/zh_cn.json）属于资源索引里的资源对象，
    /// 装游戏时已经一并下载好了，这里只改设置项即可。
    /// </summary>
    public static class GameLanguage
    {
        public const string SimplifiedChinese = "zh_cn";

        /// <summary>把游戏目录下的 options.txt 设为简体中文；返回是否改动了。</summary>
        public static bool EnsureChinese(string gameDir)
        {
            try
            {
                if (string.IsNullOrEmpty(gameDir)) return false;
                var file = Path.Combine(gameDir, "options.txt");

                string[] lines;
                bool exists = File.Exists(file);
                if (exists)
                {
                    lines = File.ReadAllLines(file, Encoding.UTF8);
                }
                else lines = Array.Empty<string>();

                bool changed = false;
                var list = new System.Collections.Generic.List<string>();
                foreach (var line in lines)
                {
                    if (line.StartsWith("lang:", StringComparison.Ordinal))
                    {
                        if (!line.Equals("lang:" + SimplifiedChinese, StringComparison.Ordinal))
                        {
                            list.Add("lang:" + SimplifiedChinese);
                            changed = true;
                        }
                        else list.Add(line);
                        continue;
                    }
                    list.Add(line);
                }
                if (!changed && exists)
                {
                    // 已经有 lang 项且是中文 → 不用动
                    bool hasLang = false;
                    foreach (var l in list)
                        if (l.StartsWith("lang:", StringComparison.Ordinal)) { hasLang = true; break; }
                    if (hasLang) return false;
                }
                if (!changed)
                {
                    list.Add("lang:" + SimplifiedChinese);
                    changed = true;
                }

                Directory.CreateDirectory(gameDir);
                File.WriteAllLines(file, list, new UTF8Encoding(false));
                Log.Write("game language -> " + SimplifiedChinese + " (" + file + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("set language fail: " + ex.Message);
                return false;
            }
        }
    }
}
