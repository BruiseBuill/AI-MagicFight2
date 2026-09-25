# -*- coding: utf-8 -*-
"""
UI 元素图集切分器
==================
把 `542f45c2-….png`（= 参考图去掉背景与人物之后的整层 UI）切成独立 UI 元素 Sprite，
输出到 `Assets/Art/Ui/`。

## 关键认识：图集是「整层」不是「元素拼版」

顶栏上的名字 / 血量 / 金币数字、冷却槽上的数字与「冷却区N」标签、能量球上的 `1/1`，
**全都是烘焙在图里的**。于是处理口径分两类：

| 情况 | 例子 | 做法 |
|---|---|---|
| 烘焙文字**每行固定**、永远正确 | 冷却槽的 `4` + `冷却区4` | 直接切出来用，**不擦** |
| 烘焙文字是**动态值** | 顶栏 `旅者/铁卫/72/80/125`、能量球 `1/1` | 擦掉 → 交给 TMP 重排 |

## 擦除：分三区，各挑最优方向

面板是「横向大致均匀、纵向有渐变」的暗色条。三个文字区的可取样环境并不一样，
所以**每个区单独选方向**（`grow_axis`）：

| 区 | 左右邻居 | 上下邻居 | 采用 |
|---|---|---|---|
| 名字 / 称号 | 左=头像、右=长条面板 | 上=条顶高光边（**不能用**） | **横向**，取样只认右侧面板 |
| 血量数字 | 左=❤、右=长条面板 | — | **横向**，取样只认右侧面板 |
| 资源数字 | 左=💰、右=长条面板 | — | **横向**，取样只认右侧面板 |

三个必须踩过的坑（都真踩了）：

1. **不能只擦「亮像素」** —— 文字带深色描边，比面板还暗，亮阈值抓不到，
   回填后原地留下字形黑影。做法是先亮阈找字形 → 取包围盒外扩 → 整块挖。
2. **不能按「最近的非掩码像素」取样** —— 血量数字紧贴红心、资源数字紧贴钱袋，
   最近邻会直接取到图标色，回填后留一道红 / 黄拖影。必须限定取样区间。
3. **不能漏掉「数字背后的辉光」** —— 血量数字背后烘了一层红色辉光，
   它比面板亮不了多少但**饱和度很高**，只按亮度判定会漏，
   于是擦完数字还留一道红色横条。判定要加上「不像面板」。

## 其它约定

- **不缩放、共用同一画布**，与 `slice_icons.py` 同口径。
- 原图集保留在 `Ui/_Source/` 下作为唯一事实来源。
- 参考图里那 5 张手牌样式的卡面**不切** —— `Docs/06` 定了卡牌用本项目自己的
  `Assets/Art/Cards/` 那 40 张成品卡面。

用法:
    python slice_ui_kit.py            # 预览：打印包围盒 + 出擦除前后对照图
    python slice_ui_kit.py --apply    # 执行切分
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"
SHEET = ART / "Ui" / "_Source" / "542f45c2-5d2c-4803-bcbd-2cafa10226fc.png"
OUT = ART / "Ui"
WORK = PROJECT / "Artifacts" / "work" / "art"

PAD = 2
ALPHA_MIN = 16

# ── 元素表：(输出名, 源矩形 x,y,w,h) ──────────────────────────────────
# 坐标来自 analyze_new_art.py 的 alpha 连通域，并逐块肉眼核对过
# （标注图 `_work/ui_atlas_annotated.png`）。
ELEMENTS: list[tuple[str, tuple[int, int, int, int]]] = [
    # ── 顶栏（423×91 @ 17,15）───────────────────────────────
    ("Hud_Bar",       (17, 15, 704, 91)),      # 擦数字版（成品）
    ("Hud_Bar_Raw",   (17, 15, 704, 91)),      # 原样（对照 / 留档）
    ("Hud_Avatar",    (17, 15, 112, 91)),      # 头像（含框，单独摆放用）

    # ── 状态图标行（5 个）────────────────────────────────────
    ("Buff_01_Fire",       (44, 131, 50, 55)),
    ("Buff_02_RingEmpty",  (115, 134, 59, 52)),
    ("Buff_03_Potion",     (190, 129, 49, 61)),
    ("Buff_04_RuneBoost",  (295, 150, 71, 69)),
    ("Buff_05_BladeStack", (448, 144, 70, 68)),

    # ── 冷却槽 · 左（己方）──────────────────────────────────
    ("SlotL_CD4", (25, 223, 186, 91)),
    ("SlotL_CD3", (25, 322, 187, 92)),
    ("SlotL_CD2", (24, 421, 189, 92)),
    ("SlotL_CD1", (25, 521, 188, 96)),

    # ── 冷却槽 · 右（敌方）──────────────────────────────────
    ("SlotR_CD4", (1336, 236, 178, 80)),
    ("SlotR_CD3", (1336, 332, 178, 80)),
    ("SlotR_CD2", (1335, 427, 179, 80)),
    ("SlotR_CD1", (1294, 519, 220, 82)),

    # ── 能量球 ─────────────────────────────────────────────
    ("Orb_Energy",     (65, 719, 162, 173)),   # 擦数字版（成品）
    ("Orb_Energy_Raw", (65, 719, 162, 173)),   # 原样
]

# ══ 顶栏擦除区 ════════════════════════════════════════════════════════
# 实测亮块（Hud_Bar **局部**坐标，`probe` 报的就是这些数）：
#   头像 34..99 · 旅者 118..171 & 铁卫 118..155 · ❤ 227..265
#   72/80 276..339 · 💰 391..423 · 125 438..477 · ⚙ 617..662
HUD_Y = (12, 78)                 # 条身内部安全区（上下留出可取样的面板带）
HUD_INK_LUM = 105                # 面板亮度中位数 35、p75 仅 40 → 105 干净切出笔画
HUD_GLYPH_PAD = 5                # 字形包围盒外扩量（描边在字形外 1–4 px）

# (x0, x1, 方向, 取样区间)  方向 'v' = 纵向插值 / 'h' = 横向插值
HUD_REGIONS: list[tuple[tuple[int, int], str, tuple[int, int] | None]] = [
    ((112, 178), 'h', (180, 224)),  # 名字「旅者」+ 称号「铁卫」→ 右邻那段面板
    ((267, 350), 'h', (352, 388)),  # 血量「72/80」+ 背后红辉光 → 只从右侧面板取样
    ((432, 484), 'h', (487, 614)),  # 资源「125」            → 只从右侧面板取样
]

# ══ 冷却槽擦除区 ══════════════════════════════════════════════════════
# 右列「冷却区1」那张槽图里**烘了一张敌方卡面**（截图验收时发现的）：
# 槽里明明是空的，图上也永远画着一张红卡 —— 属于「烘焙了动态内容」。
# 迷你卡本来就由 UI 摆进去，所以把这张烘焙卡面擦成空槽。
# 位置是实测的卡面格（224×86 那张：卡面在 x 19..53, y 14..67）。
SLOT_ERASE = {
    "SlotR_CD1": (14, 9, 46, 66),      # 局部 x,y,w,h
}

# ══ 能量球擦除区 ══════════════════════════════════════════════════════
ORB_BOX = (36, 44, 110, 80)      # 局部 x,y,w,h
ORB_WHITE_SAT = 34               # max-min <= 34 视为「灰白」
ORB_WHITE_LUM = 175


# ── 基础工具 ─────────────────────────────────────────────────────────

def load_sheet() -> Image.Image:
    return Image.open(SHEET).convert("RGBA")


def crop_sprite(sheet: Image.Image, rect, pad: int = PAD) -> Image.Image:
    x, y, w, h = rect
    box = (max(0, x - pad), max(0, y - pad),
           min(sheet.width, x + w + pad), min(sheet.height, y + h + pad))
    return sheet.crop(box)


def dilate(mask: np.ndarray, r: int) -> np.ndarray:
    from scipy import ndimage
    if r <= 0:
        return mask
    return ndimage.binary_dilation(mask, np.ones((r * 2 + 1, r * 2 + 1)))


def panel_like(pil: Image.Image) -> np.ndarray:
    """「像条底面板」的像素：不透明、低饱和、落在面板那段暗亮度里。"""
    a = np.asarray(pil)
    al = a[:, :, 3].astype(np.int16)
    rgb = a[:, :, :3].astype(np.int16)
    lum = rgb.max(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    return (al > 110) & (sat <= 30) & (lum >= 18) & (lum <= 95)


def segments(flags: np.ndarray) -> list[tuple[int, int]]:
    """把一维布尔数组切成连续 True 区间。"""
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


def fill_axis(pil: Image.Image, mask: np.ndarray, axis: str,
              span: tuple[int, int] | None = None,
              band: int = 4, reach: int = 30) -> Image.Image:
    """
    沿指定轴对 mask 区域做线性插值回填。

    - <c>axis='v'</c>：逐列，用同列的上下邻近像素插值 → 保留横向渐变
    - <c>axis='h'</c>：逐行，用同行的左右邻近像素插值 → 保留纵向渐变
    - <c>span</c>：限定取样坐标区间（横向时是 x 区间，纵向时是 y 区间）。
      给了它就**不再退化成「最近的非掩码像素」** —— 那个退化会吃到图标颜色。
    """
    a = np.asarray(pil).astype(np.float32)
    out = a.copy()
    H, W = mask.shape
    ok = panel_like(pil) & ~mask
    if span is not None:
        lim = np.zeros_like(ok)
        if axis == 'h':
            lim[:, span[0]:span[1] + 1] = True
        else:
            lim[span[0]:span[1] + 1, :] = True
        ok = ok & lim

    n_iter = W if axis == 'v' else H      # 要扫多少条线
    bound = H if axis == 'v' else W       # 每条线上的坐标上限
    for i in range(n_iter):
        line_mask = mask[:, i] if axis == 'v' else mask[i, :]
        ok_line = ok[:, i] if axis == 'v' else ok[i, :]
        if not line_mask.any():
            continue
        for (p0, p1) in segments(line_mask):
            side = []
            for (s0, s1, rev) in ((p0 - 1, max(-1, p0 - 1 - reach), True),
                                  (p1 + 1, min(bound, p1 + 1 + reach), False)):
                rng = range(s0, s1, -1) if rev else range(s0, s1)
                pool = [c for c in rng if 0 <= c < bound and ok_line[c]]
                if not pool and span is None:
                    pool = [c for c in rng if 0 <= c < bound and not line_mask[c]]
                if not pool:
                    side.append(None)
                elif axis == 'v':
                    side.append(a[pool[:band], i, :].mean(axis=0))
                else:
                    side.append(a[i, pool[:band], :].mean(axis=0))
            L, R = side
            if L is None and R is None:
                continue
            if L is None:
                L = R
            if R is None:
                R = L
            n = p1 - p0 + 1
            t = np.linspace(0.0, 1.0, n + 2)[1:-1].reshape(n, 1)
            fill = L[None, :] * (1.0 - t) + R[None, :] * t
            if axis == 'v':
                out[p0:p1 + 1, i, :] = fill
            else:
                out[i, p0:p1 + 1, :] = fill

    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def inpaint(pil: Image.Image, mask: np.ndarray, iters: int = 400) -> Image.Image:
    """扩散补洞：适用于曲面 / 渐变（能量球的宝石面），比线性插值平滑。"""
    from scipy import ndimage

    a = np.asarray(pil).astype(np.float32)
    m = mask.copy()
    if not m.any():
        return pil

    known = ~m
    for _ in range(iters):
        s = np.zeros_like(a)
        w = np.zeros(a.shape[:2], np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                sh = np.roll(np.roll(a, dy, axis=0), dx, axis=1)
                ksh = np.roll(np.roll(known, dy, axis=0), dx, axis=1)
                s += np.where(ksh[:, :, None], sh, 0.0)
                w += ksh.astype(np.float32)
        avg = s / np.maximum(w[:, :, None], 1e-6)
        upd = m & (w > 0)
        a[upd] = avg[upd]
        known = known | upd
        if not (m & ~known).any():
            for _ in range(24):          # 再平滑几轮，消掉菱形纹
                s = np.zeros_like(a)
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        s += np.roll(np.roll(a, dy, axis=0), dx, axis=1)
                a[m] = (s / 9.0)[m]
            break
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


# ── 顶栏擦除 ─────────────────────────────────────────────────────────

def hud_region_masks(sprite: Image.Image) -> list[tuple[np.ndarray, str, tuple | None]]:
    """
    逐区造掩码：亮笔画 **或**「不像面板」的像素（后者用来吃数字背后的高饱和辉光），
    再取连通域包围盒外扩 —— 这样深色描边也一并圈进去。
    """
    from scipy import ndimage

    a = np.asarray(sprite)
    al = a[:, :, 3].astype(np.int16)
    lum = a[:, :, :3].max(axis=2).astype(np.int16)
    pl = panel_like(sprite)
    y0, y1 = HUD_Y
    result = []

    for ((x0, x1), axis, span) in HUD_REGIONS:
        win = np.zeros(al.shape, bool)
        win[y0:y1 + 1, x0:x1 + 1] = True
        ink = win & (al > 100) & ((lum > HUD_INK_LUM) | ~pl)
        m = np.zeros(al.shape, bool)
        if ink.any():
            ink = dilate(ink, 3)
            lab, n = ndimage.label(ink, structure=np.ones((3, 3)))
            for i, sl in enumerate(ndimage.find_objects(lab), start=1):
                if sl is None or int((lab[sl] == i).sum()) < 80:
                    continue      # 丢掉面板零星亮斑，别为它擦掉一整条
                ys, xs = sl
                m[max(y0, int(ys.start) - HUD_GLYPH_PAD):
                  min(y1, int(ys.stop) - 1 + HUD_GLYPH_PAD) + 1,
                  max(x0, int(xs.start) - HUD_GLYPH_PAD):
                  min(x1, int(xs.stop) - 1 + HUD_GLYPH_PAD) + 1] = True
        result.append((m, axis, span))
    return result


def erase_hud(sprite: Image.Image) -> Image.Image:
    for (m, axis, span) in hud_region_masks(sprite):
        if m.any():
            sprite = fill_axis(sprite, m, axis, span)
    return sprite


def orb_erase_mask(sprite: Image.Image) -> np.ndarray:
    a = np.asarray(sprite)
    al = a[:, :, 3].astype(np.int16)
    rgb = a[:, :, :3].astype(np.int16)
    lum = rgb.max(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    box = np.zeros(al.shape, bool)
    bx, by, bw, bh = ORB_BOX
    box[by:by + bh, bx:bx + bw] = True
    return dilate(box & (al > 100) & (lum > ORB_WHITE_LUM) & (sat <= ORB_WHITE_SAT), 4)


# ── 入口 ─────────────────────────────────────────────────────────────

def probe() -> None:
    sheet = load_sheet()
    WORK.mkdir(parents=True, exist_ok=True)

    print(f"源图集 {SHEET.name} {sheet.size}\n元素表:")
    for name, rect in ELEMENTS:
        x, y, w, h = rect
        print(f"  {name:20s} 图集({x:>4},{y:>3}) {w:>4}x{h:<4}")

    hud = crop_sprite(sheet, ELEMENTS[0][1])
    print("\n顶栏擦除区:")
    for ((x0, x1), axis, span), (m, _, _) in zip(HUD_REGIONS, hud_region_masks(hud)):
        nzr = np.where(m.any(axis=1))[0]
        nzc = np.where(m.any(axis=0))[0]
        info = (f"x {nzc.min()}..{nzc.max()}  y {nzr.min()}..{nzr.max()}"
                if len(nzr) else "空")
        print(f"  x{x0}..{x1}  {axis}  span={span}  → {m.sum():>5} px  {info}")
    hud_fix = erase_hud(hud)

    orb = crop_sprite(sheet, ELEMENTS[-2][1])
    m2 = orb_erase_mask(orb)
    nzr = np.where(m2.any(axis=1))[0]
    nzc = np.where(m2.any(axis=0))[0]
    print(f"\n能量球擦除：{m2.sum()} px"
          + (f"  bbox 局部 x {nzc.min()}..{nzc.max()} y {nzr.min()}..{nzr.max()}"
             if len(nzr) else ""))
    orb_fix = inpaint(orb, m2)

    hud_cmp = Image.new("RGBA", (hud.width, hud.height * 2 + 16), (30, 30, 36, 255))
    hud_cmp.alpha_composite(hud, (0, 0))
    hud_cmp.alpha_composite(hud_fix, (0, hud.height + 16))
    hud_cmp.resize((hud_cmp.width * 2, hud_cmp.height * 2), Image.NEAREST) \
           .save(WORK / "ui_hud_erase.png")
    hud_fix.save(WORK / "ui_hud_fixed_1x.png")

    orb_cmp = Image.new("RGBA", (orb.width * 2 + 20, orb.height), (30, 30, 36, 255))
    orb_cmp.alpha_composite(orb, (0, 0))
    orb_cmp.alpha_composite(orb_fix, (orb.width + 20, 0))
    orb_cmp.resize((orb_cmp.width * 2, orb_cmp.height * 2), Image.NEAREST) \
           .save(WORK / "ui_orb_erase.png")

    print(f"\n对照图 → {WORK / 'ui_hud_erase.png'}（上=原 下=擦后，2x）")
    print(f"         → {WORK / 'ui_hud_fixed_1x.png'}（擦后，1:1）")
    print(f"         → {WORK / 'ui_orb_erase.png'}（左=原 右=擦后）")

    ann = sheet.copy()
    d = ImageDraw.Draw(ann)
    for i, (name, (bx, by, bw, bh)) in enumerate(ELEMENTS):
        d.rectangle([bx, by, bx + bw - 1, by + bh - 1], outline=(0, 255, 128, 255), width=2)
        d.text((bx + 3, by + 3), f"{i} {name}", fill=(255, 64, 64, 255))
    ox, oy = 17 - PAD, 15 - PAD
    for ((x0, x1), _axis, _span) in HUD_REGIONS:
        d.rectangle([ox + x0, oy + HUD_Y[0], ox + x1, oy + HUD_Y[1]],
                    outline=(255, 220, 0, 255), width=2)
    ann.save(WORK / "ui_atlas_annotated.png")
    print(f"         → {WORK / 'ui_atlas_annotated.png'}（元素框 + 擦除窗口）")


def apply() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    sheet = load_sheet()

    for name, rect in ELEMENTS:
        sp = crop_sprite(sheet, rect)
        if name == "Hud_Bar":
            sp = erase_hud(sp)
        elif name == "Orb_Energy":
            sp = inpaint(sp, orb_erase_mask(sp))
        elif name in SLOT_ERASE:
            sp.save(OUT / f"{name}_Raw.png")        # 原样留一份
            x, y, w, h = SLOT_ERASE[name]
            hol = np.zeros((sp.height, sp.width), bool)
            hol[y:y + h, x:x + w] = True
            sp = inpaint(sp, hol, iters=600)
        sp.save(OUT / f"{name}.png")
        print(f"  {name:20s} {sp.size[0]:>4}x{sp.size[1]:<4}")

    # 源图集**不在这里**归档：搬文件交给 Unity 侧的 `ArtImportBuilder`
    # （它走 AssetDatabase.MoveAsset 保 GUID）。Python 只产成品，不碰源图。
    print(f"\n成品目录 → {OUT}")
    print("源图集留档：跑 Unity 菜单 `魔法乱斗/整理 · 配置新美术导入` 自动搬进 Ui/_Source/")


if __name__ == "__main__":
    if "--apply" in sys.argv:
        apply()
    else:
        probe()
