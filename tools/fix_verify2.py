import io

p = 'HHCL/ViewShot.cs'
s = io.open(p, encoding='utf-8').read()

if 'VERIFY_COLLAPSE' not in s:
    anchor = '''                            var vc = vt.GetField("VersionCountText"'''
    block = '''                            // VERIFY_COLLAPSE: 折叠前两组，验证折叠逻辑
                            {
                                var reb = vt.GetMethod("RebuildVersionList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var hdr = vt.GetMethod("Header_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var collapsedField = vt.GetField("_collapsed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var set = collapsedField?.GetValue(dv) as System.Collections.Generic.HashSet<string>;
                                var groupedField = vt.GetField("_grouped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var grouped = groupedField?.GetValue(dv) as System.Collections.Generic.List<Views.DetailVersion>;
                                if (set != null && grouped != null && reb != null && hdr != null)
                                {
                                    // 折叠前两组
                                    var heads = grouped.Where(x => x.IsHeader).Take(2).ToList();
                                    foreach (var h in heads) set.Add(h.GroupKey);
                                    reb.Invoke(dv, null);
                                    Console.WriteLine("VERIFY_COLLAPSE collapsed=" + set.Count);
                                }
                            }
                            var vc = vt.GetField("VersionCountText"'''
    assert anchor in s
    s = s.replace(anchor, block, 1)
    io.open(p, 'w', encoding='utf-8').write(s)
    print('折叠验证已注入')
else:
    print('已存在，跳过')
