import os

p = r'C:\Users\徐颢朗\WorkBuddy\2026-10-21-19-38-35\HHCL\nul'
print('存在:', os.path.exists(p))
for target in [p, '\\\\?\\' + p]:
    try:
        os.remove(target)
        print('✓ 已删除（方式：', '扩展路径' if '?' in target else '普通', '）')
        break
    except Exception as e:
        print('失败:', type(e).__name__, e)
print('现在还存在吗:', os.path.exists(p))
