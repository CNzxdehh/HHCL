import os, zipfile, shutil, hashlib

root = r'C:\Users\徐颢朗\WorkBuddy\2026-10-21-19-38-35'

def zip_folder(folder, out_zip, note):
    files = []
    for base, _, names in os.walk(folder):
        for n in names:
            files.append(os.path.join(base, n))
    with zipfile.ZipFile(out_zip, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for f in files:
            arc = os.path.relpath(f, folder).replace('\\', '/')
            z.write(f, arc)
    size = os.path.getsize(out_zip) / 1024 / 1024
    print(f'{out_zip}: {size:.1f} MB')
    with zipfile.ZipFile(out_zip) as z:
        for i in z.infolist():
            print(f'   {i.filename}  ({i.file_size/1024/1024:.1f} MB)')
        bad = [n for n in z.namelist()
               if n.lower().endswith(('.json', '.dat', '.db')) or 'config' in n.lower() or 'token' in n.lower()]
        print('   敏感文件检查:', bad if bad else '✓ 只有 exe + 说明，无任何账号数据')
    print()

# 1) 分享版（自包含）
zip_folder(os.path.join(root, 'HHCL-自包含版'), os.path.join(root, 'HHCL-分享版.zip'), 'share')

# 2) 轻量版（依赖 .NET）
light = os.path.join(root, 'HHCL-轻量版')
os.makedirs(light, exist_ok=True)
shutil.copy(os.path.join(root, 'HHCL发布', 'HHCL.exe'), os.path.join(light, 'HHCL.exe'))
shutil.copy(os.path.join(root, 'HHCL-自包含版', '使用说明.txt'), os.path.join(light, '使用说明.txt'))
zip_folder(light, os.path.join(root, 'HHCL-轻量版.zip'), 'light')

# 3) 校验两个 exe 的 md5，确认是最新的
def md5(p):
    h = hashlib.md5()
    with open(p, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()

pub = md5(os.path.join(root, 'HHCL发布', 'HHCL.exe'))
lite = md5(os.path.join(light, 'HHCL.exe'))
desk = md5(r'C:\Users\徐颢朗\Desktop\HHCL.exe')
print('发布版 md5 :', pub)
print('轻量版 md5 :', lite, '✓' if lite == pub else '✗ 不一致！')
print('桌面版 md5 :', desk, '✓' if desk == pub else '✗ 不一致！')
