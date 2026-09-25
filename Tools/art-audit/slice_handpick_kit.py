# -*- coding: utf-8 -*-
"""
「手牌选择弹窗」按钮素材切分器（M30）
=====================================
用户口径（2026-09-22）：确认键不要用「自己摆的一个灰蓝色方块」，要用素材里给的那条按钮条。

## 源图

`Assets/Art/148cae8c-29e8-457e-82ec-40f8088a71a2.png`（1111×1416 白底图）自上而下三块：
  1. 顶部横幅 / 标题牌（银灰描边 + 深蓝底）
  2. 中间竖向卡框（与 `Peek_Frame` 同款造型）
  3. **底部按钮条**（亮蓝底 + 六边形两端 + 辉光）← 本脚本要切的

## 输出

| 输出名 | 说明 | 九宫格策略 |
|---|---|---|
| `HandPick_Confirm.png` | 按钮底图（亮蓝） | 左右 border 落在两端六边形内侧，**纵向不拉伸** |

## 为什么用九宫格横向拉伸

按钮的常态宽 300，而素材原宽约 470 —— 两端是「六边形 + 斜角」造型，直接按尺寸缩放会把
它们压扁/拉长。左右各留出整块造型当 border，中间那段纯色躯干负责横拉，两端原地不动。
纵向不拉伸（上下 border 吃掉整高）：按钮只有一种高度，纵拉没有意义还容易把辉光抻开。

用法:
    python slice_handpick_kit.py --probe    # 打印包围盒 + 可拉伸行 / 列区间（不写盘）
    python slice_handpick_kit.py --apply    # 执行切分（只写 png，.meta 交给 Unity 生成）
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"
OUT = ART / "Ui"
SHEET = ART / "148cae8c-29e8-457e-82ec-40f8088a71a2.png"

# 包围盒用的 alpha 阈值。取 8 而不是 24 —— 按钮边缘有一圈很淡的辉光，
# 阈值太高会把辉光切掉，缩放后边缘会显出硬直角。
ALPHA_MIN = 8

# ── 元素表：(输出名, 源矩形 x, y, w, h, 左右 border, 上 border, 下 border) ──
ELEMENTS: list[tuple[str, tuple[int, int, int, int], int, int, int]] = [
    # 底部按钮条：两端造型要保住，所以左右 border 给足（各 96）；纵向整体当 border。
    # 源矩形由 --probe 的行 / 列投影量出：content band = y 1112…1280 / x 244…865，
    # 四周各放 2 px 让辉光不被削平。
    ("HandPick_Confirm", (242, 1110, 626, 174), 96, 60, 60),
]


def segs(v: np.ndarray, thr: int) -> list[tuple[int, int]]:
    """把一维投影里连续的「> thr」区段切成 [start, end] 列表。"""
    idx = np.where(v > thr)[0]
    if len(idx) == 0:
        return []
    out: list[tuple[int, int]] = []
    s = p = int(idx[0])
    for x in idx[1:]:
        x = int(x)
        if x != p + 1:
            out.append((s, p))
            s = x
        p = x
    out.append((s, p))
    return out


def probe() -> None:
    img = Image.open(SHEET).convert("RGBA")
    a = np.asarray(img)
    print("sheet size", img.size)

    mask = a[:, :, 3] > ALPHA_MIN
    print("row bands:", segs(mask.sum(axis=1), 2))

    for name, rect, *_ in ELEMENTS:
        x, y, w, h = rect
        sub = mask[y:y + h, x:x + w]
        print(f"\n[{name}] rect={rect}")
        print("  row bands(local):", segs(sub.sum(axis=1), 2))
        print("  col bands(local):", segs(sub.sum(axis=0), 2))
        # 逐列标准差：找「纯色可拉伸」的列区间
        rgb = np.asarray(img)[y:y + h, x:x + w, :3].astype(np.float32)
        rows_std = rgb.std(axis=(1, 2))
        # 核心带（上下各让开 25%）内的列标准差 —— 避开上下辉光
        pad = int(h * 0.25)
        core = rgb[pad:h - pad, :, :]
        col_std = core.std(axis=(0, 2))
        flat = [i for i, s in enumerate(col_std) if s < 6.0]
        print(f"  col std < 6 的列数 = {len(flat)} / {w}", 
              f"范围 = {(flat[0], flat[-1]) if flat else None}")


def apply() -> None:
    img = Image.open(SHEET).convert("RGBA")
    OUT.mkdir(parents=True, exist_ok=True)

    for name, rect, bl, bb, bt in ELEMENTS:
        x, y, w, h = rect
        crop = img.crop((x, y, x + w, y + h))
        dst = OUT / f"{name}.png"
        crop.save(dst)
        print(f"{name}: {crop.size} -> {dst}")
        print(f"  spriteBorder = (left {bl}, bottom {bb}, right {bl}, top {bt})")
        # 提示：右侧 border 常与左侧同值（两端造型对称），这里只报左值


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="切分手牌选择 UI 源表（默认仅预览）")
    parser.add_argument("--source", type=Path, help="源图路径；相对路径基于当前工作目录")
    parser.add_argument("--apply", action="store_true", help="将切片写入 Assets/Art/Ui")
    args = parser.parse_args()
    if args.source:
        SHEET = args.source.resolve()
    if not SHEET.is_file():
        parser.error("历史源图未随当前仓库保留，请用 --source 指定源图。现有切片无需重新生成。")
    if args.apply:
        apply()
    else:
        probe()
