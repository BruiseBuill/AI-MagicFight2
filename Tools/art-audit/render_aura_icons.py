# -*- coding: utf-8 -*-
"""
光环图标生成器（v2）
====================
把 `Docs/design/光环图标提示词.md` 里那套几何**用代码画出来**，直接产出可用的 PNG。

为什么不用生图模型：这套图标是纯几何（六边形 + 单色剪影），
矢量定义一次、脚本渲染一次，八个文件的描边粗细 / 留白 / 倒角必然一致；
生图模型每次都会把 512 网格画歪一点，切完再对不齐。

坐标口径：设计稿按 512 画布写死，渲染时整体乘 SS 超采样，最后 LANCZOS 缩到 256。
实机显示 72 px（`UiLayout.BuffIconSize`），所以 256 = 3.5 倍余量。

用法:
    python render_aura_icons.py            # 出正式 8 张 -> Assets/Resources/AuraIcons
    python render_aura_icons.py --review   # 再额外出一张核对图 -> Captures/art-review
"""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(__file__).resolve().parents[2]
OUT_DIR = PROJECT / "Assets" / "Resources" / "AuraIcons"
REVIEW_DIR = PROJECT / "Captures" / "art-review"
PREVIEW_DIR = PROJECT / "Captures" / "art-review" / "_aura-preview"

DESIGN = 512
SS = 4
CANVAS = DESIGN * SS
FINAL = 256

# 取自 UiTheme.cs
INK = (0x20, 0x20, 0x2A)        # 描边：比 AuraIconEdge(#10151E) 略浅，缩到 72 px 不糊成黑边
GLYPH = (0xFF, 0xF6, 0xE2)      # 符号：AuraGlyph
FAMILY = {
    "atk": (0xC4, 0x45, 0x3F),
    "def": (0x2F, 0x6F, 0xA8),
    "both": (0x7A, 0x55, 0xC4),
    "combo": (0x2E, 0x8B, 0x8B),
    "immune": (0xB9, 0x8A, 0x2A),
    "refill": (0x3E, 0x8E, 0x5A),
    "sigil": (0xD9, 0x82, 0x2B),
}

# 徽章几何：中心略偏上（数值条压在底部 24 px，见 UiLayout.BuffIconValue*）
HEX_CX = 256
HEX_CY = 210
HEX_R = 225          # 外接圆半径 -> 宽 450（88% 画布），高 390
EDGE = 15            # 描边宽度（512 口径）
SEAM_L, SEAM_R = 249, 257   # 「二选一」那道缝（512 口径）

FILES = [
    ("Aura_01_atk.png", "atk"),
    ("Aura_02_def.png", "def"),
    ("Aura_03_both.png", "both"),
    ("Aura_04_combo.png", "combo"),
    ("Aura_05_immunehigh.png", "immune"),
    ("Aura_06_immunelow.png", "immune"),
    ("Aura_07_refill.png", "refill"),
    ("Aura_08_sigil.png", "sigil"),
]


# ── 基础绘制 ────────────────────────────────────────────────

def s(v: float) -> float:
    """设计稿单位 -> 超采样像素。"""
    return v * SS


def hex_points(cx: float, cy: float, r: float) -> list[tuple[float, float]]:
    """flat-top 六边形：0°/60°…/300°，上边与下边水平。"""
    return [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a)))
            for a in range(0, 360, 60)]


def scaled(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    return [(s(x), s(y)) for x, y in pts]


def new_layer() -> Image.Image:
    return Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))


def clip_to_hex(layer: Image.Image) -> Image.Image:
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).polygon(
        scaled(hex_points(HEX_CX, HEX_CY, HEX_R - EDGE / 0.866)), fill=255)
    layer.putalpha(Image.composite(layer.getchannel("A"), Image.new("L", (CANVAS, CANVAS), 0), mask))
    return layer


def badge_base(fill: tuple[int, int, int]) -> Image.Image:
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # ① 描边：先画满一圈深色，再用小一圈的家族色盖回去 —— 拐角是干净的斜接，没有接缝
    d.polygon(scaled(hex_points(HEX_CX, HEX_CY, HEX_R)), fill=INK + (255,))
    d.polygon(scaled(hex_points(HEX_CX, HEX_CY, HEX_R - EDGE / 0.866)), fill=fill + (255,))

    # ② 低多边形切面：顶部一条提亮带 + 底部一条压暗带（都是硬边，不做渐变）
    top = HEX_CY - HEX_R * 0.866
    height = HEX_R * 1.732
    hi = new_layer()
    ImageDraw.Draw(hi).rectangle(
        [0, 0, CANVAS, s(top + height * 0.26)], fill=(255, 255, 255, 26))
    img = Image.alpha_composite(img, clip_to_hex(hi))

    lo = new_layer()
    ImageDraw.Draw(lo).rectangle(
        [0, s(top + height * 0.80), CANVAS, CANVAS], fill=(0, 0, 0, 34))
    return Image.alpha_composite(img, clip_to_hex(lo))


def poly(d: ImageDraw.ImageDraw, pts: list[tuple[float, float]], fill) -> None:
    d.polygon([(s(x), s(y)) for x, y in pts], fill=fill)


def disc(d: ImageDraw.ImageDraw, cx: float, cy: float, r: float, fill) -> None:
    d.ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=fill)


def rrect(d: ImageDraw.ImageDraw, x0, y0, x1, y1, r, fill) -> None:
    d.rounded_rectangle([s(x0), s(y0), s(x1), s(y1)], radius=s(r), fill=fill)


def arrow_head(d: ImageDraw.ImageDraw, tip: tuple[float, float], ang_deg: float,
               length: float, half: float, fill) -> None:
    """在 tip 处画一个指向 ang_deg（0=右，顺时针为正，屏幕坐标）的实心三角。"""
    a = math.radians(ang_deg)
    ux, uy = math.cos(a), math.sin(a)
    px, py = -uy, ux
    bx, by = tip[0] - ux * length, tip[1] - uy * length
    poly(d, [tip, (bx + px * half, by + py * half), (bx - px * half, by - py * half)], fill)


# ── 符号：每个都按「以 (0,0) 为中心、±100 归一化」写，再乘 k ──────

def sym_sword(d, cx, cy, kx, ky, fill) -> None:
    poly(d, [(cx, cy - 1.00 * ky),
             (cx + 0.19 * kx, cy - 0.70 * ky),
             (cx + 0.19 * kx, cy + 0.16 * ky),
             (cx - 0.19 * kx, cy + 0.16 * ky),
             (cx - 0.19 * kx, cy - 0.70 * ky)], fill)
    rrect(d, cx - 0.52 * kx, cy + 0.16 * ky, cx + 0.52 * kx, cy + 0.34 * ky, 0.04 * kx, fill)
    rrect(d, cx - 0.13 * kx, cy + 0.34 * ky, cx + 0.13 * kx, cy + 0.64 * ky, 0.05 * kx, fill)
    disc(d, cx, cy + 0.75 * ky, 0.14 * kx, fill)


def shield_pts(cx, cy, w, h) -> list[tuple[float, float]]:
    """「第 1 版」的盾：**上端收成一个尖**（不是平顶），两肩最宽、两侧直下、底边收成尖。

    这是概念板里那一版的口径，`w`/`h` 是半宽与半高，比例 w/h ≈ 0.767。
    平顶那版（v2 渲染稿）在小尺寸下像一块「碑」，尖顶更像纹章，也和「徽章」这个骨架更搭。
    """
    apex = (cx, cy - h)
    sh_r = (cx + w, cy - 0.667 * h)
    side_r = (cx + w, cy - 0.067 * h)
    ctrl = (cx + w, cy + 0.567 * h)
    tip = (cx, cy + h)

    pts = [apex, sh_r, side_r]
    for i in range(1, 13):
        t = i / 12
        pts.append((
            (1 - t) ** 2 * side_r[0] + 2 * (1 - t) * t * ctrl[0] + t ** 2 * tip[0],
            (1 - t) ** 2 * side_r[1] + 2 * (1 - t) * t * ctrl[1] + t ** 2 * tip[1],
        ))

    side_l = (cx - w, cy - 0.067 * h)
    ctrl_l = (cx - w, cy + 0.567 * h)
    for i in range(1, 13):
        t = i / 12
        pts.append((
            (1 - t) ** 2 * tip[0] + 2 * (1 - t) * t * ctrl_l[0] + t ** 2 * side_l[0],
            (1 - t) ** 2 * tip[1] + 2 * (1 - t) * t * ctrl_l[1] + t ** 2 * side_l[1],
        ))

    pts.append((cx - w, cy - 0.667 * h))
    return pts


def sym_shield(d, cx, cy, w, h, fill) -> None:
    poly(d, shield_pts(cx, cy, w, h), fill)


def sym_bolt(d, cx, cy, k, fill) -> None:
    poly(d, [(cx + 0.10 * k, cy - 1.00 * k),
             (cx - 0.62 * k, cy + 0.06 * k),
             (cx - 0.16 * k, cy + 0.06 * k),
             (cx - 0.08 * k, cy + 1.00 * k),
             (cx + 0.62 * k, cy - 0.10 * k),
             (cx + 0.16 * k, cy - 0.10 * k)], fill)


def sym_refill(d, cx, cy, r, stroke, head, fill) -> None:
    """回转箭头：缺口在正上方，箭头落在缺口里 —— 取「第 1 版」那套比例（环更满、笔画更粗）。"""
    d.arc([s(cx - r), s(cy - r), s(cx + r), s(cy + r)],
          start=-60, end=240, fill=fill, width=int(round(s(stroke))))

    a = math.radians(240)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    dir_deg = 240 + 90
    u = math.radians(dir_deg)
    # 底边往环里多压 12，接缝处不会留一道绿楔子
    tip = (ex + head * math.cos(u), ey + head * math.sin(u))
    arrow_head(d, tip, dir_deg, head, head * 0.45, fill)


def sym_diamond(d, cx, cy, k, fill) -> None:
    poly(d, [(cx, cy - 0.92 * k), (cx + 0.52 * k, cy), (cx, cy + 0.92 * k), (cx - 0.52 * k, cy)], fill)


# ── 八枚图标 ────────────────────────────────────────────────

def icon_atk() -> Image.Image:
    img = badge_base(FAMILY["atk"])
    d = ImageDraw.Draw(img)
    sym_sword(d, HEX_CX, HEX_CY + 6, 168, 168, GLYPH)
    return img


def icon_def() -> Image.Image:
    img = badge_base(FAMILY["def"])
    d = ImageDraw.Draw(img)
    sym_shield(d, HEX_CX, HEX_CY, 114, 155, GLYPH)
    return img


def icon_both() -> Image.Image:
    """左剑右盾，中间一道贯穿的接缝 —— 一枚徽章，两种用法，二选一。

    接缝不是叠一条线，而是把徽章**劈成两块**：右半压暗一档 + 中间一条 INK 缝，
    缝的上下两端正好落在六边形的上下边上（所以看起来是「拼起来的两半」，
    不是「一块牌上画了一道杠」）。缩到 40 px 时靠的就是这个二分结构在撑。
    """
    fill = FAMILY["both"]
    img = badge_base(fill)

    darker = tuple(int(c * 0.80) for c in fill)
    right = new_layer()
    ImageDraw.Draw(right).rectangle([s(SEAM_R), 0, CANVAS, CANVAS], fill=darker + (255,))
    img = Image.alpha_composite(img, clip_to_hex(right))

    seam = new_layer()
    ImageDraw.Draw(seam).rectangle(
        [s(SEAM_L), s(HEX_CY - 200), s(SEAM_R), s(HEX_CY + 200)], fill=INK + (255,))
    img = Image.alpha_composite(img, clip_to_hex(seam))

    d = ImageDraw.Draw(img)
    sym_sword(d, 164, HEX_CY, 106, 124, GLYPH)
    sym_shield(d, 318, HEX_CY, 60, 85, GLYPH)
    return img


def icon_combo() -> Image.Image:
    img = badge_base(FAMILY["combo"])
    d = ImageDraw.Draw(img)
    sym_bolt(d, HEX_CX, HEX_CY + 2, 168, GLYPH)
    return img


def icon_immune(up: bool) -> Image.Image:
    img = badge_base(FAMILY["immune"])
    d = ImageDraw.Draw(img)
    sym_shield(d, HEX_CX, HEX_CY, 114, 155, GLYPH)
    if up:
        poly(d, [(HEX_CX, HEX_CY - 52), (HEX_CX + 52, HEX_CY + 34),
                 (HEX_CX - 52, HEX_CY + 34)], FAMILY["immune"])
    else:
        poly(d, [(HEX_CX, HEX_CY + 68), (HEX_CX + 52, HEX_CY - 18),
                 (HEX_CX - 52, HEX_CY - 18)], FAMILY["immune"])
    return img


def icon_refill() -> Image.Image:
    img = badge_base(FAMILY["refill"])
    d = ImageDraw.Draw(img)
    sym_refill(d, HEX_CX, HEX_CY, 123, 37, 54, GLYPH)
    return img


def icon_sigil() -> Image.Image:
    img = badge_base(FAMILY["sigil"])
    d = ImageDraw.Draw(img)
    k = 150
    sym_diamond(d, HEX_CX, HEX_CY, k, GLYPH)
    for dx, dy in ((-0.44, -0.52), (0.44, -0.52), (0.0, 0.66)):
        disc(d, HEX_CX + dx * k, HEX_CY + dy * k, 0.055 * k, GLYPH)
    return img


BUILDERS = {
    "Aura_01_atk.png": icon_atk,
    "Aura_02_def.png": icon_def,
    "Aura_03_both.png": icon_both,
    "Aura_04_combo.png": icon_combo,
    "Aura_05_immunehigh.png": lambda: icon_immune(True),
    "Aura_06_immunelow.png": lambda: icon_immune(False),
    "Aura_07_refill.png": icon_refill,
    "Aura_08_sigil.png": icon_sigil,
}


def export(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.resize((FINAL, FINAL), Image.LANCZOS).save(path, "PNG", optimize=True)


# ── 核对图 ──────────────────────────────────────────────────

def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "Google-Bold.ttf" if bold else "Google-Regular.ttf"
    p = PROJECT / "Assets" / "Art" / "Fonts" / "Black" / name
    if p.exists():
        return ImageFont.truetype(str(p), size)
    return ImageFont.truetype(str(PROJECT / "Assets" / "Art" / "Fonts" / "Black" / "Google-Regular.ttf"), size)


def review(icons: list[tuple[str, Image.Image]]) -> None:
    BG = (0xF4, 0xF6, 0xF8)
    DARK = (0x26, 0x2F, 0x3F)
    TEXT = (0x1F, 0x29, 0x37)
    MUTED = (0x6B, 0x72, 0x80)

    cell = 158
    W = cell * 4 + 60
    H = 860
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)

    d.text((30, 22), "光环图标 v2 · 实机尺寸核对", font=font(24, True), fill=TEXT)
    d.text((30, 56), "每格：144 px ｜ 72 px（实机，含数值条）｜ 40 px 灰阶压力测试",
           font=font(15), fill=MUTED)

    LABELS = ["进攻力量", "防御力量", "攻防二选一", "连击", "免疫 ≥A", "免疫 ≤A", "快速回填", "刻印"]
    VALUES = ["+2", "+2", "+3", "≤6", "≥6", "≤3", "+1", ""]

    for i, ((name, im), label, val) in enumerate(zip(icons, LABELS, VALUES)):
        col, row = i % 4, i // 4
        cx = 30 + col * cell + cell // 2
        cy = 100 + row * 330

        big = im.resize((144, 144), Image.LANCZOS)
        img.paste(big, (cx - 72, cy), big)

        real = im.resize((72, 72), Image.LANCZOS)
        bx, by = cx - 36, cy + 160
        img.paste(real, (bx, by), real)
        if val:
            d.rounded_rectangle([cx - 28, by + 44, cx + 28, by + 68],
                                radius=8, fill=(0x12, 0x16, 0x1E))
            tw = d.textlength(val, font=font(20, True))
            d.text((cx - tw / 2, by + 46), val, font=font(20, True), fill=(0xFF, 0xE7, 0xB0))

        grey = im.convert("LA").convert("RGBA")
        small = grey.resize((40, 40), Image.LANCZOS)
        img.paste(small, (cx - 20, by + 84), small)

        tw = d.textlength(label, font=font(16, True))
        d.text((cx - tw / 2, by + 134), label, font=font(16, True), fill=TEXT)

    d.text((30, 760), "贴深色冷却区（44 px）：", font=font(15), fill=MUTED)
    d.rounded_rectangle([30, 786, W - 30, 838], radius=10, fill=DARK)
    for i, (name, im) in enumerate(icons):
        small = im.resize((44, 44), Image.LANCZOS)
        img.paste(small, (54 + i * 62, 790), small)

    REVIEW_DIR.mkdir(parents=True, exist_ok=True)
    img.save(REVIEW_DIR / "AuraIcons_Review.png", "PNG")


def main() -> int:
    icons = []
    for fname, key in FILES:
        img = BUILDERS[fname]()
        export(img, OUT_DIR / fname)
        icons.append((fname, img))
        print("ok", fname)
    review(icons)
    print("review ->", REVIEW_DIR / "AuraIcons_Review.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
