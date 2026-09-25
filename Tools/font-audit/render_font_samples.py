# -*- coding: utf-8 -*-
"""
魔法乱斗 1.3 · 字体对比图

用**项目里的真实文字**渲染各候选字体，便于直接肉眼选型。
每张图一个分类，每行一个字体，四行样本：
  卡名(52) / 效果文字(28) / UI 标签(24) / 数字(60)
背景取卡面下半部的深灰，白字，与实机观感一致。

输出到 Captures/art-review/
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(__file__).resolve().parents[2]
FONT_ROOT = PROJECT / "Assets" / "Art" / "Fonts"
OUT_DIR = PROJECT / "Captures" / "art-review"

W = 1500
PAD = 24
BG = (43, 43, 48)
BG_ALT = (52, 52, 58)
LABEL = (150, 150, 160)
TEXT = (245, 245, 245)
ACCENT = (255, 214, 102)

SAMPLES = [
    ("卡名 52pt", "暴风雪  地动波  冰封铠甲  狂躁蘑菇", 52, TEXT),
    ("效果文字 28pt", "加速 · 区域减速 ／ 快速回填 · 防御时，力量+1", 28, TEXT),
    ("UI 标签 24pt", "手牌 ×6   冷却区 3   生命 3/4   放弃防御   设置", 24, TEXT),
    ("数字 60pt", "4  7  9  3  2  1  0", 60, ACCENT),
]

GROUPS = {
    "01-正文黑体": (
        "UI 正文／按钮／长文本候选 —— 小字号可读性是第一要求",
        [
            ("Noto Sans SC", r"Black\Google-Regular.ttf"),
            ("Source Han Sans CN Normal", r"Black\Google-Normal.otf"),
            ("LXGW 975 Gothic SC", r"Black\Lxgw975GoSC-400W.ttf"),
        ],
    ),
    "02-标题黑体": (
        "标题／卡名／结算大字候选 —— 需要更重的笔画与个性",
        [
            ("字体圈伟君黑 W1", r"BlackLike\字体圈伟君黑-W1.ttf"),
            ("爱点风雅黑", r"BlackLike\爱点风雅黑.ttf"),
            ("站酷仓耳渔阳体 W02", r"BlackLike\站酷仓耳渔阳体-W02.ttf"),
            ("站酷仓耳渔阳体 W03", r"BlackLike\站酷仓耳渔阳体-W03.ttf"),
        ],
    ),
    "03-手写体": (
        "装饰／演出提示候选 —— 长文本慎用，字号小易糊",
        [
            ("站酷快乐体 ZCOOL KuaiLe", r"HandWriting\站酷快乐体（开源版）推荐.ttf"),
            ("HappyZcool-2016", r"HandWriting\HappyZcool-2016.ttf"),
            ("也字工厂小石头", r"HandWriting\也字工厂小石头.ttf"),
            ("千图马克手写体", r"HandWriting\千图马克手写体.ttf"),
            ("素材集市康康体 3.0", r"HandWriting\素材集市康康体3.0.ttf"),
            ("摄图摩登小方体", r"HandWriting\摄图摩登小方体(免费商用).ttf"),
            ("小賴字體 SC", r"HandWriting\XiaolaiSC-Regular.ttf"),
            ("cjkFonts allseto", r"HandWriting\cjkFonts_allseto_v1.11.ttf"),
            ("清松手写体1", r"HandWriting\清松手写体1.ttf"),
        ],
    ),
    "04-英文点缀": (
        "英文标题／副标／装饰候选 —— 无中文字形，只能做英文",
        [
            ("ZCOOL Addict Italic 02", r"English-Handwriting-Italy\ZCOOL Addict Italic 02.ttf"),
        ],
    ),
}

LABEL_FONT = str(FONT_ROOT / r"Black\Google-Regular.ttf")


def block_height(scale: float) -> int:
    return int(sum(int(s[2] * 1.55) for s in SAMPLES) + 46)


def render(rows, title, subtitle, scale=1.0) -> Image.Image:
    row_h = block_height(scale)
    img = Image.new("RGB", (W, PAD * 2 + int(row_h * len(rows)) + 96), BG)
    d = ImageDraw.Draw(img)

    f_title = ImageFont.truetype(LABEL_FONT, 34)
    f_sub = ImageFont.truetype(LABEL_FONT, 22)
    f_label = ImageFont.truetype(LABEL_FONT, 18)

    d.text((PAD, PAD), title, font=f_title, fill=TEXT)
    d.text((PAD, PAD + 46), subtitle, font=f_sub, fill=LABEL)

    y = PAD + 92
    for i, (label, rel) in enumerate(rows):
        if i % 2:
            d.rectangle([0, y, W, y + row_h], fill=BG_ALT)

        font_path = FONT_ROOT / rel
        d.text((PAD, y + 10), f"{label}   ·   {rel}", font=f_label, fill=LABEL)

        cy = y + 36
        for cap, sample, size, color in SAMPLES:
            try:
                f = ImageFont.truetype(str(font_path), int(size * scale))
            except Exception as exc:  # noqa: BLE001
                d.text((PAD, cy), f"[载入失败] {exc}", font=f_label, fill=(255, 120, 120))
                cy += int(size * 1.55)
                continue
            d.text((PAD, cy), sample, font=f, fill=color)
            cy += int(size * 1.55)
        y += row_h

    return img


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name, (subtitle, rows) in GROUPS.items():
        for tag, scale in (("", 1.0), ("-小字号", 0.62)):
            img = render(rows, f"魔法乱斗 1.3 · {name}", subtitle, scale)
            path = OUT_DIR / f"字体对比-{name}{tag}.png"
            img.save(path)
            print(f"{path}  {img.size}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
