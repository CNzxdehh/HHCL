import io, re

p = 'HHCL/ViewShot.cs'
s = io.open(p, encoding='utf-8').read()

# 删掉 VERIFY_GROUPING 与 VERIFY_COLLAPSE 两个验证块
for marker in ['                            // VERIFY_GROUPING', '                            // VERIFY_COLLAPSE']:
    i = s.find(marker)
    if i < 0:
        continue
    # 找到该块结束的锚点
    end_anchor = s.find('                            var vc = vt.GetField("VersionCountText"', i)
    if end_anchor > 0:
        s = s[:i] + s[end_anchor:]
        print('已删除:', marker.strip())

io.open(p, 'w', encoding='utf-8').write(s)
print('ViewShot 验证代码已清理')
