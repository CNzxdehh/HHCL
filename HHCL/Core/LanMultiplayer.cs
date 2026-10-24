using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HHCL.Core
{
    /// <summary>一个局域网房间的信息。</summary>
    public class LanRoom
    {
        public int Port;
        public string Address = "";      // 192.168.1.23:51234
        public string Code = "";         // 5 位房间码（Java 版就是端口号）
        public string FoundAt = "";       // 检测到的时间
    }

    /// <summary>
    /// 局域网联机（不需要任何服务器）：
    /// 主机在游戏里"开放到局域网"，游戏自带的集成服务器会监听一个随机端口；
    /// 我们从游戏日志里读出这个端口，配上本机内网 IP 就是朋友要填的直连地址。
    /// </summary>
    public static class LanMultiplayer
    {
        /// <summary>本机所有可用的内网 IPv4（排除回环和虚拟网卡）。</summary>
        public static List<string> LocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.ToString();
                        if (ip.StartsWith("127.")) continue;
                        // 跳过 Hyper-V / 虚拟机的网卡（名字带 vEthernet / VMware / VirtualBox）
                        var desc = ni.Description ?? "";
                        if (desc.Contains("Hyper-V") || desc.Contains("VMware") || desc.Contains("VirtualBox")) continue;
                        if (!list.Contains(ip)) list.Add(ip);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>最可能给朋友用的那个地址：优先私有网段（192.168 / 10.x / 172.16-31.x）。</summary>
        public static string BestIPv4()
        {
            var ips = LocalIPv4();
            if (ips.Count == 0) return "";
            foreach (var ip in ips)
            {
                if (ip.StartsWith("192.168.")) return ip;
            }
            foreach (var ip in ips)
            {
                if (ip.StartsWith("10.")) return ip;
            }
            foreach (var ip in ips)
            {
                var parts = ip.Split('.');
                if (parts.Length == 4 && int.TryParse(parts[0], out var a) && a == 172 &&
                    int.TryParse(parts[1], out var b) && b >= 16 && b <= 31) return ip;
            }
            return ips[0];
        }

        /// <summary>
        /// 从游戏日志里找"开放到局域网"的端口。
        /// Minecraft 会写：Starting LAN server on port 51234
        /// </summary>
        public static int? DetectLanPort(string workDir)
        {
            try
            {
                var log = Path.Combine(workDir ?? "", "logs", "latest.log");
                if (!File.Exists(log)) return null;
                var info = new FileInfo(log);
                // 只读最后 64KB，够用且不卡
                using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var len = (int)Math.Min(fs.Length, 64 * 1024);
                fs.Seek(fs.Length - len, SeekOrigin.Begin);
                var buf = new byte[len];
                fs.ReadExactly(buf, 0, len);   // Read 可能读不满，要精确读满
                var text = System.Text.Encoding.UTF8.GetString(buf);

                var matches = Regex.Matches(text, @"LAN server on port (\d+)");
                if (matches.Count == 0) return null;
                var last = matches[matches.Count - 1];
                if (int.TryParse(last.Groups[1].Value, out var port) && port > 0) return port;
            }
            catch (Exception ex) { Log.Write("lan detect: " + ex.Message); }
            return null;
        }

        /// <summary>生成房间信息（地址 + 5 位房间码）。</summary>
        public static LanRoom MakeRoom(int port)
        {
            var ip = BestIPv4();
            return new LanRoom
            {
                Port = port,
                Code = port.ToString(),
                Address = string.IsNullOrEmpty(ip) ? port.ToString() : $"{ip}:{port}",
                FoundAt = DateTime.Now.ToString("HH:mm:ss")
            };
        }

        /// <summary>解析用户填的 "IP:端口" 或只填端口。</summary>
        public static bool TryParseAddress(string text, out string host, out int port)
        {
            host = ""; port = 0;
            var t = (text ?? "").Trim();
            if (t.Length == 0) return false;

            if (t.Contains(':'))
            {
                var parts = t.Split(':');
                host = parts[0].Trim();
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p)) port = p;
            }
            else if (int.TryParse(t, out var onlyPort))
            {
                // 只填了 5 位房间码 → 当作端口，主机地址让用户另外填
                port = onlyPort;
                return true;
            }
            else host = t;

            return port > 0 && port <= 65535 && host.Length > 0;
        }
    }
}
