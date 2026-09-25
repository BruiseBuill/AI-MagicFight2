# -*- coding: utf-8 -*-
"""
魔法乱斗 1.3 · 卡牌美术资源重命名

把 Assets/Art 下 40 个 Screen_760x1056_<时间戳>_<n>.png 重命名为
    Card_<卡ID>_<卡名>.png
并整体移入 Assets/Art/Cards/。

编号 n（1–40）与 Docs/02-卡牌图鉴.md 的卡 ID 顺序 a–an 严格一一对应，
已通过标题栏拼图逐张核对。

关键点：.png.meta 一并改名且**内容原样保留**，因此 Unity 的 GUID 不变，
已有/将来的引用不会断。

用法：
    python rename_cards.py          # 预览（dry-run）
    python rename_cards.py --apply  # 实际执行
"""
import csv
import re
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
ART = PROJECT / "Assets" / "Art"
DEST = ART / "Cards"
REPORT = Path(__file__).resolve().parents[2] / "Artifacts" / "audits" / "font" / "rename-report.csv"

# 卡 ID → 卡名，顺序即 Docs/02-卡牌图鉴.md 的 a–an
CARDS = [
    ("a", "暴风雪"), ("b", "冰风暴"), ("c", "凝固"), ("d", "寒流"),
    ("e", "潮汐"), ("f", "滚石冲击"), ("g", "陨石"), ("h", "沉重打击"),
    ("i", "地震"), ("j", "闪电"), ("k", "电弧"), ("l", "磁暴"),
    ("m", "雷鸣"), ("n", "引雷"), ("o", "过载"), ("p", "自燃"),
    ("q", "海涌"), ("r", "水刃"), ("s", "喷泉"), ("t", "荆棘"),
    ("u", "飞叶连击"), ("v", "藤蔓"), ("w", "狂躁蘑菇"), ("x", "模仿"),
    ("y", "瀑流"), ("z", "地动波"), ("aa", "漩涡"), ("ab", "湍流"),
    ("ac", "雪球"), ("ad", "雷云"), ("ae", "灼烧"), ("af", "冰封铠甲"),
    ("ag", "淬火"), ("ah", "烈焰斗篷"), ("ai", "爆燃"), ("aj", "火灾"),
    ("ak", "石化"), ("al", "石盾"), ("am", "充能"), ("an", "雪崩"),
]
PATTERN = re.compile(r"^Screen_760x1056_.*_(\d+)\.png$")


def main() -> int:
    apply = "--apply" in sys.argv
    sources = {}
    for p in ART.glob("Screen_760x1056_*.png"):
        m = PATTERN.match(p.name)
        if m:
            sources[int(m.group(1))] = p
    if len(sources) != 40:
        print(f"[中止] 期望 40 张源图，实际 {len(sources)}", file=sys.stderr)
        return 1
    for n in range(1, 41):
        if n not in sources:
            print(f"[中止] 缺少编号 {n}", file=sys.stderr)
            return 1

    if apply:
        DEST.mkdir(parents=True, exist_ok=True)

    rows = []
    for idx, (cid, cname) in enumerate(CARDS, start=1):
        src = sources[idx]
        new_png = f"Card_{cid}_{cname}.png"
        dst = DEST / new_png
        meta_src = src.with_name(src.name + ".meta")
        meta_dst = DEST / (new_png + ".meta")

        if dst.exists() and dst != src:
            print(f"[中止] 目标已存在：{dst}", file=sys.stderr)
            return 1
        if not meta_src.exists():
            print(f"[中止] 缺少 meta：{meta_src}", file=sys.stderr)
            return 1

        rows.append((idx, cid, cname, src.name, new_png))
        print(f"{idx:>3}  {src.name}  ->  Cards/{new_png}")

        if apply:
            src.replace(dst)                       # 同盘 rename，meta 内容不变
            meta_src.replace(meta_dst)

    with REPORT.open("w", newline="", encoding="utf-8-sig") as fh:
        w = csv.writer(fh)
        w.writerow(["序号", "卡ID", "卡名", "原文件名", "新文件名"])
        w.writerows(rows)

    print(f"\n{'已执行' if apply else '[预览] 未改动任何文件'}")
    print(f"映射表：{REPORT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
