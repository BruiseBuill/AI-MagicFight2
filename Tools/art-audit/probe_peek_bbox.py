# -*- coding: utf-8 -*-
"""U8 查看手牌弹窗：在 5 个分区里各求一次严格 alpha 包围盒。"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
SHEET = PROJECT / "Assets" / "Art" / "661ca76a-c73d-4618-b6e5-918c75968b58.png"

img = Image.open(SHEET).convert("RGBA")
a = np.asarray(img)
alpha = a[:, :, 3].astype(np.int16)
mask = alpha > 24

REGIONS = {
    "Peek_Panel": (0, 0, 1040, 600),
    "Peek_Frame": (1040, 0, 1536, 600),
    "Peek_PanelSmall": (0, 600, 570, 1024),
    "Peek_Banner": (570, 600, 1080, 1024),
    "Peek_CardBack": (1080, 600, 1536, 1024),
}

for name, (x0, y0, x1, y1) in REGIONS.items():
    sub = mask[y0:y1, x0:x1]
    ys = np.where(sub.any(axis=1))[0]
    xs = np.where(sub.any(axis=0))[0]
    if len(ys) == 0:
        print(f"{name:16s} 空")
        continue
    bx0, bx1 = int(xs.min()), int(xs.max())
    by0, by1 = int(ys.min()), int(ys.max())
    w, h = bx1 - bx0 + 1, by1 - by0 + 1
    print(f"{name:16s} 全局 x={x0+bx0:>4} y={y0+by0:>4} w={w:>4} h={h:>4}  aspect={w/h:.4f}")
