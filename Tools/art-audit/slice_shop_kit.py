# -*- coding: utf-8 -*-
"""
商店 UI 套件切分器（2026-09-26）
================================
把 `Art/d30f41c7-…png`（用户按 ReferenceShop 风格出的商店 UI 整层图）切成
可用的 UI 元素 Sprite，输出到 `Assets/Art/Ui/`。

## 这张图上有什么

| 区域 | 图集坐标 | 处理 |
|---|---|---|
| 巫师角色（左） | x 72..452, y 128..882 | **不切** —— 本作商店不摆 NPC 立绘（口径：只做货架 + 资源栏） |
| 4 张示例卡面 | x 482..1567, y 128..568 | **不切** —— 那些是别人的卡；本作用 `CardView_Hand.prefab` 装自己的卡 |
| 4 张价格牌（金币 + 数字） | x 488..1563, y 583..672 | 切 1 张，**擦掉数字**（动态：20 / 10），保留金币 |
| 「离开」按钮 | x 1097..1466, y 742..872 | 切成按钮底图 |
| 背包图标 | x 1487..1626, y 742..872 | 切出来备用（本版式暂不放） |

## 为什么价格牌要擦数字

牌面上的 `80 / 60 / 70` 是**动态值**（本作是 20 原价 / 10 特价），
和 `slice_ui_kit.py` 擦顶栏 `72/80/125` 是同一类处理：
擦掉 → 交给 TMP 按同一位置重排。金币图标是**固定装饰**，留着。

用法:
    python slice_shop_kit.py            # 预览：打印坐标 + 出标注图与擦除对照图
    python slice_shop_kit.py --apply    # 执行切分
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"
SHEET = ART / "d30f41c7-1d35-43f6-9755-ca54cc6f4a6a.png"
OUT = ART / "Ui"
WORK = PROJECT / "Artifacts" / "work" / "art"

PAD = 2
ALPHA_MIN = 16

# ── 元素表：(输出名, 源矩形 x,y,w,h) ──────────────────────────────────
# 坐标来自 alpha 逐行/逐列分段（见本脚本 probe 输出），逐块肉眼核对过。
ELEMENTS: list[tuple[str, tuple[int, int, int, int]]] = [
    # 价格牌：取左起第 2 张（x 764..995），形状与其它三张一致
    ("Shop_PricePlate", (764, 583, 232, 90)),
    # 「离开」按钮底图（带菱形装饰的木质六边形）
    ("Shop_LeaveButton", (1097, 742, 370, 131)),
    # 背包图标（备用，本版式暂不摆）
    ("Shop_BagIcon", (1487, 742, 140, 131)),
]

# ══ 价格牌擦数字区 ════════════════════════════════════════════════════
# 实测：金币在局部 x 55..105，数字在局部 x 126..176、y 20..66。
# 数字背后没有辉光（暗棕牌面），所以只需擦「亮白笔画」再横向插值。
PLATE_TEXT_X = (120, 190)      # 局部 x 区间（含外扩）
PLATE_Y = (14, 74)             # 牌身内安全区
PLATE_INK_LUM = 175            # 数字亮度 > 175（牌面中位亮度约 50）
PLATE_GLYPH_PAD = 5


# ── 基础工具 ─────────────────────────────────────────────────────────

def load_sheet() -> Image.Image:
    return Image.open(SHEET).convert("RGBA")


def crop_sprite(sheet: Image.Image, rect, pad: int = PAD) -> Image.Image:
    x, y, w, h = rect
    box = (max(0, x - pad), max(0, y - pad),
           min(sheet.width, x + w + pad), min(sheet.height, y + h + pad))
    return sheet.crop(box)


def dilate(mask: np.ndarray, r: int) -> np.ndarray:
    """方形膨胀（纯 numpy，不依赖 scipy）。"""
    if r <= 0:
        return mask
    out = mask.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out |= np.roll(np.roll(mask, dy, axis=0), dx, axis=1)
    return out


def label_components(mask: np.ndarray) -> tuple[np.ndarray, int]:
    """
    8 连通域标记（纯 numpy 迭代膨胀，不依赖 scipy.ndimage）。

    迭代次数 = 连通域直径，这里图很小（价格牌 232×90），收敛很快。
    """
    lab = np.zeros(mask.shape, np.int32)
    cur = 0
    remain = mask.copy()
    while remain.any():
        cur += 1
        ys, xs = np.where(remain)
        seed = np.zeros(mask.shape, bool)
        seed[ys[0], xs[0]] = True
        while True:
            grown = dilate(seed, 1) & mask & (lab == 0)
            if grown.sum() == seed.sum():
                break
            seed = grown
        lab[seed] = cur
        remain = mask & (lab == 0)
    return lab, cur


def bbox_of(lab: np.ndarray, index: int) -> tuple[int, int, int, int] | None:
    ys, xs = np.where(lab == index)
    if len(ys) == 0:
        return None
    return int(ys.min()), int(ys.max()), int(xs.min()), int(xs.max())


def segments(flags: np.ndarray) -> list[tuple[int, int]]:
    idx = np.where(flags)[0]
    if len(idx) == 0:
        return []
    out, s, p = [], idx[0], idx[0]
    for v in idx[1:]:
        if v != p + 1:
            out.append((s, p))
            s = v
        p = v
    out.append((s, p))
    return out


def fill_axis_h(pil: Image.Image, mask: np.ndarray, reach: int = 40) -> Image.Image:
    """逐行用左右邻近的**牌面像素**做线性插值回填（保留纵向渐变）。"""
    a = np.asarray(pil).astype(np.float32)
    out = a.copy()
    H, W = mask.shape
    rgb = a[:, :, :3]
    al = a[:, :, 3]
    lum = rgb.max(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    # 「像牌面」= 不透明、暗、低饱和
    plate = (al > 150) & (lum <= 120) & (sat <= 60)
    ok = plate & ~mask

    for i in range(H):
        lm = mask[i, :]
        if not lm.any():
            continue
        ok_line = ok[i, :]
        for (p0, p1) in segments(lm):
            pool_l = [c for c in range(p0 - 1, max(-1, p0 - 1 - reach), -1) if ok_line[c]]
            pool_r = [c for c in range(p1 + 1, min(W, p1 + 1 + reach)) if ok_line[c]]
            L = a[i, pool_l[:4], :].mean(axis=0) if pool_l else None
            R = a[i, pool_r[:4], :].mean(axis=0) if pool_r else None
            if L is None and R is None:
                continue
            if L is None:
                L = R
            if R is None:
                R = L
            n = p1 - p0 + 1
            t = np.linspace(0.0, 1.0, n + 2)[1:-1].reshape(n, 1)
            out[i, p0:p1 + 1, :] = L[None, :] * (1.0 - t) + R[None, :] * t

    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def plate_text_mask(sprite: Image.Image) -> np.ndarray:
    a = np.asarray(sprite)
    al = a[:, :, 3].astype(np.int16)
    rgb = a[:, :, :3].astype(np.int16)
    lum = rgb.max(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    x0, x1 = PLATE_TEXT_X
    y0, y1 = PLATE_Y
    win = np.zeros(al.shape, bool)
    win[y0:y1 + 1, x0:min(x1, al.shape[1]) + 1] = True
    ink = win & (al > 150) & (lum > PLATE_INK_LUM) & (sat < 60)
    if not ink.any():
        return ink
    ink = dilate(ink, 3)
    lab, n = label_components(ink)
    m = np.zeros(al.shape, bool)
    for i in range(1, n + 1):
        bb = bbox_of(lab, i)
        if bb is None:
            continue
        y0b, y1b, x0b, x1b = bb
        if int((lab[y0b:y1b + 1, x0b:x1b + 1] == i).sum()) < 40:
            continue
        m[max(0, y0b - PLATE_GLYPH_PAD):min(al.shape[0], y1b + 1 + PLATE_GLYPH_PAD),
          max(0, x0b - PLATE_GLYPH_PAD):min(al.shape[1], x1b + 1 + PLATE_GLYPH_PAD)] = True
    return m


# ── 入口 ─────────────────────────────────────────────────────────────

def probe() -> None:
    sheet = load_sheet()
    WORK.mkdir(parents=True, exist_ok=True)

    print(f"源图集 {SHEET.name} {sheet.size}\n元素表:")
    for name, rect in ELEMENTS:
        x, y, w, h = rect
        print(f"  {name:20s} 图集({x:>4},{y:>3}) {w:>4}x{h:<4}")

    plate = crop_sprite(sheet, ELEMENTS[0][1])
    m = plate_text_mask(plate)
    nzr = np.where(m.any(axis=1))[0]
    nzc = np.where(m.any(axis=0))[0]
    print(f"\n价格牌数字擦除：{m.sum()} px"
          + (f"  局部 x {nzc.min()}..{nzc.max()}  y {nzr.min()}..{nzr.max()}" if len(nzr) else "  空"))
    fixed = fill_axis_h(plate, m)

    cmp = Image.new("RGBA", (plate.width, plate.height * 2 + 12), (30, 30, 36, 255))
    cmp.alpha_composite(plate, (0, 0))
    cmp.alpha_composite(fixed, (0, plate.height + 12))
    cmp.resize((cmp.width * 3, cmp.height * 3), Image.NEAREST) \
       .save(WORK / "shop_plate_erase.png")
    print(f"对照图 → {WORK / 'shop_plate_erase.png'}（上=原 下=擦后，3x）")

    ann = sheet.copy()
    d = ImageDraw.Draw(ann)
    for i, (name, (bx, by, bw, bh)) in enumerate(ELEMENTS):
        d.rectangle([bx, by, bx + bw - 1, by + bh - 1], outline=(0, 255, 128, 255), width=3)
        d.text((bx + 6, by + 6), f"{i} {name}", fill=(255, 64, 64, 255))
    ann.save(WORK / "shop_kit_annotated.png")
    print(f"标注图 → {WORK / 'shop_kit_annotated.png'}")


def apply() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    sheet = load_sheet()

    for name, rect in ELEMENTS:
        sp = crop_sprite(sheet, rect)
        if name == "Shop_PricePlate":
            sp = fill_axis_h(sp, plate_text_mask(sp))
        sp.save(OUT / f"{name}.png")
        print(f"  {name:20s} {sp.size[0]:>4}x{sp.size[1]:<4}")

    # 源图集不在 Python 侧归档：交给 Unity 的 `ArtImportBuilder`（走 AssetDatabase.MoveAsset 保 GUID）
    print(f"\n成品目录 → {OUT}")
    print("源图集留档：跑 Unity 菜单 `魔法乱斗/整理 · 配置新美术导入` 自动搬进 Ui/_Source/")


if __name__ == "__main__":
    if "--apply" in sys.argv:
        apply()
    else:
        probe()
