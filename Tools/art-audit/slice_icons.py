# -*- coding: utf-8 -*-
"""
三合一图标卡切分器
==================
把 `IconSheet_盾剑特殊_三合一.png` 里并排的三个图标切成三张独立 Sprite。

要点：
- **按 alpha 列分布自动找分界**，不硬编码坐标 —— 换一张同类拼图只要改 ROLES 的数量即可。
- **三张共用同一个方形画布（1024×1024）、且不缩放**，这样三者的相对大小与设计稿一致；
  若各自紧贴裁切，宽高比不同的图标放进固定尺寸 Image 后会显得大小不一。
- 原拼图**保留**在 `_Source/` 下，作为唯一事实来源。

用法:
    python slice_icons.py            # 预览：只打印分界与包围盒，不写文件
    python slice_icons.py --apply    # 执行切分
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
ICONS = PROJECT / "Assets" / "Art" / "Icons"
SHEET = ICONS / "_Source" / "IconSheet_盾剑特殊_三合一.png"
SHEET_FINAL = "IconSheet_盾剑特殊_三合一.png"      # 留档时改名：第三个图标是 γ 特殊，不是「强制」
SOURCE_DIR = ICONS / "_Source"
REVIEW = PROJECT / "Captures" / "art-review"

# 从左到右三个图标 → (文件名, 业务 ID=EffectTrigger, 中文名)
# 顺序 = Core 里 EffectTrigger 的枚举序（Attack=0 / Defend=1 / Special=2）
# ⚠ 顺序必须与「源拼图从左到右」一致。已肉眼核对（见 Captures/art-review/_tmp_icon_blocks.png）：
#   源第 1 块 = 蓝盾（β 防御） · 源第 2 块 = 剑（α 进攻） · 源第 3 块 = 六边形感叹号（γ 特殊）
#   文件序号按 Core 里 EffectTrigger 的枚举序排（Attack=0 / Defend=1 / Special=2），
#   所以序号 01 是剑、02 是盾 —— 与源图左右顺序不同，这是有意为之。
ROLES = [
    ("Icon_02_defend_盾.png",      "defend",  "盾"),
    ("Icon_01_attack_剑.png",      "attack",  "剑"),
    ("Icon_03_special_感叹号.png", "special", "感叹号"),
]

CANVAS = 1024      # 公共方形画布边长
ALPHA_MIN = 8      # 判定「有内容」的 alpha 阈值
MIN_GAP = 8        # 认定为「图标之间」的最小空白列宽


def column_blocks(mask: np.ndarray) -> list[tuple[int, int]]:
    """按列扫描，返回被空白列隔开的若干非空列区间 [(x0, x1), ...]。"""
    col_has = mask.any(axis=0)
    blocks, i, n = [], 0, len(col_has)
    while i < n:
        if col_has[i]:
            j = i
            while j < n and col_has[j]:
                j += 1
            blocks.append((i, j - 1))
            i = j
        else:
            i += 1

    # 合并间隔过窄的块（图标内部的断开不应被当成两个图标）
    merged = [blocks[0]]
    for b in blocks[1:]:
        if b[0] - merged[-1][1] - 1 < MIN_GAP:
            merged[-1] = (merged[-1][0], b[1])
        else:
            merged.append(b)
    return merged


def main() -> int:
    apply = "--apply" in sys.argv

    if not SHEET.exists():
        print(f"[ERR] 找不到拼图：{SHEET}")
        return 1

    sheet = Image.open(SHEET).convert("RGBA")
    mask = np.array(sheet)[:, :, 3] > ALPHA_MIN
    blocks = column_blocks(mask)

    print(f"拼图 {sheet.size[0]}×{sheet.size[1]}，识别到 {len(blocks)} 个图标列块")
    if len(blocks) != len(ROLES):
        print(f"[ERR] 期望 {len(ROLES)} 个，实得 {len(blocks)} 个 —— 分界不可靠，中止")
        for b in blocks:
            print("    ", b)
        return 1

    pieces = []
    for (x0, x1), (fname, _bid, zh) in zip(blocks, ROLES):
        sub = mask[:, x0:x1 + 1]
        ys, xs = np.where(sub)
        bx0, bx1 = x0 + xs.min(), x0 + xs.max()
        by0, by1 = ys.min(), ys.max()
        w, h = bx1 - bx0 + 1, by1 - by0 + 1
        print(f"  {zh:<4} x[{bx0},{bx1}] 宽 {w}  y[{by0},{by1}] 高 {h}  → {fname}")
        pieces.append((sheet.crop((bx0, by0, bx1 + 1, by1 + 1)), fname, zh))

    max_dim = max(max(p.size) for p, _, _ in pieces)
    if max_dim > CANVAS:
        print(f"[ERR] 最大边长 {max_dim} 超过画布 {CANVAS}，请调大 CANVAS")
        return 1

    if not apply:
        print(f"\n[预览] 干跑结束。画布 {CANVAS}×{CANVAS}，最大图标 {max_dim}px（四周留白 {(CANVAS - max_dim) // 2}px）")
        print("       加 --apply 才会真正写文件。")
        return 0

    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    outs = []
    for img, fname, zh in pieces:
        canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        canvas.paste(img, ((CANVAS - img.width) // 2, (CANVAS - img.height) // 2))
        out = ICONS / fname
        canvas.save(out, optimize=True)
        outs.append((out, zh))
        print(f"  [OK] {out.relative_to(PROJECT)}  {CANVAS}×{CANVAS}")

    # 拼图留档「不在这里做」—— Unity 正在运行时直接 mv 文件 + .meta 有被判定成
    # 「删除 + 新建」而换掉 GUID 的风险。交给菜单 `魔法乱斗/整理 · 建触发图标库`
    # 用 AssetDatabase.MoveAsset 一并改名并移入 _Source/。
    print(f"  [源表] 保留在 {SHEET.relative_to(PROJECT)}")

    # 出一张核对图
    REVIEW.mkdir(parents=True, exist_ok=True)
    cell, pad = 260, 16
    sheet_out = Image.new("RGB", (len(outs) * cell, cell + 40), (38, 42, 52))
    for i, (p, zh) in enumerate(outs):
        with Image.open(p) as im:
            im = im.convert("RGBA")
            im.thumbnail((cell - pad * 2, cell - pad * 2), Image.LANCZOS)
            plate = Image.new("RGBA", (cell - pad, cell - pad), (38, 42, 52, 255))
            plate.alpha_composite(im, ((plate.width - im.width) // 2,
                                       (plate.height - im.height) // 2))
            sheet_out.paste(plate.convert("RGB"), (i * cell + pad // 2, pad // 2))

    from PIL import ImageDraw, ImageFont
    fp = PROJECT / "Assets" / "Art" / "Fonts" / "Black" / "Google-Regular.ttf"
    fnt = ImageFont.truetype(str(fp), 22) if fp.exists() else ImageFont.load_default()
    d = ImageDraw.Draw(sheet_out)
    for i, (_p, zh) in enumerate(outs):
        d.text((i * cell + pad, cell + 4), f"{i + 1}. {zh}", font=fnt, fill=(235, 235, 240))
    vout = REVIEW / "新素材总览-06-图标切分结果.png"
    sheet_out.save(vout, optimize=True)
    print(f"  [OK] 核对图 → {vout.relative_to(PROJECT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
