# -*- coding: utf-8 -*-
"""
新美术素材分析器（只读，不写成品）
====================================
把 `Assets/Art/` 下这批新导入的源图扫一遍，输出切分所需的全部客观尺寸：
- UI 图集：按 alpha 连通域找元素包围盒
- 动画表：按行找行带（行标题 / 帧行），行内按列找帧
- 怪物表无 alpha：先统计背景色，给出可用的去底阈值

用法:
    python analyze_new_art.py
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"

UI_ATLAS = ART / "542f45c2-5d2c-4803-bcbd-2cafa10226fc.png"
HERO_SHEET = ART / "2049a678-64d5-46e6-a342-87eaf3f37435.png"
MON_SHEET = ART / "6e093879-16be-4326-b9e9-c90a767cb587.png"
BG = ART / "dungeon_background_clean.png"
REF = ART / "Reference.png"


def blocks(has: np.ndarray, min_gap: int = 4) -> list[tuple[int, int]]:
    """把一维布尔数组切成被 >=min_gap 个 False 隔开的若干 True 区间。"""
    out, i, n = [], 0, len(has)
    while i < n:
        if has[i]:
            j = i
            while j < n and has[j]:
                j += 1
            out.append((i, j - 1))
            i = j
        else:
            i += 1
    # 合并间距过小的相邻块
    merged: list[tuple[int, int]] = []
    for b in out:
        if merged and b[0] - merged[-1][1] - 1 < min_gap:
            merged[-1] = (merged[-1][0], b[1])
        else:
            merged.append(b)
    return merged


def report_ui_atlas() -> None:
    print("=" * 78)
    print(f"UI 图集 {UI_ATLAS.name}  ({Image.open(UI_ATLAS).size})")
    print("=" * 78)
    im = Image.open(UI_ATLAS).convert("RGBA")
    a = np.asarray(im)
    alpha = a[:, :, 3]
    mask = alpha > 16
    print(f"非透明像素占比: {mask.mean() * 100:.2f}%")

    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    print(f"连通域数量: {n}")
    objs = ndimage.find_objects(lab)
    items = []
    for i, sl in enumerate(objs, start=1):
        ys, xs = sl
        h = ys.stop - ys.start
        w = xs.stop - xs.start
        area = int((lab[sl] == i).sum())
        if area < 120:
            continue
        items.append((xs.start, ys.start, w, h, area))
    items.sort(key=lambda t: (t[1] // 40, t[0]))
    print(f"面积 >=120 的连通域: {len(items)}")
    print(f"{'#':>3} {'x':>5} {'y':>5} {'w':>5} {'h':>5} {'area':>7}  {'x1':>5} {'y1':>5}")
    for i, (x, y, w, h, area) in enumerate(items):
        print(f"{i:>3} {x:>5} {y:>5} {w:>5} {h:>5} {area:>7}  {x + w:>5} {y + h:>5}")


def sheet_info(path: Path, label: str) -> None:
    print()
    print("=" * 78)
    im = Image.open(path)
    print(f"{label} {path.name}  mode={im.mode} size={im.size}")
    print("=" * 78)
    a = np.asarray(im.convert("RGBA"))
    alpha = a[:, :, 3]

    if alpha.min() < 250:
        # 有透明通道 → 用 alpha 找内容
        mask = alpha > 16
        print(f"alpha 范围 {alpha.min()}–{alpha.max()}，用 alpha 定内容")
    else:
        # 全不透明 → 统计四角背景色，给出阈值
        rgb = a[:, :, :3].astype(np.int16)
        h, w, _ = rgb.shape
        corners = np.concatenate([
            rgb[0:8, 0:8].reshape(-1, 3), rgb[0:8, w - 8:w].reshape(-1, 3),
            rgb[h - 8:h, 0:8].reshape(-1, 3), rgb[h - 8:h, w - 8:w].reshape(-1, 3),
        ])
        bg = corners.mean(axis=0)
        print(f"四角背景色 ≈ RGB({bg[0]:.0f},{bg[1]:.0f},{bg[2]:.0f})")

        # 左上角一小块单独看（含行标题）与主体区域
        tl = rgb[0:30, 0:200].reshape(-1, 3).mean(axis=0)
        print(f"左上 30x200 均值 ≈ RGB({tl[0]:.0f},{tl[1]:.0f},{tl[2]:.0f})")

        # 行剖面：每行的「明显亮于背景」像素数
        lum = rgb.max(axis=2)
        bgmax = float(bg.max())
        for t in (24, 32, 40, 56, 72):
            m = lum > bgmax + t
            rows = m.sum(axis=1)
            print(f"  阈值 bgmax+{t:>2} = {bgmax + t:>5.0f} → 前景占比 {m.mean() * 100:5.2f}%")
        mask = lum > bgmax + 40

    rowhas = mask.any(axis=1)
    rowbands = blocks(rowhas, min_gap=6)
    print(f"\n横向行带（{len(rowbands)} 条）:")
    for (y0, y1) in rowbands:
        sub = mask[y0:y1 + 1]
        colhas = sub.any(axis=0)
        cols = blocks(colhas, min_gap=10)
        cols = [(c0, c1) for (c0, c1) in cols if c1 - c0 >= 20]
        print(f"  y {y0:>4}–{y1:>4} (h={y1 - y0 + 1:>4})  列块 {len(cols)} 个")
        line = "    "
        for (c0, c1) in cols:
            line += f"[{c0}–{c1} w={c1 - c0 + 1}] "
            if len(line) > 150:
                print(line)
                line = "    "
        if line.strip():
            print(line)


def main() -> None:
    for p in (UI_ATLAS, HERO_SHEET, MON_SHEET, BG, REF):
        print(f"存在 {p.name}: {p.exists()}  {Image.open(p).size if p.exists() else ''}")
    report_ui_atlas()
    sheet_info(HERO_SHEET, "主角动画表")
    sheet_info(MON_SHEET, "怪物动画表")


if __name__ == "__main__":
    main()
