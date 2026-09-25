# -*- coding: utf-8 -*-
"""文档断链 + 文档中引用的已消失资产 检测"""
import os, re, sys, collections

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
       r'E:\UnityProject\Unity_AI_CardFight2'
DOCS = os.path.join(ROOT, 'Docs')
LINK = re.compile(r'\[[^\]]*\]\(([^)]+)\)')
CODE = re.compile(r'`([^`]+)`')

out = []
broken = []
for dp, dns, fns in os.walk(DOCS):
    for fn in fns:
        if not fn.endswith('.md'): continue
        p = os.path.join(dp, fn)
        txt = open(p, 'r', encoding='utf-8', errors='ignore').read()
        for target in LINK.findall(txt):
            t = target.split('#')[0].strip()
            if not t or t.startswith(('http', 'mailto:')): continue
            t = t.replace('/', os.sep).replace('\\', os.sep)
            fp = os.path.normpath(os.path.join(dp, t))
            if not os.path.exists(fp):
                broken.append((os.path.relpath(p, ROOT).replace('\\', '/'), target))

out.append('# ===== A. 文档断链 (%d) =====' % len(broken))
for a, t in broken:
    out.append('  %s  ->  %s' % (a, t))

# ---------- B. 文档里被 `引用` 的 Assets 路径是否存在 ----------
out.append('')
out.append('# ===== B. 文档里点名、但工程中已不存在的资产路径 =====')
PAT = re.compile(r'`((?:Assets|Captures|Tools)/[^`\n]+?)`')
seen = collections.defaultdict(set)
for dp, dns, fns in os.walk(DOCS):
    for fn in fns:
        if not fn.endswith('.md'): continue
        p = os.path.join(dp, fn)
        txt = open(p, 'r', encoding='utf-8', errors='ignore').read()
        for m in PAT.findall(txt):
            m2 = m.strip().rstrip(',.;:')
            if '*' in m2 or '<' in m2 or '>' in m2 or ' ' in m2.split('/')[-1][:0]:
                pass
            seen[m2].add(os.path.relpath(p, ROOT).replace('\\', '/'))
for m2 in sorted(seen):
    if any(ch in m2 for ch in '*<>{}'):
        continue
    cand = os.path.join(ROOT, m2.replace('/', os.sep))
    if not os.path.exists(cand):
        out.append('  %s' % m2)
        for s in sorted(seen[m2])[:4]:
            out.append('        <- ' + s)

sys.stdout.write('\n'.join(out))
