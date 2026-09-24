# -*- coding: utf-8 -*-
"""U8 查看手牌弹窗：图集分析探针（一次性脚本，只打印不产图）。"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
SHEET = PROJECT / "Assets" / "Art" / "661ca76a-c73d-4618-b6e5-918c75968b58.png"

img = Image.open(SHEET).convert("RGBA")
a = np.asarray(img)
print("size", img.size)

alpha = a[:, :, 3]
mask = alpha > 24
print("alpha>24 px", int(mask.sum()))

try:
    from scipy import ndimage

    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    print("components", n)
    objs = ndimage.find_objects(lab)
    comps = []
    for i, sl in enumerate(objs, start=1):
        if sl is None:
            continue
        cnt = int((lab[sl] == i).sum())
        if cnt < 200:
            continue
        ys, xs = sl
        comps.append((cnt, xs.start, ys.start, xs.stop - xs.start, ys.stop - ys.start))
    comps.sort(reverse=True)
    for c in comps[:20]:
        print(f"  px={c[0]:>7}  x={c[1]:>4} y={c[2]:>4} w={c[3]:>4} h={c[4]:>4}")
except ImportError:
    print("no scipy")

# 行 / 列投影，帮助切分上下两块
rows = mask.sum(axis=1)
cols = mask.sum(axis=0)
def segs(v, thr):
    idx = np.where(v > thr)[0]
    if len(idx) == 0:
        return []
    out, s, p = [], idx[0], idx[0]
    for x in idx[1:]:
        if x != p + 1:
            out.append((int(s), int(p)))
            s = x
        p = x
    out.append((int(s), int(p)))
    return out

print("row bands:", segs(rows, 2))
print("col bands:", segs(cols, 2))

# 上半 / 下半各自的列投影
h = img.size[1]
for name, sl in (("top", slice(0, h // 2)), ("bottom", slice(h // 2, h))):
    band = mask[sl, :]
    print(f"{name} col bands:", segs(band.sum(axis=0), 2))
    sub = mask[sl, :]
    print(f"{name} row bands:", [(s + (0 if name == 'top' else h // 2), e + (0 if name == 'top' else h // 2)) for s, e in segs(sub.sum(axis=1), 2)])
