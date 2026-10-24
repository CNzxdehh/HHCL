import io

log = r'C:\Users\徐颢朗\WorkBuddy\2026-10-21-19-38-35\.workbuddy\memory\2026-10-21.md'
add = r'C:\Users\徐颢朗\WorkBuddy\2026-10-21-19-38-35\tools\mem_append.md'

s = io.open(log, encoding='utf-8').read()
marker = '### 23:58 全功能实测收尾'
i = s.find(marker)
if i >= 0:
    s = s[:i].rstrip() + '\n'
    print('truncated old block')

s = s.rstrip() + '\n\n' + io.open(add, encoding='utf-8').read().lstrip('\n')
io.open(log, 'w', encoding='utf-8').write(s)
print('ok, tail:')
print(s[-300:])
