# -*- coding: utf-8 -*-
"""
人物动画表切分器
==================
把两张动画参考表切成**逐帧独立 Sprite**，输出到 `Assets/Art/Chars/<角色>/<动作>/`。

| 源图 | 内容 |
|---|---|
| `2049a678-….png` | 主角：待机 5 帧（外加 1 张立绘）、攻击 5 帧、防御 5 帧 —— 带 Alpha |
| `6e093879-….png` | 怪物：待机 6 帧、攻击 5 帧、防御 5 帧 —— **无 Alpha，黑底需抠** |

## 为什么不能按等距网格切

这两张是 AI 生成的**动画参考表**，不是标准 sprite sheet：帧间距 224–314 px 不等、
帧宽 179–326 px 不等（姿势把腿/尾巴/光效撑开了）。所以：

1. **按列剖面自动分段**（`alpha>100` / 抠底后的前景），帧与帧之间是近乎空白的窄缝。
2. **光效会桥接窄缝** —— 怪物攻击行在 50–170 的亮度阈值下都分不开（能量波横跨半个表），
   退化成「用帧标签中心当参考，再在 ±60 px 内找剖面最低点」来定切点。

## 对齐：按「身体」锚定，不按包围盒居中

帧包围盒包含**光效**（攻击 3 的月牙、攻击 4 的能量波），按包围盒居中会让身体一帧一跳。
改法 = 锚点取 **「竖直跨度最大的那一列」**（身体比光效高）的水平位置 + 内容底边，
再把所有帧按锚点对齐到**同一张画布**上。

结果：同一动作的所有帧画布尺寸相同、锚点落在同一像素 → Unity 里不管用什么 pivot 都不会错位。

用法:
    python slice_char_sheets.py            # 预览：打印分段与锚点 + 出逐行对照图
    python slice_char_sheets.py --apply    # 执行切分
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
ART = PROJECT / "Assets" / "Art"
OUT = ART / "Chars"
WORK = PROJECT / "Tools" / "art-audit" / "_work"

HERO_SHEET = ART / "Chars" / "_Source" / "2049a678-64d5-46e6-a342-87eaf3f37435.png"
MON_SHEET = ART / "Chars" / "_Source" / "6e093879-16be-4326-b9e9-c90a767cb587.png"

SEG_ALPHA = 100        # 主角表：分段用的 alpha 阈值（40 时光效会把帧连起来）
CONTENT_ALPHA = 12     # 取包围盒用的宽阈值（比分段宽，保证光效不被裁）
MIN_GAP = 6            # 认定为帧间空缝的最小列数
FRAGMENT_W = 60        # 宽于此值的段落才算真帧；更窄的当光效碎片并入前一段
                       # （防御行末尾有个 33 px 宽、被打破的护盾光点）
                       # ⚠ 不能反过来用「缝宽 < N 就合并」：攻击行相邻帧的缝只有 7 / 16 px，
                       #   而防御行碎片与主帧的缝有 25 px —— 按缝宽合并必然两边错一个。

MON_BG_LUM = 27        # 怪物表背景亮度（实测四角 RGB(21,24,27)）
MON_LO_LUM = 34        # 抠底：亮度低于此且低饱和 → 全透明
MON_HI_LUM = 58        # 抠底：亮度高于此 → 全不透明
MON_LO_SAT = 10
MON_HI_SAT = 24

MARGIN = 6             # 画布四周留白

# (角色, 动作, 行带 y0,y1, 期望帧数, 跳过前 N 段, 分段模式, 行标题矩形)
#
# 行带两端各留几像素余量：analyze 给的「内容行带」按原始阈值算，换成更宽的取框阈值后
# 内容可能略微溢出，留余量免得裁到头顶 / 脚底。但也**不能压到行标题或帧标签行**
# （怪物表标签在行带下方 2–10 px 处）。
# 行标题矩形 = (x0, x1, dy0, dy1)，相对行带左上角；只用来把那几行文字排除出取框。
ROWS = [
    # ⚠ 主角表的行标题在**每行右上**（实测「待机动画」在 x 424+ / y 43–70，
    #   后面还拖一条通栏分隔线），正好压在待机第 1 帧的头顶上。
    #   第一版掩码按「左上角 x 20..200」设，完全没遮住，于是标题进了 Hero_idle_01.png。
    #   现在按实测范围遮，只遮标题那几行（人物本体从 y 116 / 410 / 710 才开始，安全）。
    ("Hero", "idle", (30, 350), 5, 1, "alpha", (400, 1660, 0, 46)),
    ("Hero", "attack", (370, 634), 5, 0, "alpha", (40, 1640, 0, 40)),
    ("Hero", "defend", (659, 906), 5, 0, "alpha", (40, 1640, 0, 38)),
    ("Monster", "idle", (66, 282), 6, 0, "key", None),
    ("Monster", "attack", (364, 558), 5, 0, "key_cut", None),
    ("Monster", "defend", (657, 890), 5, 0, "key", None),
]


# ── 抠底 ─────────────────────────────────────────────────────────────

def monster_alpha(rgb: np.ndarray) -> np.ndarray:
    """
    黑底抠图。背景是低饱和的深灰 RGB(21,24,27)，怪物是蓝紫色（饱和度高）。

    单看亮度会把怪物身上偏暗的爪子一起抠掉，所以判据取
    「亮度」与「饱和度」两条的**较大者**，两条都做成软过渡，边缘不会出现硬锯齿。
    """
    v = rgb.astype(np.float32)
    lum = v.max(axis=2)
    sat = v.max(axis=2) - v.min(axis=2)
    a_lum = (lum - MON_LO_LUM) / (MON_HI_LUM - MON_LO_LUM)
    a_sat = (sat - MON_LO_SAT) / (MON_HI_SAT - MON_LO_SAT)
    return np.clip(np.maximum(a_lum, a_sat), 0.0, 1.0)


def load_sheet(path: Path, keyed: bool) -> Image.Image:
    im = Image.open(path).convert("RGBA")
    if not keyed:
        return im
    rgb = np.asarray(im)[:, :, :3]
    al = (monster_alpha(rgb) * 255.0).round().astype(np.uint8)
    out = np.dstack([rgb, al])
    return Image.fromarray(out, "RGBA")


# ── 分段 ─────────────────────────────────────────────────────────────

def runs_of(flags: np.ndarray) -> list[tuple[int, int]]:
    out, i, n = [], 0, len(flags)
    while i < n:
        if flags[i]:
            j = i
            while j < n and flags[j]:
                j += 1
            out.append((i, j - 1))
            i = j
        else:
            i += 1
    return out


def segment_columns(seg_mask: np.ndarray, cut_hints: list[int] | None) -> list[tuple[int, int]]:
    """
    把一行的列剖面切成帧区间。

    <paramref name="cut_hints"/> 为 None 时纯靠空缝分段；给了切点提示（帧标签中心算出来的）
    则在每个提示点 ±60 px 内找剖面最低点当切点 —— 光效把空缝填满的行只能这么切。
    """
    col = seg_mask.sum(axis=0)
    if cut_hints is not None:
        cuts = []
        for h in cut_hints:
            lo, hi = max(1, h - 60), min(len(col) - 2, h + 60)
            cuts.append(lo + int(np.argmin(col[lo:hi + 1])))
        cuts = sorted(set(cuts))
        edges = [0] + cuts + [len(col) - 1]
        return [(edges[i], edges[i + 1] - 1) for i in range(len(edges) - 1)]

    empty = col == 0
    gaps = [(a, b) for (a, b) in runs_of(empty) if b - a + 1 >= MIN_GAP]

    segs, prev = [], 0
    for (a, b) in gaps:
        if a - prev > 0:
            segs.append([prev, a - 1])
        prev = b + 1
    if prev < len(col):
        segs.append([prev, len(col) - 1])

    merged = [segs[0]]
    for sg in segs[1:]:
        if sg[1] - sg[0] + 1 < FRAGMENT_W:
            merged[-1][1] = sg[1]          # 光效碎片：并回前一段
        else:
            merged.append(sg)
    return [(a, b) for (a, b) in merged]


# ── 锚点 ─────────────────────────────────────────────────────────────

def frame_anchor(seg_mask: np.ndarray, x0: int, x1: int) -> tuple[int, int, tuple]:
    """
    返回 (锚点 x, 锚点 y, 内容包围盒)。

    锚点 x = 竖直跨度最大的一列（= 身体；光效比身体矮，抢不走）。
    锚点 y = 内容底边（脚底）。
    """
    sub = seg_mask[:, x0:x1 + 1]
    extent = sub.sum(axis=0).astype(np.float64)
    if extent.max() <= 0:
        return (x0 + x1) // 2, seg_mask.shape[0] - 1, (x0, 0, x1, seg_mask.shape[0] - 1)
    peak = extent.max()
    strong = np.where(extent >= peak * 0.55)[0]      # 取身体那一段列，避免单列毛刺
    ax = x0 + int(round(strong.mean()))

    ys, xs = np.where(sub)
    return ax, int(ys.max()), (x0 + int(xs.min()), int(ys.min()),
                               x0 + int(xs.max()), int(ys.max()))


# ── 主流程 ───────────────────────────────────────────────────────────

def build(cut_hint_labels: dict) -> None:
    H = load_sheet(HERO_SHEET, keyed=False)
    M = load_sheet(MON_SHEET, keyed=True)
    sheets = {"Hero": H, "Monster": M}

    plan: dict[tuple[str, str], list[dict]] = {}
    report = []

    for (who, anim, (y0, y1), expect, skip, mode, title) in ROWS:
        sheet = sheets[who]
        a = np.asarray(sheet)
        al = a[:, :, 3]
        band_alpha = al[y0:y1 + 1]
        seg_mask = band_alpha > (SEG_ALPHA if mode == "alpha" else 110)
        content_mask = band_alpha > CONTENT_ALPHA

        if title is not None:
            # 行标题（「待机动画」等灰字）就压在行带左上角，会把取框顶到天花板上去
            tx0, tx1, ty0, ty1 = title
            content_mask = content_mask.copy()
            content_mask[ty0:ty1 + 1, tx0:tx1 + 1] = False
            seg_mask = seg_mask.copy()
            seg_mask[ty0:ty1 + 1, tx0:tx1 + 1] = False

        hints = None
        if mode == "key_cut":
            hints = cut_hint_labels[(who, anim)]

        segs = segment_columns(seg_mask, hints)
        skipped = segs[:skip]
        segs = segs[skip:]

        print(f"\n{who}/{anim}  行 y{y0}-{y1}  模式 {mode}")
        print(f"  分段 {len(segs)}（期望 {expect}）" + ("  ✔" if len(segs) == expect else "  ✘ 数量不符"))
        for i, (a0, a1) in enumerate(segs):
            print(f"    [{i}] x {a0}..{a1}  w={a1 - a0 + 1}")

        frames = []
        for (a0, a1) in segs:
            # ⚠ 这些掩码是**行带内局部坐标**，y 必须加回 y0 才是全图坐标 ——
            #   一开始漏了这一步，于是每行都往前串了一个行带的高度，
            #   裁出来的帧顶上顶着行标题、怪物头被切掉。
            ax, ay_local, _ = frame_anchor(content_mask, a0, a1)
            ax += 0
            ay = ay_local + y0
            ys, xs = np.where(content_mask[:, a0:a1 + 1])
            bx0, bx1 = a0 + int(xs.min()), a0 + int(xs.max())
            by0, by1 = int(ys.min()) + y0, int(ys.max()) + y0
            frames.append({"ax": ax, "ay": ay, "bbox": (bx0, by0, bx1, by1),
                           "seg": (a0, a1)})

        # 统一画布：把所有帧按锚点对齐后取并集
        L = min(f["bbox"][0] - f["ax"] for f in frames)
        R = max(f["bbox"][2] - f["ax"] for f in frames)
        T = min(f["bbox"][1] - f["ay"] for f in frames)
        B = 0
        cw, ch = R - L + 1 + MARGIN * 2, B - T + 1 + MARGIN * 2
        anchor_canvas = (-L + MARGIN, -T + MARGIN)
        print(f"  画布 {cw}×{ch}  锚点画布坐标 {anchor_canvas}")

        plan[(who, anim)] = []
        for f in frames:
            plan[(who, anim)].append({
                "src": (f["bbox"][0], f["bbox"][1], f["bbox"][2], f["bbox"][3]),
                "dst": (f["bbox"][0] - f["ax"] + anchor_canvas[0],
                        f["bbox"][1] - f["ay"] + anchor_canvas[1]),
                "size": (cw, ch),
                "anchor": anchor_canvas,
            })
        report.append((who, anim, cw, ch, len(frames), anchor_canvas))

        # 立绘（待机行跳过的第 1 段）单独留
        if skip:
            x0, x1 = skipped[0]
            ys, xs = np.where(content_mask[:, x0:x1 + 1])
            plan[(who, "portrait")] = [{
                "src": (x0 + int(xs.min()), int(ys.min()) + y0,
                        x0 + int(xs.max()), int(ys.max()) + y0),
                "dst": (0, 0), "size": None, "anchor": None,
            }]

    return sheets, plan, report


def render_preview(sheets, plan) -> None:
    """逐动作出一张「画布对齐后」的预览条：能一眼看出帧有没有跳。"""
    WORK.mkdir(parents=True, exist_ok=True)
    for (who, anim), frames in plan.items():
        if not frames or frames[0]["size"] is None:
            continue
        cw, ch = frames[0]["size"]
        strip = Image.new("RGBA", (cw * len(frames), ch), (26, 28, 33, 255))
        for i, f in enumerate(frames):
            sp = sheets[who].crop(tuple(f["src"]))
            strip.alpha_composite(sp, (i * cw + f["dst"][0], f["dst"][1]))
        d = ImageDraw.Draw(strip)
        ax, ay = frames[0]["anchor"]
        for i in range(len(frames)):
            # 红竖线 = 锚点（身体中心），黄横线 = 锚点（脚底）。
            # 两线每格都该落在怪物身上的同一处 —— 落在不同处就是对齐算错了。
            d.line([i * cw + ax, 0, i * cw + ax, ch], fill=(255, 80, 80, 220), width=1)
            d.line([i * cw, ay, i * cw + cw, ay], fill=(255, 210, 0, 220), width=1)
        for i in range(1, len(frames)):
            d.line([i * cw, 0, i * cw, ch], fill=(90, 96, 110, 255), width=1)
        scale = max(1, min(3, 1500 // strip.width))
        strip.resize((strip.width * scale, strip.height * scale), Image.NEAREST) \
             .save(WORK / f"anim_{who}_{anim}.png")
        print(f"  预览 → _work/anim_{who}_{anim}.png  ({len(frames)} 帧 × {cw}×{ch})")


def apply(sheets, plan) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    total = 0
    # 帧清单：Unity 侧的导入脚本据此给每帧设 Custom pivot（锚点 = 脚底中心）。
    # 写成纯文本而不是让 C# 去猜 —— 锚点算不出来，只能传过去。
    manifest: list[str] = [
        "# 角色/动作  画布宽 画布高 锚点X 锚点Y 帧数",
        "# 锚点 = 身体中心 / 脚底，图像坐标（左上原点）。",
        "# Unity 侧把每帧 Sprite 的 pivot 设成 Custom = 锚点，摆位时 RectTransform 的位置即「脚底中心」。",
    ]
    for (who, anim), frames in plan.items():
        if anim == "portrait":
            f = frames[0]
            sp = sheets[who].crop(tuple(f["src"]))
            dst_dir = OUT / who
            dst_dir.mkdir(parents=True, exist_ok=True)
            sp.save(dst_dir / f"{who}_portrait.png")
            manifest.append(f"# 立绘 {who}_portrait.png  {sp.width}x{sp.height}")
            total += 1
            continue
        dst_dir = OUT / who / anim
        dst_dir.mkdir(parents=True, exist_ok=True)
        (dst_dir / "_meta.txt").write_text(
            f"# {who} / {anim}\n"
            f"# 画布 = {frames[0]['size'][0]}x{frames[0]['size'][1]}（所有帧一致）\n"
            f"# 锚点（身体中心 / 脚底）画布坐标 = {frames[0]['anchor']}\n"
            f"# 生成方式见 Tools/art-audit/slice_char_sheets.py\n",
            encoding="utf-8")
        for i, f in enumerate(frames):
            canvas = Image.new("RGBA", f["size"], (0, 0, 0, 0))
            canvas.alpha_composite(sheets[who].crop(tuple(f["src"])), f["dst"])
            canvas.save(dst_dir / f"{who}_{anim}_{i + 1:02d}.png")
            total += 1
        cw, ch = frames[0]["size"]
        ax, ay = frames[0]["anchor"]
        manifest.append(f"{who}/{anim} {cw} {ch} {ax} {ay} {len(frames)}")

    (OUT / "anim_frames.txt").write_text("\n".join(manifest) + "\n", encoding="utf-8")
    # 源表**不在这里**归档：搬文件交给 Unity 侧 `ArtImportBuilder`
    # （走 AssetDatabase.MoveAsset 保 GUID）。Python 只产成品，不碰源图。
    print(f"\n共导出 {total} 张 → {OUT}")
    print(f"帧清单     → {OUT / 'anim_frames.txt'}")
    print("源表留档：跑 Unity 菜单 `魔法乱斗/整理 · 配置新美术导入` 自动搬进 Chars/_Source/")


def main() -> None:
    # 怪物攻击行：光效横跨，只能用帧标签中心推切点（标签实测中心见 analyze_new_art.py）
    # ⚠ 不能取「相邻帧标签中心的中点」当切点：攻击 4 的能量波一直拖到 ~1450，
    #   中点 1269 会把波劈成两半，后一帧看起来像站了两只怪。
    #   正确口径 = 让每个光效留在**它自己那一帧**里，切点落在光效尾端之后的空档。
    hints = {("Monster", "attack"): [296, 584, 960, 1460]}
    sheets, plan, report = build(hints)
    print("\n小结:")
    for (who, anim, cw, ch, n, anc) in report:
        print(f"  {who:8s} {anim:7s} {n} 帧  画布 {cw}×{ch}  锚点 {anc}")

    render_preview(sheets, plan)
    if "--apply" in sys.argv:
        apply(sheets, plan)


if __name__ == "__main__":
    main()
