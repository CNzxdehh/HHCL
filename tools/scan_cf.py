import re
p = r'E:/Plain Craft Launcher 2.exe'
d = open(p, 'rb').read()
print('size', len(d))
pat = re.compile(rb'\$2a\$10\$[A-Za-z0-9./]{20,60}')
hits = pat.findall(d)
print('hits', len(hits))
for h in hits[:10]:
    print(h[:80])
# also look for curseforge api host
for kw in [b'api.curseforge.com', b'curseforge', b'CurseForge']:
    print(kw, d.count(kw))
