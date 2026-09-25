# -*- coding: utf-8 -*-
"""生成 40 张卡牌的总览索引图（重命名后的成果核对用）。"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(__file__).resolve().parents[2]
CARDS = PROJECT / "Assets" / "Art" / "Cards"
OUT = PROJECT / "Captures" / "art-review" / "卡牌总览-40张.png"
LABEL_FONT = PROJECT / "Assets" / "Art" / "Fonts" / "Black" / "Google-Regular.ttf"

COLS, ROWS = 8, 5
CW, CH = 236, 328          # 单元格
PAD, LABEL_H = 18, 26

files = sorted(CARDS.glob("Card_*.png"))
assert len(files) == 40, f"期望 40 张，实际 {len(files)}"

W = PAD * 2 + COLS * CW
H = PAD * 2 + 58 + ROWS * (CH + LABEL_H)
sheet = Image.new("RGB", (W, H), (250, 250, 252))
d = ImageDraw.Draw(sheet)

f_head = ImageFont.truetype(str(LABEL_FONT), 26)
f_label = ImageFont.truetype(str(LABEL_FONT), 17)
d.text((PAD, PAD), "魔法乱斗 1.3 · 卡牌美术总览（40 张 · a–an）", font=f_head, fill=(30, 30, 35))

y0 = PAD + 58
for i, f in enumerate(files):
    r, c = divmod(i, COLS)
    x = PAD + c * CW
    y = y0 + r * (CH + LABEL_H)
    with Image.open(f) as im:
        tile = im.convert("RGB").resize((CW - 8, CH - 8), Image.LANCZOS)
    sheet.paste(tile, (x + 4, y))
    cid = f.stem.split("_")[1]
    name = f.stem.split("_", 2)[2]
    d.text((x + 6, y + CH - 2), f"{cid} · {name}", font=f_label, fill=(60, 60, 70))

OUT.parent.mkdir(parents=True, exist_ok=True)
sheet.save(OUT)
print(f"{OUT}  {sheet.size}")
