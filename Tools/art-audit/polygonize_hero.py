# -*- coding: utf-8 -*-
"""Reduce the hero art to broad, faceted color planes.

The script keeps the existing PNG dimensions, alpha, filenames, and Unity meta
files intact. It writes a review copy first; --apply creates a timestamped
backup before replacing the active hero frames and portraits.
"""
from __future__ import annotations

import argparse
import shutil
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw


PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"
HERO = ART / "Chars" / "Hero"
UI = ART / "Ui"
PREVIEW = PROJECT / "Artifacts" / "work" / "hero_polygon_v1"

POSES = ("idle", "attack", "defend", "behit", "death")
FRAME_COUNTS = {"idle": 16, "attack": 12, "defend": 12, "behit": 16, "death": 16}


def load_rgba(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.uint8)


def palette_for(rgb: np.ndarray, alpha: np.ndarray, colors: int) -> np.ndarray:
    solid = alpha > 24
    pixels = rgb[solid].reshape(-1, 3).astype(np.float32)
    if len(pixels) == 0:
        return np.array([[32, 42, 48]], dtype=np.uint8)
    if len(pixels) > 26000:
        step = max(1, len(pixels) // 26000)
        pixels = pixels[::step]

    pixels = np.ascontiguousarray(pixels)
    criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 28, 0.7)
    count = min(colors, len(pixels))
    _compactness, _labels, centers = cv2.kmeans(
        pixels, count, None, criteria, 2, cv2.KMEANS_PP_CENTERS
    )
    return np.clip(np.rint(centers), 0, 255).astype(np.uint8)


def nearest_palette(rgb: np.ndarray, palette: np.ndarray) -> np.ndarray:
    values = rgb.astype(np.int32)
    distances = np.empty((*values.shape[:2], len(palette)), dtype=np.int32)
    for index, color in enumerate(palette):
        delta = values - color.astype(np.int32)
        distances[..., index] = np.sum(delta * delta, axis=2)
    return palette[np.argmin(distances, axis=2)]


def smooth_and_flatten(rgb: np.ndarray, alpha: np.ndarray, colors: int) -> tuple[np.ndarray, np.ndarray]:
    """Flatten brush detail while keeping the silhouette mask out of the filter."""
    background = np.array([18, 28, 34], dtype=np.uint8)
    work = rgb.copy()
    work[alpha < 8] = background
    bgr = cv2.cvtColor(work, cv2.COLOR_RGB2BGR)
    # Mean-shift creates larger material regions before palette reduction.
    filtered = cv2.pyrMeanShiftFiltering(bgr, sp=10, sr=25, maxLevel=1)
    filtered = cv2.cvtColor(filtered, cv2.COLOR_BGR2RGB)
    palette = palette_for(filtered, alpha, colors)
    return nearest_palette(filtered, palette), palette


def triangle_grid(width: int, height: int, step: int):
    """Yield a stable, slightly irregular triangular grid."""
    xs = list(range(0, width, step))
    ys = list(range(0, height, step))

    def point(x: int, y: int) -> tuple[int, int]:
        # The offsets depend only on grid position, so every animation frame
        # receives the same facet layout and does not shimmer during playback.
        if x in (0, width) or y in (0, height):
            return x, y
        jx = ((x * 17 + y * 7) % 7) - 3
        jy = ((x * 5 + y * 19) % 7) - 3
        return max(0, min(width, x + jx)), max(0, min(height, y + jy))

    for row, y in enumerate(ys):
        for col, x in enumerate(xs):
            x1, y1 = min(width, x + step), min(height, y + step)
            if x1 <= x or y1 <= y:
                continue
            p00, p10 = point(x, y), point(x1, y)
            p01, p11 = point(x, y1), point(x1, y1)
            if (row + col) % 2 == 0:
                yield (p00, p10, p11), (p00, p11, p01)
            else:
                yield (p00, p10, p01), (p10, p11, p01)


def facetize(rgb: np.ndarray, alpha: np.ndarray, base: np.ndarray,
             palette: np.ndarray, step: int) -> np.ndarray:
    """Paint broad triangle facets, clipped by the original alpha silhouette."""
    height, width = alpha.shape
    layer = Image.new("RGB", (width, height), (0, 0, 0))
    layer_pixels = np.asarray(layer).copy()
    source = base.astype(np.float32)

    for first, second in triangle_grid(width, height, step):
        for polygon in (first, second):
            mask = Image.new("L", (width, height), 0)
            ImageDraw.Draw(mask).polygon(polygon, fill=255)
            polygon_mask = np.asarray(mask) > 0
            solid = polygon_mask & (alpha > 32)
            count = int(solid.sum())
            if count < max(10, step * step // 6):
                continue
            coverage = count / max(1, int(polygon_mask.sum()))
            # Near the silhouette, keep the original edge instead of filling
            # the transparent corner of a triangle.
            if coverage < 0.42:
                continue
            color = source[solid].mean(axis=0)
            distances = ((palette.astype(np.float32) - color[None, :]) ** 2).sum(axis=1)
            flat_color = palette[int(np.argmin(distances))]
            layer_pixels[polygon_mask] = flat_color

    # The triangle layer only controls color; the source alpha controls edges.
    result = layer_pixels
    result[alpha <= 8] = rgb[alpha <= 8]
    return result


def outline(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """Add a restrained dark outer contour to make the simplified planes read."""
    solid = (alpha > 20).astype(np.uint8)
    expanded = cv2.dilate(solid, np.ones((3, 3), np.uint8), iterations=1)
    border = (expanded > 0) & (solid == 0) & (alpha == 0)
    out = rgb.copy()
    out[border] = np.array([17, 28, 32], dtype=np.uint8)
    return out


def polygonize(path: Path) -> Image.Image:
    rgba = load_rgba(path)
    rgb, alpha = rgba[..., :3], rgba[..., 3]
    height, width = alpha.shape
    colors = 20 if min(width, height) < 350 else 24
    step = max(16, min(width, height) // 14)
    base, palette = smooth_and_flatten(rgb, alpha, colors)
    faceted = facetize(rgb, alpha, base, palette, step)
    faceted = outline(faceted, alpha)
    return Image.fromarray(np.dstack([faceted, alpha]), mode="RGBA")


def make_portrait(idle: Image.Image) -> Image.Image:
    rgba = np.asarray(idle.convert("RGBA"))
    alpha = rgba[..., 3]
    ys, xs = np.where(alpha > 24)
    if len(xs) == 0:
        return idle
    left, right = max(0, int(xs.min()) - 8), min(idle.width, int(xs.max()) + 9)
    top = max(0, int(ys.min()) - 5)
    bottom = min(idle.height, top + int((ys.max() - top + 1) * 0.73))
    crop = idle.crop((left, top, right, bottom))
    canvas = Image.new("RGBA", (259, 311), (0, 0, 0, 0))
    scale = min(235 / crop.width, 292 / crop.height)
    size = (max(1, int(round(crop.width * scale))), max(1, int(round(crop.height * scale))))
    crop = crop.resize(size, Image.Resampling.LANCZOS)
    canvas.alpha_composite(crop, ((canvas.width - crop.width) // 2, 8))
    return canvas


def make_hud_avatar(idle: Image.Image) -> Image.Image:
    rgba = np.asarray(idle.convert("RGBA"))
    alpha = rgba[..., 3]
    ys, xs = np.where(alpha > 24)
    if len(xs) == 0:
        return Image.new("RGBA", (116, 95), (19, 31, 39, 255))
    left, right = max(0, int(xs.min()) - 5), min(idle.width, int(xs.max()) + 6)
    top = max(0, int(ys.min()) - 4)
    bottom = min(idle.height, top + int((ys.max() - top + 1) * 0.62))
    crop = idle.crop((left, top, right, bottom))
    canvas = Image.new("RGBA", (116, 95), (19, 31, 39, 255))
    draw = ImageDraw.Draw(canvas)
    draw.polygon([(3, 9), (11, 3), (105, 3), (113, 11), (113, 84), (105, 92), (11, 92), (3, 84)],
                 fill=(24, 51, 61, 255), outline=(69, 111, 120, 255), width=2)
    scale = min(101 / crop.width, 86 / crop.height)
    size = (max(1, int(round(crop.width * scale))), max(1, int(round(crop.height * scale))))
    crop = crop.resize(size, Image.Resampling.LANCZOS)
    canvas.alpha_composite(crop, ((canvas.width - crop.width) // 2, 5))
    draw = ImageDraw.Draw(canvas)
    draw.line([(5, 12), (5, 82), (13, 90), (103, 90), (111, 82)], fill=(9, 23, 29, 220), width=2)
    return canvas


def make_hud_bar(bar: Image.Image, avatar: Image.Image) -> Image.Image:
    result = bar.convert("RGBA")
    result.alpha_composite(avatar, (0, 0))
    return result


def iter_inputs():
    for pose in POSES:
        for index in range(1, FRAME_COUNTS[pose] + 1):
            path = HERO / pose / f"Hero_{pose}_{index:02d}.png"
            if path.exists():
                yield path


def relative_output(path: Path) -> Path:
    return PREVIEW / path.relative_to(PROJECT)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="replace active files after making a backup")
    args = parser.parse_args()

    files = list(iter_inputs())
    if not files:
        raise SystemExit("No active hero frames found")

    if PREVIEW.exists():
        shutil.rmtree(PREVIEW)
    for source in files:
        target = relative_output(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        polygonize(source).save(target)

    idle_preview = relative_output(HERO / "idle" / "Hero_idle_01.png")
    portrait_target = PREVIEW / "Assets/Art/Chars/Hero/Hero_portrait.png"
    portrait_target.parent.mkdir(parents=True, exist_ok=True)
    make_portrait(Image.open(idle_preview)).save(portrait_target)
    hud_target = PREVIEW / "Assets/Art/Ui/Hud_Avatar.png"
    hud_target.parent.mkdir(parents=True, exist_ok=True)
    avatar = make_hud_avatar(Image.open(idle_preview))
    avatar.save(hud_target)
    bar_target = PREVIEW / "Assets/Art/Ui/Hud_Bar.png"
    bar_target.parent.mkdir(parents=True, exist_ok=True)
    make_hud_bar(Image.open(UI / "Hud_Bar.png"), avatar).save(bar_target)

    if args.apply:
        backup = PROJECT / "Artifacts" / "backup" / ("hero_pre_polygon_v1_" + time.strftime("%Y%m%d_%H%M%S"))
        for source in files + [HERO / "Hero_portrait.png", UI / "Hud_Avatar.png", UI / "Hud_Bar.png"]:
            dest = backup / source.relative_to(PROJECT)
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, dest)
            shutil.copy2(relative_output(source), source)
        print(f"Applied {len(files)} frames and 2 portraits. Backup: {backup}")
    else:
        print(f"Wrote review copy: {PREVIEW}")


if __name__ == "__main__":
    main()
