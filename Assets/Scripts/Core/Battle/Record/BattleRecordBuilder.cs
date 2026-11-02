using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>把引擎事件流压成一张对局记录</b>（2026-11-02）。
    ///
    /// <para><b>为什么从事件重建、而不是在规则里到处埋点</b>：引擎已经把每一个结算事实
    /// 都发成了 <see cref="BattleEvent"/>（进攻 / 防御结果 / 掉血 / 上限 / 冷却改动 / 光环 / 查看…）。
    /// 记录层只是这些事实的<b>另一个消费者</b> —— 不动规则、不加分支，
    /// 也就不会出现「日志说掉了 1 血、引擎其实没掉」这类两套真相的问题。</para>
    ///
    /// <para><b>记什么（用户 2026-11-02 口径）</b>：</para>
    /// <list type="bullet">
    /// <item><b>谁用什么牌进攻 / 对方用什么牌防御 / 挡住还是掉血</b> —— 每段一条；</item>
    /// <item><b>需要选目标的效果</b>：加速·减速（点名那张冷却牌）、区域加速·减速（点名哪个区）、
    /// 立即冷却完成、重置冷却、查看→冷却、弃手牌、复制、光环落点、移出游戏；</item>
    /// <item><b>改变状态的效果</b>：免疫触发、虚弱、回血、生命上限下降；</item>
    /// <item><b>多段攻击</b>：双发 / 连击不单独记，直接体现为「这条半场有几段」。</item>
    /// </list>
    ///
    /// <para><b>一律不记</b>：力量值、冷却数值、回手、发牌、替换、刻印、以及
    /// 双发 / 连击 / 守护这类「没有目标可选、结果已经体现在攻防链里」的效果。</para>
    /// </summary>
    public sealed class BattleRecordBuilder
    {
        private BattleRecord _record;
        private TurnRecord _turn;
        private HalfRecord _half;

        /// <summary>区域类效果的合并表（同一半场里同一档只写一条）。</summary>
        private readonly List<ZoneNote> _zones = new List<ZoneNote>();

        private sealed class ZoneNote
        {
            public string Label;
            public int ZoneSeat;
            public int Count;
        }

        public BattleRecordBuilder(int localSeat)
        {
            Reset(localSeat);
        }

        public BattleRecord Record
        {
            get { return _record; }
        }

        public void Reset(int localSeat)
        {
            _record = new BattleRecord { LocalSeat = localSeat };
            _turn = null;
            _half = null;
            _zones.Clear();
        }

        /// <summary>喂一条事件。可以在对局进行中实时喂，也可以结束后整批回放。</summary>
        public void Observe(BattleEvent e)
        {
            if (e == null || _record == null) return;

            if (e is RoundBeganEvent)
            {
                BeginTurn(e.TurnNumber);
                return;
            }

            TurnStartedEvent turnStarted = e as TurnStartedEvent;
            if (turnStarted != null)
            {
                // 连击的追加进攻算同一个半场 —— 只多一段，不另起一行。
                if (!turnStarted.IsComboFollowUp)
                {
                    BeginHalf(turnStarted.Seat);
                }

                return;
            }

            AttackDeclaredEvent declared = e as AttackDeclaredEvent;
            if (declared != null)
            {
                HalfRecord half = EnsureHalf(declared.Seat);
                if (half != null && string.IsNullOrEmpty(half.AttackCard))
                {
                    half.AttackCard = declared.Card.Def.Name;
                }

                return;
            }

            DefenseResolvedEvent defense = e as DefenseResolvedEvent;
            if (defense != null)
            {
                if (_half == null) BeginHalf(1 - defense.DefenderSeat);
                if (_half == null) return;

                var hit = new HitRecord();
                if (defense.Success && defense.Cards != null && defense.Cards.Count > 0)
                {
                    var names = new List<string>(defense.Cards.Count);
                    for (int i = 0; i < defense.Cards.Count; i++)
                    {
                        if (defense.Cards[i] != null) names.Add(defense.Cards[i].Def.Name);
                    }

                    hit.DefenseCard = string.Join("＋", names.ToArray());
                }

                if (defense.Success)
                {
                    hit.Result = defense.UsedImmune ? RecordHitResult.BlockedImmune : RecordHitResult.Blocked;
                    hit.Damage = 0;
                }
                else
                {
                    hit.Result = RecordHitResult.Unblocked;
                    hit.Damage = 1;
                }

                _half.Hits.Add(hit);
                return;
            }

            CooldownChangedEvent cooldown = e as CooldownChangedEvent;
            if (cooldown != null)
            {
                ObserveCooldown(cooldown.Change);
                return;
            }

            AuraConsumedEvent aura = e as AuraConsumedEvent;
            if (aura != null)
            {
                string note = "光环 " + (aura.Source != null ? aura.Source.Def.Name : "?");
                if (aura.TargetCard != null)
                {
                    note += "→" + aura.TargetCard.Def.Name;
                }

                AddNote(note);
                return;
            }

            HandRevealedEvent peek = e as HandRevealedEvent;
            if (peek != null)
            {
                AddNote("查看 " + peek.Card.Def.Name + (peek.Cooled ? "→冷却" : string.Empty));
                return;
            }

            CardCopiedEvent copied = e as CardCopiedEvent;
            if (copied != null)
            {
                AddNote("复制 " + copied.CardName);
                return;
            }

            CardRemovedEvent removed = e as CardRemovedEvent;
            if (removed != null)
            {
                AddNote("移出 " + removed.Card.Def.Name);
                return;
            }

            HpMaxChangedEvent hpMax = e as HpMaxChangedEvent;
            if (hpMax != null)
            {
                AddNote("上限 " + hpMax.From + "→" + hpMax.To);
                return;
            }

            HealEvent heal = e as HealEvent;
            if (heal != null)
            {
                AddNote("回血 +" + heal.Actual);
                return;
            }

            WeakenAppliedEvent weakened = e as WeakenAppliedEvent;
            if (weakened != null)
            {
                AddNote("虚弱 " + SideName(weakened.Seat));
                return;
            }

            DamageTakenEvent damage = e as DamageTakenEvent;
            if (damage != null)
            {
                ObserveDamage(damage);
                return;
            }

            GameOverEvent over = e as GameOverEvent;
            if (over != null)
            {
                _record.Draw = over.WinnerSeat < 0;
                _record.Victory = over.WinnerSeat == _record.LocalSeat;
                _record.Reason = over.Reason ?? string.Empty;
            }
        }

        /// <summary>收尾：把区域注记并进对应半场，返回最终记录。</summary>
        public BattleRecord Build()
        {
            FlushZones();
            if (_turn != null && !_record.Rows.Contains(_turn))
            {
                _record.Rows.Add(_turn);
            }

            _record.Turns = _record.Rows.Count;
            _turn = null;
            _half = null;
            return _record;
        }

        // ── 回合 / 半场 ─────────────────────────────────────────

        private void BeginTurn(int turnNumber)
        {
            FlushZones();
            if (_turn != null && !_record.Rows.Contains(_turn))
            {
                _record.Rows.Add(_turn);
            }

            _turn = new TurnRecord { Turn = turnNumber };
            _record.Rows.Add(_turn);
            _half = null;
        }

        private void BeginHalf(int attackerSeat)
        {
            if (_turn == null) BeginTurn(1);
            FlushZones();

            UserSeat(attackerSeat, out HalfRecord half);
            _half = half;
        }

        /// <summary>按进攻方取本回合里的对应半场（座位 0 恒为 A）。</summary>
        private void UserSeat(int attackerSeat, out HalfRecord half)
        {
            half = attackerSeat == 0 ? _turn.First : _turn.Second;
            half.AttackerSeat = attackerSeat;
        }

        private HalfRecord EnsureHalf(int attackerSeat)
        {
            if (_half != null && _half.AttackerSeat == attackerSeat) return _half;
            if (_turn == null) BeginTurn(1);
            UserSeat(attackerSeat, out _half);
            return _half;
        }

        private void AddNote(string note)
        {
            if (_half == null || string.IsNullOrEmpty(note)) return;
            if (!_half.Notes.Contains(note)) _half.Notes.Add(note);
        }

        // ── 冷却类效果的归类 ───────────────────────────────────

        /// <summary>
        /// 冷却改动是「加速 / 减速 / 完成 / 重置 / 弃牌」这一族效果<b>唯一的落点</b>
        /// （<see cref="CooldownOps"/> 是冷却数值的唯一入口，每条改动都会带一个 reason）。
        /// 只把有语义的那几条转成注记，其余（进攻开始 −1、打出牌入场、作废）直接丢掉。
        /// </summary>
        private void ObserveCooldown(CooldownChange change)
        {
            string reason = change.Reason ?? string.Empty;
            string name = change.Card != null ? change.Card.Def.Name : null;

            if (reason.Length == 0) return;
            if (reason.StartsWith("进攻开始")) return;
            if (reason.StartsWith("按基础冷却")) return;
            if (reason.StartsWith("光环指示物")) return;
            if (reason.StartsWith("无匹配目标")) return;
            if (reason.StartsWith("查看")) return;          // 由 HandRevealedEvent 统一写

            if (reason.StartsWith("区域加速") || reason.StartsWith("区域减速"))
            {
                if (change.Card == null) return;
                AddZone(reason.StartsWith("区域加速") ? "区域加速" : "区域减速", change.Card.OwnerSeat);
                return;
            }

            if (reason.StartsWith("加速")) { AddNote(name == null ? "加速" : "加速 " + name); return; }
            if (reason.StartsWith("减速")) { AddNote(name == null ? "减速" : "减速 " + name); return; }
            if (reason.StartsWith("重置冷却")) { AddNote("重置 " + name); return; }
            if (reason.StartsWith("立即冷却完成")) { AddNote("完成 " + name); return; }
            if (reason.StartsWith("弃")) { AddNote(name == null ? "弃牌" : "弃 " + name); return; }
        }

        private void AddZone(string label, int zoneSeat)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i].Label != label) continue;
                if (_zones[i].ZoneSeat == zoneSeat) { _zones[i].Count++; return; }
                _zones[i].ZoneSeat = -1;                    // 双方都被影响 → 合并成「双方」
                _zones[i].Count++;
                return;
            }

            _zones.Add(new ZoneNote { Label = label, ZoneSeat = zoneSeat, Count = 1 });
        }

        private void FlushZones()
        {
            if (_half == null || _zones.Count == 0) { _zones.Clear(); return; }

            for (int i = 0; i < _zones.Count; i++)
            {
                ZoneNote zone = _zones[i];
                string suffix = zone.ZoneSeat < 0 ? "(双方)" : (zone.ZoneSeat == _record.LocalSeat ? string.Empty : "(敌)");
                AddNote(zone.Label + suffix);
            }

            _zones.Clear();
        }

        // ── 掉血 ───────────────────────────────────────────────

        private void ObserveDamage(DamageTakenEvent damage)
        {
            string source = damage.Source ?? string.Empty;

            // 未出牌 / 手上无牌：进攻方自损 1 点。这不是「被打」，是这一半场没打成。
            if (source.IndexOf("未出牌", StringComparison.Ordinal) >= 0
                || source.IndexOf("无法攻击", StringComparison.Ordinal) >= 0)
            {
                HalfRecord half = FindHalf(damage.Seat);
                if (half != null)
                {
                    half.NoPlay = true;
                    half.Hits.Add(new HitRecord
                    {
                        DefenseCard = string.Empty,
                        Result = RecordHitResult.Unblocked,
                        Damage = damage.Amount,
                    });
                }

                return;
            }

            // 其余掉血（沉重打击的额外伤害、自爆、怪物强制伤害）—— 用一条附注表达。
            AddNote("♥" + damage.Amount);
        }

        private HalfRecord FindHalf(int attackerSeat)
        {
            if (_half != null && _half.AttackerSeat == attackerSeat) return _half;
            if (_turn == null) return null;
            return attackerSeat == 0 ? _turn.First : _turn.Second;
        }

        private string SideName(int seat)
        {
            return seat == _record.LocalSeat ? "你" : "敌";
        }
    }
}
