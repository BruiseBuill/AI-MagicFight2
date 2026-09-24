# -*- coding: utf-8 -*-
"""
新导入美术资源总览核对图生成器
================================
把一批图片缩略拼成带编号/名称水印的网格图，用于「一次读完」而非逐张打开。

用法:
    python render_art_sheets.py            # 全部
    python render_art_sheets.py elements   # 只出元素原画

产出: Captures/art-review/ 下的 PNG
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
ART = PROJECT / "Assets" / "Art"
REVIEW = PROJECT / "Captures" / "art-review"

# 标签字体：工程内自带的 Noto Sans SC（有中文）
FONT_PATH = ART.parent / "Font" / "Black" / "Google-Regular.ttf"
FONT_TITLE = 26
FONT_LABEL = 22

LABEL_H = 34          # 每格底部留给文字的高度
BG = (246, 246, 248)
FG = (32, 32, 36)
MUTED = (130, 130, 140)

EXTS = (".png", ".jpg", ".jpeg")


def font(size: int) -> ImageFont.FreeTypeFont:
    if FONT_PATH.exists():
        return ImageFont.truetype(str(FONT_PATH), size)
    return ImageFont.load_default()


def sheet(items, cols, cell, out_name, title, note="", plate_color=(255, 255, 255)):
    """items: [(Path, 标签)]；cell=(宽, 高)；plate_color=缩略图底板色（白色精灵要用深底）"""
    cw, ch = cell
    rows = (len(items) + cols - 1) // cols
    head = 64 if title else 0
    sheet_w = cols * cw
    sheet_h = head + rows * (ch + LABEL_H) + (44 if note else 8)

    canvas = Image.new("RGB", (sheet_w, sheet_h), BG)
    draw = ImageDraw.Draw(canvas)

    if title:
        draw.text((16, 16), title, font=font(FONT_TITLE), fill=FG)

    for i, (path, label) in enumerate(items):
        r, c = divmod(i, cols)
        x = c * cw
        y = head + r * (ch + LABEL_H)

        # 缩略图底板
        draw.rectangle([x + 4, y + 4, x + cw - 6, y + ch - 2], fill=plate_color)

        try:
            with Image.open(path) as im:
                im = im.convert("RGBA")
                im.thumbnail((cw - 16, ch - 12), Image.LANCZOS)
                # 居中贴到底板上（透明区透出底板色，便于看轮廓）
                plate = Image.new("RGBA", (cw - 16, ch - 12), plate_color + (255,))
                plate.alpha_composite(im, ((plate.width - im.width) // 2,
                                           (plate.height - im.height) // 2))
                canvas.paste(plate.convert("RGB"), (x + 8, y + 8))
        except Exception as exc:          # noqa: BLE001
            draw.text((x + 12, y + 12), f"读取失败\n{exc}", font=font(16), fill=(200, 60, 60))

        draw.text((x + 8, y + ch + 4), label, font=font(FONT_LABEL), fill=FG)

    if note:
        draw.text((16, sheet_h - 34), note, font=font(20), fill=MUTED)

    REVIEW.mkdir(parents=True, exist_ok=True)
    out = REVIEW / out_name
    canvas.save(out, optimize=True)
    print(f"[OK] {out.name}  {canvas.width}x{canvas.height}  {len(items)} 项")
    return out


def element_items():
    order = ["Ice", "Water", "Electric", "Fire", "Grass", "Stone", "Sound"]
    zh = {"Ice": "冰", "Water": "水", "Electric": "电", "Fire": "火",
          "Grass": "草", "Stone": "石", "Sound": "音"}
    items = []
    for el in order:
        d = ART / el
        if not d.is_dir():
            continue
        for f in sorted([p for p in d.iterdir() if p.suffix.lower() in EXTS],
                        key=lambda p: p.name):
            items.append((f, f"{zh[el]} · {f.stem}"))
    return items


def polysprite_items():
    items = []
    for f in sorted([p for p in ART.joinpath("PolySprite").rglob("*")
                     if p.suffix.lower() in EXTS], key=lambda p: str(p).lower()):
        items.append((f, f.stem))
    return items


def stray_items():
    """原 Art 根目录散落的两张图 —— 整理后已归位，这里从新位置取，标注新名与去处。"""
    moved = [
        (ART / "Icons" / "IconSheet_盾剑强制_三合一.png", "Icons/ 三合一图标源图"),
        (ART / "Fx" / "Fx_Explosion_Light.png", "Fx/ 爆炸光效"),
    ]
    out = []
    for p, note in moved:
        if p.exists():
            out.append((p, note))
    return out


def cardart_items():
    """整理后的 Art/CardArt：按卡表序号排，标签 = 序号 + 卡ID + 卡名。"""
    d = ART / "CardArt"
    items = []
    for f in sorted([p for p in d.iterdir() if p.suffix.lower() in EXTS],
                    key=lambda p: p.name):
        stem = f.stem                      # Card_07_g_陨石
        parts = stem.split("_", 3)
        label = parts[1] + " " + parts[2] + " " + parts[3] if len(parts) == 4 else stem
        items.append((f, label))
    return items


def draft_items():
    d = ART / "CardArt" / "_Draft"
    if not d.is_dir():
        return []
    return [(f, f.stem) for f in sorted([p for p in d.iterdir() if p.suffix.lower() in EXTS],
                                        key=lambda p: p.name)]


def main():
    what = sys.argv[1] if len(sys.argv) > 1 else "all"

    if what in ("all", "cardart"):
        items = cardart_items()
        sheet(items, cols=8, cell=(150, 205),
              out_name="新素材总览-04-插画原图-按卡序.png",
              title=f"卡牌插画原图 · 按卡表顺序 01–{len(items)}（Art/CardArt）",
              note="用于核对「图 ↔ 卡名」是否对错；成品卡面见 卡牌总览-40张.png")

    if what in ("all", "draft"):
        items = draft_items()
        if items:
            sheet(items, cols=5, cell=(200, 270),
                  out_name="新素材总览-05-无对应卡草稿.png",
                  title=f"无对应卡的插画草稿 · 共 {len(items)} 张（Art/CardArt/_Draft）",
                  note="素材已就位但卡表未收录：音系 3 张、水/电/火/石各 1 张、草系 2 张")

    if what in ("all", "elements"):
        items = element_items()
        if not items:
            # 整理之后元素子目录已并入 Art/CardArt，这里不再有输入。
            # 已有的「新素材总览-01」是整理前的留档，不要用空图覆盖它。
            print("[SKIP] Art/Ice … 已不存在（已整理），保留 新素材总览-01 作为整理前留档")
        else:
            sheet(items, cols=5, cell=(230, 300),
                  out_name="新素材总览-01-元素原画.png",
                  title=f"元素原画 · 共 {len(items)} 张（整理前形态）",
                  note="整理前的编排：按元素目录 + 中文名，未按卡表编号")

    if what in ("all", "poly"):
        items = polysprite_items()
        sheet(items, cols=7, cell=(180, 190),
              out_name="新素材总览-02-PolySprite形状包.png",
              title=f"PolySprite 通用形状包 · 共 {len(items)} 张",
              note="第三方素材包，命名保留原样以便对照来源；白形+透明底，故用深色底板",
              plate_color=(46, 52, 62))

    if what in ("all", "stray"):
        items = stray_items()
        sheet(items, cols=4, cell=(300, 360),
              out_name="新素材总览-03-原Art根目录两张图.png",
              title=f"原 Art 根目录散落的两张图 · 共 {len(items)} 张（已重命名并归位）",
              note="原命名无语义：ChatGPT Image 2025年4月24日… / Explose_Light（拼写有误）")


if __name__ == "__main__":
    main()
