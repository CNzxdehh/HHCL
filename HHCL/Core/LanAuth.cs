using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HHCL.Core
{
    /// <summary>
    /// 带密码认证的局域网联机转发。
    ///
    /// 背景：Minecraft「开放到局域网」用的是集成服务器，**没有密码**，
    /// 谁知道地址谁就能进。这里在主机侧加一层转发：
    ///
    ///   朋友 → 主机代理端口(45679) ──校验密码──→ 127.0.0.1:游戏端口
    ///
    /// 密码在**主机侧**校验（不是客机自觉填的那种），所以"输错密码就连不上"是真的。
    /// 客机侧则起一个本地代理，游戏连本地代理，代理完成密码握手后再转发到主机。
    ///
    /// 握手协议（只用于认证阶段，之后就是 Minecraft 的原始字节流）：
    ///     客机 → 主机:  "HHCL-AUTH/1\n"
    ///     客机 → 主机:  "&lt;密码&gt;\n"
    ///     主机 → 客机:  "OK\n"  或  "DENIED\n"
    /// </summary>
    public static class LanAuth
    {
        /// <summary>协议第一行，固定不变。</summary>
        public const string Hello = "HHCL-AUTH/1";

        /// <summary>默认的代理监听端口（和 UDP 探测端口 45678 挨着）。</summary>
        public const int DefaultProxyPort = 45679;

        /// <summary>握手成功。</summary>
        public const string Ok = "OK";

        /// <summary>密码不对。</summary>
        public const string Denied = "DENIED";

        // ---------------- 主机端 ----------------

        /// <summary>
        /// 主机侧：监听代理端口，校验密码后把流量转到本机的游戏端口。
        /// </summary>
        public sealed class Server : IDisposable
        {
            private TcpListener _listener;
            private CancellationTokenSource _cts;
            private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TcpClient> _clients = new();

            /// <summary>实际监听的端口（默认端口被占用时会自动往后找）。</summary>
            public int Port { get; private set; }

            /// <summary>当前是否在监听。</summary>
            public bool IsRunning => _listener != null;

            /// <summary>密码（明文留在内存里，只用于比对）。</summary>
            public string Password { get; private set; } = "";

            /// <summary>转发的目标：游戏集成服务器端口。</summary>
            public int GamePort { get; private set; }

            /// <summary>启动监听。密码为空表示不校验（等价于旧的直连方式）。</summary>
            public bool Start(int gamePort, string password, int preferredPort = DefaultProxyPort)
            {
                Stop();
                GamePort = gamePort;
                Password = password ?? "";
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                for (int p = preferredPort; p < preferredPort + 20; p++)
                {
                    try
                    {
                        var l = new TcpListener(IPAddress.Any, p);
                        l.Start();
                        _listener = l;
                        Port = p;
                        _ = AcceptLoopAsync(l, ct);
                        Log.Write($"lan auth server on {p} -> 127.0.0.1:{gamePort}, password={(Password.Length > 0 ? "on" : "off")}");
                        return true;
                    }
                    catch (SocketException)
                    {
                        // 端口被占，试下一个
                    }
                }
                Log.Write("lan auth server: no free port");
                return false;
            }

            private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex) { Log.Write("lan auth accept: " + ex.Message); break; }

                    _clients[client.GetHashCode()] = client;
                    _ = Task.Run(async () =>
                    {
                        try { await HandleAsync(client, ct).ConfigureAwait(false); }
                        catch (Exception ex) { Log.Write("lan auth handle: " + ex.Message); }
                        finally
                        {
                            _clients.TryRemove(client.GetHashCode(), out _);
                            try { client.Dispose(); } catch { }
                        }
                    }, ct);
                }
            }

            private async Task HandleAsync(TcpClient client, CancellationToken ct)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                stream.ReadTimeout = 10000;

                // 1) 协议头
                var hello = await ReadLineAsync(stream, ct).ConfigureAwait(false);
                if (!string.Equals(hello, Hello, StringComparison.Ordinal))
                {
                    await SafeWriteAsync(stream, "BAD\n", ct).ConfigureAwait(false);
                    return;
                }

                // 2) 密码
                var pwd = await ReadLineAsync(stream, ct).ConfigureAwait(false);
                if (!FixedEquals(pwd, Password))
                {
                    Log.Write("lan auth: wrong password");
                    await SafeWriteAsync(stream, Denied + "\n", ct).ConfigureAwait(false);
                    return;
                }

                // 3) 放行：连本机游戏端口
                var upstream = new TcpClient { NoDelay = true };
                try
                {
                    await upstream.ConnectAsync(IPAddress.Loopback, GamePort).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Write("lan auth: game port unreachable: " + ex.Message);
                    await SafeWriteAsync(stream, "NOGAME\n", ct).ConfigureAwait(false);
                    upstream.Dispose();
                    return;
                }

                await SafeWriteAsync(stream, Ok + "\n", ct).ConfigureAwait(false);
                Log.Write("lan auth: client authenticated, forwarding");

                using (upstream)
                {
                    var t = ct;
                    var a = PumpAsync(stream, upstream.GetStream(), t);
                    var b = PumpAsync(upstream.GetStream(), stream, t);
                    await Task.WhenAny(a, b).ConfigureAwait(false);
                }
            }

            /// <summary>停止监听并断开所有连接。</summary>
            public void Stop()
            {
                try { _cts?.Cancel(); } catch { }
                try { _listener?.Stop(); } catch { }
                _listener = null;
                foreach (var kv in _clients)
                {
                    try { kv.Value.Close(); } catch { }
                }
                _clients.Clear();
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }

            public void Dispose() => Stop();
        }

        // ---------------- 客机端 ----------------

        /// <summary>
        /// 客机侧：起一个本地代理端口让游戏连（127.0.0.1:本地端口），
        /// 代理再去连主机的代理端口并完成密码握手，通过后开始转发。
        /// </summary>
        public sealed class Client : IDisposable
        {
            private TcpListener _listener;
            private CancellationTokenSource _cts;

            /// <summary>本地代理端口（游戏要连这个）。</summary>
            public int LocalPort { get; private set; }

            /// <summary>主机的代理地址。</summary>
            public string RemoteHost { get; private set; } = "";

            /// <summary>主机的代理端口。</summary>
            public int RemotePort { get; private set; }

            /// <summary>要交的密码。</summary>
            public string Password { get; private set; } = "";

            /// <summary>最近一次连接的结果（给 UI 显示）。</summary>
            public string LastResult { get; private set; } = "";

            /// <summary>密码校验是否通过过（true=已放行）。</summary>
            public bool Authenticated { get; private set; }

            /// <summary>启动本地代理。</summary>
            public bool Start(string remoteHost, int remotePort, string password)
            {
                Stop();
                RemoteHost = remoteHost;
                RemotePort = remotePort;
                Password = password ?? "";
                Authenticated = false;
                LastResult = "";
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, 0);   // 让系统挑空闲端口
                    _listener.Start();
                    LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    _ = AcceptLoopAsync(_listener, ct);
                    Log.Write($"lan auth client local {LocalPort} -> {remoteHost}:{remotePort}");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write("lan auth client start: " + ex.Message);
                    _listener = null;
                    return false;
                }
            }

            private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient game;
                    try
                    {
                        game = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex) { Log.Write("lan auth client accept: " + ex.Message); break; }

                    _ = Task.Run(async () =>
                    {
                        try { await BridgeAsync(game, ct).ConfigureAwait(false); }
                        catch (Exception ex) { Log.Write("lan auth bridge: " + ex.Message); }
                        finally { try { game.Dispose(); } catch { } }
                    }, ct);
                }
            }

            private async Task BridgeAsync(TcpClient game, CancellationToken ct)
            {
                game.NoDelay = true;
                var gameStream = game.GetStream();

                var up = new TcpClient { NoDelay = true };
                try
                {
                    await up.ConnectAsync(RemoteHost, RemotePort, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LastResult = "连不上主机：" + ex.Message;
                    Log.Write("lan auth client connect: " + ex.Message);
                    return;
                }

                using (up)
                {
                    var upStream = up.GetStream();
                    upStream.ReadTimeout = 15000;

                    await SafeWriteAsync(upStream, Hello + "\n", ct).ConfigureAwait(false);
                    await SafeWriteAsync(upStream, (Password ?? "") + "\n", ct).ConfigureAwait(false);

                    string resp;
                    try { resp = await ReadLineAsync(upStream, ct).ConfigureAwait(false); }
                    catch (Exception ex) { LastResult = "握手失败：" + ex.Message; return; }

                    if (!string.Equals(resp, Ok, StringComparison.Ordinal))
                    {
                        LastResult = resp == Denied ? "密码不对" : ("主机拒绝：" + resp);
                        Log.Write("lan auth client: " + LastResult);
                        return;
                    }

                    Authenticated = true;
                    LastResult = "已通过密码验证";
                    Log.Write("lan auth client: authenticated");

                    var a = PumpAsync(gameStream, upStream, ct);
                    var b = PumpAsync(upStream, gameStream, ct);
                    await Task.WhenAny(a, b).ConfigureAwait(false);
                }
            }

            /// <summary>停止本地代理。</summary>
            public void Stop()
            {
                try { _cts?.Cancel(); } catch { }
                try { _listener?.Stop(); } catch { }
                _listener = null;
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }

            public void Dispose() => Stop();
        }

        // ---------------- 公共工具 ----------------

        /// <summary>双向转发。</summary>
        private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await from.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    await to.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    await to.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // 连接断了就结束这一路
            }
        }

        /// <summary>读一行（以 \n 结尾，最多 256 字节）。</summary>
        private static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(10000);
            while (sb.Length < 256)
            {
                int n = await stream.ReadAsync(one.AsMemory(0, 1), timeout.Token).ConfigureAwait(false);
                if (n <= 0) break;
                if (one[0] == (byte)'\n') break;
                if (one[0] != (byte)'\r') sb.Append((char)one[0]);
            }
            return sb.ToString();
        }

        /// <summary>写一行并 flush。</summary>
        private static async Task SafeWriteAsync(Stream stream, string text, CancellationToken ct)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>固定时间比较，避免通过响应时间猜密码。</summary>
        private static bool FixedEquals(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a ?? "");
            var bb = Encoding.UTF8.GetBytes(b ?? "");
            // 长度不同也要比完，抹掉长度信息
            int n = Math.Max(ba.Length, bb.Length);
            var pa = new byte[n];
            var pb = new byte[n];
            Array.Copy(ba, pa, ba.Length);
            Array.Copy(bb, pb, bb.Length);
            return CryptographicOperations.FixedTimeEquals(pa, pb);
        }
    }
}
