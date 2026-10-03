using System;
using System.Collections.Generic;
using System.Reflection;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-01 建 · 2026-10-02 补「效果转移」· <b>女巫的工坊（特殊强化）</b>的定点断言。
    ///
    /// <para><b>为什么这条线要单独脚本化</b>：与 <see cref="CardUpgradeScenario"/> 同一个理由 ——
    /// 特殊强化完全发生在战斗之外，<b>完全不进万局随机对局</b>，万局统计一个缺陷也抓不到。
    /// 而它的失效方式全是无声的：</para>
    /// <list type="bullet">
    /// <item><description>卡池门槛写成 <c>&gt; 8</c> 而不是 <c>&gt;= 9</c> → 8 张时照样弹界面，
    /// 玩家能把起始牌池吃掉一张；</description></item>
    /// <item><description>「只有一个效果」判成 <c>&lt;= 1</c> → 0 条效果的牌（如果有）也合格；
    /// 判成「不含光环」→ 纯光环牌被误拒；</description></item>
    /// <item><description>没判 <see cref="CardUpgrade.IsUpgraded"/> → 已经强化过的牌能再做一次；</description></item>
    /// <item><description>「两张不能相同」漏判 → 玩家把同一张牌同时放进两个空位，
    /// 确认之后那张牌被消耗掉、什么也没得到；</description></item>
    /// <item><description><b>连锁的牌没挡住</b>（2026-10-02）→ 玩家把「沉重打击」的
    /// 「进攻力量不能增加」搬到别人的牌上，或者把「模仿」的复制效果搬走 ——
    /// 前者给目标挂了一条莫名的限制，后者搬走的是半条效果；</description></item>
    /// <item><description><b>效果拍平存的时候漏字段</b>（2026-10-02）→ 转移过来的效果
    /// 「看着在、一点也不生效」（最典型的是参数键名写错，<c>A</c> 静默读成 0）。</description></item>
    /// </list>
    ///
    /// <para>断言分七层：① 卡池门槛的边界；② 目标判据的三条拒绝 + 一条放行；
    /// ③ 全表扫描（判据必须与「恰好 1 条效果 且 未强化 且 不连锁」逐张对齐）；
    /// ④ 两个空位一起看（缺牌 / 同 ID）；⑤ <b>连锁牌不能进第一个空位</b>；
    /// ⑥ <b>单 / 多效果两条分支</b>；⑦ <b>转移合成</b>（效果真的多了一条、其余字段一个不动）；
    /// ⑧ <b>价钱</b>（2026-10-03）：手算样本 / 两条边界 / 四个因素的单调性 / 钱不够时不让确认。</para>
    /// </summary>
    public static class WitchWorkshopScenario
    {
        public static bool Run(List<string> report)
        {
            var bad = new List<string>();
            var notes = new List<string>();

            CheckPoolGate(bad);
            CheckTargetBoundaries(bad, notes);
            CheckWholeTable(bad, notes);
            CheckPair(bad);
            CheckChained(bad, notes);
            CheckEffectChoice(bad);
            CheckTransfer(bad);
            CheckCost(bad, notes);

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "女巫的工坊（卡池门槛 / 单效果目标 / 连锁不可献祭 / 效果转移）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                report.Add("      ~ " + notes[i]);
            }

            return bad.Count == 0;
        }

        // ── ① 卡池门槛 ──────────────────────────────────────────────

        private static void CheckPoolGate(List<string> bad)
        {
            // 用户 2026-10-01 口径「只有 8 张牌 → 无法进行特殊强化」。
            // 8 以下同样禁止（确认过：判据是 ≤ 8，不是 == 8）。
            if (WitchWorkshop.CanOpen(7))
            {
                bad.Add("卡池 7 张却判定为能开工（门槛应 ≥ " + WitchWorkshop.MinPoolSize + "）");
            }

            if (WitchWorkshop.CanOpen(8))
            {
                bad.Add("卡池 8 张（起始牌池）却判定为能开工 —— 玩家会把家底吃掉一张");
            }

            if (!WitchWorkshop.CanOpen(9))
            {
                bad.Add("卡池 9 张却判定为不能开工");
            }

            if (!WitchWorkshop.CanOpen(45))
            {
                bad.Add("满卡池却判定为不能开工");
            }

            if (string.IsNullOrEmpty(WitchWorkshop.OpenBlockedReason(8)))
            {
                bad.Add("被门槛拦下时没有给出给玩家看的提示语");
            }
        }

        // ── ② 目标判据的边界 ────────────────────────────────────────

        private static void CheckTargetBoundaries(List<string> bad, List<string> notes)
        {
            string reason;

            if (WitchWorkshop.CanBeTarget(null, out reason))
            {
                bad.Add("空牌却判定为可以当目标");
            }

            CardDef single = null;
            CardDef multi = null;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                if (single == null && def.Effects.Count == 1)
                {
                    single = def;
                }

                if (multi == null && def.Effects.Count >= 2)
                {
                    multi = def;
                }
            }

            if (single == null)
            {
                bad.Add("卡表里找不到一张「恰好 1 条效果」的牌 —— 本场景将永远无法开工");
            }
            else if (!WitchWorkshop.CanBeTarget(single, out reason)
                     && !WitchWorkshop.IsChained(single, out reason))
            {
                bad.Add("单效果牌被误拒：" + single.Name + "（" + single.Id + "）—— " + reason);
            }

            if (multi == null)
            {
                notes.Add("卡表里没有「≥ 2 条效果」的牌，多效果拒绝这一支本次没取到样本");
            }
            else if (WitchWorkshop.CanBeTarget(multi, out reason))
            {
                bad.Add("多效果牌被误判合格：" + multi.Name + "（" + multi.Id + "）有 "
                        + multi.Effects.Count + " 条效果");
            }

            // 强化过的牌不能当目标：拿一张单效果牌现造一个强化版。
            if (single != null)
            {
                CardDef upgraded = CardUpgrade.Apply(single, 30000);
                if (WitchWorkshop.CanBeTarget(upgraded, out reason))
                {
                    bad.Add("强化过的牌被误判合格：" + upgraded.Name + "（" + upgraded.Id + "）");
                }

                // 但**献祭位只排除「效果连锁」的牌**：强化过的牌照样可以被吃掉
                // （用户 2026-10-01 口径：第一个空位不限牌；2026-10-02 补了连锁那一条）。
                if (!WitchWorkshop.IsChained(upgraded, out reason)
                    && !WitchWorkshop.CanBeSacrifice(upgraded, out reason))
                {
                    bad.Add("强化过的牌被拒绝放进献祭位：" + upgraded.Id
                            + "（第一个空位本应不限牌）—— " + reason);
                }
            }

            if (WitchWorkshop.CanBeSacrifice(null, out reason))
            {
                bad.Add("空牌却判定为可以献祭");
            }
        }

        // ── ③ 全表扫描 ──────────────────────────────────────────────

        private static void CheckWholeTable(List<string> bad, List<string> notes)
        {
            int eligible = 0;
            int rejected = 0;
            int maxEffects = 0;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                if (def.Effects.Count > maxEffects)
                {
                    maxEffects = def.Effects.Count;
                }

                string reason;
                bool can = WitchWorkshop.CanBeTarget(def, out reason);

                // 口径：**恰好 1 条效果**（光环也算一条）+ **从未被强化过** + **效果不连锁**
                // （2026-10-02 补：连锁的牌连目标都不该当 —— 否则它会出现在候选里、
                //   点了才在确认那一步被拒）。
                bool expected = def.Effects.Count == 1 && !CardUpgrade.IsUpgraded(def)
                                && !HasChainedOp(def) && !def.HiddenPower;
                if (can != expected)
                {
                    bad.Add("判据不符：" + def.Name + "（" + def.Id + "）效果 "
                            + def.Effects.Count + " 条 / 光环 " + def.AuraTokenCount
                            + " 枚 / HiddenPower=" + def.HiddenPower
                            + " → CanBeTarget=" + can + "，应为 " + expected);
                    continue;
                }

                if (!can)
                {
                    rejected++;
                    if (string.IsNullOrEmpty(reason))
                    {
                        bad.Add("拒绝时没有给原因：" + def.Name + "（" + def.Id + "）"
                                + "—— 界面会显示一张灰卡却写不出为什么");
                    }

                    continue;
                }

                eligible++;
            }

            // ⚠ 不写死张数：卡表每加一张牌、或某张牌的效果条数被改动，这个数就会变。
            //   打印出来是为了让人一眼看到「这一轮有多少张牌能当目标」，
            //   真出问题时上面的逐张比对会先红。
            notes.Add("全表 " + CardLibrary.Count + " 张：可作目标 " + eligible
                      + " 张，被拒 " + rejected + " 张 · 单卡最大效果数 " + maxEffects);

            // 「效果选择」那一屏的行池在 `UiLayout.WitchEffectMaxOptions`（App 侧，Core 自测引不到）——
            // 这里把上限抄一份钉住：**卡表一旦出现更多效果的牌，这条会先红**，
            // 而不是在界面上少半行且零报错。改卡表时若真的超过，记得同时改那个常量。
            const int effectRowPool = 4;
            if (maxEffects > effectRowPool)
            {
                bad.Add("卡表里出现了 " + maxEffects + " 条效果的牌，超过「效果选择」屏的行池 "
                        + effectRowPool + "（见 UiLayout.WitchEffectMaxOptions）");
            }
        }

        // ── ④ 两个空位一起看 ────────────────────────────────────────

        private static void CheckPair(List<string> bad)
        {
            string reason;

            CardDef a = null;
            CardDef b = null;
            for (int i = 0; i < CardLibrary.Count && b == null; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);

                // ⚠ 两个空位都要合格才行：献祭位排连锁（模仿 / 沉重打击那一类），
                //   目标位要「恰好 1 条效果 + 未强化 + 不连锁」。
                //   只按「1 条效果」挑会让模仿（x）混进来，然后在下面那条
                //   「两张不同的单效果牌应当能确认」上假红。
                string ignored;
                if (!WitchWorkshop.CanBeSacrifice(def, out ignored)
                    || !WitchWorkshop.CanBeTarget(def, out ignored))
                {
                    continue;
                }

                if (a == null)
                {
                    a = def;
                }
                else
                {
                    b = def;
                }
            }

            if (a == null || b == null)
            {
                bad.Add("卡表里凑不出两张「可献祭 + 可作目标」的牌，两个空位的断言无法执行");
                return;
            }

            if (WitchWorkshop.BothSlotsFilled(a, null))
            {
                bad.Add("只填了一个空位却判定为「两个都填好了」");
            }

            if (WitchWorkshop.BothSlotsFilled(a, b) == false)
            {
                bad.Add("两个空位都填了却判定为「没填好」");
            }

            // 同一张牌：两个空位都放它 → 不能确认
            if (WitchWorkshop.CanConfirm(a, a, out reason))
            {
                bad.Add("两个空位放了同一张牌（" + a.Name + "）却允许确认 —— "
                        + "确认之后这张牌会被消耗掉、玩家什么也没得到");
            }
            else if (string.IsNullOrEmpty(reason))
            {
                bad.Add("两个空位同牌被拒时没有给原因");
            }

            // 两张不同的牌 → 可以确认
            if (!WitchWorkshop.CanConfirm(a, b, out reason))
            {
                bad.Add("两张不同的单效果牌（" + a.Name + " / " + b.Name + "）却不能确认 —— " + reason);
            }

            if (WitchWorkshop.CanConfirm(null, b, out reason))
            {
                bad.Add("献祭位空着却允许确认");
            }

            if (WitchWorkshop.CanConfirm(a, null, out reason))
            {
                bad.Add("目标位空着却允许确认");
            }

            // 消耗的永远是**第一个**空位那张。
            if (WitchWorkshop.ConsumedId(a) != a.Id)
            {
                bad.Add("ConsumedId 返回的不是献祭位那张：" + WitchWorkshop.ConsumedId(a));
            }

            if (WitchWorkshop.ConsumedId(null) != null)
            {
                bad.Add("ConsumedId(null) 应返回 null");
            }
        }

        // ── ⑤ 连锁牌不能进第一个空位 ────────────────────────────────

        private static void CheckChained(List<string> bad, List<string> notes)
        {
            string why;

            // 用户 2026-10-02 点名的两张
            CardDef heavy = CardLibrary.Get("h");      // 沉重打击：NoAtkBuff + ExtraDamageIfUnblocked
            CardDef mimic = CardLibrary.Get("x");      // 模仿：HiddenPower + Copy

            if (WitchWorkshop.CanBeSacrifice(heavy, out why))
            {
                bad.Add("沉重打击（h）却允许放进献祭位 —— 它会把自己那条「力量不能增加」搬给别人");
            }
            else if (string.IsNullOrEmpty(why))
            {
                bad.Add("沉重打击（h）被拒时没有给原因");
            }

            if (WitchWorkshop.CanBeSacrifice(mimic, out why))
            {
                bad.Add("模仿（x）却允许放进献祭位 —— 它的效果与「力量视为 X」是绑死的");
            }
            else if (string.IsNullOrEmpty(why))
            {
                bad.Add("模仿（x）被拒时没有给原因");
            }

            if (!WitchWorkshop.IsChained(heavy, out why) || !WitchWorkshop.IsChained(mimic, out why))
            {
                bad.Add("IsChained 对沉重打击 / 模仿没有返回 true");
            }

            // 全表：含连锁算子（或力量 X）的牌**一张都不许**进献祭位。
            // ⚠ 判据在这里**自己重写一遍**（不去调 IsChained）—— 复用被测实现等于没验。
            int blocked = 0;
            int allowed = 0;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                bool expectedBlocked = def.HiddenPower || HasChainedOp(def);

                bool can = WitchWorkshop.CanBeSacrifice(def, out why);
                if (can == expectedBlocked)
                {
                    bad.Add("献祭判据不符：" + def.Name + "（" + def.Id + "）HiddenPower="
                            + def.HiddenPower + " 含连锁算子=" + HasChainedOp(def)
                            + " → CanBeSacrifice=" + can + "，应为 " + !expectedBlocked);
                    continue;
                }

                if (expectedBlocked)
                {
                    blocked++;
                    if (string.IsNullOrEmpty(why))
                    {
                        bad.Add("连锁牌被拒时没有给原因：" + def.Id);
                    }
                }
                else
                {
                    allowed++;

                    // 能当献祭牌 ⇒ 它身上**一条连锁算子都没有** ⇒ 界面上永远不会
                    // 出现「把连锁效果搬走」这种选项（两条规则不打架）。
                    if (HasChainedOp(def))
                    {
                        bad.Add("能献祭的牌里却含连锁算子：" + def.Id);
                    }

                    // 而且**它的效果一条不少地都可选**（TransferableEffects 不做过滤）。
                    if (WitchWorkshop.TransferableEffects(def).Count != def.Effects.Count)
                    {
                        bad.Add("可献祭牌的可选效果条数与卡面不符：" + def.Id + " → "
                                + WitchWorkshop.TransferableEffects(def).Count + " / "
                                + def.Effects.Count);
                    }
                }
            }

            notes.Add("献祭位：可放 " + allowed + " 张 · 因连锁被挡 " + blocked + " 张");

            // 一个「连锁算子」也不在卡表里的话，上面那条全表断言就是空跑。
            if (blocked == 0)
            {
                bad.Add("卡表里一张连锁牌都没有 —— 这条断言没被真正覆盖");
            }
        }

        /// <summary>
        /// 2026-10-02 口径里「效果连锁」的算子表（**在自测里重写一遍**，不去调 Core 的实现）。
        ///
        /// <list type="bullet">
        /// <item><c>NoAtkBuff</c>（沉重打击）—— 对本牌力量的**性质声明**，不是一条能用出来的能力；</item>
        /// <item><c>Copy</c>（模仿）—— 依赖「本牌力量视为 X」这个身份；</item>
        /// <item><c>DoubleAtkBonus</c>（飞叶连击）—— 「额外进攻力量翻倍」是倍增器，单独搬走等于空效果。</item>
        /// </list>
        /// </summary>
        private static bool HasChainedOp(CardDef def)
        {
            if (def.Effects == null)
            {
                return false;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectOp op = def.Effects[i].Op;
                if (op == EffectOp.NoAtkBuff || op == EffectOp.Copy || op == EffectOp.DoubleAtkBonus)
                {
                    return true;
                }
            }

            return false;
        }

        // ── ⑥ 单 / 多效果两条分支 ───────────────────────────────────

        private static void CheckEffectChoice(List<string> bad)
        {
            // 单效果牌：不用问，取第 0 条。
            CardDef one = CardLibrary.Get("ab");        // 湍流：加速 ×2（恰好 1 条）
            if (one.Effects.Count != 1)
            {
                bad.Add("ab（湍流）的效果条数变了（" + one.Effects.Count + "）—— 这条断言要重新挑样本");
            }
            else
            {
                if (WitchWorkshop.NeedsEffectChoice(one))
                {
                    bad.Add("单效果牌（" + one.Name + "）却判定为「需要问玩家选哪一条」");
                }

                if (WitchWorkshop.AutoEffectIndex(one) != 0)
                {
                    bad.Add("单效果牌自动取的序号应为 0，实为 " + WitchWorkshop.AutoEffectIndex(one));
                }
            }

            // 多效果牌：必须问。
            CardDef two = CardLibrary.Get("v");         // 藤蔓：双发 + 对方无法防御时减速
            if (two.Effects.Count < 2)
            {
                bad.Add("v（藤蔓）的效果条数变了（" + two.Effects.Count + "）—— 这条断言要重新挑样本");
            }
            else
            {
                if (!WitchWorkshop.NeedsEffectChoice(two))
                {
                    bad.Add("多效果牌（" + two.Name + "）却没有要求玩家选一条效果");
                }

                if (WitchWorkshop.AutoEffectIndex(two) != -1)
                {
                    bad.Add("多效果牌自动取的序号应为 −1（= 必须问），实为 "
                            + WitchWorkshop.AutoEffectIndex(two));
                }

                // 不带序号的旧口径确认入口：应当拦下并说明「先选一条」。
                string reason;
                CardDef target = CardLibrary.Get("c");  // 凝固：减速 ×2
                if (WitchWorkshop.CanConfirm(two, target, out reason))
                {
                    bad.Add("多效果献祭牌没选效果却允许确认");
                }
                else if (string.IsNullOrEmpty(reason))
                {
                    bad.Add("多效果献祭牌没选效果被拒时没有给原因");
                }

                // 序号越界 → 拒
                if (WitchWorkshop.CanConfirm(two, 9, target, out reason))
                {
                    bad.Add("效果序号越界却允许确认");
                }

                // 序号 −1 → 拒
                if (WitchWorkshop.CanConfirm(two, -1, target, out reason))
                {
                    bad.Add("效果序号为 −1（没选）却允许确认");
                }

                // 选一条合法的 → 过
                if (!WitchWorkshop.CanConfirm(two, 0, target, out reason))
                {
                    bad.Add("多效果献祭牌选了第 0 条却不能确认 —— " + reason);
                }
            }

            if (WitchWorkshop.TransferableEffects(null).Count != 0)
            {
                bad.Add("TransferableEffects(null) 应返回空表");
            }

            if (WitchWorkshop.EffectAt(one, 0) == null || WitchWorkshop.EffectAt(one, 1) != null)
            {
                bad.Add("EffectAt 的越界处理不对");
            }
        }

        // ── ⑦ 效果真的转移过去了 ────────────────────────────────────

        private static void CheckTransfer(List<string> bad)
        {
            CardDef source = CardLibrary.Get("v");      // 藤蔓：0 = 双发、1 = 对方无法防御时减速
            CardDef target = CardLibrary.Get("c");      // 凝固：减速 ×2（恰好 1 条）

            if (source.Effects.Count < 2 || target.Effects.Count != 1)
            {
                bad.Add("转移断言的样本变了（v 有 " + source.Effects.Count + " 条、c 有 "
                        + target.Effects.Count + " 条）—— 请重新挑样");
                return;
            }

            // ① 拍平 → 还原：逐项一致（这是「搬过去之后还认得出是哪条效果」的根）
            EffectDef picked = source.Effects[0];
            EffectSpec spec = WitchWorkshop.BuildSpec(source, 0);
            if (spec == null)
            {
                bad.Add("BuildSpec 返回了 null");
                return;
            }

            Expect(bad, "拍平后算子", spec.handlerId, picked.HandlerId);
            Expect(bad, "拍平后主参数键名", spec.primaryKey, EffectDef.PrimaryArgumentName(picked.Op));
            Expect(bad, "拍平后主参数值", spec.primary.ToString(), picked.A.ToString());
            Expect(bad, "拍平后触发时机", spec.trigger.ToString(), picked.Trigger.ToString());
            Expect(bad, "拍平后来源卡", spec.sourceBaseId, source.Id);

            if (!spec.ToEffectDef().ToString().Equals(picked.ToString(), StringComparison.Ordinal))
            {
                bad.Add("还原后的效果与原始效果不同："
                        + spec.ToEffectDef() + " ≠ " + picked);
            }

            EffectSpec copy = EffectSpec.From(picked, source.Id);
            if (!spec.Matches(copy))
            {
                bad.Add("EffectSpec.Matches 对同一份画像返回了 false");
            }

            // ② 合成：目标牌多了一条效果，其余字段一个不动
            CardUpgradeRecord record = CardUpgradeRecord.Of(target.Id,
                WitchWorkshop.BuildTransferMod(source, 0));
            CardDef up = CardUpgrade.Apply(target, record, CardUpgrade.SynthesizedIndexBase);

            Expect(bad, "转移后 ID", up.Id, target.Id + "+");
            Expect(bad, "转移后卡名", up.Name, target.Name + "+");
            Expect(bad, "转移后效果条数", up.Effects.Count.ToString(),
                (target.Effects.Count + 1).ToString());
            Expect(bad, "转移后力量", up.Power.ToString(), target.Power.ToString());
            Expect(bad, "转移后冷却", up.Cooldown.ToString(), target.Cooldown.ToString());
            Expect(bad, "转移后 ArtId", up.ArtId, target.ArtId);
            Expect(bad, "转移后元素", up.Element.ToString(), target.Element.ToString());

            EffectDef added = up.Effects[up.Effects.Count - 1];
            Expect(bad, "搬来的效果算子", added.HandlerId, picked.HandlerId);
            Expect(bad, "搬来的效果主参数", added.A.ToString(), picked.A.ToString());
            Expect(bad, "搬来的效果文案", added.Text, picked.Text);
            Expect(bad, "搬来的效果触发时机（非光环应保持原样）", added.Trigger.ToString(),
                picked.Trigger.ToString());

            if (up.EffectTextWithSymbols.IndexOf(picked.Text, StringComparison.Ordinal) < 0)
            {
                bad.Add("合成后的卡面文案里看不到搬来的效果：" + up.EffectTextWithSymbols.Replace("\n", " | "));
            }

            // ③ 「目标已经有同一条效果」要拒。
            //    喷泉（s）的第 1 条是「快速回填」，而陨石（g）本身就是「快速回填」。
            CardDef fountain = CardLibrary.Get("s");
            CardDef meteor = CardLibrary.Get("g");
            if (fountain.Effects.Count < 2 || meteor.Effects.Count != 1)
            {
                bad.Add("重复效果判定的样本变了（s / g）");
            }
            else
            {
                string reason;
                if (WitchWorkshop.CanConfirm(fountain, 1, meteor, out reason))
                {
                    bad.Add("把「" + fountain.Effects[1].Text + "」搬给本来就有它的《"
                            + meteor.Name + "》却允许确认");
                }
                else if (string.IsNullOrEmpty(reason))
                {
                    bad.Add("重复效果被拒时没有给原因");
                }

                // 对照组：同一张源牌的**另一条**效果（区域加速）搬给陨石应当放行。
                if (!WitchWorkshop.CanConfirm(fountain, 0, meteor, out reason))
                {
                    bad.Add("把「" + fountain.Effects[0].Text + "」搬给《" + meteor.Name
                            + "》却不能确认 —— " + reason);
                }

                // 合成出来之后，那条重复效果确实只出现了一次（没有被并进去）。
                CardDef dup = CardUpgrade.Apply(meteor,
                    CardUpgradeRecord.Of(meteor.Id, WitchWorkshop.BuildTransferMod(fountain, 0)),
                    CardUpgrade.SynthesizedIndexBase);
                if (dup.Effects.Count != 2)
                {
                    bad.Add("对照组合成后效果条数应为 2，实为 " + dup.Effects.Count);
                }
            }

            // ④ 光环的符号要跟着**目标牌已有的光环**改派。
            //    卡表里的光环全是 α，所以造一张只有 β 光环的牌来验这条路径。
            CardDef betaHost = new CardDef(0, "test.betaaura", "测试·β光环", 3, 3,
                new List<EffectDef>
                {
                    new EffectDef(EffectTrigger.Defend, EffectOp.Aura, 2, 0, 0,
                        AuraKind.DefPower, false, "光环：防御力量 +2"),
                },
                false, "test.betaaura", 1, CardElement.None);

            CardDef blaze = CardLibrary.Get("ai");      // 爆燃：α 光环（进攻力量 +4）
            CardDef hostUp = CardUpgrade.Apply(betaHost,
                CardUpgradeRecord.Of(betaHost.Id, WitchWorkshop.BuildTransferMod(blaze, 0)),
                CardUpgrade.SynthesizedIndexBase);

            Expect(bad, "β 目标拿到光环后的指示物数", hostUp.AuraTokenCount.ToString(), "2");
            Expect(bad, "β 目标的光环符号应被改派为 β",
                hostUp.AuraTokenCountOf(EffectTrigger.Defend).ToString(), "2");
            Expect(bad, "β 目标不应出现 α 光环",
                hostUp.AuraTokenCountOf(EffectTrigger.Attack).ToString(), "0");

            // ⑤ 力量为 X 的牌不能当接收方（与效果词条同口径）
            string why;
            if (CardUpgrade.CanTransfer(CardLibrary.Get("x"), spec, out why))
            {
                bad.Add("模仿（力量 X）却允许接收转移来的效果");
            }
            else if (string.IsNullOrEmpty(why))
            {
                bad.Add("力量 X 被拒时没有给原因");
            }

            // ⑥ 存档形状：EffectSpec / EffectSpecCondition 也要能被 JsonUtility 序列化
            CheckSerializableType(bad, typeof(EffectSpec), new HashSet<Type>());
            CheckSerializableType(bad, typeof(EffectSpecCondition), new HashSet<Type>());
        }

        // ── JsonUtility 形状（与 UpgradeBookScenario 同一套判据）──────

        private static void CheckSerializableType(List<string> bad, Type type, HashSet<Type> visited)
        {
            if (!visited.Add(type))
            {
                return;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                bad.Add("存档类型缺少无参构造（JsonUtility 建不出来）：" + type.Name);
            }

            if (type.GetCustomAttribute<SerializableAttribute>() == null)
            {
                bad.Add("存档类型缺少 [Serializable]：" + type.Name);
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance
                                                | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsDefined(typeof(NonSerializedAttribute), false)
                    || field.IsStatic || field.IsPrivate || field.IsInitOnly)
                {
                    bad.Add(type.Name + "." + field.Name + " 不会被 JsonUtility 序列化"
                            + "（需 public 非只读实例字段）");
                }
            }
        }

        // ── ⑧ 价钱（2026-10-03）──────────────────────────────────────

        /// <summary>
        /// 用户 2026-10-03 的价钱公式：
        /// <c>40 × 被消耗牌的冷却 ÷ 它的力量 ÷ 它的效果数 × Max(被消耗牌冷却 − 被强化牌冷却, 1)</c>。
        ///
        /// <para><b>为什么这条线值得单独钉住</b>：价钱的失效方式全是无声的 ——
        /// 分母写反（「÷ 力量」写成「× 力量」）、漏掉 Max 那个下限、
        /// 冷却差取绝对值（把两张牌的顺序写反了）、常数写成别的数。
        /// 前三个都会让价钱**看起来正常**，只有手算样本与方向断言能抓住。</para>
        /// </summary>
        private static void CheckCost(List<string> bad, List<string> notes)
        {
            // ① 手算样本。用**合成牌**而不是卡表里的牌：价钱只读 力量 / 冷却 / 效果条数 三个数，
            //    卡表一改数值这条就假红，而那与价钱无关。
            CardDef cheap = Make("test.cost.cheap", 4, 4, 1);       // 40×4÷4÷1 = 40
            CardDef fast = Make("test.cost.fast", 9, 2, 2);         // 40×2÷9÷2 = 4.444 → 4
            CardDef target2 = Make("test.cost.t2", 3, 2, 1);
            CardDef target4 = Make("test.cost.t4", 3, 4, 1);

            Expect(bad, "冷却差 2 的价钱（40×4÷4÷1×2）",
                WitchWorkshop.Cost(cheap, target2).ToString(), "80");
            Expect(bad, "冷却差 0（被 Max 抬到下限 1）的价钱",
                WitchWorkshop.Cost(cheap, target4).ToString(), "40");
            Expect(bad, "除不尽时四舍五入（4.444 → 4）",
                WitchWorkshop.Cost(fast, target2).ToString(), "4");

            Expect(bad, "底价（MinCost = 冷却差取下限）",
                WitchWorkshop.MinCost(cheap).ToString(), "40");

            if (WitchWorkshop.MinCost(fast) > WitchWorkshop.Cost(fast, target2))
            {
                bad.Add("底价高于实际价钱（MinCost=" + WitchWorkshop.MinCost(fast)
                        + " > Cost=" + WitchWorkshop.Cost(fast, target2) + "）");
            }

            // ② 边界：缺牌 / 力量 0 / 零效果 —— 一律**不抛异常**，且给出给玩家看的原因
            //    （公式里力量与效果数都是分母，漏了守卫就是除以 0）。
            int cost;
            string error;
            if (WitchWorkshop.TryCost(null, target2, out cost, out error)
                || string.IsNullOrEmpty(error))
            {
                bad.Add("只填了一个空位却算出了价钱（或没给原因）");
            }

            if (WitchWorkshop.Cost(null, null) != 0 || WitchWorkshop.Cost(cheap, null) != 0)
            {
                bad.Add("缺牌时价钱应为 0");
            }

            if (WitchWorkshop.TryCost(Make("test.cost.nopower", 0, 3, 1), target2, out cost, out error)
                || string.IsNullOrEmpty(error))
            {
                bad.Add("力量为 0 的牌算价钱时没有安全退出（分母是 0）");
            }

            if (WitchWorkshop.TryCost(Make("test.cost.noeffect", 5, 3, 0), target2, out cost, out error)
                || string.IsNullOrEmpty(error))
            {
                bad.Add("零效果的牌算价钱时没有安全退出（分母是 0）");
            }

            // ③ 四个因素的**方向**（这就是「为什么价钱会变」的根，界面那四句口诀同源）
            CardDef baseCard = Make("test.cost.base", 4, 4, 1);
            int baseCost = WitchWorkshop.Cost(baseCard, target2);           // = 80

            if (WitchWorkshop.Cost(Make("test.cost.cd", 4, 5, 1), target2) <= baseCost)
            {
                bad.Add("冷却变大价钱没变贵 —— 冷却应当在分子上");
            }

            if (WitchWorkshop.Cost(Make("test.cost.pw", 8, 4, 1), target2) >= baseCost)
            {
                bad.Add("力量变大价钱没变便宜 —— 力量应当在分母上");
            }

            if (WitchWorkshop.Cost(Make("test.cost.eff", 4, 4, 2), target2) >= baseCost)
            {
                bad.Add("效果数变大价钱没变便宜 —— 效果数应当在分母上");
            }

            if (WitchWorkshop.Cost(baseCard, target4) >= baseCost)
            {
                bad.Add("目标冷却变大价钱没变便宜 —— 冷却差应当在分子上（差越小越便宜）");
            }

            // ④ 全表扫描：任意两张真牌之间都算得出价钱，且 底价 ≤ 实际价。
            int pairs = 0;
            int maxCost = 0;
            int minCost = int.MaxValue;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef sac = CardLibrary.GetByIndex(i);
                int floor = WitchWorkshop.MinCost(sac);
                if (floor <= 0)
                {
                    bad.Add("卡表里的牌算不出底价：" + sac.Name + "（" + sac.Id + "）P"
                            + sac.Power + " CD" + sac.Cooldown + " E" + sac.Effects.Count);
                    continue;
                }

                for (int j = 0; j < CardLibrary.Count; j++)
                {
                    CardDef tgt = CardLibrary.GetByIndex(j);
                    if (string.Equals(sac.Id, tgt.Id, StringComparison.Ordinal))
                    {
                        continue;       // 同一张牌本来就不让确认，价钱也不必管
                    }

                    int c = WitchWorkshop.Cost(sac, tgt);
                    pairs++;
                    if (c < floor)
                    {
                        bad.Add("实际价钱低于底价：" + sac.Id + " → " + tgt.Id
                                + " Cost=" + c + " < MinCost=" + floor);
                    }

                    if (c < minCost) minCost = c;
                    if (c > maxCost) maxCost = c;
                }
            }

            if (pairs == 0)
            {
                bad.Add("全表一对牌都没算到 —— 这条断言是空跑");
            }

            notes.Add("价钱：全表 " + pairs + " 对，区间 " + minCost + " ~ " + maxCost
                      + " 金（基础值 " + WitchWorkshop.CostBase + "）");

            // ⑤ 金币那一条：钱不够不让确认、原因里要提钱；钱够了照旧放行。
            string reason;
            CardDef sacReal = CardLibrary.Get("v");      // 藤蔓：P5 CD3 E2
            CardDef tgtReal = CardLibrary.Get("c");      // 凝固：CD2
            int realCost = WitchWorkshop.Cost(sacReal, tgtReal);

            if (realCost <= 0)
            {
                bad.Add("真牌照理算得出价钱，实为 " + realCost);
            }
            else
            {
                if (!WitchWorkshop.CanConfirm(sacReal, 0, tgtReal, realCost, out reason))
                {
                    bad.Add("钱刚好够却不允许确认 —— " + reason);
                }

                if (WitchWorkshop.CanConfirm(sacReal, 0, tgtReal, realCost - 1, out reason))
                {
                    bad.Add("钱差 1 金却允许确认");
                }
                else if (string.IsNullOrEmpty(reason)
                         || reason.IndexOf("金", StringComparison.Ordinal) < 0)
                {
                    bad.Add("金币不足被拒时的原因里没提金币：「" + reason + "」");
                }

                if (!WitchWorkshop.CanAfford(0, 0) || WitchWorkshop.CanAfford(realCost - 1, realCost))
                {
                    bad.Add("CanAfford 的边界不对");
                }

                // ⚠ 不带金币的那个重载**必须不受影响** —— 它是规则侧的入口，
                //   「玩家有没有钱」不该绑在纯规则判定上。
                if (!WitchWorkshop.CanConfirm(sacReal, 0, tgtReal, out reason))
                {
                    bad.Add("不带金币的 CanConfirm 被价钱影响了 —— " + reason);
                }
            }

            // ⑥ 给玩家看的那几行字（界面只是把它们摆上去，Core 里必须写得出来）
            if (string.IsNullOrEmpty(WitchWorkshop.CostFormula()))
            {
                bad.Add("公式文案是空的");
            }

            string line = WitchWorkshop.CostLine(sacReal, tgtReal);
            if (string.IsNullOrEmpty(line)
                || line.IndexOf("金", StringComparison.Ordinal) < 0
                || line.IndexOf("=", StringComparison.Ordinal) < 0)
            {
                bad.Add("价钱等式写不出来：「" + line + "」");
            }

            if (string.IsNullOrEmpty(WitchWorkshop.CostRuleLine()))
            {
                bad.Add("四句读法是空的 —— 玩家没有「为什么价钱会变」的口诀");
            }

            if (string.IsNullOrEmpty(WitchWorkshop.CostFloorLine(sacReal)))
            {
                bad.Add("只填了献祭牌时的底价提示写不出来");
            }

            // 「为什么和刚才不一样」：**同一对牌 → 空串**；换目标 → 指出是哪个因素动了。
            if (WitchWorkshop.CostChangeLine(sacReal, tgtReal, realCost, sacReal, tgtReal,
                    realCost).Length != 0)
            {
                bad.Add("价钱没变却给出了变化解释");
            }

            // ⚠ 必须用 CD4 的献祭牌来验「换目标」：CD3 的牌不管配哪张真目标，
            //   冷却差都被 Max 抬到 1，价钱压根不会变。
            CardDef sacChange = CardLibrary.Get("j");    // 闪电：P8 CD4 E1
            CardDef tgtLow = CardLibrary.Get("c");       // 凝固：CD2 → 冷却差 2
            CardDef tgtHigh = CardLibrary.Get("f");      // 滚石冲击：CD4 → 冷却差 1
            int costLow = WitchWorkshop.Cost(sacChange, tgtLow);
            int costHigh = WitchWorkshop.Cost(sacChange, tgtHigh);

            if (costLow == costHigh)
            {
                bad.Add("样本挑错了：换目标之后价钱没变，这条断言是空跑（" + costLow + "）");
            }

            string change = WitchWorkshop.CostChangeLine(sacChange, tgtLow, costLow, sacChange,
                tgtHigh, costHigh);
            if (change.IndexOf("目标冷却", StringComparison.Ordinal) < 0)
            {
                bad.Add("换目标之后的变化解释没指出「目标冷却」这个因素：「" + change + "」");
            }

            if (WitchWorkshop.CostChangeLine(null, null, 0, sacReal, tgtReal, realCost).Length != 0)
            {
                bad.Add("没有上一笔价钱时不该编出变化解释");
            }
        }

        /// <summary>
        /// 造一张<b>合成测试牌</b>（只为价钱断言用 —— 价钱只读 力量 / 冷却 / 效果条数 三个数，
        /// 效果内容本身无所谓，但**条数要对**，它是公式里的分母）。
        /// </summary>
        private static CardDef Make(string id, int power, int cooldown, int effects)
        {
            var list = new List<EffectDef>();
            for (int i = 0; i < effects; i++)
            {
                list.Add(new EffectDef(EffectTrigger.Attack, EffectOp.Haste, 1, text: "测试效果" + i));
            }

            return new CardDef(0, id, "测试·" + id, power, cooldown, list, false, id, 1,
                CardElement.None);
        }

        // ── 辅助 ────────────────────────────────────────────────────

        private static void Expect(List<string> bad, string label, string actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                bad.Add(label + " 不符：实为 " + actual + "，期望 " + expected);
            }
        }
    }
}
