# -*- coding: utf-8 -*-
"""把源图指定区域放大导出到 _work/，用于肉眼定序 / 判断文字是否烘焙。"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
ART = PROJECT / "Assets" / "Art"
WORK = PROJECT / "Tools" / "art-audit" / "_work"

FILES = {
    "ui": ART / "542f45c2-5d2c-4803-bcbd-2cafa10226fc.png",
    "hero": ART / "2049a678-64d5-46e6-a342-87eaf3f37435.png",
    "mon": ART / "6e093879-16be-4326-b9e9-c90a767cb587.png",
    "ref": ART / "Reference.png",
}

# (key, 名字, x, y, w, h, 放大倍数, 棋盘底?)
CROPS = [
    ("ref", "ref_slotL",   10, 245, 230, 400, 3, False),
    ("ref", "ref_slotR", 1290, 245, 230, 400, 3, False),
    ("ref", "ref_top",      5,   5, 700,  70, 3, False),
    ("ref", "ref_buffs",   20,  75, 300,  70, 4, False),
    ("ref", "ref_stage",  130, 120, 620, 290, 2, False),
    ("ref", "ref_orb",     40, 540, 200, 180, 3, False),
]


def checkerboard(size, step=12):
    w, h = size
    bg = Image.new("RGBA", size, (255, 255, 255, 255))
    px = bg.load()
    for y in range(h):
        for x in range(w):
            if ((x // step) + (y // step)) % 2:
                px[x, y] = (205, 205, 205, 255)
    return bg


def main() -> None:
    WORK.mkdir(parents=True, exist_ok=True)
    for (key, name, x, y, w, h, z, board) in CROPS:
        im = Image.open(FILES[key]).convert("RGBA")
        crop = im.crop((x, y, x + w, y + h))
        crop = crop.resize((w * z, h * z), Image.NEAREST)
        if board:
            base = checkerboard(crop.size)
            base.alpha_composite(crop)
            crop = base
        out = WORK / f"{name}.png"
        crop.save(out)
        print(f"{out.name:22s} {crop.size}")


if __name__ == "__main__":
    main()
