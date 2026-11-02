using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>一次攻防的结算结果（多段攻击时会有多条）。</summary>
    public enum RecordHitResult
    {
        /// <summary>用牌挡住了（含守护）。</summary>
        Blocked = 0,

        /// <summary>免疫光环直接化解（石化 / 石盾）。</summary>
        BlockedImmune = 1,

        /// <summary>没挡住 / 放弃防御 → 掉血。</summary>
        Unblocked = 2,
    }

    /// <summary>
    /// <b>一段攻防</b>。双发 / 连击会让同一张进攻牌产生多段 —— 每段一条。
    ///
    /// <para>双发与连击<b>本身不记</b>：它们的存在完全由「这里有几条」表达，
    /// 再写一个「双发」标记就是重复信息。</para>
    /// </summary>
    [Serializable]
    public sealed class HitRecord
    {
        /// <summary>防御方打出的牌名；空串 = 没出牌（未防御 / 无可挡之牌）。</summary>
        public string DefenseCard = string.Empty;

        public RecordHitResult Result;

        /// <summary>这一段造成的掉血。</summary>
        public int Damage;
    }

    /// <summary>
    /// <b>一个半场</b>（一方进攻、另一方防御）。一回合有两段。
    ///
    /// <para>记什么见 <see cref="BattleRecordBuilder"/> 的类注释 —— 判定线是
    /// 「是否需要选目标 / 是否改变了棋盘状态」，<b>力量、冷却、回手一律不记</b>。</para>
    /// </summary>
    [Serializable]
    public sealed class HalfRecord
    {
        /// <summary>进攻方座位。</summary>
        public int AttackerSeat;

        /// <summary>进攻牌名；<see cref="NoPlay"/> 时为空串。</summary>
        public string AttackCard = string.Empty;

        /// <summary>这一半场进攻方没出牌（主动结束 / 手上无牌）→ 自损 1 点。</summary>
        public bool NoPlay;

        /// <summary>效果注记，按发生顺序。文案见 <see cref="BattleRecordText"/>。</summary>
        public List<string> Notes = new List<string>();

        /// <summary>攻防段（至少 1 条；<see cref="NoPlay"/> 时为 0 条）。</summary>
        public List<HitRecord> Hits = new List<HitRecord>();

        /// <summary>有没有值得显示的内容。</summary>
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(AttackCard) && !NoPlay && Hits.Count == 0 && Notes.Count == 0; }
        }
    }

    /// <summary>一回合 = 半场 A（你攻）+ 半场 B（敌攻）。</summary>
    [Serializable]
    public sealed class TurnRecord
    {
        public int Turn;

        /// <summary>半场 A：座位 0 进攻。</summary>
        public HalfRecord First = new HalfRecord();

        /// <summary>半场 B：座位 1 进攻。</summary>
        public HalfRecord Second = new HalfRecord();

        /// <summary>取某个座位的进攻半场。</summary>
        public HalfRecord Of(int attackerSeat, int seat0)
        {
            return attackerSeat == seat0 ? First : Second;
        }
    }

    /// <summary>
    /// <b>一整局的记录</b>（对局结束时的快照，也是历史列表里的一条）。
    ///
    /// <para>它是<b>纯数据</b>：字段全是可序列化的值类型 / 字符串，
    /// <c>JsonUtility</c> 直接能存盘。牌引用在构建时就换成了牌名 ——
    /// 存档里不能出现 <see cref="CardInstance"/>，那是本场战斗的活对象。</para>
    /// </summary>
    [Serializable]
    public sealed class BattleRecord
    {
        /// <summary>对手显示名（怪物名 / 「训练对手」）。</summary>
        public string Opponent = string.Empty;

        /// <summary>结束时间（UTC ticks）—— 列表按它倒序。</summary>
        public long EndedAtTicks;

        public bool Victory;
        public bool Draw;

        /// <summary>结束原因（引擎给的那句）。</summary>
        public string Reason = string.Empty;

        /// <summary>总回合数。</summary>
        public int Turns;

        /// <summary>本地玩家座位（半场 A 的进攻方）。</summary>
        public int LocalSeat;

        public List<TurnRecord> Rows = new List<TurnRecord>();

        public DateTime EndedAtLocal
        {
            get { return new DateTime(EndedAtTicks, DateTimeKind.Utc).ToLocalTime(); }
        }

        /// <summary>列表行上的「回合数」文案。</summary>
        public string TurnsText
        {
            get { return Turns + " 回合"; }
        }

        /// <summary>胜负文案（平局单列）。</summary>
        public string ResultText
        {
            get { return Draw ? "平" : (Victory ? "胜" : "负"); }
        }
    }
}
