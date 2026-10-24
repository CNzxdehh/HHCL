import os, shutil, hashlib

root = r'C:\Users\徐颢朗\WorkBuddy\2026-10-21-19-38-35'
out = os.path.join(root, '分享给朋友')

# 清空重建
if os.path.isdir(out):
    shutil.rmtree(out)
os.makedirs(out)

def md5(p):
    h = hashlib.md5()
    with open(p, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()

# 1) 自包含 exe（对方双击即用，不用装 .NET）
shutil.copy(os.path.join(root, 'HHCL-自包含版', 'HHCL.exe'), os.path.join(out, 'HHCL.exe'))
# 2) 使用说明
shutil.copy(os.path.join(root, 'HHCL-自包含版', '使用说明.txt'), os.path.join(out, '使用说明.txt'))
# 3) 轻量版小包（对方愿意装 .NET 时可以用，发起来快很多）
shutil.copy(os.path.join(root, 'HHCL-轻量版.zip'), os.path.join(out, '轻量版-需装NET.zip'))

# 校验
pub = md5(os.path.join(root, 'HHCL发布', 'HHCL.exe'))
for name in ['HHCL.exe']:
    m = md5(os.path.join(out, name))
    print(f'{name:<12} {os.path.getsize(os.path.join(out,name))/1024/1024:>7.1f} MB   md5 {"✓与发布版一致" if m==pub else "✗不一致!"}')

print()
print('分享给朋友/ 目录内容：')
for base, _, names in os.walk(out):
    for n in sorted(names):
        p = os.path.join(base, n)
        print(f'   {n:<24} {os.path.getsize(p)/1024/1024:>7.1f} MB')
