using System;
using System.IO;

namespace HHCL.Core
{
    public static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string text)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path.Combine(Config.DataDir, "hhcl.log"),
                        $"[{DateTime.Now:MM-dd HH:mm:ss}] {text}\r\n");
                }
            }
            catch { }
        }
    }
}
