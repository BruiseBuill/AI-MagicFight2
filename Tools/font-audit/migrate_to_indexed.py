# -*- coding: utf-8 -*-
"""把 Card_<id>_<名>.png 迁移为 Card_<两位序号>_<id>_<名>.png，修正文件排序。"""
import sys
from pathlib import Path

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

CARDS_DIR = Path(r"E:\UnityProject\Unity_AI_CardFight2\Assets\Art\Cards")
apply = "--apply" in sys.argv

for idx, (cid, cname) in enumerate(CARDS, start=1):
    old = CARDS_DIR / f"Card_{cid}_{cname}.png"
    new = CARDS_DIR / f"Card_{idx:02d}_{cid}_{cname}.png"
    if not old.exists():
        print(f"[跳过] 找不到 {old.name}", file=sys.stderr)
        continue
    print(f"{old.name}  ->  {new.name}")
    if apply:
        old.replace(new)
        old.with_name(old.name + ".meta").replace(new.with_name(new.name + ".meta"))

print("已执行" if apply else "[预览] 未改动")
