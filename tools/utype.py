"""用 SendInput 以 Unicode 方式往当前焦点窗口输入文本。"""
import ctypes, sys, time

user32 = ctypes.windll.user32
ULONG_PTR = ctypes.c_ulonglong if ctypes.sizeof(ctypes.c_void_p) == 8 else ctypes.c_ulong


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", ctypes.c_ushort), ("wScan", ctypes.c_ushort),
                ("dwFlags", ctypes.c_ulong), ("time", ctypes.c_ulong),
                ("dwExtraInfo", ULONG_PTR)]


class INPUT(ctypes.Structure):
    class _U(ctypes.Union):
        _fields_ = [("ki", KEYBDINPUT)]
    _anonymous_ = ("u",)
    _fields_ = [("type", ctypes.c_ulong), ("u", _U)]


def type_unicode(text):
    for ch in text:
        for flags in (0x0004, 0x0004 | 0x0002):   # UNICODE, 然后 KEYUP
            inp = INPUT()
            inp.type = 1                              # INPUT_KEYBOARD
            inp.ki = KEYBDINPUT(0, ord(ch), flags, 0, 0)
            user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))
        time.sleep(0.01)


if __name__ == "__main__":
    type_unicode(sys.argv[1])
    print("typed:", sys.argv[1])
