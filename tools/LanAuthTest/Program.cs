using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>测试用的日志桩（真实实现会写文件，测试里只要不炸就行）。</summary>
    public static class Log
    {
        public static void Write(string text) { }
    }
}

namespace LanAuthTest
{
    internal static class Program
    {
        private static int _failed = 0;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + what);
            if (!ok) _failed++;
        }

        /// <summary>假扮 Minecraft 集成服务器：接受连接后把收到的东西记下来。</summary>
        private sealed class FakeGameServer
        {
            private readonly TcpListener _l;
            public int Port { get; }
            public string Received = "";
            public int Connects;

            public FakeGameServer()
            {
                _l = new TcpListener(IPAddress.Loopback, 0);
                _l.Start();
                Port = ((IPEndPoint)_l.LocalEndpoint).Port;
                _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        TcpClient c;
                        try { c = await _l.AcceptTcpClientAsync(); } catch { return; }
                        Interlocked.Increment(ref Connects);
                        _ = Task.Run(async () =>
                        {
                            var buf = new byte[256];
                            try
                            {
                                while (true)
                                {
                                    int n = await c.GetStream().ReadAsync(buf, 0, buf.Length);
                                    if (n <= 0) break;
                                    Received += Encoding.UTF8.GetString(buf, 0, n);
                                }
                            }
                            catch { }
                        });
                    }
                });
            }

            public void Stop() { try { _l.Stop(); } catch { } }
        }

        private static async Task Main()
        {
            Console.WriteLine("=== LanAuth 密码认证联机自测 ===\n");

            var game = new FakeGameServer();
            Console.WriteLine($"假游戏服务器端口 {game.Port}");

            // ---------- 场景 1：正确密码应能连通 ----------
            Console.WriteLine("\n[1] 密码正确 → 应该连得上");
            using (var server = new HHCL.Core.LanAuth.Server())
            {
                bool started = server.Start(game.Port, "824193", 45790);
                Check(started, "主机代理启动成功");
                Check(server.Port == 45790, $"代理端口 = {server.Port}");

                using (var client = new HHCL.Core.LanAuth.Client())
                {
                    Check(client.Start("127.0.0.1", server.Port, "824193"), "客机本地代理启动");
                    using var gameSide = new TcpClient();
                    await gameSide.ConnectAsync(IPAddress.Loopback, client.LocalPort);
                    var payload = Encoding.UTF8.GetBytes("PING-FROM-GAME");
                    await gameSide.GetStream().WriteAsync(payload, 0, payload.Length);
                    await gameSide.GetStream().FlushAsync();

                    for (int i = 0; i < 30 && game.Received.Length == 0; i++) await Task.Delay(100);
                    Check(game.Received.Contains("PING-FROM-GAME"), "游戏数据已转发到游戏服务器");
                    Check(client.Authenticated, "客机标记为已认证");
                }
            }

            // ---------- 场景 2：错误密码应被拒 ----------
            Console.WriteLine("\n[2] 密码错误 → 应该被挡住");
            var game2 = new FakeGameServer();
            using (var server = new HHCL.Core.LanAuth.Server())
            {
                server.Start(game2.Port, "824193", 45791);
                int connectsBefore = game2.Connects;
                using (var client = new HHCL.Core.LanAuth.Client())
                {
                    client.Start("127.0.0.1", server.Port, "111111");   // 错的
                    using var gameSide = new TcpClient();
                    await gameSide.ConnectAsync(IPAddress.Loopback, client.LocalPort);
                    var payload = Encoding.UTF8.GetBytes("PING-SHOULD-NOT-ARRIVE");
                    try
                    {
                        await gameSide.GetStream().WriteAsync(payload, 0, payload.Length);
                        await gameSide.GetStream().FlushAsync();
                    }
                    catch { }

                    for (int i = 0; i < 30 && string.IsNullOrEmpty(client.LastResult); i++) await Task.Delay(100);
                    await Task.Delay(500);

                    Check(!client.Authenticated, "客机未被标记为已认证");
                    Check(client.LastResult.Contains("密码不对"), $"提示是「{client.LastResult}」");
                    Check(!game2.Received.Contains("PING-SHOULD-NOT-ARRIVE"), "数据没有到达游戏服务器");
                    Check(game2.Connects == connectsBefore, "游戏服务器没有收到连接");
                }
            }
            game2.Stop();

            // ---------- 场景 3：空密码（不启用认证）也应能通 ----------
            Console.WriteLine("\n[3] 空密码（不认证）→ 直连也能通");
            var game3 = new FakeGameServer();
            using (var server = new HHCL.Core.LanAuth.Server())
            {
                server.Start(game3.Port, "", 45792);
                using (var client = new HHCL.Core.LanAuth.Client())
                {
                    client.Start("127.0.0.1", server.Port, "");
                    using var gameSide = new TcpClient();
                    await gameSide.ConnectAsync(IPAddress.Loopback, client.LocalPort);
                    var payload = Encoding.UTF8.GetBytes("PING-NOAUTH");
                    await gameSide.GetStream().WriteAsync(payload, 0, payload.Length);
                    await gameSide.GetStream().FlushAsync();
                    for (int i = 0; i < 30 && game3.Received.Length == 0; i++) await Task.Delay(100);
                    Check(game3.Received.Contains("PING-NOAUTH"), "数据已转发");
                }
            }
            game3.Stop();

            // ---------- 场景 4：协议不对的连接应被拒 ----------
            Console.WriteLine("\n[4] 协议不对 → 应立刻被拒");
            var game4 = new FakeGameServer();
            using (var server = new HHCL.Core.LanAuth.Server())
            {
                server.Start(game4.Port, "824193", 45793);
                using (var raw = new TcpClient())
                {
                    await raw.ConnectAsync(IPAddress.Loopback, server.Port);
                    var junk = Encoding.UTF8.GetBytes("GARBAGE-NOT-HELLO\n");
                    await raw.GetStream().WriteAsync(junk, 0, junk.Length);
                    await raw.GetStream().FlushAsync();
                    var buf = new byte[32];
                    int n = 0;
                    try { n = await raw.GetStream().ReadAsync(buf, 0, buf.Length); } catch { }
                    var resp = Encoding.UTF8.GetString(buf, 0, Math.Max(n, 0));
                    Check(resp.StartsWith("BAD"), $"回了「{resp.Trim()}」");
                }
            }
            game4.Stop();

            game.Stop();
            Console.WriteLine();
            if (_failed == 0)
            {
                Console.WriteLine("全部通过 ✓");
                return;
            }
            Console.WriteLine($"有 {_failed} 项失败 ✗");
            Environment.Exit(1);
        }
    }
}
