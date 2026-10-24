import ctypes, sys, time
from ctypes import wintypes
from PIL import ImageGrab

u = ctypes.windll.user32
k = ctypes.windll.kernel32


def find_window():
    hs = []

    def cb(h, _):
        n = u.GetWindowTextLengthW(h)
        if n > 0:
            b = ctypes.create_unicode_buffer(n + 1)
            u.GetWindowTextW(h, b, n + 1)
            if b.value.strip() == "HHCL" and u.IsWindowVisible(h):
                hs.append(h)
        return True

    CB = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    u.EnumWindows(CB(cb), 0)
    return hs[0] if hs else None


def force_fg(hwnd):
    if u.GetForegroundWindow() == hwnd:
        return True
    fg = u.GetForegroundWindow()
    tid_fg = u.GetWindowThreadProcessId(fg, None)
    tid_me = k.GetCurrentThreadId()
    u.AttachThreadInput(tid_me, tid_fg, True)
    u.ShowWindow(hwnd, 9)
    u.BringWindowToTop(hwnd)
    u.SetForegroundWindow(hwnd)
    u.SetFocus(hwnd)
    u.AttachThreadInput(tid_me, tid_fg, False)
    time.sleep(0.35)
    return u.GetForegroundWindow() == hwnd


class RECT(ctypes.Structure):
    _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long), ("r", ctypes.c_long), ("b", ctypes.c_long)]


hwnd = find_window()
if not hwnd:
    print("NO_WINDOW")
    sys.exit(1)

if not force_fg(hwnd):
    print("FG_FAIL")
    sys.exit(2)

r = RECT()
u.GetWindowRect(hwnd, ctypes.byref(r))

args = sys.argv[1:]
out = None
if args and args[0].startswith("shot="):
    out = args[0][5:]
    args = args[1:]

coords = [int(v) for v in args] if args else []
for i in range(0, len(coords) - 1, 2):
    force_fg(hwnd)
    u.SetCursorPos(r.l + coords[i], r.t + coords[i + 1])
    time.sleep(0.12)
    u.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.06)
    u.mouse_event(0x0004, 0, 0, 0, 0)
    time.sleep(0.8)

if out:
    # 注意：这里不要��� force_fg，否则 SetFocus 会把 ComboBox 弹出的下拉框关掉
    time.sleep(0.4)
    u.GetWindowRect(hwnd, ctypes.byref(r))
    img = ImageGrab.grab(bbox=(r.l, r.t, r.r, r.b), all_screens=True)
    w, h = img.size
    if w > 1280:
        img = img.resize((1280, int(h * 1280 / w)), 1)
    img.save(out)
    print("saved", out, img.size)
