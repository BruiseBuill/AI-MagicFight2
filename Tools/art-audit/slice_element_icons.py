"""把七元素符号表切成 7 张独立透明底 PNG。

输出：`Assets/Resources/Elements/Element_<序号>_<英文名>.png`（256×256，居中、含 6% 留白）

⚠ **输出目录就是 `Assets/Resources/Elements/` —— 不要另存一份。**
运行时由 `ElementIconLibrary` 用 `Resources.Load<Sprite>("Elements/Element_XX_Name")`
按名取图，所以那批图**必须**落在 Resources 下；再往 `Art/Icons/Elements/` 里放一份副本
只会变成「改了一份、另一份还是旧的」的静默错。源表 `Type.png` 归档在
`Assets/Art/Icons/Elements/_Source/Type.png`（由 `ArtImportBuilder` 搬），
那是唯一输入，本脚本从它切。

切法依据：源图 1536×1024，元素按 4 + 3 两行排布，用 alpha 连通区（行/列投影）量出
每个元素的外接框，再各自切出来、居中放进方形画布 —— 不做等分网格，
因为源图里 7 个元素的宽高与间距并不一致，等分一定会切到邻居的边或者切掉尖角。

用法（在工程根目录，先把 Type.png 归位到 _Source 或直接改 SRC）：
    python Tools/art-audit/slice_element_icons.py
"""

import os

import numpy as np
from PIL import Image

SRC = r"Assets/Art/Icons/Elements/_Source/Type.png"
OUT_DIR = r"Assets/Resources/Elements"

# (行上界, 行下界, 列左界, 列右界, 输出名) —— 均为「含端点」的图像坐标（左上原点）
CELLS = [
    (49, 503, 28, 369, "01_Ice"),
    (49, 503, 406, 756, "02_Water"),
    (49, 503, 810, 1131, "03_Electric"),
    (49, 503, 1181, 1499, "04_Fire"),
    (539, 963, 174, 556, "05_Grass"),
    (539, 963, 607, 929, "06_Stone"),
    (539, 963, 994, 1360, "07_Curse"),
]

# 元素行的上下边界（由行投影量出，两行之间是干净的空行）
ROW_BANDS = [(49, 503), (539, 963)]

SIZE = 256          # 输出边长
PADDING = 0.06      # 留白比例（相对外接框长边）


def bands(profile, threshold=0):
    """把一维投影里的「非空连续段」找出来（[start, end] 闭区间）。"""
    out = []
    start = None
    for i, v in enumerate(profile):
        if v > threshold and start is None:
            start = i
        elif v <= threshold and start is not None:
            out.append((start, i - 1))
            start = None
    if start is not None:
        out.append((start, len(profile) - 1))
    return out


def main():
    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    src = os.path.join(project_root, SRC.replace("/", os.sep))
    out_dir = os.path.join(project_root, OUT_DIR.replace("/", os.sep))
    os.makedirs(out_dir, exist_ok=True)

    image = Image.open(src).convert("RGBA")
    alpha = np.array(image)[:, :, 3]
    opaque = alpha > 16

    # 自检：源图的行分带必须与硬编码的两行一致；不一致说明换了图，硬编码的列边界要重新量
    row_bands = bands(opaque.sum(axis=1))
    if [tuple(b) for b in row_bands] != [tuple(b) for b in ROW_BANDS]:
        raise SystemExit("行分带与预期不符：%s（预期 %s）—— 请重新量列边界。" % (row_bands, ROW_BANDS))

    for r0, r1, c0, c1, name in CELLS:
        sub = opaque[r0:r1 + 1, c0:c1 + 1]
        rows = np.where(sub.any(axis=1))[0]
        cols = np.where(sub.any(axis=0))[0]
        if rows.size == 0 or cols.size == 0:
            raise SystemExit("单元格 %s 是空的 —— 列边界量错了。" % name)

        y0, y1 = r0 + rows[0], r0 + rows[-1]
        x0, x1 = c0 + cols[0], c0 + cols[-1]
        crop = image.crop((x0, y0, x1 + 1, y1 + 1))

        w, h = crop.size
        side = max(w, h)
        canvas_side = side + int(side * PADDING) * 2
        canvas = Image.new("RGBA", (canvas_side, canvas_side), (0, 0, 0, 0))
        canvas.paste(crop, ((canvas_side - w) // 2, (canvas_side - h) // 2), crop)
        canvas = canvas.resize((SIZE, SIZE), Image.LANCZOS)

        path = os.path.join(out_dir, "Element_%s.png" % name)
        canvas.save(path)
        print("%-14s 源框 %s 尺寸 %sx%s → %s" % (name, (x0, y0, x1, y1), w, h, path))


if __name__ == "__main__":
    main()
