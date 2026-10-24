"""一次性 UI 验证脚本：导航 → 搜索 → 选项目 → 截图。
只做验证用，改完 UI 不要反复跑截图。
"""
import ctypes
import sys
import time

u = ctypes.windll.user32
k = ctypes.windll.kernel32

# 窗口矩形与可见内容的偏移（本机实测：点击坐标要比图上位置大 x+80 / y+40）
OFF_X, OFF_Y = 80, 40


def find_window(title="HHCL"):
    hs = []

    def cb(h, _):
        n = u.GetWindowTextLengthW(h)
        if n > 0:
            b = ctypes.create_unicode_buffer(n + 1)
            u.GetWindowTextW(h, b, n + 1)
            if b.value.strip() == title and u.IsWindowVisible(h):
                hs.append(h)
        return True

    ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)(cb)
    u.EnumWindows(ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)(cb), 0)
    return hs[0] if hs else None


class RECT(ctypes.Structure):
    _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long), ("r", ctypes.c_long), ("b", ctypes.c_long)]


def fg(hwnd):
    cur = u.GetForegroundWindow()
    tid_fg = u.GetWindowThreadProcessId(cur, None)
    tid_me = k.GetCurrentThreadId()
    u.AttachThreadInput(tid_me, tid_fg, True)
    u.ShowWindow(hwnd, 9)
    u.BringWindowToTop(hwnd)
    u.SetForegroundWindow(hwnd)
    u.SetFocus(hwnd)
    u.AttachThreadInput(tid_me, tid_fg, False)
    time.sleep(0.3)


def click(hwnd, ix, iy, hold=0.06):
    r = RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    u.SetCursorPos(r.l + ix + OFF_X, r.t + iy + OFF_Y)
    time.sleep(0.12)
    u.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(hold)
    u.mouse_event(0x0004, 0, 0, 0, 0)
    time.sleep(0.35)


def wheel(hwnd, ix, iy, notches=-3):
    """在下拉/列表上滚滚轮"""
    r = RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    u.SetCursorPos(r.l + ix + OFF_X, r.t + iy + OFF_Y)
    time.sleep(0.15)
    for _ in range(abs(notches)):
        u.mouse_event(0x0800, 0, 0, 120 if notches > 0 else -120, 0)
        time.sleep(0.12)


def type_text(s):
    for ch in s:
        vk = u.VkKeyScanW(ord(ch))
        vkey = vk & 0xFF
        shift = (vk >> 8) & 1
        if shift:
            u.keybd_event(0x10, 0, 0, 0)
        u.keybd_event(vkey, 0, 0, 0)
        u.keybd_event(vkey, 0, 2, 0)
        if shift:
            u.keybd_event(0x10, 0, 2, 0)
        time.sleep(0.04)


def shot(hwnd, path):
    from PIL import ImageGrab
    r = RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    img = ImageGrab.grab(bbox=(r.l, r.t, r.r, r.b), all_screens=True)
    w, h = img.size
    if w > 1280:
        img = img.resize((1280, int(h * 1280 / w)), 1)
    img.save(path)
    print("saved", path, img.size)


def enter():
    u.keybd_event(0x0D, 0, 0, 0)
    u.keybd_event(0x0D, 0, 2, 0)
    time.sleep(0.2)


def main():
    hwnd = find_window()
    if not hwnd:
        print("NO_WINDOW")
        return 1

    # 1) 模组与资源
    fg(hwnd); click(hwnd, 60, 250); time.sleep(2)
    # 2) 在线下载
    click(hwnd, 1125, 150); time.sleep(2)
    # 3) 搜索框输入 fabric 并回车
    click(hwnd, 400, 196)
    type_text("fabric")
    enter()
    time.sleep(6)
    shot(hwnd, "tools/verify_1_results.png")

    # 4) 点第一个搜索结果 → 右侧详情 + 版本列表
    click(hwnd, 600, 250); time.sleep(5)
    shot(hwnd, "tools/verify_2_versions.png")

    # 5) 版本列表上滚滚轮（验证可滚动）
    wheel(hwnd, 1050, 500, -3)
    time.sleep(0.6)
    shot(hwnd, "tools/verify_3_scrolled.png")

    # 6) 已安装页的版本下拉：展开 + 滚轮
    fg(hwnd); click(hwnd, 60, 250); time.sleep(2)
    click(hwnd, 420, 196); time.sleep(1.2)
    shot(hwnd, "tools/verify_4_verbox_open.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())