# -*- coding: utf-8 -*-
"""
「查看对方手牌」弹窗素材切分器
================================
把 `661ca76a-….png`（用户按 reference-2 的查看弹窗风格出的一张 UI 套件图）
切成独立 Sprite，输出到 `Assets/Art/Ui/`。

## 图里有什么

1536×1024 的白底图上摊着 5 块元素（另有 2 枚极小的点缀徽记，见文末）：

| 输出名 | 源包围盒 | 比例 | 形态 |
|---|---|---|---|
| `Peek_Panel`      | 992×447  | 2.22 | 大弹窗底板：深色圆角面板 + **顶部中央凸起的标题牌位** |
| `Peek_Frame`      | 431×556  | 0.78 | 竖向边框（顶部有尖顶造型、内嵌一个矩形开口） |
| `Peek_PanelSmall` | 504×281  | 1.79 | 同款小面板（较短，没有顶部牌位） |
| `Peek_Banner`     | 499×192  | 2.60 | 两端带缺口的横幅 / 标题牌 |
| `Peek_CardBack`   | 186×256  | 0.727| **牌背**（比例与卡面 0.72 一致，中央菱形纹章） |

## 关键尺寸关系：这套图是按「4 张牌一行」画的

```
面板宽 992 = 4×186(牌背) + 3×24(间距) + 2×88(左右留白)
面板高 447 =   256(牌背) + 2×95.5(上下留白)
```

于是版式可以完全按这几个数摆，不需要缩放：**一行最多 4 张**，
超过 4 张换两行（行高 256 + 行距 24），面板只在**纵向**变高 ——
横向拉伸会连同顶部那块凸起的标题牌位一起抻开，所以横向保持原宽不切。

## 九宫格：只有「纵向」需要，而且边界是量出来的

对面板逐行求「核心带（左右各让开 60 px）内的最大通道标准差」：

```
 0– 47   std  29–108   标题牌位（凸起的那块）
48– 87   std 103–126   顶部装饰带
88–126   std  28–47    装饰带收尾
127–412  std  1.06–1.44  ← 纯色躯干（亮度恒 35–36，拉伸它等于什么都不做）
413–446  std  4–11     底部装饰 + 描边
```

所以 `spriteBorder = (left 0, bottom 48, right 0, top 128)`
（见 <c>UiLayout.PeekPanelBorderXxx</c> / 编辑器侧 `EnsurePeekImporters`）：

- **纵向**保留 128…398 这段绝对平坦的躯干做拉伸区间（两行牌时 ×2.0，肉眼零差别）；
- **左右 border = 0 = 横向永不拉伸**，宽度恒用原图的 992 ——
  这样顶部中央那块标题牌位就不会被抻开（九宫格的「上中」格一定会横向拉伸，
  而牌位正好在中央，保护不了它，只能选择「不改宽度」）。

用法:
    python slice_peek_kit.py --probe    # 打印包围盒 + 可拉伸行 / 列区间
    python slice_peek_kit.py --apply    # 执行切分
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
ART = PROJECT / "Assets" / "Art"
OUT = ART / "Ui"
SHEET = ART / "661ca76a-c73d-4618-b6e5-918c75968b58.png"

# 包围盒用的 alpha 阈值。取 8 而不是 24 —— 面板/牌背边缘有一圈很淡的辉光，
# 阈值太高会把辉光切掉，缩放后边缘会显出硬直角。
ALPHA_MIN = 8

# ── 元素表：(输出名, 源矩形 x,y,w,h) ──────────────────────────────────
ELEMENTS: list[tuple[str, tuple[int, int, int, int]]] = [
    ("Peek_Panel",      (35, 62, 992, 447)),
    ("Peek_Frame",      (1050, 40, 431, 556)),
    ("Peek_PanelSmall", (51, 656, 504, 281)),
    ("Peek_Banner",     (573, 681, 499, 192)),
    ("Peek_CardBack",   (1125, 646, 186, 256)),
]

# 这两块上还各压着一枚极小的点缀徽记（面板下沿 / 横幅下沿），
# 属于「装饰件」而不是版式件，本轮不切 —— 记在这里免得下次重新分析一遍。
DECOR_SKIPPED = "x≈258–343 与 x≈782–862, y≈906–936（两枚小徽记，暂不用）"


def tight_bbox(sheet: Image.Image, rect) -> tuple[int, int, int, int]:
    """在给定分区内求严格 alpha 包围盒（返回全局坐标）。"""
    x0, y0, w, h = rect
    a = np.asarray(sheet.crop((x0, y0, x0 + w, y0 + h)))[:, :, 3]
    mask = a > ALPHA_MIN
    ys = np.where(mask.any(axis=1))[0]
    xs = np.where(mask.any(axis=0))[0]
    if len(ys) == 0:
        return (x0, y0, 0, 0)
    return (x0 + int(xs.min()), y0 + int(ys.min()),
            int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1))


def flat_band(vals: np.ndarray, tol: float) -> tuple[int, int]:
    """取 vals 里 < tol 的最长连续段（没有则返回 (0,0)）。"""
    flat = np.where(vals < tol)[0]
    if len(flat) == 0:
        return (0, 0)

    best = cur = (int(flat[0]), int(flat[0]))
    for v in flat[1:]:
        v = int(v)
        if v == cur[1] + 1:
            cur = (cur[0], v)
        else:
            if cur[1] - cur[0] > best[1] - best[0]:
                best = cur
            cur = (v, v)

    if cur[1] - cur[0] > best[1] - best[0]:
        best = cur

    return best


def stretch_rows(img: Image.Image, inset: int = 60, tol: float = 2.0) -> tuple[int, int]:
    """
    找出「横向完全均匀」的行区间 —— 这些行纵向拉伸不会改变任何像素。

    只看中间那段（左右各让开 `inset` 像素），避开圆角与描边的横向变化。
    `tol` 是「均匀」的容忍度：这套图是 AI 画的颗粒质感，纯色躯干也不是逐像素相同，
    实测标准差分位在 1.0–1.5，装饰带则 >4 —— 2.0 能干净地把两者分开。
    """
    a = np.asarray(img).astype(np.int16)
    core = a[:, inset:img.width - inset, :]
    return flat_band(core.std(axis=1).max(axis=1), tol)


def stretch_cols(img: Image.Image, inset: int = 60, tol: float = 2.0) -> tuple[int, int]:
    """同上，纵向均匀的列区间。"""
    a = np.asarray(img).astype(np.int16)
    core = a[inset:img.height - inset, :, :]
    return flat_band(core.std(axis=0).max(axis=1), tol)


def load_sheet() -> Image.Image:
    if not SHEET.exists():
        raise SystemExit("找不到源图集：" + str(SHEET))
    return Image.open(SHEET).convert("RGBA")


def probe() -> None:
    sheet = load_sheet()
    print("# 源图集 %s  %s" % (SHEET.name, sheet.size))
    print()
    for name, rect in ELEMENTS:
        x, y, w, h = tight_bbox(sheet, rect)
        print("%-16s 全局 x=%-4d y=%-4d w=%-4d h=%-4d aspect=%.4f"
              % (name, x, y, w, h, (w / h) if h else 0))

    print()
    print("# 可拉伸区间（方差 = 0 的行 / 列，九宫格边界取它之外）")
    for name, rect in ELEMENTS:
        x, y, w, h = tight_bbox(sheet, rect)
        img = sheet.crop((x, y, x + w, y + h))
        r0, r1 = stretch_rows(img)
        c0, c1 = stretch_cols(img)
        print("%-16s 纵向可拉伸行 %d..%d  → border top=%d bottom=%d"
              % (name, r0, r1, r0, h - 1 - r1))
        print("%-16s 横向可拉伸列 %d..%d  → border left=%d right=%d"
              % ("", c0, c1, c0, w - 1 - c1))

    print()
    print("# 未切： " + DECOR_SKIPPED)


def apply() -> None:
    sheet = load_sheet()
    OUT.mkdir(parents=True, exist_ok=True)

    for name, rect in ELEMENTS:
        x, y, w, h = tight_bbox(sheet, rect)
        if w == 0 or h == 0:
            raise SystemExit("%s 分区里没有像素，坐标表需要复核" % name)
        sheet.crop((x, y, x + w, y + h)).save(OUT / (name + ".png"))
        print("  → %-16s %4d×%-4d  (%d,%d)" % (name + ".png", w, h, x, y))

    print()
    print("完成。接下来在 Unity 里跑：")
    print("  1) 菜单 `魔法乱斗/整理 · 配置新美术导入`（把源表归档进 Ui/_Source、重设导入参数）")
    print("  2) 菜单 `魔法乱斗/M8 · 构建 BattleUi`（它会把 Peek 面板的九宫格 border 也校验一遍）")


def main() -> None:
    if "--apply" in sys.argv:
        apply()
    elif "--probe" in sys.argv:
        probe()
    else:
        print(__doc__)
        print("（加 --probe 看包围盒与可拉伸区间，加 --apply 执行切分）")


if __name__ == "__main__":
    main()
