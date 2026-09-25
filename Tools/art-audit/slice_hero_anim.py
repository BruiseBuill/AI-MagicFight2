# -*- coding: utf-8 -*-
"""
主角动画表切分器（第二批素材，2026-09-18；2026-09-21 换待机新图并支持 --only）
==========================================

把 `Assets/Art/` 下的主角动画表切成逐帧独立 Sprite，
输出到 `Assets/Art/Chars/Hero/<动作>/`，并改写 `Chars/anim_frames.txt`。

| 源图 | 内容 | 底色 |
|---|---|---|
| `Idle.png`    1774×887 | 待机 **16 帧（4×4，2026-09-21 换图，旧版 6×3=18 帧作废）** | 白底 254,254,254 |
| `Attack.png`  1774×887 | 攻击 12 帧（4×3） | **已带 Alpha** |
| `defense.png` 1774×887 | 防御 12 帧（4×3） | 白底 |
| `BeHit.png`   1536×1024| 受击 12 帧（4×3） | 白底 |
| `Death.png`   1536×1024| 死亡 12 帧（4×3） | 白底 |

## 与第一批（`slice_char_sheets.py`）的区别

第一批是「AI 参考表」——帧间距不等、光效桥接窄缝，只能按列剖面自动分段。
**这批是规则网格**（每格等宽等高，格内右下角带一个编号标签），所以改用
「先定位网格线 → 再逐格取内容包围盒」的口径，比自动分段稳得多。

## 三个必踩的坑（本脚本的处理）

1. **编号标签会被当成内容切进帧里**。标签在每格底部中央，正好压在脚的下面。
   做法 = 每格内先按实测标签带把标签那几行排除出取框（`LABEL_BAND`），
   切帧时也从内容里剔掉。
2. **白底不能只按「与白的距离」抠** —— 角色脚下烘了一层淡灰投影，距离白色 25 左右，
   会被一起保留，于是锚点（脚底）落到阴影下沿、角色整体上浮。
   做法 = 阴影判据单列一条（低饱和 + 中高亮度），与主体距离取 **max**。
3. **光效（攻击 5–8 帧的能量波、防御的护盾）比身体宽得多**，按包围盒居中的话身体会一帧一跳。
   做法 = 锚点取「竖直跨度最大的那一列」（= 身体），与第一批同口径。

用法:
    python slice_hero_anim.py                       # 预览：打印实测网格 + 出核对图（全部表）
    python slice_hero_anim.py --only idle           # 只处理 idle 一张（换新图批次用）
    python slice_hero_anim.py --apply --only idle   # 执行切分（只落 idle）
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"
OUT = ART / "Chars" / "Hero"
WORK = PROJECT / "Artifacts" / "work" / "art"

# ── 抠底参数（白底批次）─────────────────────────────────────────────
# 距离 = 通道级「离纯白最远的那一档」（Chebyshev），能保住浅蓝护盾/浅色书本，
# 又不会被抗锯齿边缘的淡色骗到。
BG_LO = 10             # 距离 ≤ 此值 → 全透明（纯白背景）
BG_HI = 26             # 距离 ≥ 此值 → 全不透明
SHADOW_SAT = 20        # 饱和度低于此值 …
SHADOW_LUM = 165       # … 且亮度高于此值 → 判为地面投影，直接抠掉
SHADOW_KILL = 0.0
# ⚠ 原来留了 0.55 的残影，想「边缘自然一点」，实际效果是每帧脚底挂着一小条浅灰，
#   而且它把内容底边从「靴底」拉到「投影下沿」，锚点 y 跟着往下飘、
#   角色在自己的画布里整体上浮。界面里角色本来就是悬空摆的，不需要烘焙投影。

# ── 帧清单 ──────────────────────────────────────────────────────────
# (源文件, 动作名, 列数, 行数)
# ⚠ idle 2026-09-21 换图：4×4 = 16 帧（旧 6×3 = 18 帧作废；
#   apply() 会把超出新帧数的旧编号残帧清掉，防止 17/18 帧混进 clip）。
SHEETS = [
    ("Chars/_Source/Hero_Anim_Idle.png", "idle", 4, 4),
    ("Chars/_Source/Hero_Anim_Attack.png", "attack", 4, 3),
    ("Chars/_Source/Hero_Anim_Defend.png", "defend", 4, 3),
    ("Chars/_Source/Hero_Anim_BeHit.png", "behit", 4, 3),
    ("Chars/_Source/Hero_Anim_Death.png", "death", 4, 3),
]

# ── 编号标签的识别口径（实测，不是猜）───────────────────────────────
#
# ⚠ 第一版按「格底 10%」的比例遮标签，结果**非等距行**上被截断了脚底：
#   idle 三行格高 260 / 289 / 338，同样比例裁掉的是 26 / 29 / 34 px，
#   于是三行内容高度变成 186 / 248 / 287 —— 越往下越大，一眼假。
#
# 实测发现标签的真实特征：**它是格内一个独立的行段**，
# 高 12–16 px、宽只占格宽 2–6%（就一个阿拉伯数字），紧贴在内容下方 14 px 左右。
# 而且**标签不一定落在自己那格里** —— 行界切在标签上方时，
# 上一格的标签会掉进下一格的顶部（idle 格 7 的 y271..286 就是格 1 的标签）。
# 所以判据只能按「形状」找，不能按「位置」找。
LABEL_MAX_H = 40           # 行段高 ≤ 此值 …
LABEL_MAX_W_RATIO = 0.25   # … 且宽 ≤ 格宽的 25% → 判为编号标签
SEG_GAP = 4                # 行段之间至少空这么多行才算断开

MARGIN = 6             # 画布四周留白

# 贴边渐隐（用户 2026-09-18 定）：光效一直画到图片边缘的帧，把贴边那一段做成渐隐。
#
# 实测只有一张命中 —— `Attack.png` 第 8 帧的能量波画到了 x=1773（图片宽 1774），
# 被硬切成平口；同一动作的其余帧光波都是完整的圆形收尾，播到这一帧会突兀一下。
# 宽度取 70 px：在 554 px 宽的画布上是 12.6%，动画里一闪而过，看不出是「补的」。
EDGE_FADE = 70


# ── 抠底 ────────────────────────────────────────────────────────────

def is_keyed(path: Path) -> bool:
    """带 Alpha 的源图（Attack.png）直接用它自己的通道，不重抠。"""
    return Image.open(path).mode in ("RGBA", "LA")


def key_out(im: Image.Image) -> np.ndarray:
    """
    白底 → RGBA。返回 float32 数组（0..1 的 alpha + 已反预乘的 RGB）。

    **反预乘**是关键：边缘像素实际是 `α·前景 + (1−α)·白`，
    直接保留观测色会在抠完之后留一圈白晕；按 α 反解前景色才能得到干净边缘。
    """
    rgb = np.asarray(im.convert("RGB")).astype(np.float32)
    dist = np.max(255.0 - rgb, axis=2)          # 距纯白最远的那一档
    alpha = np.clip((dist - BG_LO) / (BG_HI - BG_LO), 0.0, 1.0)

    lum = rgb.max(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    shadow = (sat < SHADOW_SAT) & (lum > SHADOW_LUM)
    alpha = np.where(shadow, alpha * SHADOW_KILL, alpha)

    a = np.clip(alpha, 0.0, 1.0)[:, :, None]
    fg = np.zeros_like(rgb)
    solid = a[:, :, 0] > 1e-4
    fg[solid] = (rgb[solid] - (1.0 - a[solid]) * 255.0) / a[solid]
    fg = np.clip(fg, 0.0, 255.0)
    return np.dstack([fg, a[:, :, 0] * 255.0])


def load_sheet(path: Path) -> Image.Image:
    if is_keyed(path):
        return Image.open(path).convert("RGBA")
    return Image.fromarray(key_out(Image.open(path)).round().astype(np.uint8), "RGBA")


# ── 网格实测 ────────────────────────────────────────────────────────

def project_gaps(mask: np.ndarray, axis: int, min_gap: int) -> list[tuple[int, int]]:
    """返回沿 `axis` 方向「全空」的连续区间（列或行）。"""
    line = mask.any(axis=axis)                  # axis=0 → 逐列；axis=1 → 逐行
    gaps, i, n = [], 0, len(line)
    while i < n:
        if not line[i]:
            j = i
            while j < n and not line[j]:
                j += 1
            if j - i >= min_gap:
                gaps.append((i, j - 1))
            i = j
        else:
            i += 1
    return gaps


def grid_cuts(mask: np.ndarray, cols: int, rows: int) -> tuple[list[int], list[int]]:
    """
    定出网格线。

    规则网格的期望切点是 `round(W/cols)·k`，但生成器不一定严格等距，
    所以先看期望切点附近是不是真的有空缝；是就以空缝中点为准，
    不是（光效把缝填满了）就退回期望值。
    """
    h, w = mask.shape
    vgaps = project_gaps(mask, 0, 2)
    hgaps = project_gaps(mask, 1, 2)

    xs = []
    for k in range(1, cols):
        want = w * k / cols
        hit = [g for g in vgaps if g[0] <= want + 40 and g[1] >= want - 40]
        xs.append(int(round((hit[0][0] + hit[0][1]) / 2.0)) if hit else int(round(want)))

    ys = []
    for k in range(1, rows):
        want = h * k / rows
        hit = [g for g in hgaps if g[0] <= want + 40 and g[1] >= want - 40]
        ys.append(int(round((hit[0][0] + hit[0][1]) / 2.0)) if hit else int(round(want)))

    return xs, ys


def cells_from(w: int, h: int, xs: list[int], ys: list[int]) -> list[tuple[int, int, int, int]]:
    ex = [0] + xs + [w]
    ey = [0] + ys + [h]
    return [(ex[c], ey[r], ex[c + 1], ey[r + 1])
            for r in range(len(ey) - 1) for c in range(len(ex) - 1)]


def row_segments(mask: np.ndarray) -> list[tuple[int, int]]:
    """把掩码按行投影切成若干连续行段（行间隔 > SEG_GAP 才算断开）。"""
    nz = np.where(mask.any(axis=1))[0]
    if len(nz) == 0:
        return []
    segs, start, prev = [], int(nz[0]), int(nz[0])
    for y in nz[1:]:
        if y - prev > SEG_GAP:
            segs.append((start, prev))
            start = y
        prev = y
    segs.append((start, prev))
    return segs


def scan_cells(solid: np.ndarray, cells: list[tuple[int, int, int, int]],
               cols: int) -> list[dict]:
    """逐格扫出「行段 + 每段的列跨度」（全图坐标），供后面的标签投票用。"""
    out = []
    for i, (cx0, cy0, cx1, cy1) in enumerate(cells):
        sub = solid[cy0:cy1, cx0:cx1]
        cw = cx1 - cx0
        segs = []
        for (s, e) in row_segments(sub):
            band = sub[s:e + 1]
            ci = np.where(band.any(axis=0))[0]
            span = int(ci.max() - ci.min()) + 1 if len(ci) else 0
            segs.append({"y0": cy0 + s, "y1": cy0 + e, "span": span, "cw": cw})
        out.append({"idx": i, "row": i // cols, "cx0": cx0, "cy0": cy0,
                    "cx1": cx1, "cy1": cy1, "segs": segs})
    return out


def vote_labels(scanned: list[dict], tol: int = 10) -> set[tuple[int, int, int]]:
    """
    **跨格投票**定出编号标签，而不是看单个段长得像不像。

    ⚠ 为什么必须投票：光效碎片、斗篷尖角也会形成「矮且窄」的行段
    （death 第 6 格的 y448–460 就是一片飞散的披风），单看形状判不出来。
    但编号标签有个独有特征 —— **同一行里每个格都有一枚，且 y 基本一致**（实测 ±5）。
    所以判据 = 「本行至少有另一个格在同一 y 处也有这么一段」。

    返回 {(行号, y0, y1)}。
    """
    by_row: dict[int, list[list[dict]]] = {}
    for cell in scanned:
        by_row.setdefault(cell["row"], []).append(cell["segs"])

    labels: set[tuple[int, int, int]] = set()
    for row, groups in by_row.items():
        for gi, segs in enumerate(groups):
            for s in segs:
                if s["y1"] - s["y0"] + 1 > LABEL_MAX_H or s["span"] > s["cw"] * LABEL_MAX_W_RATIO:
                    continue
                center = (s["y0"] + s["y1"]) / 2.0
                votes = 1
                for gj, other in enumerate(groups):
                    if gj == gi:
                        continue
                    if any(abs((t["y0"] + t["y1"]) / 2.0 - center) <= tol for t in other):
                        votes += 1
                if votes >= 2:
                    labels.add((row, s["y0"], s["y1"]))
    return labels


def own_by_cell(band: np.ndarray, cols: int, ex: list[int]) -> list[np.ndarray]:
    """
    行带内**按连通域整体归位**到某一列，而不是按格硬切。

    ⚠ 为什么必须整体归位：攻击第 7 帧的红披风向左飘过了列界 887（最左到 823）。
    按格硬切的结果是「切得干干净净、编号也对，只是第 6 帧右下角无端多出一块红布」——
    正是 `Docs/06` 里那类**错得很安静**的坑，只有把帧按锚点摆成一条带子才看得见。

    判据用**包围盒中心**而不是像素质心：光效的像素疏密极不均匀，
    质心会被浓的那一侧拖走（实测把这条披风拖到过列界另一侧）。

    没有 scipy 时退回「按格硬切」并在报告里说明 —— 宁可退化成已知的次优，
    也不要因为缺依赖就整批切不出来。
    """
    out = [np.zeros(band.shape, dtype=bool) for _ in range(cols)]
    try:
        from scipy import ndimage
    except ImportError:
        for c in range(cols):
            out[c][:, ex[c]:ex[c + 1]] = band[:, ex[c]:ex[c + 1]]
        return out

    lab, n = ndimage.label(band, structure=np.ones((3, 3)))
    for i in range(1, n + 1):
        m = lab == i
        xs = np.where(m.any(axis=0))[0]
        if len(xs) == 0:
            continue
        cx = (int(xs.min()) + int(xs.max())) / 2.0
        c = int(np.searchsorted(ex, cx, side="right") - 1)
        out[max(0, min(cols - 1, c))] |= m

    # 保险：整体归位后变空的格（连通域被邻居整块拿走），退回本格区域内的原始内容
    for c in range(cols):
        if not out[c].any():
            out[c][:, ex[c]:ex[c + 1]] = band[:, ex[c]:ex[c + 1]]
    return out


def body_height(mask: np.ndarray, bbox: tuple[int, int, int, int],
                ax: int, ay: int) -> int:
    """
    角色本体高（脚底 → 头顶），**在锚点列附近量**而不是用整帧包围盒。

    整帧包围盒含光效（攻击 8 的能量波比角色高出半截），拿它标定显示缩放会把角色越缩越小。
    锚点列 = 身体中心，它那一带的竖直跨度才是人高。
    """
    lo, hi = max(bbox[0], ax - 18), min(bbox[2], ax + 18)
    sub = mask[bbox[1]:ay + 1, lo:hi + 1]
    ys = np.where(sub.any(axis=1))[0]
    return 0 if len(ys) == 0 else int(ys.max() - ys.min()) + 1


def fade_edges(patch: np.ndarray, x0: int, x1: int, y0: int, y1: int,
               img_w: int, img_h: int) -> str:
    """
    内容贴到源图边界的帧，把贴边那一段的 alpha 线性收到 0，让断裂看起来像自然消散。

    只处理**真的贴边**（留 2 px 容差）的方向；没贴边就一个像素都不动，
    免得把「角色自己站在画面边上」也一起削掉。
    """
    hits = []
    h, w = patch.shape[:2]
    ramp = np.linspace(1.0, 0.0, min(EDGE_FADE, w), dtype=np.float32)

    if x1 >= img_w - 2:
        k = ramp.shape[0]
        patch[:, w - k:, 3] *= ramp[None, :]
        hits.append("右")
    if x0 <= 1:
        k = ramp.shape[0]
        patch[:, :k, 3] *= ramp[::-1][None, :]
        hits.append("左")

    ramp_h = np.linspace(1.0, 0.0, min(EDGE_FADE, h), dtype=np.float32)
    if y1 >= img_h - 2:
        k = ramp_h.shape[0]
        patch[h - k:, :, 3] *= ramp_h[:, None]
        hits.append("下")
    if y0 <= 1:
        k = ramp_h.shape[0]
        patch[:k, :, 3] *= ramp_h[::-1][:, None]
        hits.append("上")

    return "/".join(hits)


# ── 锚点 ────────────────────────────────────────────────────────────

def anchor_of(mask: np.ndarray, bbox: tuple[int, int, int, int]) -> tuple[int, int]:
    """
    返回 (锚点 x, 锚点 y)，**格内局部坐标**。

    - **y = 内容底边**（靴底）。
    - **x = 脚底往上 15% 那一带内容的水平中心**。

    ⚠ x 的取法返工过一次。原来用「竖直跨度最大的那一列」（第一批的口径），
    在**纯待机**上没问题，但攻击帧的能量波又高又宽，会跟身体抢这一票 ——
    idle 第 9 / 15 帧的红线直接穿到了右臂外侧，attack 更是每帧都在飘。

    改成量**脚底那一带**：光效永远到不了靴子那么低，脚一定是角色的脚。
    只有当脚底带自己就很宽（死亡末尾的**躺姿** —— 那时这一带里是躯干而非双脚）
    才退回整体包围盒中心。
    """
    x0, y0, x1, y1 = bbox
    w, h = x1 - x0 + 1, y1 - y0 + 1
    band_h = max(6, int(h * 0.15))
    band = mask[y1 - band_h + 1:y1 + 1, x0:x1 + 1]
    ci = np.where(band.any(axis=0))[0]
    if len(ci):
        span = int(ci.max() - ci.min()) + 1
        if span <= w * 0.6:
            return x0 + int(round((ci.min() + ci.max()) / 2.0)), y1
    return x0 + w // 2, y1


# ── 主流程 ──────────────────────────────────────────────────────────

def build() -> tuple[dict, list[str]]:
    plan: dict[str, list[dict]] = {}
    report: list[str] = []

    for fname, anim, cols, rows in SHEETS:
        path = ART / fname
        if not path.exists():
            report.append(f"✘ 缺源图 {path}")
            continue

        rgba = load_sheet(path)
        a = np.asarray(rgba)
        alpha = a[:, :, 3]
        solid = (alpha > 40).astype(np.uint8)
        h, w = solid.shape

        xs, ys = grid_cuts(solid, cols, rows)
        cells = cells_from(w, h, xs, ys)
        ex = [0] + xs + [w]
        row_bounds = [0] + ys + [h]
        report.append(f"\n{anim:7s} {fname}  {w}×{h}  网格 {cols}×{rows}"
                      f"  实测列界 {xs} 行界 {ys}")

        # 1) 跨格投票定出编号标签
        scanned = scan_cells(solid, cells, cols)
        labels = vote_labels(scanned)

        # 2) 把标签像素从掩码里剔掉（标签可能落在邻居格的顶部）
        cleaned = solid.astype(bool).copy()
        for cell in scanned:
            for s in cell["segs"]:
                if (cell["row"], s["y0"], s["y1"]) in labels:
                    cleaned[s["y0"]:s["y1"] + 1, cell["cx0"]:cell["cx1"]] = False

        # 3) 逐行带做连通域归位
        owned: list[list[np.ndarray]] = []
        for r in range(rows):
            y0, y1 = row_bounds[r], row_bounds[r + 1]
            owned.append(own_by_cell(cleaned[y0:y1, :], cols, ex))

        frames = []
        for cell in scanned:
            i, r, c = cell["idx"], cell["row"], cell["idx"] % cols
            cx0, cy0 = cell["cx0"], cell["cy0"]
            y0 = row_bounds[r]

            # ⚠ clean 保持**行带全宽**，不截到格宽 ——
            #   连通域归位后，一条披风可能整块属于本格、却把像素伸进邻格
            #   （攻击 7 的披风最左到 823，越过了列界 887）。
            #   要是这里按格宽截断，那段就两边都不要、直接被吃掉。
            clean = owned[r][c]

            hit = [f"{s['y0']}-{s['y1']}" for s in cell["segs"]
                   if (r, s["y0"], s["y1"]) in labels]
            tag = " ".join(hit) if hit else "无"

            ys_i, xs_i = np.where(clean)
            if len(xs_i) == 0:
                report.append(f"    ✘ 第 {i + 1} 格是空的")
                continue

            lb = (int(xs_i.min()), int(ys_i.min()), int(xs_i.max()), int(ys_i.max()))
            ax_l, ay_l = anchor_of(clean, lb)
            bh = body_height(clean, lb, ax_l, ay_l)

            bbox = (lb[0], y0 + lb[1], lb[2], y0 + lb[3])
            frames.append({"i": i, "bbox": bbox, "ax": ax_l, "ay": y0 + ay_l,
                           "clean": clean, "origin": (0, y0), "body_h": bh})
            report.append(f"    [{i + 1:02d}] 格 ({cx0},{cy0})-({cell['cx1']},{cell['cy1']})"
                          f"  标签段 {tag}"
                          f"  内容 {bbox[2] - bbox[0] + 1}×{bbox[3] - bbox[1] + 1}"
                          f"  锚点 ({ax_l},{y0 + ay_l})  本体高 {bh}")

        if not frames:
            continue

        # 把「被抹掉的标签」也落实成透明 —— 万一某个标签正好夹在两段内容之间，
        # 包围盒会跨过它，直接从原图裁就会把数字重新带进帧里。
        # 顺带处理贴边渐隐（见 EDGE_FADE 的注释）。
        W, H = rgba.size
        for f in frames:
            ox, oy = f["origin"]
            x0, y0, x1, y1 = f["bbox"]
            patch = np.asarray(rgba)[y0:y1 + 1, x0:x1 + 1].astype(np.float32)
            keep = f["clean"][y0 - oy:y1 - oy + 1, x0 - ox:x1 - ox + 1]
            patch[:, :, 3] *= (keep > 0)
            f["edge"] = fade_edges(patch, x0, x1, y0, y1, W, H)
            f["patch"] = patch.astype(np.uint8)

        faded = [f"{f['i'] + 1}({f['edge']})" for f in frames if f["edge"]]
        report.append("    贴边渐隐：" + (" ".join(faded) if faded else "无"))

        # 统一画布：所有帧按锚点对齐后取并集
        L = min(f["bbox"][0] - f["ax"] for f in frames)
        R = max(f["bbox"][2] - f["ax"] for f in frames)
        T = min(f["bbox"][1] - f["ay"] for f in frames)
        cw, chh = R - L + 1 + MARGIN * 2, -T + 1 + MARGIN * 2
        anc = (-L + MARGIN, -T + MARGIN)

        body_hs = [f["body_h"] for f in frames if f["body_h"] > 0]
        body_med = int(np.median(body_hs)) if body_hs else 0
        report.append(f"    → 画布 {cw}×{chh}  锚点画布坐标 {anc}"
                      f"  本体高中位 {body_med}（{min(body_hs)}–{max(body_hs)}）")

        plan[anim] = [{
            "patch": f["patch"],
            "dst": (f["bbox"][0] - f["ax"] + anc[0], f["bbox"][1] - f["ay"] + anc[1]),
            "size": (cw, chh), "anchor": anc, "body_h": f["body_h"],
        } for f in frames]
        plan[anim + "__meta"] = [{"body_med": body_med, "canvas": (cw, chh),
                                  "anchor": anc, "count": len(frames)}]

    return plan, report


def preview(plan: dict) -> None:
    """
    每个动作出一条「按锚点对齐后」的核对带：红竖线 = 锚点 x，黄横线 = 脚底，
    每帧左上角标帧号。

    帧数多时**折行**（每行最多 6 帧）—— 18 帧铺成一条 4248 px 的长条，
    缩到能看全的宽度后每帧只剩 80 px，等于没看。
    """
    WORK.mkdir(parents=True, exist_ok=True)
    for anim, frames in plan.items():
        if anim.endswith("__meta"):
            continue
        cw, ch = frames[0]["size"]
        # 目标总宽 2400：低于这个值 Read 出去会缩得太小看不清，高于这个值又只能压成 1 帧/行
        per_row = max(1, min(len(frames), max(1, 2400 // cw), 6))
        rows_n = (len(frames) + per_row - 1) // per_row
        strip = Image.new("RGBA", (cw * per_row, ch * rows_n), (30, 32, 38, 255))
        for i, f in enumerate(frames):
            r, c = divmod(i, per_row)
            strip.alpha_composite(Image.fromarray(f["patch"]),
                                  (c * cw + f["dst"][0], r * ch + f["dst"][1]))

        d = ImageDraw.Draw(strip)
        ax, ay = frames[0]["anchor"]
        for i in range(len(frames)):
            r, c = divmod(i, per_row)
            ox, oy = c * cw, r * ch
            d.line([ox, oy, ox + cw, oy], fill=(90, 96, 110, 255), width=1)
            d.line([ox, oy, ox, oy + ch], fill=(90, 96, 110, 255), width=1)
            d.line([ox + ax, oy, ox + ax, oy + ch], fill=(255, 80, 80, 200), width=1)
            d.line([ox, oy + ay, ox + cw, oy + ay], fill=(255, 210, 0, 200), width=2)
            d.text((ox + 5, oy + 3), str(i + 1), fill=(255, 235, 90, 255))

        scale = max(1, min(3, 1700 // strip.width))
        out = WORK / f"hero2_{anim}.png"
        strip.resize((strip.width * scale, strip.height * scale), Image.LANCZOS).save(out)
        print(f"  预览 → {out.relative_to(PROJECT)}"
              f"  ({len(frames)} 帧 × {cw}×{ch}，{rows_n} 行)")


def apply(plan: dict) -> None:
    manifest = [
        "# 角色/动作  画布宽 画布高 锚点X 锚点Y 帧数",
        "# 锚点 = 身体中心 / 脚底，图像坐标（左上原点）。",
        "# Unity 侧把每帧 Sprite 的 pivot 设成 Custom = 锚点，摆位时 RectTransform 的位置即「脚底中心」。",
    ]
    # ⚠ 清单采用「合并写」：旧清单里**不属于本次重切动作**的行全部保留
    #   （怪物三行 + 本次没动的主角动作），只覆盖本次重切的动作。
    #   2026-09-21 修：旧版「非 Hero/ 全保留」在 --only 单切 idle 时会把
    #   Hero/attack 等其它动作的行整个丢掉，Unity 侧锚点悄悄退回底部居中。
    regenerated = {name for name in plan if not name.endswith("__meta")}
    old_lines = (ART / "Chars" / "anim_frames.txt").read_text(encoding="utf-8").splitlines()
    for ln in old_lines:
        s = ln.strip()
        if s.startswith("#") or not s:
            continue
        name = s.split()[0]
        if name not in regenerated:
            manifest.append(s)

    total = 0
    for anim, frames in plan.items():
        if anim.endswith("__meta"):
            continue
        dst = OUT / anim
        dst.mkdir(parents=True, exist_ok=True)
        cw, ch = frames[0]["size"]
        ax, ay = frames[0]["anchor"]

        # ⚠ 换图后帧数可能变少（idle 18 → 16）：先把旧编号超出新帧数的残帧删掉，
        #   否则 Unity 侧 BuildCharacter 按 01..24 顺序加载，会把两张旧帧混进新 clip。
        #   这个目录由本脚本独家产出（见 _meta.txt），按文件名前缀清理是安全的。
        keep = {f"Hero_{anim}_{i + 1:02d}.png" for i in range(len(frames))}
        for old in sorted(dst.glob(f"Hero_{anim}_*.png")):
            if old.name not in keep:
                print(f"  清掉超编旧帧 {old.name}")
                old.unlink()

        (dst / "_meta.txt").write_text(
            f"# Hero / {anim}\n"
            f"# 画布 = {cw}x{ch}（所有帧一致）\n"
            f"# 锚点（身体中心 / 脚底）画布坐标 = ({ax}, {ay})\n"
            f"# 生成方式见 Tools/art-audit/slice_hero_anim.py\n",
            encoding="utf-8")
        for i, f in enumerate(frames):
            canvas = Image.new("RGBA", f["size"], (0, 0, 0, 0))
            canvas.alpha_composite(Image.fromarray(f["patch"]), f["dst"])
            canvas.save(dst / f"Hero_{anim}_{i + 1:02d}.png")
            total += 1
        manifest.append(f"Hero/{anim} {cw} {ch} {ax} {ay} {len(frames)}")

    (ART / "Chars" / "anim_frames.txt").write_text("\n".join(manifest) + "\n", encoding="utf-8")
    print(f"\n共导出 {total} 张 → {OUT}")
    print(f"帧清单    → {ART / 'Chars' / 'anim_frames.txt'}")


def report_motion(plan: dict) -> None:
    """
    帧间差异：判断 idle 那 18 帧是**一次完整动作**还是「6 帧重复 3 遍」。

    这件事不能靠眼睛看着像就定 —— 重复帧留在序列里，播起来会一顿一顿。
    口径 = 相邻帧的像素差（对齐到同一画布后），差异接近 0 的那一对就是循环接缝。
    """
    for anim, frames in plan.items():
        if anim.endswith("__meta") or len(frames) < 3:
            continue
        cw, ch = frames[0]["size"]
        stacks = []
        for f in frames:
            canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
            canvas.alpha_composite(Image.fromarray(f["patch"]), f["dst"])
            stacks.append(np.asarray(canvas)[:, :, 3].astype(np.float32))
        diffs = [float(np.abs(stacks[i] - stacks[i + 1]).mean()) for i in range(len(stacks) - 1)]
        # 首尾差：接近 0 说明这组帧天然闭环
        wrap = float(np.abs(stacks[-1] - stacks[0]).mean())
        lo = min(diffs)
        idx = int(np.argmin(diffs))
        print(f"  {anim:7s} {len(frames):2d} 帧  相邻差 min {lo:.2f} @{idx + 1}→{idx + 2}"
              f"  max {max(diffs):.2f}  首尾差 {wrap:.2f}")


def main() -> None:
    plan, report = build()

    # --only <动作名>：单切一张（换图批次用）。过滤放在 build 之后，
    # 报告里仍能看到整表的网格实测，只是 plan / apply 只留指定动作。
    if "--only" in sys.argv:
        want = sys.argv[sys.argv.index("--only") + 1]
        plan = {k: v for k, v in plan.items()
                if k.endswith("__meta") or k == want}
        report_only = want
    else:
        report_only = None

    if report_only:
        report = [ln for ln in report if not ln.startswith(("Idle", "Attack", "defense", "BeHit", "Death"))
                  or ln.startswith(report_only[:4])]

    print("\n".join(report))
    print("\n── 帧间动态 ──")
    report_motion(plan)
    preview(plan)
    if "--apply" in sys.argv:
        apply(plan)


if __name__ == "__main__":
    main()
