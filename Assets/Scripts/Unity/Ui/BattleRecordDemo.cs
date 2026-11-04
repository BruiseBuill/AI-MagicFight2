using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>一份写死的样例战报</b>（2026-11-02）。只做两件事：给界面当排版样本、给自测当回放数据。
    ///
    /// <para><b>为什么直接拼字符串、而不是跑一局</b>：<see cref="BattleRecord"/> 里存的全是牌名，
    /// 不是 <c>CardInstance</c>（那是本场战斗的活对象）。所以造样本根本不需要引擎 ——
    /// 想验版式就直接拼，比「跑一局直到某张牌出现」稳定得多。</para>
    ///
    /// <para>覆盖了这几种排版：普通挡住 · 免疫 · 未防御 · 多段（双发/连击）·
    /// 带效果注记 · 双方区域减速 · 未出牌 · 最后一回合半场为空。</para>
    /// </summary>
    public static class BattleRecordDemo
    {
        public static BattleRecord Sample()
        {
            var record = new BattleRecord
            {
                Opponent = "影法师",
                Victory = true,
                Turns = 8,
                Reason = "对方生命归零",
                LocalSeat = 0,
            };

            record.Rows.Add(Turn(1,
                Half(0, "暴风雪", new[] { "加速 雷云", "区域减速(敌)" },
                    Hit("冰风暴", RecordHitResult.Blocked)),
                Half(1, "凝固", new[] { "减速 雷云" },
                    Hit("雷云", RecordHitResult.Blocked))));

            record.Rows.Add(Turn(2,
                Half(0, "雷云", new[] { "减速 藤蔓" },
                    Hit("藤蔓", RecordHitResult.Blocked)),
                Half(1, "狂躁蘑菇", new[] { "查看 地刺→冷却" },
                    Hit("石盾", RecordHitResult.Blocked))));

            record.Rows.Add(Turn(3,
                Half(0, "淬火", new[] { "减速 石化", "光环 烈焰斗篷" },
                    Hit("石化", RecordHitResult.BlockedImmune)),
                Half(1, "灼烧", null,
                    Hit("", RecordHitResult.Unblocked, 1))));

            record.Rows.Add(Turn(4,
                Half(0, "荆棘", null,
                    Hit("冰封铠甲", RecordHitResult.Blocked),
                    Hit("", RecordHitResult.Unblocked, 1)),
                Half(1, "寒流", new[] { "重置 荆棘" },
                    Hit("石盾", RecordHitResult.Blocked))));

            record.Rows.Add(Turn(5,
                Half(0, "闪电", null,
                    Hit("石盾", RecordHitResult.Blocked),
                    Hit("", RecordHitResult.Unblocked, 1)),
                Half(1, "火灾", new[] { "光环 爆燃" },
                    Hit("", RecordHitResult.Unblocked, 1))));

            record.Rows.Add(Turn(6,
                Half(0, "地刺", null,
                    Hit("", RecordHitResult.Unblocked, 1)),
                Half(1, "雪崩", new[] { "区域减速(双方)" },
                    Hit("石盾", RecordHitResult.Blocked))));

            record.Rows.Add(Turn(7,
                Half(0, "漩涡", new[] { "光环 水刃" },
                    Hit("石化", RecordHitResult.Blocked)),
                Half(1, "引雷", new[] { "光环 雷云" },
                    Hit("石盾", RecordHitResult.Blocked))));

            var last = Turn(8,
                Half(0, "沉重打击", null,
                    Hit("", RecordHitResult.Unblocked, 2)),
                new HalfRecord { AttackerSeat = 1 });
            record.Rows.Add(last);

            return record;
        }

        private static TurnRecord Turn(int number, HalfRecord first, HalfRecord second)
        {
            return new TurnRecord { Turn = number, First = first, Second = second };
        }

        private static HalfRecord Half(int seat, string card, string[] notes, params HitRecord[] hits)
        {
            var half = new HalfRecord { AttackerSeat = seat, AttackCard = card };
            if (notes != null)
            {
                for (int i = 0; i < notes.Length; i++) half.Notes.Add(notes[i]);
            }

            if (hits != null)
            {
                for (int i = 0; i < hits.Length; i++) half.Hits.Add(hits[i]);
            }

            return half;
        }

        private static HitRecord Hit(string card, RecordHitResult result)
        {
            return new HitRecord { DefenseCard = card ?? string.Empty, Result = result, Damage = 0 };
        }

        private static HitRecord Hit(string card, RecordHitResult result, int damage)
        {
            return new HitRecord { DefenseCard = card ?? string.Empty, Result = result, Damage = damage };
        }

        /// <summary>把样例铺到屏幕上（自测 / 「看一眼排版」用）。</summary>
        public static void ShowSample()
        {
            BattleRecordOverlay.ShowRecord(Sample(), "胜 利", "第 8 回合 · 影法师", null, null, BattleRecordOverlay.Hide);
        }
    }
}
