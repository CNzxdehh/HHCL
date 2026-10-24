import io

p = 'HHCL/ViewShot.cs'
s = io.open(p, encoding='utf-8').read()

# 1) 参数解析：支持 window 整窗模式
old = '''            var which = args.Length > 1 ? args[1].ToLowerInvariant() : "mods";
            int w = args.Length > 2 ? int.Parse(args[2]) : 1200;
            int h = args.Length > 3 ? int.Parse(args[3]) : 800;'''
new = '''            // --shot <page> <w> <h> [light]
            // --shot window <page> <w> <h> [light]   渲染整个主窗口（能看到侧边栏/标题栏/窗口渐变）
            bool fullWindow = args.Length > 1 && args[1].ToLowerInvariant() == "window";
            int idx = fullWindow ? 2 : 1;
            var which = args.Length > idx ? args[idx].ToLowerInvariant() : "mods";
            int w = args.Length > idx + 1 ? int.Parse(args[idx + 1]) : 1200;
            int h = args.Length > idx + 2 ? int.Parse(args[idx + 2]) : 800;'''
assert old in s, 'parse anchor'
s = s.replace(old, new, 1)

# 2) 主题参数索引
s = s.replace('var themeArg = args.Length > 4 ? args[4] : "";',
              'var themeArg = args.Length > idx + 3 ? args[idx + 3] : "";')
s = s.replace('var theme = args.Length > 4 ? args[4] : "";',
              'var theme = args.Length > idx + 3 ? args[idx + 3] : "";')

# 3) 整窗渲染目标
old2 = '''                    System.Windows.FrameworkElement target = host;
                    if (which == "window")'''
new2 = '''                    System.Windows.FrameworkElement target = host;
                    if (fullWindow)'''
assert old2 in s, 'target anchor'
s = s.replace(old2, new2, 1)

io.open(p, 'w', encoding='utf-8').write(s)
print('ViewShot 参数与整窗模式已就绪')
