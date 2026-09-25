# -*- coding: utf-8 -*-
import os, re, sys, collections

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
       r'E:\UnityProject\Unity_AI_CardFight2'
def rel(p): return os.path.relpath(p, ROOT).replace('\\', '/')
def rd(p):
    try: return open(p, 'r', encoding='utf-8', errors='ignore').read()
    except Exception: return ''

out = []

# ---------- 1. Prefabs / Resources / Scenes / Settings 全清单 ----------
for d in ['Assets/Prefabs', 'Assets/Resources', 'Assets/Scenes', 'Assets/Settings']:
    base = os.path.join(ROOT, d)
    out.append('=== %s' % d)
    if os.path.isdir(base):
        for dp, dns, fns in os.walk(base):
            for fn in sorted(fns):
                if fn.endswith('.meta'): continue
                p = os.path.join(dp, fn)
                out.append('   %8.1f KB  %s' % (os.path.getsize(p) / 1024.0, rel(p)))
    out.append('')

# ---------- 2. asmdef 引用格式复查 ----------
out.append('=== asmdef 引用关系（GUID: 形式）')
GUIDCOLON = re.compile(r'GUID:([0-9a-f]{32})', re.I)
asmdef_guids = {}
for dp, dns, fns in os.walk(os.path.join(ROOT, 'Assets')):
    for fn in fns:
        if fn.endswith('.asmdef'):
            p = os.path.join(dp, fn)
            m = re.search(r'guid:\s*([0-9a-f]{32})', rd(p + '.meta'))
            if m: asmdef_guids[m.group(1)] = rel(p)
allrefs = set()
for dp, dns, fns in os.walk(os.path.join(ROOT, 'Assets')):
    for fn in fns:
        ext = os.path.splitext(fn)[1].lower()
        if ext in ('.asmdef', '.csproj', '.json', '.prefab', '.unity', '.asset'):
            allrefs |= set(GUIDCOLON.findall(rd(os.path.join(dp, fn))))
for g, a in asmdef_guids.items():
    out.append('   %-45s 被引用: %s' % (a, 'YES' if g in allrefs else 'NO'))
out.append('')

# ---------- 3. Captures 分组 ----------
out.append('=== Captures 按前缀分组（体积 top）')
base = os.path.join(ROOT, 'Captures')
grp = collections.defaultdict(lambda: [0, 0, ''])
for dp, dns, fns in os.walk(base):
    for fn in fns:
        p = os.path.join(dp, fn)
        sz = os.path.getsize(p)
        m = re.match(r'(m\d+)', fn)
        key = m.group(1) if m else (os.path.relpath(dp, base) + '/其他')
        g = grp[key]; g[0] += 1; g[1] += sz
        import time
        mt = time.strftime('%Y-%m-%d', time.localtime(os.path.getmtime(p)))
        if mt > g[2]: g[2] = mt
for k in sorted(grp, key=lambda x: -grp[x][1]):
    c, s, last = grp[k]
    out.append('   %-12s %3d 张  %7.1f MB  last=%s' % (k, c, s / 1048576.0, last))
out.append('')

# ---------- 4. Tools 下 png 分组 ----------
out.append('=== Tools 下 png 分组')
base = os.path.join(ROOT, 'Tools')
grp = collections.defaultdict(lambda: [0, 0])
for dp, dns, fns in os.walk(base):
    for fn in fns:
        if not fn.lower().endswith(('.png', '.jpg')): continue
        p = os.path.join(dp, fn)
        key = os.path.relpath(dp, base).replace('\\', '/')
        grp[key][0] += 1; grp[key][1] += os.path.getsize(p)
for k in sorted(grp, key=lambda x: -grp[x][1]):
    out.append('   %-40s %3d 张 %7.1f MB' % (k, grp[k][0], grp[k][1] / 1048576.0))
out.append('')

# ---------- 5. Tools 根目录清单 ----------
out.append('=== Tools 结构')
for dp, dns, fns in os.walk(base):
    lvl = dp[len(base):].count(os.sep)
    if lvl > 1: 
        dns[:] = []
        continue
    out.append('  [D] ' + rel(dp))
    for fn in sorted(fns)[:25]:
        if fn.endswith('.meta'): continue
        out.append('       %8.1f KB %s' % (os.path.getsize(os.path.join(dp, fn)) / 1024.0, fn))
out.append('')

sys.stdout.write('\n'.join(out))
