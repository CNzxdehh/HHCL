import io, glob

# 1) 所有 User-Agent 统一走 AppInfo
n = 0
for path in glob.glob('HHCL/Core/*.cs'):
    s = io.open(path, encoding='utf-8').read()
    orig = s
    s = s.replace('"HHCL/1.0.0 (Minecraft Launcher; Windows)"', 'AppInfo.UserAgent')
    s = s.replace('"HHCL/1.0.0"', 'AppInfo.UserAgent')
    s = s.replace('"MinecraftBedrockLauncher/1.0"', 'AppInfo.UserAgent')
    if s != orig:
        io.open(path, 'w', encoding='utf-8').write(s)
        n += 1
print('统一 User-Agent 的文件数:', n)

# 检查 BedrockInstaller 里的两处（可能有别的写法）
for path in ['HHCL/Core/BedrockInstaller.cs']:
    s = io.open(path, encoding='utf-8').read()
    s = s.replace('"MinecraftBedrockLauncher/1.0"', 'AppInfo.UserAgent')
    io.open(path, 'w', encoding='utf-8').write(s)
print('BedrockInstaller 已处理')
