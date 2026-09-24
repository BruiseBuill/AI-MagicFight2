#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
把 Art/Icons 下的三张触发符号（剑 α / 盾 β / 感叹号 γ）合成**一张 TMP 图集**。

为什么必须合成一张：
    TMP 的 `TMP_SpriteAsset` 只有**一个** `spriteSheet` 贴图和一份材质，所有
    inline sprite 都从这一张图里取区域。三张独立 PNG 做不出图文混排。

为什么不能各自紧贴裁切：
    三张图标共用同一块 1024×1024 透明画布且**没有缩放过** —— 画布内的相对大小
    就是设计稿里的相对大小（剑比盾瘦、感叹号比盾宽）。
    若各自按自己的包围盒拉到同样大小，三个符号会变得一样大，肉眼能看出不协调。
    做法 = 取三者的**并集包围盒**当统一取景框，一起裁、一起缩放 → 相对大小被保住。

为什么单元格是「紧裁 + 一点留白」而不是正方形：
    TMP 的 inline sprite 靠 `metrics.horizontalAdvance` 推进光标。正方形单元格里
    内容只占中间一条，为了不让相邻文字离得太远就得给负的 bearingX —— 那是没必要的
    复杂度。把单元格贴着内容裁（四边只留几个像素防采样出血），advance 就等于
    「图标宽度 + 一点点」，中英混排的间距自然。

⚠ 本脚本输出的尺寸必须与
    `Assets/Scripts/Unity/Editor/TriggerSpriteAssetBuilder.cs`
    里的常量一致 —— 脚本跑完会把该填的数字直接打印出来，照抄过去即可。

用法：
    python build_tmp_icon_sheet.py            # 只打印计划（dry-run）
    python build_tmp_icon_sheet.py --apply    # 写盘
"""
import argparse
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ICON_DIR = os.path.join(ROOT, "Assets", "Art", "Icons")
OUT_NAME = "IconSheet_触发符号_TMP.png"
WORK_DIR = os.path.join(ROOT, "Tools", "art-audit", "_work")

# 顺序与 TriggerIconLibrary 一致：Attack=剑、Defend=盾、Special=感叹号
SOURCES = [
    ("attack", "Icon_01_attack_剑.png"),
    ("defend", "Icon_02_defend_盾.png"),
    ("special", "Icon_03_special_感叹号.png"),
]

TARGET_CONTENT_H = 232   # 取景框缩放后的高度（图集内的像素）
PAD = 4                  # 四边留白，防双线性采样把边吃出锯齿


def union_bbox(images):
    """三张图的 alpha 包围盒并集 —— 统一取景框，保住相对大小。"""
    box = None
    for im in images:
        bb = im.split()[3].getbbox()
        if bb is None:
            raise SystemExit("有一张图是全透明的，取不到景")
        box = bb if box is None else (
            min(box[0], bb[0]), min(box[1], bb[1]),
            max(box[2], bb[2]), max(box[3], bb[3]),
        )
    return box


def build():
    images = []
    for key, name in SOURCES:
        path = os.path.join(ICON_DIR, name)
        if not os.path.exists(path):
            raise SystemExit("缺素材：" + path)
        images.append((key, Image.open(path).convert("RGBA")))

    box = union_bbox([im for _, im in images])
    content_w = box[2] - box[0]
    content_h = box[3] - box[1]
    scale = TARGET_CONTENT_H / float(content_h)      # 按高度定标 → 相对大小保住

    inner_w = max(1, int(round(content_w * scale)))
    inner_h = TARGET_CONTENT_H
    cell_w = inner_w + PAD * 2
    cell_h = inner_h + PAD * 2

    sheet = Image.new("RGBA", (cell_w * len(images), cell_h), (0, 0, 0, 0))
    plan = []

    for i, (key, im) in enumerate(images):
        resized = im.crop(box).resize((inner_w, inner_h), Image.LANCZOS)
        sheet.paste(resized, (i * cell_w + PAD, PAD), resized)
        # 每个图标在统一取景框内的**自身**包围盒，用来核对相对大小没丢
        own = resized.split()[3].getbbox()
        plan.append((key, i * cell_w, (own[2] - own[0], own[3] - own[1])))

    return sheet, plan, box, scale, (cell_w, cell_h)


def preview(sheet):
    """核对图：图集按 1:1 与三档实机尺寸排开，肉眼过一遍。"""
    heights = [sheet.height, 40, 24, 16]
    gap = 14
    blocks = []
    for h in heights:
        w = max(1, int(round(sheet.width * h / float(sheet.height))))
        blocks.append(sheet.resize((w, h), Image.LANCZOS))
    width = sum(b.width for b in blocks) + gap * (len(blocks) + 1)
    height = max(b.height for b in blocks) + gap * 2
    canvas = Image.new("RGBA", (width, height), (32, 28, 36, 255))
    x = gap
    for b in blocks:
        canvas.paste(b, (x, gap), b)
        x += b.width + gap
    return canvas


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="真正写盘")
    args = ap.parse_args()

    sheet, plan, box, scale, cell = build()
    cell_w, cell_h = cell

    print("统一取景框（并集包围盒） = %s  内容 %d×%d" % (box, box[2] - box[0], box[3] - box[1]))
    print("缩放比 = %.5f（目标内容高 %d，四边留白 %d）" % (scale, TARGET_CONTENT_H, PAD))
    print("单元格 = %d×%d   图集 = %d×%d" % (cell_w, cell_h, sheet.width, sheet.height))
    for key, x, own in plan:
        print("  %-8s glyphRect=(%d, 0, %d, %d)  自身内容 %d×%d  (占格宽 %.0f%%)"
              % (key, x, cell_w, cell_h, own[0], own[1], own[0] * 100.0 / cell_w))

    out = os.path.join(ICON_DIR, OUT_NAME)
    work = os.path.join(WORK_DIR, "tmp_icon_sheet_preview.png")

    if not args.apply:
        print("\n[dry-run] 未写盘。加 --apply 才会写出：\n  " + out + "\n  " + work)
        return 0

    os.makedirs(WORK_DIR, exist_ok=True)
    sheet.save(out)
    preview(sheet).save(work)

    print("\n已写出：\n  " + out + "\n  " + work)
    print("\n── 复制进 TriggerSpriteAssetBuilder.cs 的常量 ──")
    print("        private const int   CellWidth  = %d;" % cell_w)
    print("        private const int   CellHeight = %d;" % cell_h)
    print("        private const float ContentHeight = %d.0f;" % TARGET_CONTENT_H)
    print("        private const int   Pad        = %d;" % PAD)
    return 0


if __name__ == "__main__":
    sys.exit(main())
