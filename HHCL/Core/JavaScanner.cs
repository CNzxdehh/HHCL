using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HHCL.Core
{
    public class JavaInfo
    {
        public string Path { get; set; } = "";
        public string Version { get; set; } = "";
        public int Major { get; set; }
        public bool Is64 { get; set; }
        public string Vendor { get; set; } = "";

        public string Display => $"Java {Version}{(Is64 ? " · 64 位" : " · 32 位")}";
        public string Short => $"Java {Major}";
        public string Detail => Path;
    }

    public static class JavaScanner
    {
        public static JavaInfo Probe(string exe)
        {
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-version");
                using var p = Process.Start(psi);
                if (p == null) return null;
                string text = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);

                if (string.IsNullOrWhiteSpace(text)) return null;

                var m = Regex.Match(text, "version \"([^\"]+)\"");
                string ver = m.Success ? m.Groups[1].Value : "";
                if (string.IsNullOrEmpty(ver)) return null;

                int major;
                var mm = Regex.Match(ver, @"^(\d+)");
                if (mm.Success)
                {
                    major = int.Parse(mm.Groups[1].Value);
                    if (major == 1)
                    {
                        var m2 = Regex.Match(ver, @"^1\.(\d+)");
                        if (m2.Success) major = int.Parse(m2.Groups[1].Value);
                    }
                }
                else major = 0;

                string vendor = "";
                var low = text.ToLowerInvariant();
                if (low.Contains("microsoft")) vendor = "Microsoft";
                else if (low.Contains("temurin") || low.Contains("adoptium") || low.Contains("adoptopenjdk")) vendor = "Temurin";
                else if (low.Contains("zulu")) vendor = "Zulu";
                else if (low.Contains("graal")) vendor = "GraalVM";
                else if (low.Contains("corretto")) vendor = "Corretto";
                else if (low.Contains("oracle")) vendor = "Oracle";
                else if (low.Contains("openjdk")) vendor = "OpenJDK";

                bool is64 = low.Contains("64-bit") || low.Contains("amd64") || low.Contains("x86_64");

                return new JavaInfo { Path = exe, Version = ver, Major = major, Is64 = is64, Vendor = vendor };
            }
            catch { return null; }
        }

        public static List<JavaInfo> Scan(string preferred = null)
        {
            var candidates = new List<string>();

            void Add(string dir)
            {
                if (string.IsNullOrWhiteSpace(dir)) return;
                try
                {
                    var javaw = Path.Combine(dir, "bin", "javaw.exe");
                    var java = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(javaw)) candidates.Add(javaw);
                    else if (File.Exists(java)) candidates.Add(java);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                if (Directory.Exists(preferred)) Add(preferred);
                else if (File.Exists(preferred)) candidates.Add(preferred);
            }

            Add(Environment.GetEnvironmentVariable("JAVA_HOME"));

            var roots = new[]
            {
                @"C:\Program Files\Java", @"C:\Program Files (x86)\Java",
                @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Eclipse Foundation",
                @"C:\Program Files\Microsoft", @"C:\Program Files\Zulu",
                @"C:\Program Files\BellSoft", @"C:\Program Files\Amazon Corretto",
                @"C:\Program Files\Semeru", @"C:\Program Files\GraalVM",
                @"C:\Program Files\JetBrains",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HHCL", "jdk"),
                @"C:\Program Files\Minecraft Launcher\runtime",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime"),
            };

            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var sub in Directory.GetDirectories(root))
                    {
                        Add(sub);
                        try
                        {
                            foreach (var sub2 in Directory.GetDirectories(sub))
                            {
                                Add(sub2);
                                foreach (var sub3 in Directory.GetDirectories(sub2)) Add(sub3);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // PATH 中的 java
            try
            {
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var seg in pathVar.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(seg)) continue;
                    var j = Path.Combine(seg.Trim('"'), "javaw.exe");
                    if (File.Exists(j)) candidates.Add(j);
                }
            }
            catch { }

            var result = new List<JavaInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var info = Probe(c);
                if (info == null) continue;
                var key = Path.GetDirectoryName(Path.GetDirectoryName(info.Path)) ?? info.Path;
                if (!seen.Add(key)) continue;
                result.Add(info);
            }
            return result.OrderByDescending(j => j.Major).ThenByDescending(j => j.Is64).ToList();
        }

        public static JavaInfo PickFor(List<JavaInfo> javas, int requiredMajor, string preferredPath)
        {
            if (javas == null || javas.Count == 0) return null;
            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                var hit = javas.FirstOrDefault(j => string.Equals(j.Path, preferredPath, StringComparison.OrdinalIgnoreCase));
                if (hit != null && (requiredMajor <= 0 || hit.Major >= requiredMajor)) return hit;
            }
            var ok = javas.Where(j => requiredMajor <= 0 || j.Major >= requiredMajor).OrderBy(j => j.Major).FirstOrDefault();
            return ok ?? javas.OrderByDescending(j => j.Major).First();
        }
    }
}
