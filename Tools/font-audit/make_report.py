# -*- coding: utf-8 -*-
"""输出最终重命名映射表（CSV + Markdown），供文档与代码引用。"""
import csv
from pathlib import Path

CARDS_DIR = Path(__file__).resolve().parents[2] / "Assets" / "Art" / "Cards"
OUT_CSV = Path(__file__).resolve().parents[2] / "Artifacts" / "audits" / "font" / "rename-report.csv"
OUT_MD = Path(__file__).resolve().parents[2] / "Artifacts" / "audits" / "font" / "rename-report.md"

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

rows = []
for idx, (cid, cname) in enumerate(CARDS, start=1):
    fn = f"Card_{idx:02d}_{cid}_{cname}.png"
    assert (CARDS_DIR / fn).exists(), f"缺失 {fn}"
    rows.append((idx, cid, cname, fn))

OUT_CSV.parent.mkdir(parents=True, exist_ok=True)
with OUT_CSV.open("w", newline="", encoding="utf-8-sig") as fh:
    w = csv.writer(fh)
    w.writerow(["序号", "卡ID", "卡名", "文件名"])
    w.writerows(rows)

lines = ["| 序号 | 卡ID | 卡名 | 文件名 |", "|---|---|---|---|"]
lines += [f"| {i} | `{c}` | {n} | `{f}` |" for i, c, n, f in rows]
OUT_MD.write_text("\n".join(lines) + "\n", encoding="utf-8")

print(f"{OUT_CSV}")
print(f"{OUT_MD}")
print(f"共 {len(rows)} 条，全部文件存在 ✓")
