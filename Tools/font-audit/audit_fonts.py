# -*- coding: utf-8 -*-
"""
魔法乱斗 1.3 · 字体资源审计

用途：
  1. 统计 Assets/Font 下每个字体的字形数、字重/风格、家族名
  2. 用「项目实际用字集」+「GB2312 全集」双向核验覆盖率，列出缺字
  3. 输出机器可读的 JSON，供规范文档引用

运行：
  python audit_fonts.py
"""
import json
import os
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

PROJECT = Path(r"E:\UnityProject\Unity_AI_CardFight2")
FONT_ROOT = PROJECT / "Assets" / "Font"
DOCS = PROJECT / "Docs"
OUT_JSON = Path(__file__).with_name("font-audit-result.json")

# ---------------------------------------------------------------- 用字集

# UI 必备词表：界面上会真正渲染出来的文字（按钮、标签、提示）
UI_VOCAB = """
魔法乱斗 开始 继续 设置 退出 再来一局 确定 取消 返回 关闭 是 否
你 我 对方 玩家 敌方 双方 进攻 防御 放弃 出牌 选牌 选择 目标
手牌 冷却区 冷却 剩余 基础 力量 生命 生命值 生命上限 上限 恢复 减少
加速 减速 区域 区域加速 区域减速 连击 双发 守护 快速回填 光环 免疫 重置
立即冷却完成 查看 复制 移出游戏 翻倍 额外 结算 回合 先手 后手 补牌 替换
牌池 牌堆 抽牌 使用 生效 失效 未使用 已使用 指示物 双光环 单次 持续
战斗日志 日志 开关 音效 音乐 分辨率 全屏 窗口 退出游戏
你赢了 你输了 胜利 失败 平局 点击 点击任意处继续 加载中 请稍候
力量不足 无法防御 手牌已满 不能打出 必须 不可 允许
""".split()

# 额外单字兜底（卡名/效果里出现、词表可能漏掉的）
EXTRA_CHARS = "αβγ×≥≤"


def project_charset() -> set:
    """项目实际用字 = Docs 全部中文 + UI 词表 + 卡名与效果文字"""
    chars = set()
    for md in sorted(DOCS.glob("*.md")):
        chars |= {c for c in md.read_text(encoding="utf-8") if "\u4e00" <= c <= "\u9fff"}
    for w in UI_VOCAB:
        chars |= {c for c in w if "\u4e00" <= c <= "\u9fff"}
    chars |= {c for c in EXTRA_CHARS}
    # 数字与标点（卡面数字、冷却值、括号、方括号）
    chars |= set("0123456789+-×/()（）[]【】、。，：·！？—…")
    chars |= set("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")
    return chars


def gb2312_charset() -> set:
    """GB2312 全集（一级 3755 + 二级 3008 = 6763 汉字）"""
    chars = set()
    for hi in range(0xB0, 0xF8):
        for lo in range(0xA1, 0xFF):
            try:
                ch = bytes([hi, lo]).decode("gb2312")
            except UnicodeDecodeError:
                continue
            chars.add(ch)
    return chars


# ---------------------------------------------------------------- 字体分析

WEIGHT_HINT = {
    "thin": "极细", "extralight": "特细", "ultralight": "特细",
    "light": "细", "regular": "常规", "normal": "常规", "book": "常规",
    "medium": "中等", "semibold": "半粗", "demibold": "半粗",
    "bold": "粗", "extrabold": "特粗", "heavy": "特粗", "black": "极粗",
}


def name_of(font: TTFont, name_id: int) -> str:
    for rec in font["name"].names:
        if rec.nameID == name_id:
            try:
                return rec.toUnicode().strip()
            except Exception:
                continue
    return ""


def analyze(path: Path, required: set, gb: set) -> dict:
    font = TTFont(str(path), fontNumber=0, lazy=True)
    cmap = set()
    for table in font["cmap"].tables:
        if table.isUnicode():
            cmap |= set(table.cmap.keys())
    cmap_chars = {chr(cp) for cp in cmap}

    os2 = font.get("OS/2")
    weight_class = getattr(os2, "usWeightClass", None)
    fs_selection = getattr(os2, "fsSelection", 0)

    missing_req = sorted(c for c in required if c not in cmap_chars)
    missing_gb = [c for c in gb if c not in cmap_chars]

    return {
        "file": path.name,
        "dir": path.parent.name,
        "size_kb": round(path.stat().st_size / 1024),
        "family": name_of(font, 1),
        "subfamily": name_of(font, 2),
        "full_name": name_of(font, 4),
        "version": name_of(font, 5),
        "glyph_count": len(font.getGlyphOrder()),
        "cmap_count": len(cmap_chars),
        "cjk_count": sum(1 for c in cmap_chars if "\u4e00" <= c <= "\u9fff"),
        "us_weight_class": weight_class,
        "weight_label": WEIGHT_HINT.get(
            (name_of(font, 2) or "").lower().replace(" ", ""), "—"
        ),
        "is_italic": bool(fs_selection & 0x01),
        "req_total": len(required),
        "req_missing": missing_req,
        "req_coverage": round(100 * (1 - len(missing_req) / len(required)), 2),
        "gb_total": len(gb),
        "gb_missing_count": len(missing_gb),
        "gb_coverage": round(100 * (1 - len(missing_gb) / len(gb)), 2),
        "units_per_em": font["head"].unitsPerEm,
    }


def main() -> int:
    required = project_charset()
    gb = gb2312_charset()
    print(f"项目用字集：{len(required)} 个字符 | GB2312 基准：{len(gb)} 字\n")

    results = []
    for path in sorted(FONT_ROOT.rglob("*")):
        if path.suffix.lower() not in (".ttf", ".otf"):
            continue
        try:
            results.append(analyze(path, required, gb))
        except Exception as exc:  # noqa: BLE001
            print(f"[失败] {path.name}: {exc}", file=sys.stderr)

    results.sort(key=lambda r: (-r["gb_coverage"], r["dir"], r["file"]))

    header = (
        f"{'字体':<34}{'分类':<24}{'KB':>7}{'字形':>8}{'汉字':>8}"
        f"{'项目覆盖':>10}{'GB覆盖':>9}{'字重':>8}"
    )
    print(header)
    print("-" * len(header))
    for r in results:
        label = r["file"][:33]
        pad = 34 - sum(2 if ord(c) > 127 else 1 for c in label)
        print(
            f"{label}{' ' * max(pad, 1)}"
            f"{r['dir'][:23]:<24}"
            f"{r['size_kb']:>7}"
            f"{r['glyph_count']:>8}"
            f"{r['cjk_count']:>8}"
            f"{r['req_coverage']:>9.1f}%"
            f"{r['gb_coverage']:>8.1f}%"
            f"{str(r['us_weight_class']):>8}"
        )

    print("\n=== 缺字明细（项目用字集） ===")
    for r in results:
        miss = r["req_missing"]
        if miss:
            print(f"  {r['file']}: 缺 {len(miss)} → {''.join(miss[:60])}")
        else:
            print(f"  {r['file']}: 项目用字全覆盖 ✓")

    OUT_JSON.write_text(
        json.dumps(
            {"project_charset_size": len(required), "fonts": results},
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    print(f"\n结果已写入 {OUT_JSON}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
