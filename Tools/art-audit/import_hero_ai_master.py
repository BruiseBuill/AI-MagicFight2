# -*- coding: utf-8 -*-
"""Fit the approved LikeAI hero master into the existing Unity art slots."""
from __future__ import annotations

import argparse
import math
import shutil
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


PROJECT = Path(__file__).resolve().parents[2]
MASTER = PROJECT / "Artifacts" / "work" / "imagegen" / "hero-polygon-card-v2-transparent.png"
HERO = PROJECT / "Assets" / "Art" / "Chars" / "Hero"
UI = PROJECT / "Assets" / "Art" / "Ui"
PREVIEW = PROJECT / "Artifacts" / "work" / "hero_ai_v2"

FRAME_W, FRAME_H = 272, 331
PIVOT_X, BASELINE_Y = 136, 324


def visible_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    alpha = np.asarray(image.getchannel("A"))
    ys, xs = np.where(alpha > 18)
    if len(xs) == 0:
        raise ValueError("The AI master has no visible pixels")
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def fit_content(crop: Image.Image, max_width: int, max_height: int) -> Image.Image:
    scale = min(max_width / crop.width, max_height / crop.height)
    size = (max(1, round(crop.width * scale)), max(1, round(crop.height * scale)))
    return crop.resize(size, Image.Resampling.LANCZOS)


def idle_frame(source: Image.Image, phase: int) -> Image.Image:
    box = visible_bbox(source)
    crop = source.crop(box)
    # Keep the new AI master close to the established battle scale and hold the
    # feet on the existing pivot line while adding a restrained breathing loop.
    bob = math.sin((phase / 16.0) * math.tau) * 1.5
    scale = 1.0 + math.sin((phase / 16.0) * math.tau) * 0.012
    max_height = round(314 * scale)
    fitted = fit_content(crop, 258, max_height)
    x = round(PIVOT_X - fitted.width / 2 + math.sin((phase / 16.0) * math.tau) * 0.8)
    y = round(BASELINE_Y - fitted.height + bob)
    canvas = Image.new("RGBA", (FRAME_W, FRAME_H), (0, 0, 0, 0))
    canvas.alpha_composite(fitted, (x, y))
    return canvas


def full_portrait(source: Image.Image) -> Image.Image:
    box = visible_bbox(source)
    crop = source.crop(box)
    fitted = fit_content(crop, 244, 298)
    canvas = Image.new("RGBA", (259, 311), (0, 0, 0, 0))
    canvas.alpha_composite(fitted, ((canvas.width - fitted.width) // 2, 7))
    return canvas


def hud_avatar(source: Image.Image) -> Image.Image:
    box = visible_bbox(source)
    left, top, right, bottom = box
    # Head, scarf, and a little of the coat; keep the crop tied to the master
    # rather than hand-tuning pixel coordinates for one generated image.
    head_bottom = top + round((bottom - top) * 0.52)
    crop = source.crop((left, top, right, head_bottom))
    fitted = fit_content(crop, 101, 86)

    canvas = Image.new("RGBA", (116, 95), (19, 31, 39, 255))
    draw = ImageDraw.Draw(canvas)
    draw.polygon(
        [(3, 9), (11, 3), (105, 3), (113, 11), (113, 84), (105, 92), (11, 92), (3, 84)],
        fill=(24, 51, 61, 255),
        outline=(69, 111, 120, 255),
        width=2,
    )
    canvas.alpha_composite(fitted, ((canvas.width - fitted.width) // 2, 5))
    return canvas


def hud_bar(source: Image.Image, avatar: Image.Image) -> Image.Image:
    result = source.convert("RGBA")
    result.alpha_composite(avatar, (0, 0))
    return result


def write_preview() -> None:
    if not MASTER.exists():
        raise SystemExit(f"Missing approved master: {MASTER}")
    if PREVIEW.exists():
        shutil.rmtree(PREVIEW)

    source = Image.open(MASTER).convert("RGBA")
    idle_dir = PREVIEW / "Assets" / "Art" / "Chars" / "Hero" / "idle"
    idle_dir.mkdir(parents=True, exist_ok=True)
    for phase in range(16):
        idle_frame(source, phase).save(idle_dir / f"Hero_idle_{phase + 1:02d}.png")

    hero_dir = PREVIEW / "Assets" / "Art" / "Chars" / "Hero"
    hero_dir.mkdir(parents=True, exist_ok=True)
    portrait = full_portrait(source)
    portrait.save(hero_dir / "Hero_portrait.png")

    ui_dir = PREVIEW / "Assets" / "Art" / "Ui"
    ui_dir.mkdir(parents=True, exist_ok=True)
    avatar = hud_avatar(source)
    avatar.save(ui_dir / "Hud_Avatar.png")
    hud_bar(Image.open(UI / "Hud_Bar.png"), avatar).save(ui_dir / "Hud_Bar.png")


def apply_preview() -> None:
    backup = PROJECT / "Artifacts" / "backup" / ("hero_pre_ai_v2_" + time.strftime("%Y%m%d_%H%M%S"))
    sources = [
        *(HERO / "idle").glob("Hero_idle_*.png"),
        HERO / "Hero_portrait.png",
        UI / "Hud_Avatar.png",
        UI / "Hud_Bar.png",
    ]
    for source in sources:
        destination = backup / source.relative_to(PROJECT)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)

    for source in sources:
        generated = PREVIEW / source.relative_to(PROJECT)
        shutil.copy2(generated, source)
    print(f"Applied AI master to 16 idle frames and 3 portrait/HUD assets. Backup: {backup}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    write_preview()
    if args.apply:
        apply_preview()
    else:
        print(f"Wrote review copy: {PREVIEW}")


if __name__ == "__main__":
    main()
