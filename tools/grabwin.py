"""直接用 PrintWindow 抓指定窗口内容，不受其它窗口遮挡影响。"""
import ctypes, sys
from ctypes import wintypes
from PIL import Image

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
user32.SetProcessDPIAware()

hwnd = int(sys.argv[1])
out = sys.argv[2]

class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]

r = RECT()
user32.GetWindowRect(hwnd, ctypes.byref(r))
w, h = r.right - r.left, r.bottom - r.top
if w <= 0 or h <= 0:
    print("BAD_RECT", w, h); sys.exit(1)

hdc = user32.GetWindowDC(hwnd)
memdc = gdi32.CreateCompatibleDC(hdc)
bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
gdi32.SelectObject(memdc, bmp)

# 2 = PW_RENDERFULLCONTENT（能抓到 WPF/WinUI 内容）
ok = user32.PrintWindow(hwnd, memdc, 2)
if not ok:
    user32.PrintWindow(hwnd, memdc, 0)

class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                ("biClrImportant", ctypes.c_uint32)]

bi = BITMAPINFOHEADER()
bi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
bi.biWidth = w
bi.biHeight = -h          # 负数 = 自上而下
bi.biPlanes = 1
bi.biBitCount = 32
bi.biCompression = 0
buf = ctypes.create_string_buffer(w * h * 4)
gdi32.GetDIBits(memdc, bmp, 0, h, buf, ctypes.byref(bi), 0)

gdi32.DeleteObject(bmp)
gdi32.DeleteDC(memdc)
user32.ReleaseDC(hwnd, hdc)

img = Image.frombuffer("RGB", (w, h), buf, "raw", "BGRX", 0, 1)
img.save(out)
print(f"saved {out} {w}x{h} printwindow={ok}")
