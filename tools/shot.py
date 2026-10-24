import ctypes, sys, time
from ctypes import wintypes
from PIL import ImageGrab

user32 = ctypes.windll.user32

hwnds = []
def enum(hwnd, _):
    n = user32.GetWindowTextLengthW(hwnd)
    if n > 0:
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        if "HHCL" in buf.value and user32.IsWindowVisible(hwnd):
            hwnds.append((hwnd, buf.value))
    return True

CB = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows(CB(enum), 0)

if not hwnds:
    print("NO_WINDOW")
    sys.exit(1)

hwnd, title = hwnds[0]
user32.SetForegroundWindow(hwnd)
time.sleep(0.7)

class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long), ("right", ctypes.c_long), ("bottom", ctypes.c_long)]

r = RECT()
user32.GetWindowRect(hwnd, ctypes.byref(r))
print("title=%s rect=%d,%d,%d,%d" % (title, r.left, r.top, r.right, r.bottom))

img = ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom), all_screens=True)
out = sys.argv[1] if len(sys.argv) > 1 else "shot.png"
w, h = img.size
if w > 1280:
    img = img.resize((1280, int(h * 1280 / w)), 1)
img.save(out)
print("saved", out, img.size)
