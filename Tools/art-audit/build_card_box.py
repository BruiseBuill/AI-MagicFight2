#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
生成组成式卡面需要的两块圆角矩形底图（**九宫格切图**）。

为什么不用 `ThirdParty/MagicCardKit` 自带的圆角材质：
    那套 `RoundBox` / `RoundBoxLine` 是 **ShaderGraph**，为 3D 展示卡写的。实测塞进 UGUI 后
    整张卡渲成纯白 —— 它不吃 UGUI 的顶点色，`_MainTex` 也不走 `CanvasRenderer.SetTexture`
    那条路；`_size` 的 G/B 通道被当成宽高、`_ColorMask` 还被接到 SampleTexture2D 的 UV 上。
    参数含义没有文档，调不出来。改用「一张九宫格圆角图 + 普通 UI/Default 材质」：可预期、
    能染色、任意尺寸不变形。

为什么不用 `ThirdParty/PolySprite/RoundedRectangle.png`：
    它是 256×256、圆角只有 16 px、描边 16 px —— 九宫格切图时圆角与描边都按精灵原生像素
    画（不随目标尺寸缩放），落到 622 宽的卡上圆角只有 6 px、描边 6 px，跟设计稿差一个量级。

输出（PPU 100 → 精灵像素 = UI 单位，因此这里的 30 px 圆角就是设计空间里的 30 单位）：
    Assets/Art/Ui/CardBox.png       填充圆角矩形
    Assets/Art/Ui/CardBox_Line.png  圆角矩形描边

⚠ 改这里的尺寸必须同步 `Assets/Scripts/Unity/Editor/UiKitBuilder.cs` 里的
   `CardBoxBorder`（= 九宫格 border，单位是**精灵像素**）和 UiLayout 的 `CardSpaceRadius`。

用法：
    python build_card_box.py           # dry-run
    python build_card_box.py --apply   # 写盘
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "Art", "Ui")
WORK_DIR = os.path.join(ROOT, "Tools", "art-audit", "_work")

SPRITE = 160     # 精灵边长
INSET = 2        # 形状距精灵边缘的留白（防九宫格采样把抗锯齿边切掉）
RADIUS = 30      # 圆角半径（精灵像素 = UI 单位，见文件头）
STROKE = 4       # 描边粗细
SS = 4           # 超采样倍数
BORDER = INSET + RADIUS + 6  # 九宫格 border：必须把整段圆角包住再留一点直边

FILL_NAME = "CardBox.png"
LINE_NAME = "CardBox_Line.png"


def rounded_rect_mask(size, inset, radius, ss):
    """用超采样画一个抗锯齿的圆角矩形蒙版。"""
    m = Image.new("L", (size * ss, size * ss), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle(
        [inset * ss, inset * ss, (size - inset) * ss - 1, (size - inset) * ss - 1],
        radius=radius * ss,
        fill=255,
    )
    return m.resize((size, size), Image.LANCZOS)


def build():
    outer = rounded_rect_mask(SPRITE, INSET, RADIUS, ss=SS)

    # 描边 = 外圆角矩形 − 内圆角矩形（内圈半径同步缩小 STROKE，否则内壁会出现直角）
    inner = rounded_rect_mask(SPRITE, INSET + STROKE, max(1, RADIUS - STROKE), ss=SS)
    ring = Image.new("L", (SPRITE, SPRITE), 0)
    ring.paste(outer)
    ring = Image.composite(Image.new("L", (SPRITE, SPRITE), 0), ring, inner)

    fill_img = Image.new("RGBA", (SPRITE, SPRITE), (255, 255, 255, 255))
    fill_img.putalpha(outer)

    line_img = Image.new("RGBA", (SPRITE, SPRITE), (255, 255, 255, 255))
    line_img.putalpha(ring)

    return fill_img, line_img


def preview(fill_img, line_img):
    """核对图：原尺寸 + 拉成三种真实比例的九宫格效果（必须自己实现九宫格，才看得见变形）。"""
    def nine_slice(src, w, h, b):
        out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        # 四角 1:1
        out.paste(src.crop((0, 0, b, b)), (0, 0))
        out.paste(src.crop((src.width - b, 0, src.width, b)), (w - b, 0))
        out.paste(src.crop((0, src.height - b, b, src.height)), (0, h - b))
        out.paste(src.crop((src.width - b, src.height - b, src.width, src.height)), (w - b, h - b))
        # 四边拉伸
        out.paste(src.crop((b, 0, src.width - b, b)).resize((w - 2 * b, b)), (b, 0))
        out.paste(src.crop((b, src.height - b, src.width - b, src.height)).resize((w - 2 * b, b)), (b, h - b))
        out.paste(src.crop((0, b, b, src.height - b)).resize((b, h - 2 * b)), (0, b))
        out.paste(src.crop((src.width - b, b, src.width, src.height - b)).resize((b, h - 2 * b)), (w - b, b))
        # 中间
        out.paste(src.crop((b, b, src.width - b, src.height - b)).resize((w - 2 * b, h - 2 * b)), (b, b))
        return out

    targets = [(196, 272), (138, 194), (462, 92), (570, 236)]
    gap = 16
    width = gap + sum(t[0] for t in targets) + gap * len(targets)
    height = max(t[1] for t in targets) + gap * 2
    canvas = Image.new("RGBA", (width, height), (58, 50, 62, 255))
    x = gap
    for w, h in targets:
        canvas.paste(nine_slice(fill_img, w, h, BORDER), (x, gap), nine_slice(fill_img, w, h, BORDER))
        canvas.paste(nine_slice(line_img, w, h, BORDER), (x, gap), nine_slice(line_img, w, h, BORDER))
        x += w + gap
    return canvas


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="真正写盘")
    args = ap.parse_args()

    fill_img, line_img = build()

    print("精灵 %d×%d  形状内缩 %d  圆角 %d  描边 %d  九宫格 border = %d"
          % (SPRITE, SPRITE, INSET, RADIUS, STROKE, BORDER))
    print("PPU 计划 = 100 → 精灵像素与 UI 单位 1:1，圆角/描边即为设计空间里的 %d / %d"
          % (RADIUS, STROKE))

    fill_out = os.path.join(OUT_DIR, FILL_NAME)
    line_out = os.path.join(OUT_DIR, LINE_NAME)
    work = os.path.join(WORK_DIR, "card_box_preview.png")

    if not args.apply:
        print("\n[dry-run] 未写盘。加 --apply 才会写出：\n  " + fill_out + "\n  " + line_out + "\n  " + work)
        return 0

    os.makedirs(WORK_DIR, exist_ok=True)
    fill_img.save(fill_out)
    line_img.save(line_out)
    preview(fill_img, line_img).save(work)

    print("\n已写出：\n  " + fill_out + "\n  " + line_out + "\n  " + work)
    print("\n── 复制进 UiKitBuilder.cs 的常量 ──")
    print("        private const int CardBoxBorder = %d;" % BORDER)
    return 0


if __name__ == "__main__":
    sys.exit(main())
