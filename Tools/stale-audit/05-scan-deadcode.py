# -*- coding: utf-8 -*-
import os, re, sys

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
       r'E:\UnityProject\Unity_AI_CardFight2'
S = os.path.join(ROOT, 'Assets/Scripts')
blobs = {}
for dp, dns, fns in os.walk(S):
    for fn in fns:
        if fn.endswith('.cs'):
            p = os.path.join(dp, fn)
            blobs[os.path.relpath(p, ROOT).replace('\\', '/')] = open(p, encoding='utf-8', errors='ignore').read()

out = []
out.append('# ===== 从未被本文件以外引用的 public const / static readonly =====')
seen = []
for a, t in blobs.items():
    for n in re.findall(r'public\s+(?:const|static\s+readonly)\s+[\w<>\[\]\.]+\s+([A-Za-z_]\w*)', t):
        seen.append((a, n))
for a, n in sorted(set(seen)):
    hits = 0
    for b, t in blobs.items():
        if b == a:
            continue
        if re.search(r'\b' + re.escape(n) + r'\b', t):
            hits += 1
    if hits == 0:
        # 本文件内自己用了没用？
        own = len(re.findall(r'\b' + re.escape(n) + r'\b', blobs[a]))
        out.append('  %-60s %-34s (本文件出现 %d 次)' % (a, n, own))

out.append('')
out.append('# ===== 从未被任何 .cs 引用的 public class / static class =====')
for a, t in blobs.items():
    for n in re.findall(r'public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_]\w*)', t):
        hits = 0
        for b, t2 in blobs.items():
            if b == a:
                continue
            if re.search(r'\b' + re.escape(n) + r'\b', t2):
                hits += 1
        if hits == 0:
            out.append('  %-60s %s' % (a, n))

sys.stdout.write('\n'.join(out))
