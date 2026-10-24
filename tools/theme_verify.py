import io

p = 'HHCL/ViewShot.cs'
s = io.open(p, encoding='utf-8').read()

if 'VERIFY_THEME' not in s:
    anchor = '''                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);'''
    block = '''                    // VERIFY_THEME: 可用 --shot <page> <w> <h> light 切浅色主题
                    var theme = args.Length > 4 ? args[4] : "";
                    if (theme == "light") Core.ThemeManager.Apply(Core.ThemeManager.Light);

                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);'''
    assert anchor in s
    s = s.replace(anchor, block, 1)
    io.open(p, 'w', encoding='utf-8').write(s)
    print('主题验证分支已加')
else:
    print('已存在，跳过')
