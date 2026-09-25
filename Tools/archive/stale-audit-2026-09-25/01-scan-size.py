# -*- coding: utf-8 -*-
import os, sys, time, re, json, collections

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
       r'E:\UnityProject\Unity_AI_CardFight2'

def mt(p):
    try:
        return time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(p)))
    except Exception:
        return '?'

# ---------- 1. 资产规模概览 ----------
lines = []
for top in ['Assets', 'Captures', '_TrashArt', 'Tools', 'Docs']:
    base = os.path.join(ROOT, top)
    if not os.path.isdir(base):
        lines.append('=== %s  <不存在>' % top); continue
    n = 0; size = 0; byext = collections.Counter(); byextsz = collections.Counter()
    for dp, dns, fns in os.walk(base):
        for fn in fns:
            p = os.path.join(dp, fn)
            try:
                s = os.path.getsize(p)
            except Exception:
                s = 0
            n += 1; size += s
            e = os.path.splitext(fn)[1].lower() or '(noext)'
            byext[e] += 1; byextsz[e] += s
    lines.append('=== %s : %d files, %.1f MB' % (top, n, size / 1048576.0))
    for e, c in byext.most_common(14):
        lines.append('    %-10s %5d  %8.1f KB' % (e, c, byextsz[e] / 1024.0))
    lines.append('')

# ---------- 2. Assets 二级目录明细 ----------
lines.append('=== Assets 二级目录 ===')
base = os.path.join(ROOT, 'Assets')
for d in sorted(os.listdir(base)):
    p = os.path.join(base, d)
    if not os.path.isdir(p): 
        lines.append('  [F] %s' % d); continue
    n = 0; size = 0
    for dp, dns, fns in os.walk(p):
        for fn in fns:
            n += 1
            try: size += os.path.getsize(os.path.join(dp, fn))
            except Exception: pass
    lines.append('  [D] %-18s %4d files  %8.2f MB' % (d, n, size / 1048576.0))

sys.stdout.write('\n'.join(lines))
