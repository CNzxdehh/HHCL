"""点 HHCL 侧栏某项并用 PrintWindow 抓图（不受其它窗口遮挡）。"""
import ctypes, time, sys, subprocess
from PIL import Image

u = ctypes.windll.user32
gdi = ctypes.windll.gdi32
u.SetProcessDPIAware()

def find_hhcl():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq HHCL.exe", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    pid = int(out.split(",")[1].strip().strip('"'))
    hwnd = None
    def cb(h, _):
        nonlocal hwnd
        p = ctypes.c_ulong(); u.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u.IsWindowVisible(h): hwnd = h
        return True
    CB = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    u.EnumWindows(CB(cb), 0)
    return hwnd

def grab(hwnd, path):
    # 64 位下 GDI 句柄必须显式声明成指针，否则 ctypes 按 int 传会溢出
    gdi.CreateCompatibleDC.argtypes = [ctypes.c_void_p]
    gdi.CreateCompatibleDC.restype = ctypes.c_void_p
    gdi.CreateCompatibleBitmap.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
    gdi.CreateCompatibleBitmap.restype = ctypes.c_void_p
    gdi.SelectObject.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    gdi.SelectObject.restype = ctypes.c_void_p
    gdi.GetDIBits.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                              ctypes.c_uint, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
    u.GetWindowDC.argtypes = [ctypes.c_void_p]
    u.GetWindowDC.restype = ctypes.c_void_p
    u.PrintWindow.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
    u.PrintWindow.restype = ctypes.c_bool
    u.GetWindowRect.argtypes = [ctypes.c_void_p, ctypes.c_void_p]

    class RECT(ctypes.Structure):
        _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long), ("r", ctypes.c_long), ("b", ctypes.c_long)]
    r = RECT(); u.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.r - r.l, r.b - r.t
    hdc = u.GetWindowDC(hwnd)
    memdc = gdi.CreateCompatibleDC(hdc)
    bmp = gdi.CreateCompatibleBitmap(hdc, w, h)
    gdi.SelectObject(memdc, bmp)
    u.PrintWindow(hwnd, memdc, 2)
    class BIH(ctypes.Structure):
        _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32), ("biHeight", ctypes.c_int32),
                    ("biPlanes", ctypes.c_uint16), ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                    ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32), ("biYPelsPerMeter", ctypes.c_int32),
                    ("biClrUsed", ctypes.c_uint32), ("biClrImportant", ctypes.c_uint32)]
    bi = BIH(); bi.biSize = ctypes.sizeof(BIH); bi.biWidth = w; bi.biHeight = -h
    bi.biPlanes = 1; bi.biBitCount = 32
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi.GetDIBits(memdc, bmp, 0, h, buf, ctypes.byref(bi), 0)
    try:
        gdi.DeleteObject(ctypes.c_void_p(bmp)); gdi.DeleteDC(ctypes.c_void_p(memdc)); u.ReleaseDC(hwnd, hdc)
    except Exception: pass
    Image.frombuffer("RGB", (w, h), buf, "raw", "BGRX", 0, 1).save(path)
    return w, h

if __name__ == "__main__":
    hwnd = find_hhcl()
    if not hwnd:
        print("no hhcl window"); sys.exit(1)
    print("hwnd", hwnd)
    # 最小化所有非 HHCL 的可见窗口（Chrome 等），把前景让出来
    def cb(h, _):
        if h != hwnd and u.IsWindowVisible(h):
            n = u.GetWindowTextLengthW(h)
            if n > 0:
                b = ctypes.create_unicode_buffer(n + 1); u.GetWindowTextW(h, b, n + 1)
                if "Chrome" in b.value or "chrome" in b.value:
                    u.ShowWindow(h, 6)
        return True
    CB = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    u.EnumWindows(CB(cb), 0)
    time.sleep(0.8)
    u.SetForegroundWindow(hwnd); time.sleep(0.6)

    # 侧栏项的屏幕 y 坐标（从 UIA 读到的「联机」在 620~660，间距 40）
    items = sys.argv[1:] if len(sys.argv) > 1 else ["设置", "720"]
    label, y = items[0], int(items[1])
    u.SetCursorPos(753, y); time.sleep(0.3)
    u.mouse_event(0x0002, 0, 0, 0, 0); u.mouse_event(0x0004, 0, 0, 0, 0)
    time.sleep(2.0)
    print("clicked", label, y)
    print("saved", grab(hwnd, "tools/audit/page.png"))
