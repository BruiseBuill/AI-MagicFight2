# -*- coding: utf-8 -*-
"""
引用检查器
==========
给定一批资产路径，反查其 GUID 是否被 Assets/ ProjectSettings/ Packages/ 下的
其它文件引用。用于「移动/重命名前先确认无引用」这一步。

用法:
    python check_refs.py <相对工程根的路径> [更多路径...]
    python check_refs.py --dir Assets/Art/PolySprite
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
SCAN_ROOTS = ["Assets", "ProjectSettings", "Packages"]
GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")


def read_guid(asset: Path) -> str | None:
    meta = asset.with_name(asset.name + ".meta")
    if not meta.exists():
        return None
    m = GUID_RE.search(meta.read_text(encoding="utf-8", errors="ignore"))
    return m.group(1) if m else None


def build_index():
    """guid -> 所有引用该 guid 的文件（排除它自己的 .meta）"""
    index: dict[str, list[str]] = {}
    for root in SCAN_ROOTS:
        base = PROJECT / root
        if not base.is_dir():
            continue
        for f in base.rglob("*"):
            if not f.is_file():
                continue
            if f.suffix in (".png", ".jpg", ".jpeg", ".ttf", ".otf", ".asset",
                            ".unity", ".prefab", ".mat", ".shadergraph",
                            ".controller", ".anim", ".json", ".cs", ".txt"):
                try:
                    text = f.read_text(encoding="utf-8", errors="ignore")
                except Exception:                       # noqa: BLE001
                    continue
                for g in GUID_RE.findall(text):
                    index.setdefault(g, []).append(str(f.relative_to(PROJECT)))
    return index


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 1

    paths: list[Path] = []
    if args[0] == "--dir":
        paths = [p for p in (PROJECT / args[1]).rglob("*")
                 if p.is_file() and p.suffix != ".meta"]
    else:
        paths = [PROJECT / a for a in args]

    index = build_index()
    print(f"扫描根: {', '.join(SCAN_ROOTS)}  索引 GUID 数: {len(index)}\n")

    orphan, referenced = [], []
    for p in paths:
        g = read_guid(p)
        rel = str(p.relative_to(PROJECT))
        if g is None:
            print(f"  ? 无 .meta      {rel}")
            continue
        hits = [h for h in index.get(g, []) if not h.startswith(rel + ".meta")]
        if hits:
            referenced.append((rel, g, hits))
            print(f"  ✗ 被引用({len(hits)})  {rel}")
            for h in hits[:5]:
                print(f"        ← {h}")
        else:
            orphan.append(rel)
            print(f"  ✓ 无引用        {rel}")

    print(f"\n合计: {len(paths)} 项 —— 无引用 {len(orphan)} / 被引用 {len(referenced)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
