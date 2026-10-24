import io

p = 'HHCL/ViewShot.cs'
s = io.open(p, encoding='utf-8').read()

if 'VERIFY_GROUPING' not in s:
    # 在 detail 分支里，用"真实分布"造数据：40 个组，每组若干文件（含快照/预发布变体）
    anchor = '''                            var vc = vt.GetField("VersionCountText"'''
    block = '''                            // VERIFY_GROUPING: 用接近真实的分布造数据并触发折叠
                            {
                                var bases = new List<string> { "26.3", "26.2", "26.1", "26.0", "1.21.8", "1.21.4",
                                    "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2",
                                    "1.17.1", "1.16.5", "1.15.2", "1.14.4", "1.13.2", "1.12.2", "1.11.2" };
                                var real = new System.Collections.Generic.List<Views.DetailVersion>();
                                int idx = 0;
                                foreach (var b in bases)
                                {
                                    // 每个基础版本下：1 个正式版 + 若干快照/预发布变体
                                    real.Add(new Views.DetailVersion
                                    {
                                        Name = $"Fabric API 0.1{idx % 60}.0+{b}", Kind = "正式版", Loader = "fabric",
                                        Mc = b, Size = "1.7 MB", Date = "10-2" + (idx % 9)
                                    });
                                    for (int k = 1; k <= (idx % 4) + 1; k++)
                                    {
                                        var suffix = (idx % 3 == 0) ? $"-snapshot-{k}" : (idx % 3 == 1 ? $"-pre-{k}" : $"-rc.{k}");
                                        real.Add(new Views.DetailVersion
                                        {
                                            Name = $"Fabric API 0.1{idx % 60}.0+{b}{suffix}", Kind = "快照", Loader = "fabric",
                                            Mc = b + suffix, Size = "1.7 MB", Date = "10-2" + (idx % 9)
                                        });
                                    }
                                    idx++;
                                }
                                // 再补 1200 条，模拟真实规模（分到各基础版本下）
                                for (int i = 0; i < 1200; i++)
                                {
                                    var b = bases[i % bases.Length];
                                    var suffix = (i % 7 == 0) ? $"-snapshot-{i % 9}" : (i % 5 == 0 ? $"-pre-{i % 6}" : "");
                                    real.Add(new Views.DetailVersion
                                    {
                                        Name = $"Fabric API 0.{i % 200}.0+{b}{suffix}", Kind = suffix.Length > 0 ? "快照" : "正式版",
                                        Loader = "fabric", Mc = b + suffix, Size = "1.7 MB", Date = "10-20"
                                    });
                                }

                                var grp2 = vt.GetMethod("GroupByGameVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                                var grouped2 = grp2.Invoke(null, new object[] { real }) as System.Collections.IEnumerable;
                                var gl2 = new System.Collections.Generic.List<Views.DetailVersion>();
                                foreach (var one in grouped2) gl2.Add((Views.DetailVersion)one);
                                Console.WriteLine($"VERIFY_GROUPING 分组数={gl2.Count(x => x.IsHeader)} 条目={gl2.Count(x => !x.IsHeader)}");
                                vlist.ItemsSource = gl2;
                                vlist.SelectedIndex = 1;
                                var vc3 = vt.GetField("VersionCountText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(dv) as TextBlock;
                                if (vc3 != null) vc3.Text = gl2.Count(x => x.IsHeader) + " 个版本 · " + gl2.Count(x => !x.IsHeader) + " 个文件";
                            }
                            var vc = vt.GetField("VersionCountText"'''
    assert anchor in s
    s = s.replace(anchor, block, 1)
    io.open(p, 'w', encoding='utf-8').write(s)
    print('验证数据已注入')
else:
    print('已存在，跳过')
