"""把窗口固定到 1200x800 放在 (0,0)，这样截图像素 == 窗口坐标，点击就不用猜偏移。"""
import ctypes
import sys
import time

u = ctypes.windll.user32
k = ctypes.windll.kernel32


def find(title="HHCL"):
    hs = []

    def cb(h, _):
        n = u.GetWindowTextLengthW(h)
        if n > 0:
            b = ctypes.create_unicode_buffer(n + 1)
            u.GetWindowTextW(h, b, n + 1)
            if b.value.strip() == title and u.IsWindowVisible(h):
                hs.append(h)
        return True

    u.EnumWindows(ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)(cb), 0)
    return hs[0] if hs else None


class RECT(ctypes.Structure):
    _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long), ("r", ctypes.c_long), ("b", ctypes.c_long)]


def rect_of(h):
    r = RECT()
    u.GetWindowRect(h, ctypes.byref(r))
    return r


def place(h, w=1200, h_=800):
    u.ShowWindow(h, 9)  # SW_RESTORE
    time.sleep(0.6)
    u.SetWindowPos(h, 0, 0, 0, w, h_, 0x0004)  # SWP_NOZORDER
    time.sleep(0.8)


def fg(h):
    cur = u.GetForegroundWindow()
    u.AttachThreadInput(k.GetCurrentThreadId(), u.GetWindowThreadProcessId(cur, None), True)
    u.SetForegroundWindow(h)
    u.SetFocus(h)
    u.AttachThreadInput(k.GetCurrentThreadId(), u.GetWindowThreadProcessId(cur, None), False)
    time.sleep(0.25)


OFF = 0  # 截图像素 y 与实际点击 y 的差（实测窗口在 (0,0) 时仍差 40）


def click(h, ix, iy):
    fg(h)
    r = rect_of(h)
    u.SetCursorPos(r.l + ix, r.t + iy + OFF)
    time.sleep(0.12)
    u.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.06)
    u.mouse_event(0x0004, 0, 0, 0, 0)
    time.sleep(0.4)


def wheel(h, ix, iy, n=-3):
    r = rect_of(h)
    u.SetCursorPos(r.l + ix, r.t + iy)
    time.sleep(0.15)
    for _ in range(abs(n)):
        u.mouse_event(0x0800, 0, 0, 120 if n > 0 else -120, 0)
        time.sleep(0.12)


def type_text(s):
    for ch in s:
        vk = u.VkKeyScanW(ord(ch))
        vkey, shift = vk & 0xFF, (vk >> 8) & 1
        if shift:
            u.keybd_event(0x10, 0, 0, 0)
        u.keybd_event(vkey, 0, 0, 0)
        u.keybd_event(vkey, 0, 2, 0)
        if shift:
            u.keybd_event(0x10, 0, 2, 0)
        time.sleep(0.04)


def enter():
    u.keybd_event(0x0D, 0, 0, 0)
    u.keybd_event(0x0D, 0, 2, 0)
    time.sleep(0.2)


def shot(h, path):
    from PIL import ImageGrab
    r = rect_of(h)
    img = ImageGrab.grab(bbox=(r.l, r.t, r.r, r.b), all_screens=True)
    img.save(path)
    print("saved", path, img.size, "rect", r.l, r.t, r.r - r.l, r.b - r.t)