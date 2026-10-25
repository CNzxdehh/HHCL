"""诊断：切到联机页，读取各输入框的真实 Value（看有没有不可见空白字符）。"""
import ctypes, time, sys, subprocess
import uiautomation as auto

auto.SetGlobalSearchTimeout(4)
u = ctypes.windll.user32

# 找 HHCL.exe 的 PID
out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq HHCL.exe", "/FO", "CSV", "/NH"],
                     capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
pid = int(out.split(",")[1].strip().strip('"'))
print("PID =", pid)

hwnd = None
def cb(h, _):
    global hwnd
    p = ctypes.c_ulong(); u.GetWindowThreadProcessId(h, ctypes.byref(p))
    if p.value == pid and u.IsWindowVisible(h): hwnd = h
    return True
CB = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
u.EnumWindows(CB(cb), 0)
print("hwnd =", hwnd)
win = auto.ControlFromHandle(hwnd)
win.SetActive(); time.sleep(0.8)

def walk(c, d=0, out=None):
    if out is None: out = []
    try:
        for ch in c.GetChildren():
            try:
                out.append((d, ch.ControlTypeName, ch.Name, ch))
            except Exception: pass
            if d < 14: walk(ch, d + 1, out)
    except Exception: pass
    return out

items = walk(win)

# 侧栏 ListItem「联机」→ Invoke
target = None
for d, t, n, ch in items:
    if n == "联机" and t in ("ListItemControl", "ListControl", "TreeItemControl"):
        target = ch; break
if target is None:
    # 退而求其次：找所有 ListItem
    lis = [(d,t,n,ch) for d,t,n,ch in items if t == "ListItemControl"]
    print("ListItem 列表:", [(t,n) for d,t,n,ch in lis])
    for d,t,n,ch in lis:
        if n == "联机": target = ch; break
if target:
    try:
        target.GetInvokePattern().Invoke()
        print("已 Invoke 联机")
    except Exception as e:
        print("invoke 失败", e)
        try:
            target.GetSelectionItemPattern().Select(); print("已 Select")
        except Exception as e2:
            print("select 也失败", e2)
    time.sleep(2.0)

items = walk(win)
print("\n=== 所有 EditControl ===")
for d, t, n, ch in items:
    if t == "EditControl":
        try:
            v = ch.GetValuePattern().Value if ch.GetValuePattern() else ""
        except Exception:
            v = "<no value pattern>"
        r = ch.BoundingRectangle
        print(f"  name={n!r}")
        print(f"     value={v!r}  len={len(v) if isinstance(v,str) else '?'}")
        print(f"     rect=({r.left},{r.top},{r.right},{r.bottom}) 宽={r.right-r.left}")
        if isinstance(v, str):
            print(f"     逐字符={[hex(ord(c)) for c in v]}")
print("\n=== 页面文字（判断在哪个页）===")
seen = []
for d, t, n, ch in items:
    if n and n not in seen and len(n) < 26:
        seen.append(n)
print(" | ".join(seen[:30]))
