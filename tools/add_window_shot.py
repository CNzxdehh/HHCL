import io

p = 'HHCL/ViewShot.cs'
s = io.open(p, encoding='utf-8').read()

if 'SHOT_WINDOW' not in s:
    old = '''                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(host);'''
    new = '''                    // SHOT_WINDOW: 渲染整个主窗口（能看到侧边栏/标题栏/窗口背景渐变）
                    System.Windows.Controls.FrameworkElement target = host;
                    if (which == "window")
                    {
                        try
                        {
                            var win = new MainWindow();
                            win.Width = w; win.Height = h;
                            win.PageHost.Content = view;
                            win.Measure(new Size(w, h));
                            win.Arrange(new Rect(0, 0, w, h));
                            win.UpdateLayout();
                            Pump(500);
                            win.UpdateLayout();
                            target = win;
                        }
                        catch (Exception ex) { Console.WriteLine("SHOT_WINDOW_FAIL: " + ex.Message); }
                    }

                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(target);'''
    assert old in s, 'rtb anchor'
    s = s.replace(old, new, 1)
    io.open(p, 'w', encoding='utf-8').write(s)
    print('整窗渲染模式已加')
else:
    print('已存在')
