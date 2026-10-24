using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>扫描到的局域网房间。</summary>
    public class DiscoveredRoom
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Code => Port.ToString();
        public string Address => $"{Host}:{Port}";
        public DateTime FoundAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 局域网房间自动发现：
    /// 主机开房间后在本机 UDP 45678 上应答；加入方广播探测，自动列出附近能连的房间，
    /// 不用手抄 IP 地址。
    /// 协议很简单：客机发 "HELLO"，主机回 "ROOM|ip|游戏端口"。
    /// </summary>
    public static class LanDiscovery
    {
        public const int DiscoveryPort = 45678;
        private const string Hello = "HELLO";
        private const string RoomPrefix = "ROOM|";

        private static UdpClient _responder;
        private static int _responderGamePort;

        // ---------------- 主机：应答 ----------------

        /// <summary>开始应答房间探测（游戏开放到局域网后调用）。</summary>
        public static void StartResponder(int gamePort)
        {
            StopResponder();
            _responderGamePort = gamePort;
            try
            {
                var udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                udp.EnableBroadcast = true;
                _responder = udp;
                _ = Task.Run(async () =>
                {
                    while (_responder != null)
                    {
                        try
                        {
                            var r = await udp.ReceiveAsync().ConfigureAwait(false);
                            var text = Encoding.UTF8.GetString(r.Buffer);
                            if (!text.StartsWith(Hello, StringComparison.OrdinalIgnoreCase)) continue;
                            var ip = LanMultiplayer.BestIPv4();
                            var reply = $"{RoomPrefix}{ip}|{_responderGamePort}";
                            var bytes = Encoding.UTF8.GetBytes(reply);
                            await udp.SendAsync(bytes, bytes.Length, r.RemoteEndPoint).ConfigureAwait(false);
                            Log.Write($"lan discovery reply -> {r.RemoteEndPoint}");
                        }
                        catch (ObjectDisposedException) { break; }
                        catch (Exception ex) { Log.Write("lan discovery: " + ex.Message); break; }
                    }
                });
                Log.Write($"lan discovery responder on {DiscoveryPort}, game port {gamePort}");
            }
            catch (Exception ex)
            {
                Log.Write("lan discovery start fail: " + ex.Message);
            }
        }

        public static void StopResponder()
        {
            try { _responder?.Dispose(); } catch { }
            _responder = null;
        }

        // ---------------- 客机：扫描 ----------------

        /// <summary>广播扫描附近能连的房间。</summary>
        public static async Task<List<DiscoveredRoom>> ScanAsync(int timeoutMs = 1600, CancellationToken ct = default)
        {
            var rooms = new List<DiscoveredRoom>();
            UdpClient udp = null;
            try
            {
                udp = new UdpClient();
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 300;

                var hello = Encoding.UTF8.GetBytes(Hello);
                foreach (var bcast in BroadcastAddresses())
                {
                    try
                    {
                        await udp.SendAsync(hello, hello.Length,
                            new IPEndPoint(IPAddress.Parse(bcast), DiscoveryPort)).ConfigureAwait(false);
                    }
                    catch { }
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    try
                    {
                        // ReceiveAsync 不带超时会一直挂着 → 每次接收单独给 300ms 超时
                        using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        one.CancelAfter(300);
                        var r = await udp.ReceiveAsync(one.Token).ConfigureAwait(false);
                        var text = Encoding.UTF8.GetString(r.Buffer);
                        if (!text.StartsWith(RoomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var parts = text.Substring(RoomPrefix.Length).Split('|');
                        if (parts.Length < 2) continue;
                        if (!int.TryParse(parts[1], out var port)) continue;
                        var host = parts[0];
                        if (host.Length == 0) continue;
                        if (!seen.Add($"{host}:{port}")) continue;
                        rooms.Add(new DiscoveredRoom { Host = host, Port = port });
                        Log.Write($"lan discovery found {host}:{port}");
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) break;   // 整体取消
                        continue;                               // 单次接收超时，继续等
                    }
                    catch (SocketException) { continue; }      // 超时，继续等
                    catch (ObjectDisposedException) { break; }
                }
            }
            catch (Exception ex)
            {
                Log.Write("lan discovery scan: " + ex.Message);
            }
            finally
            {
                try { udp?.Dispose(); } catch { }
            }
            return rooms;
        }

        /// <summary>广播地址列表：各网段的广播地址 + 全局广播。</summary>
        public static List<string> BroadcastAddresses()
        {
            var list = new List<string> { "255.255.255.255" };
            foreach (var ip in LanMultiplayer.LocalIPv4())
            {
                var parts = ip.Split('.');
                if (parts.Length != 4) continue;
                if (!int.TryParse(parts[0], out var a)) continue;
                if (a == 169) continue;                     // 链路本地，跳过
                list.Add($"{parts[0]}.{parts[1]}.{parts[2]}.255");
            }
            return list;
        }
    }
}
