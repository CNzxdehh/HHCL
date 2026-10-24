import io

p = 'HHCL/App.xaml.cs'
s = io.open(p, encoding='utf-8').read()

if 'lan scan' not in s:
    old = '''                    if (cmd == "lang")'''
    new = '''                    if (cmd == "scan")
                    {
                        // --lan scan → 自测：一边应答一边扫描，看能不能发现自己的房间
                        Core.LanDiscovery.StartResponder(51234);
                        var found = await Core.LanDiscovery.ScanAsync(2500);
                        lanSb.AppendLine($"扫描到 {found.Count} 个房间");
                        foreach (var r in found) lanSb.AppendLine("  " + r.Address + "  房间码 " + r.Code);
                        Core.LanDiscovery.StopResponder();
                    }
                    else if (cmd == "lang")'''
    assert old in s, 'cmd anchor'
    s = s.replace(old, new, 1)
    io.open(p, 'w', encoding='utf-8').write(s)
    print('自检命令已加: --lan scan')
else:
    print('已存在')
