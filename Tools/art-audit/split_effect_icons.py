# -*- coding: utf-8 -*-
"""
效果图标切分器（2026-09-21）
============================

把 `Assets/Art/Icon.png`（黑底合成图，7 枚效果图标：4 上 3 下）切成独立透明 PNG，
输出到 `Assets/Art/Icons/`，与既有的触发图标（Icon_01..03）同一目录、同一命名规范。

与 `slice_icons.py`（三合一触发图标）的口径差异：
- 那张源图列缝干净、按 alpha 列分布切段就行；
- 这张是 **两行拼版**，且图标带外辉光 —— 但底子是 alpha=0 的真透明（实测四角
  alpha=0，图标辉光 alpha 渐变），所以直接按 alpha 连通域分块即可，
  连通域天然把「图标 + 辉光」圈成一个整体，不用做泛洪。

坑位：
1. 辉光让 alpha 阈值不能太高 —— 取 alpha > 8 的「有内容」口径，与导入端
   `alphaIsTransparency` 的观感一致；阈值太高会把辉光切出锯齿圈。
2. 映射不能靠位置猜 —— 先出深底核对图（图标是发光体，白底会糊），
   人工确认「第 N 块 = 哪个效果」之后再 --apply。本案实测：
   上排 = 力量↑ / 防御↑ / 连击 / 免疫，下排 = 虚弱 / 脆弱 / 加速。
3. 所有切片**不缩放**、各自紧贴裁切（4 px 余量），保留原分辨率与相对大小
   （7 枚在设计稿里本来就近似等大，网格已保证）。
4. RGB 通道原样保留，只改 alpha（乘羽化后的部件掩膜）——
   部件贴到深色 UI 上时边缘不镶黑边。

用法:
    python split_effect_icons.py             # 预览：出深底核对图，不写 Assets
    python split_effect_icons.py --apply     # 执行切分，写进 Assets/Art/Icons/
"""
from __future__ import annotations

import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
SRC = PROJECT / "Assets" / "Art" / "Icon.png"
OUT_DIR = PROJECT / "Assets" / "Art" / "Icons"
WORK = PROJECT / "Tools" / "art-audit" / "_work"
FONT = PROJECT / "Assets" / "Font" / "Black" / "Lxgw975GoSC-400W.ttf"

ALPHA_MIN = 8          # alpha > 此值算「有内容」（保住外圈辉光）
MIN_PART = 2000        # 连通域面积下限（挡噪点；7 枚图标最小也上万）
PAD = 4                # 紧贴裁切四周余量
FEATHER = 0.6          # 部件边缘羽化半径（px），消硬切感

# 切块顺序 = 上排左→右 4 枚 + 下排左→右 3 枚。
# ⚠ 顺序是**看图确认过的**（见核对图），不是按位置想当然。
NAMES = [
    ("Icon_04_atkplus_力量增加.png", "力量增加"),
    ("Icon_05_defplus_防御增加.png", "防御增加"),
    ("Icon_06_combo_连击.png", "连击"),
    ("Icon_07_immune_免疫.png", "免疫"),
    ("Icon_08_weak_虚弱.png", "虚弱"),
    ("Icon_09_fragile_脆弱.png", "脆弱"),
    ("Icon_10_haste_加速.png", "加速"),
]


def components(mask: np.ndarray) -> list[dict]:
    """8 连通域标记（BFS，避免引 scipy 依赖——图不大，几十万像素秒级）。"""
    h, w = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    out = []
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or seen[y, x]:
                continue
            q = deque([(y, x)])
            seen[y, x] = True
            ys, xs = [], []
            while q:
                cy, cx = q.popleft()
                ys.append(cy)
                xs.append(cx)
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            q.append((ny, nx))
            out.append({
                "bbox": (min(xs), min(ys), max(xs), max(ys)),
                "area": len(ys),
                "cx": sum(xs) / len(ys),
                "cy": sum(ys) / len(ys),
            })
    return out


def split() -> list[dict]:
    im = Image.open(SRC).convert("RGBA")
    a = np.asarray(im).astype(np.float32)
    mask = a[:, :, 3] > ALPHA_MIN

    parts = [p for p in components(mask) if p["area"] >= MIN_PART]
    if len(parts) != len(NAMES):
        raise SystemExit(f"✘ 期望 {len(NAMES)} 块，实测 {len(parts)} 块 —— 先看核对图再改判据")

    # 上排（cy 在上半）4 枚、下排 3 枚，各按 x 排
    mid = im.size[1] / 2.0
    top = sorted([p for p in parts if p["cy"] < mid], key=lambda p: p["cx"])
    bot = sorted([p for p in parts if p["cy"] >= mid], key=lambda p: p["cx"])
    ordered = top + bot
    if len(top) != 4 or len(bot) != 3:
        raise SystemExit(f"✘ 期望上 4 下 3，实测上 {len(top)} 下 {len(bot)}")

    out = []
    W, H = im.size
    for info, (fname, label) in zip(ordered, NAMES):
        x0, y0, x1, y1 = info["bbox"]
        x0, y0 = max(0, x0 - PAD), max(0, y0 - PAD)
        x1, y1 = min(W - 1, x1 + PAD), min(H - 1, y1 + PAD)

        part_mask = np.zeros((H, W), dtype=np.float32)
        # 只把「属于这块连通域」的像素置 1：以 bbox 内 & 距该域种子连通 ——
        # 简化：两排之间间隙很大、块间距远大于 PAD，bbox 内不会混进邻居，
        # 直接用 bbox 窗口内的原始 mask 即可（核对图可复核）。
        part_mask[y0:y1 + 1, x0:x1 + 1] = mask[y0:y1 + 1, x0:x1 + 1]

        crop = a[y0:y1 + 1, x0:x1 + 1].copy()
        soft = Image.fromarray((part_mask[y0:y1 + 1, x0:x1 + 1] * 255).astype(np.uint8))
        soft = soft.filter(ImageFilter.GaussianBlur(FEATHER))
        crop[:, :, 3] = np.minimum(crop[:, :, 3], np.asarray(soft, dtype=np.float32))

        out.append({"fname": fname, "label": label, "img": crop, "bbox": (x0, y0, x1, y1)})
    return out


def contact_sheet(parts: list[dict]) -> Path:
    """深底核对图：1:1 贴放（两行），每枚标「序号 + 中文名 + 尺寸」。"""
    WORK.mkdir(parents=True, exist_ok=True)
    PLATE = (46, 52, 62)
    GAP = 28
    row1, row2 = parts[:4], parts[4:]
    w1 = sum(p["img"].shape[1] for p in row1) + GAP * (len(row1) + 1)
    w2 = sum(p["img"].shape[1] for p in row2) + GAP * (len(row2) + 1)
    W = max(w1, w2)
    h1 = max(p["img"].shape[0] for p in row1)
    h2 = max(p["img"].shape[0] for p in row2)
    LABEL_H = 44
    H = h1 + h2 + LABEL_H * 2 + GAP * 3

    sheet = Image.new("RGB", (W, H), PLATE)
    font = ImageFont.truetype(str(FONT), 24)
    d = ImageDraw.Draw(sheet)

    def paste_row(row, y, start_idx):
        x = GAP
        for i, p in enumerate(row):
            tile = Image.fromarray(p["img"].astype(np.uint8), "RGBA")
            sheet.paste(tile, (x, y), tile)
            d.text((x, y + p["img"].shape[0] + 6),
                   f"{start_idx + i} {p['label']} {p['img'].shape[1]}×{p['img'].shape[0]}",
                   fill=(235, 235, 225), font=font)
            x += p["img"].shape[1] + GAP

    paste_row(row1, GAP, 1)
    paste_row(row2, GAP + h1 + LABEL_H + GAP, 5)
    out = WORK / "effect_icons_sheet.png"
    sheet.save(out)
    return out


def apply(parts: list[dict]) -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for p in parts:
        Image.fromarray(p["img"].astype(np.uint8), "RGBA").save(OUT_DIR / p["fname"])
        print(f"  → Assets/Art/Icons/{p['fname']}")
    print(f"共导出 {len(parts)} 枚 → {OUT_DIR}")


def main() -> None:
    parts = split()
    sheet = contact_sheet(parts)
    print("核对图 → " + str(sheet.relative_to(PROJECT)))
    for i, p in enumerate(parts, 1):
        print(f"  [{i}] {p['label']}  bbox={p['bbox']}  {p['img'].shape[1]}×{p['img'].shape[0]}")
    if "--apply" in sys.argv:
        apply(parts)


if __name__ == "__main__":
    main()
