using System.Collections.Generic;
using System.Text;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>记录 → 一行字</b>。纯函数、不碰 UI —— 界面拿它排版，日志 / 自测拿它对字符串。
    ///
    /// <para><b>写法（用户 2026-11-02 定稿）</b>：</para>
    /// <code>
    /// 暴风雪〔加速 雷云·区域减速〕 → 冰风暴 挡 / 空 ♥1
    /// └── 进攻牌 ──┘└── 效果 ──┘   └──── 防守链 ────┘
    /// </code>
    /// <list type="bullet">
    /// <item><c>挡</c> 用盾牌图标（界面上）/ 文字（纯文本）；<c>免</c> = 免疫光环；</item>
    /// <item><c>空</c> = 没出牌、直接掉血；<c>/</c> 分段 = 双发 / 连击的第 N 段；</item>
    /// <item><c>〔〕</c> 里是效果，多项用 <c>·</c> 分隔；<c>·敌</c> / <c>·双方</c> 是区域类的作用范围。</item>
    /// </list>
    /// </summary>
    public static class BattleRecordText
    {
        /// <summary>多段之间的分隔符。</summary>
        public const string SegmentSeparator = " / ";

        /// <summary>效果之间的分隔符。</summary>
        public const string NoteSeparator = "·";

        /// <summary>没出牌 / 无可挡之牌。</summary>
        public const string EmptyDefense = "空";

        /// <summary>效果串（不含括号）。没有效果返回空串。</summary>
        public static string Notes(HalfRecord half)
        {
            if (half == null || half.Notes.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < half.Notes.Count; i++)
            {
                if (string.IsNullOrEmpty(half.Notes[i])) continue;
                if (sb.Length > 0) sb.Append(NoteSeparator);
                sb.Append(half.Notes[i]);
            }

            return sb.ToString();
        }

        /// <summary>带括号的效果串；没有效果返回空串（不出现空括号）。</summary>
        public static string NoteBlock(HalfRecord half)
        {
            string notes = Notes(half);
            return notes.Length == 0 ? string.Empty : "〔" + notes + "〕";
        }

        /// <summary>进攻牌名；未出牌时给「未出牌」。</summary>
        public static string AttackCard(HalfRecord half)
        {
            if (half == null) return string.Empty;
            if (half.NoPlay || string.IsNullOrEmpty(half.AttackCard)) return "未出牌";
            return half.AttackCard;
        }

        /// <summary>单段结果文案。</summary>
        public static string Hit(HitRecord hit, bool useGlyph)
        {
            if (hit == null) return string.Empty;

            if (hit.Result == RecordHitResult.Unblocked)
            {
                string empty = string.IsNullOrEmpty(hit.DefenseCard) ? EmptyDefense : hit.DefenseCard;
                return empty + " " + Damage(hit.Damage, useGlyph);
            }

            string shield = useGlyph ? "\u25A3" : "挡";
            if (hit.Result == RecordHitResult.BlockedImmune) shield = useGlyph ? "\u25C8" : "免";

            return string.IsNullOrEmpty(hit.DefenseCard)
                ? shield
                : hit.DefenseCard + " " + shield;
        }

        /// <summary>掉血文案。</summary>
        public static string Damage(int amount, bool useGlyph)
        {
            return (useGlyph ? "\u2665" : "♥") + amount;
        }

        /// <summary>防守链（多段用 / 连接）。</summary>
        public static string Chain(HalfRecord half, bool useGlyph)
        {
            if (half == null || half.Hits.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < half.Hits.Count; i++)
            {
                if (i > 0) sb.Append(SegmentSeparator);
                sb.Append(Hit(half.Hits[i], useGlyph));
            }

            return sb.ToString();
        }

        /// <summary>一整行的纯文本形态（左侧半场）。</summary>
        public static string Half(HalfRecord half, bool useGlyph)
        {
            if (half == null || half.IsEmpty) return "—";

            // 未出牌：进攻方自己掉血，「空」与箭头都是废话，只留结果。
            if (half.NoPlay)
            {
                int lost = 0;
                for (int i = 0; i < half.Hits.Count; i++) lost += half.Hits[i].Damage;
                return AttackCard(half) + " " + Damage(lost, useGlyph);
            }

            string left = AttackCard(half) + NoteBlock(half);
            string right = Chain(half, useGlyph);
            return right.Length == 0 ? left : left + "  →  " + right;
        }

        /// <summary>结算 / 详情页顶部那行图例。</summary>
        public static string Legend(bool useGlyph)
        {
            string shield = useGlyph ? "\u25A3" : "挡";
            return shield + " 挡住　♥ 掉血　空 未防　/ 多段";
        }

        /// <summary>把整局拍平成「文本谱码」——存档 / 贴给别人看用（不进存档正文）。</summary>
        public static string ToPlainText(BattleRecord record)
        {
            if (record == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append(record.ResultText).Append("  ").Append(record.Turns).Append(" 回合  ")
              .Append(record.Opponent).Append('\n');

            for (int i = 0; i < record.Rows.Count; i++)
            {
                TurnRecord row = record.Rows[i];
                sb.Append("T").Append(row.Turn).Append("  ")
                  .Append(Half(row.First, false)).Append("    ")
                  .Append(Half(row.Second, false)).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>按字符数硬折行（TMP 自己有 word wrap，这个只给纯文本日志用）。</summary>
        public static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text) || width <= 0)
            {
                lines.Add(text ?? string.Empty);
                return lines;
            }

            for (int i = 0; i < text.Length; i += width)
            {
                lines.Add(text.Substring(i, System.Math.Min(width, text.Length - i)));
            }

            return lines;
        }
    }
}
