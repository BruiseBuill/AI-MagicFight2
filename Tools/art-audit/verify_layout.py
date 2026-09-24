# -*- coding: utf-8 -*-
"""
整理后校验
==========
1) 每个资源与 .meta 一一配对（无孤立的 .meta / 无缺 .meta 的资源）
2) 全工程 GUID 唯一（同一 GUID 出现在两个 .meta 里 = 引用会错乱）
3) 关键引用是否仍能解析（引用的 GUID 在工程内存在）
4) 命名规范抽查（CardArt / Cards 的序号连续性）

用法: python verify_layout.py
"""
from __future__ import annotations

import re
from collections import defaultdict
from pathlib import Path

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
ASSETS = PROJECT / "Assets"

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
CARD_RE = re.compile(r"^Card_(\d{2})_([a-z]+)_(.+?)\.(png|jpg|jpeg)$")

TEXT_EXT = {".prefab", ".mat", ".asset", ".unity", ".shadergraph", ".controller",
            ".anim", ".json", ".cs", ".txt", ".md"}


def main():
    problems = []

    # ── 1) 配对检查 ──────────────────────────────────────
    unpaired = []
    for p in ASSETS.rglob("*"):
        if p.is_dir():
            continue
        if p.name.startswith(".") or "Library" in p.parts:
            continue
        if p.suffix == ".meta":
            if not p.with_name(p.name[:-5]).exists():
                unpaired.append(f"孤立 .meta：{p.relative_to(PROJECT)}")
        else:
            if not p.with_name(p.name + ".meta").exists():
                unpaired.append(f"缺 .meta：{p.relative_to(PROJECT)}")

    print(f"[1] 资源/.meta 配对：{'OK' if not unpaired else str(len(unpaired)) + ' 处异常'}")
    for u in unpaired[:15]:
        print("     " + u)
    problems += unpaired

    # ── 2) GUID 唯一性 ──────────────────────────────────
    owner = defaultdict(list)
    for meta in ASSETS.rglob("*.meta"):
        try:
            m = GUID_RE.search(meta.read_text(encoding="utf-8", errors="ignore"))
        except Exception:                                  # noqa: BLE001
            continue
        if m:
            owner[m.group(1)].append(str(meta.relative_to(PROJECT)))

    dup = {g: v for g, v in owner.items() if len(v) > 1}
    print(f"[2] GUID 唯一性：Assets 内共 {len(owner)} 个 GUID，重复 {len(dup)} 组")
    for g, v in list(dup.items())[:10]:
        print(f"     {g} → {v}")
    problems += [f"GUID 重复 {g}: {v}" for g, v in dup.items()]

    # ── 3) 引用可解析性 ─────────────────────────────────
    # 已知 GUID 集合要连包一起收 —— ShaderGraph / URP 的编辑器脚本与内建 shader 都属于包，
    # 只扫 Assets 会得到一整屏假阳性。包有两处：嵌入式包在 Packages/，
    # 从注册表拉下来的则解在 Library/PackageCache/。
    known = set(owner)
    for pkg_root in (PROJECT / "Packages", PROJECT / "Library" / "PackageCache"):
        if not pkg_root.is_dir():
            continue
        for meta in pkg_root.rglob("*.meta"):
            try:
                m = GUID_RE.search(meta.read_text(encoding="utf-8", errors="ignore"))
            except Exception:                              # noqa: BLE001
                continue
            if m:
                known.add(m.group(1))
    print(f"      （含包后已知 GUID {len(known)} 个）")

    # Unity 内建 GUID：全 0 前缀 + e/f000 结尾，不是资产，永远不会出现在 .meta 里
    BUILTIN_RE = re.compile(r"^0{16}[0-9a-f]{16}$")

    # 已查明来龙去脉、确认与本次整理无关的历史悬空引用
    KNOWN = {"b4e77b7839b099644925129724920af6": "MagicCardKit 自带的 TMP emoji Sprite Asset（未随包导入）"}

    dangling, benign, known_issue = defaultdict(set), defaultdict(set), defaultdict(set)
    for p in ASSETS.rglob("*"):
        if not p.is_file() or p.suffix not in TEXT_EXT:
            continue
        try:
            text = p.read_text(encoding="utf-8", errors="ignore")
        except Exception:                                  # noqa: BLE001
            continue
        for g in GUID_RE.findall(text):
            if g in known or g == "0" * 32:
                continue
            rel = str(p.relative_to(PROJECT))
            if BUILTIN_RE.match(g):
                benign[rel].add(g)
            elif g in KNOWN:
                known_issue[rel].add(g)
            else:
                dangling[rel].add(g)

    print(f"[3] 悬空引用（排除包 / Unity 内建 / 已记账）：{len(dangling)} 个文件")
    for f, gs in list(dangling.items())[:10]:
        print(f"     {f} → {sorted(gs)[:3]}")
    print(f"    另有 {len(benign)} 个文件只引用 Unity 内建 GUID —— 正常")
    for f, gs in known_issue.items():
        for g in gs:
            print(f"     [已记账] {f} → {KNOWN[g]}")
    problems += [f"悬空引用 {f}" for f in dangling]

    # ── 4) 卡图命名规范 ─────────────────────────────────
    for folder, label, expect in [("Art/Cards", "成品卡面", 40),
                                  ("Art/CardArt", "插画原图", 40)]:
        d = ASSETS / folder
        if not d.is_dir():
            continue
        seqs, bad_names = [], []
        for f in sorted(d.iterdir()):
            if f.suffix.lower() not in (".png", ".jpg", ".jpeg"):
                continue
            m = CARD_RE.match(f.name)
            if not m:
                bad_names.append(f.name)
            else:
                seqs.append(int(m.group(1)))
        ok = seqs == list(range(1, expect + 1))
        print(f"[4] {label}（{folder}）：{len(seqs)} 张，序号{'连续 01–%02d ✅' % expect if ok else '不连续 ✗'}，"
              f"不符合命名 {len(bad_names)} 张")
        for b in bad_names[:8]:
            print("     " + b)
        if not ok:
            problems.append(f"{folder} 序号不连续")
        problems += [f"{folder} 命名不符：{b}" for b in bad_names]

    print()
    print("=" * 56)
    print("总判定：" + ("全部通过 ✅" if not problems else f"{len(problems)} 处问题 ✗"))
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
