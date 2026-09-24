# -*- coding: utf-8 -*-
"""把 40 张卡面的标题栏裁出来拼成一张校验图，用于一次性核对「编号 → 卡名」映射。"""
from pathlib import Path

from PIL import Image, ImageDraw

ART = Path(r"E:\UnityProject\Unity_AI_CardFight2\Assets\Art")
OUT = Path(__file__).with_name("verify-titles.png")

COLS, ROWS = 4, 10
CW, CH = 300, 92          # 单元格尺寸
CROP = (196, 62, 578, 172)  # 标题栏区域（避开左侧力量星爆与右侧冷却圆）

files = sorted(ART.glob("Screen_760x1056_*.png"),
               key=lambda p: int(p.stem.rsplit("_", 1)[1]))
assert len(files) == 40, f"期望 40 张，实际 {len(files)}"

sheet = Image.new("RGB", (COLS * CW, ROWS * CH), (245, 245, 245))
draw = ImageDraw.Draw(sheet)

for i, f in enumerate(files):
    with Image.open(f) as im:
        tile = im.convert("RGB").crop(CROP).resize((CW - 8, CH - 8), Image.LANCZOS)
    r, c = divmod(i, COLS)
    sheet.paste(tile, (c * CW + 4, r * CH + 4))

sheet.save(OUT)
print(f"{OUT}  {sheet.size}")
for i, f in enumerate(files):
    print(f"{i + 1:>3}  {f.name}")
