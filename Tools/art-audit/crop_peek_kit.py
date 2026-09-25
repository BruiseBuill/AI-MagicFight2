# -*- coding: utf-8 -*-
"""U8 查看手牌弹窗：把图集按候选包围盒裁开，供肉眼确认元素归属。"""
from __future__ import annotations

from pathlib import Path

from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
SHEET = PROJECT / "Assets" / "Art" / "Ui" / "_Source" / "661ca76a-c73d-4618-b6e5-918c75968b58.png"
WORK = PROJECT / "Artifacts" / "work" / "art"
WORK.mkdir(parents=True, exist_ok=True)

img = Image.open(SHEET).convert("RGBA")

CROPS = {
    "peek_a_topleft": (35, 41, 1026, 511),
    "peek_b_topright": (1050, 41, 1480, 511),
    "peek_c_botleft": (51, 646, 554, 901),
    "peek_d_botmid": (576, 646, 1000, 901),
    "peek_e_botright": (1000, 646, 1480, 936),
}

CARD_BACKS = (118, 298, 660, 900)   # 画布上的对照底色（深板岩）

for name, (x0, y0, x1, y1) in CROPS.items():
    sp = img.crop((x0, y0, x1, y1))
    canvas = Image.new("RGBA", (sp.width + 16, sp.height + 16), (18, 22, 30, 255))
    canvas.alpha_composite(sp, (8, 8))
    canvas.save(WORK / f"{name}.png")
    print(f"{name:18s} {sp.width:>4}x{sp.height:<4}  → {WORK / (name + '.png')}")

# 整张图集缩到一半，带网格，方便定位
grid = img.copy()
d = Image.frombytes("RGBA", grid.size, grid.tobytes())
px = d.load()
for x in range(0, grid.width, 100):
    for y in range(0, grid.height):
        r, g, b, al = px[x, y]
        px[x, y] = (255, 0, 0, 255)
for y in range(0, grid.height, 100):
    for x in range(0, grid.width):
        r, g, b, al = px[x, y]
        px[x, y] = (0, 255, 0, 255)
grid.resize((grid.width // 2, grid.height // 2), Image.NEAREST).save(WORK / "peek_sheet_grid.png")
print("grid →", WORK / "peek_sheet_grid.png")
