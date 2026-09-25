# -*- coding: utf-8 -*-
import os, re, sys, collections, time

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
       r'E:\UnityProject\Unity_AI_CardFight2'
def rel(p): return os.path.relpath(p, ROOT).replace('\\', '/')
def rd(p):
    try: return open(p, 'r', encoding='utf-8', errors='ignore').read()
    except Exception: return ''

out = []

def dirsize(d):
    n = 0; s = 0
    if not os.path.isdir(d): return 0, 0
    for dp, dns, fns in os.walk(d):
        for fn in fns:
            n += 1
            try: s += os.path.getsize(os.path.join(dp, fn))
            except Exception: pass
    return n, s

out.append('=== 1. 候选垃圾/历史目录体积 ===')
cands = [
    ('.workbuddy/tmp (Unity 会话临时)', '.workbuddy/tmp'),
    ('.workbuddy (工程内, 全部)', '.workbuddy'),
    ('Captures (截图总)', 'Captures'),
    ('Tools/art-audit/_work', 'Tools/art-audit/_work'),
    ('Tools/art-audit/_tmp', 'Tools/art-audit/_tmp'),
    ('Tools/font-audit', 'Tools/font-audit'),
    ('Docs/archive', 'Docs/archive'),
    ('Docs/validation', 'Docs/validation'),
    ('Docs/implementation', 'Docs/implementation'),
    ('Assets/Art/CardArt/_Draft', 'Assets/Art/CardArt/_Draft'),
    ('Assets/Art/_Reference', 'Assets/Art/_Reference'),
    ('Assets/Art/Chars/_Source', 'Assets/Art/Chars/_Source'),
    ('Assets/Art/Icons/_Source', 'Assets/Art/Icons/_Source'),
    ('Assets/Art/Ui/_Source', 'Assets/Art/Ui/_Source'),
    ('Assets/Plugins/Demigiant (DOTween)', 'Assets/Plugins/Demigiant'),
    ('Assets/ThirdParty/MagicCardKit', 'Assets/ThirdParty/MagicCardKit'),
    ('Assets/ThirdParty/PolySprite', 'Assets/ThirdParty/PolySprite'),
    ('Assets/Settings', 'Assets/Settings'),
    ('Assets/Font', 'Assets/Font'),
    ('Assets/Art (全部)', 'Assets/Art'),
    ('Assets/Scripts', 'Assets/Scripts'),
    ('Assets/TextMesh Pro', 'Assets/TextMesh Pro'),
    ('Library (Unity 缓存)', 'Library'),
    ('Temp', 'Temp'),
    ('obj', 'obj'),
    ('Logs', 'Logs'),
    ('.git', '.git'),
    ('Tools (全部)', 'Tools'),
    ('Tools/RuleSelfTest 日志', None),
]
for label, d in cands:
    if d is None:
        n = s = 0
        for fn in os.listdir(os.path.join(ROOT, 'Tools/RuleSelfTest')):
            if fn.endswith(('.log', '.txt')):
                n += 1; s += os.path.getsize(os.path.join(ROOT, 'Tools/RuleSelfTest', fn))
        out.append('  %-42s %4d 个 %8.2f MB' % (label, n, s / 1048576.0))
        continue
    n, s = dirsize(os.path.join(ROOT, d))
    out.append('  %-42s %4d 个 %8.2f MB' % (label, n, s / 1048576.0))
out.append('')

# ---------- 2. 字体使用情况 ----------
out.append('=== 2. Font 目录：源文件体积 + 是否被引用 ===')
refs = set()
for dp, dns, fns in os.walk(os.path.join(ROOT, 'Assets')):
    for fn in fns:
        ext = os.path.splitext(fn)[1].lower()
        if ext in ('.cs', '.prefab', '.unity', '.asset', '.mat'):
            refs |= set(re.findall(r'guid:\s*([0-9a-f]{32})', rd(os.path.join(dp, fn))))
fd = os.path.join(ROOT, 'Assets/Font')
rows = []
for dp, dns, fns in os.walk(fd):
    for fn in fns:
        if fn.endswith('.meta'): continue
        p = os.path.join(dp, fn)
        g = re.search(r'guid:\s*([0-9a-f]{32})', rd(p + '.meta'))
        used = bool(g and g.group(1) in refs)
        rows.append((os.path.getsize(p), rel(p), used))
for s, a, u in sorted(rows, reverse=True):
    out.append('  %8.2f MB  %-60s %s' % (s / 1048576.0, a, 'USE' if u else 'UNUSED'))
out.append('')

# ---------- 3. 代码里点名的字体路径 ----------
out.append('=== 3. 代码里出现的字体路径 ===')
for dp, dns, fns in os.walk(os.path.join(ROOT, 'Assets/Scripts')):
    for fn in fns:
        if not fn.endswith('.cs'): continue
        p = os.path.join(dp, fn)
        txt = rd(p)
        for m in re.findall(r'["\'](Assets/Font[^"\']*)["\']', txt):
            out.append('  %s  <- %s' % (m, rel(p)))
        for m in re.findall(r'([A-Za-z0-9_]*FontPath[A-Za-z0-9_]*)\s*=', txt):
            pass
out.append('')

# ---------- 4. 卡图 vs 卡表 ----------
out.append('=== 4. Art/Cards 与 Art/CardArt 文件清单 ===')
for d in ['Assets/Art/Cards', 'Assets/Art/CardArt', 'Assets/Art/Backgrounds', 'Assets/Art/Fx', 'Assets/Art/Icons']:
    base = os.path.join(ROOT, d)
    out.append('  --- %s' % d)
    if os.path.isdir(base):
        for dp, dns, fns in os.walk(base):
            for fn in sorted(fns):
                if fn.endswith('.meta'): continue
                p = os.path.join(dp, fn)
                out.append('     %7.1f KB %s' % (os.path.getsize(p) / 1024.0, rel(p)))
out.append('')

# ---------- 5. Captures 全清单（按日期） ----------
out.append('=== 5. Captures 全清单 ===')
base = os.path.join(ROOT, 'Captures')
rows = []
for dp, dns, fns in os.walk(base):
    for fn in fns:
        p = os.path.join(dp, fn)
        rows.append((time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(p))),
                     os.path.getsize(p), rel(p)))
for mt, s, a in sorted(rows):
    out.append('  %s %8.1f KB %s' % (mt, s / 1024.0, a))

sys.stdout.write('\n'.join(out))
