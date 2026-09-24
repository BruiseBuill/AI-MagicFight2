# -*- coding: utf-8 -*-
"""
触发图标核对图
==============
出一张「角色映射 + 实机尺寸」的核对图，用来确认两件事：

1. **映射对不对** —— 剑=α 进攻 / 盾=β 防御 / 感叹号=γ 特殊
2. **小字号认不认得出** —— 图标在 24 / 40 px 时还看不看得清（迷你卡与详情浮层的真实尺寸）

配色取 `Assets/Scripts/Unity/Ui/UiTheme.cs` 的实际主题色，保证「实机观感」而不是凭空配色。

用法: python render_icon_sheet.py
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
ICONS = PROJECT / "Assets" / "Art" / "Icons"
REVIEW = PROJECT / "Docs" / "art-review"

# 取自 UiTheme.cs
BACKDROP = (0x0F, 0x13, 0x1B)
PANEL = (0x1A, 0x21, 0x30)
MINI_FACE = (0x26, 0x2F, 0x3F)
TEXT_PRIMARY = (0xEE, 0xF2, 0xF8)
TEXT_SECONDARY = (0x96, 0xA3, 0xB8)
ACCENT = (0x4F, 0xC3, 0xF7)

# (文件名, 符号, 枚举, 中文角色)
ROLES = [
    ("Icon_01_attack_剑.png", "α", "Attack", "剑 = 进攻时结算"),
    ("Icon_02_defend_盾.png", "β", "Defend", "盾 = 防御时结算"),
    ("Icon_03_special_感叹号.png", "γ", "Special", "感叹号 = 特殊时机"),
]

BIG = 180
SMALL = [40, 24]          # 实机字号：迷你卡数值 26–32、详情正文 22–26
W = 940
H = 96 + BIG + 150


def font(size: int) -> ImageFont.FreeTypeFont:
    p = PROJECT / "Assets" / "Font" / "Black" / "Google-Regular.ttf"
    return ImageFont.truetype(str(p), size) if p.exists() else ImageFont.load_default()


def main() -> int:
    img = Image.new("RGB", (W, H), BACKDROP)
    d = ImageDraw.Draw(img)

    d.text((24, 20), "触发图标 · 角色映射与实机尺寸核对", font=font(26), fill=TEXT_PRIMARY)
    d.text((24, 58), "素材来源：Assets/Art/Icons/_Source/IconSheet_盾剑特殊_三合一.png（已切分）",
           font=font(18), fill=TEXT_SECONDARY)

    col = W // len(ROLES)
    for i, (fname, sym, enum, desc) in enumerate(ROLES):
        cx = i * col + col // 2
        top = 96

        # 大图
        p = ICONS / fname
        if p.exists():
            with Image.open(p) as im:
                im = im.convert("RGBA")
                im.thumbnail((BIG, BIG), Image.LANCZOS)
                img.paste(im, (cx - im.width // 2, top + (BIG - im.height) // 2), im)

        y = top + BIG + 8
        label = f"{sym}  {enum}"
        w = d.textlength(label, font=font(22))
        d.text((cx - w / 2, y), label, font=font(22), fill=ACCENT)
        y += 32
        w = d.textlength(desc, font=font(18))
        d.text((cx - w / 2, y), desc, font=font(18), fill=TEXT_PRIMARY)

        # 实机尺寸：贴在迷你卡同色底板上
        y += 36
        x = cx - (sum(SMALL) + 24 * len(SMALL)) // 2
        for s in SMALL:
            plate = Image.new("RGB", (s + 16, s + 16), MINI_FACE)
            if p.exists():
                with Image.open(p) as im:
                    t = im.convert("RGBA").resize((s, s), Image.LANCZOS)
                    plate.paste(t, (8, 8), t)
            img.paste(plate, (x, y))
            d.text((x + 4, y + s + 18), f"{s}px", font=font(15), fill=TEXT_SECONDARY)
            x += s + 40

    out = REVIEW / "新素材总览-06-图标切分结果.png"
    img.save(out, optimize=True)
    print(f"[OK] {out.relative_to(PROJECT)}  {img.width}x{img.height}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
