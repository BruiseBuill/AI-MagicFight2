# -*- coding: utf-8 -*-
"""把 PNG 以 ASCII 灰度图打印到终端，用于在「看不到图」的环境里辨认元素形状。

用法:
  python ascii_preview.py <png> [--cols 150] [--bbox x,y,w,h] [--mode lum|alpha]
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

RAMP = " .:-=+*#%@"


def render(img: Image.Image, cols: int, mode: str) -> str:
    w, h = img.size
    rows = max(1, int(round(cols * h / w * 0.5)))  # 字符高宽比 ~2:1
    small = img.resize((cols, rows), Image.LANCZOS)
    a = np.asarray(small).astype(np.float32)

    if a.ndim == 2:
        a = np.stack([a] * 3 + [np.full_like(a, 255)], axis=-1)

    alpha = a[:, :, 3] / 255.0
    lum = (0.299 * a[:, :, 0] + 0.587 * a[:, :, 1] + 0.114 * a[:, :, 2]) / 255.0

    if mode == "alpha":
        v = alpha
    else:
        # 透明处按白底合成
        v = lum * alpha + 1.0 * (1.0 - alpha)

    idx = np.clip((v * (len(RAMP) - 1)).round().astype(int), 0, len(RAMP) - 1)
    lines = []
    for r in range(rows):
        line = "".join(RAMP[i] for i in idx[r])
        lines.append(f"{r:>3}|{line}|")
    return "\n".join(lines)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("png")
    ap.add_argument("--cols", type=int, default=150)
    ap.add_argument("--bbox", type=str, default="")
    ap.add_argument("--mode", choices=["lum", "alpha"], default="lum")
    args = ap.parse_args()

    img = Image.open(args.png).convert("RGBA")
    print(f"# {Path(args.png).name}  size={img.size}  mode={args.mode}")
    if args.bbox:
        x, y, w, h = (int(t) for t in args.bbox.split(","))
        img = img.crop((x, y, x + w, y + h))
        print(f"# crop=({x},{y},{w},{h}) -> {img.size}")

    print(render(img, args.cols, args.mode))
    # 列标尺
    print("    " + "".join(str((i // 10) % 10) for i in range(args.cols)))
    print("    " + "".join(str(i % 10) for i in range(args.cols)))


if __name__ == "__main__":
    main()
