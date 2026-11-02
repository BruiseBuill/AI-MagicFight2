using System;
using MagicBrawl.Core;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>把一场战斗的事件流接进战报</b>（2026-11-02）。
    ///
    /// <para>用法只有两步：开打时 <see cref="Attach"/>，结束时 <see cref="Finish"/>。
    /// 记录本身由 <see cref="BattleRecordBuilder"/> 在 Core 里生成 ——
    /// 这一层只负责「接上事件」和「算完落盘」。</para>
    ///
    /// <para><b>为什么在 BattleDriver 里挂、而不是在 BattleUi 里</b>：引擎实例归 Driver 持有，
    /// 它的 <c>OnEvent</c> 是唯一的全量事件出口。挂在 Driver 上，UI 换不换都不影响记录。</para>
    /// </summary>
    public static class BattleRecordRecorder
    {
        private static BattleRecordBuilder _builder;

        /// <summary>本局还没落盘的记录构建器；null = 没有正在进行的对局。</summary>
        public static BattleRecordBuilder Current
        {
            get { return _builder; }
        }

        /// <summary>开一局：清空旧记录并订阅这个引擎的事件。</summary>
        public static void Attach(BattleEngine engine, int localSeat)
        {
            if (engine == null) return;

            // 把 builder 捕获进闭包：换局之后旧引擎即使还残留几条事件，
            // 也只会写进它自己那个已经废弃的 builder，不会污染新的一局。
            var builder = new BattleRecordBuilder(localSeat);
            _builder = builder;
            engine.OnEvent += e => builder.Observe(e);
        }

        /// <summary>
        /// 收尾：生成记录、补上结局信息、写入本地存档。
        /// 返回落盘的那一份（没有进行中的对局时返回 null）。
        /// </summary>
        public static BattleRecord Finish(string opponent, bool victory, bool draw, string reason)
        {
            if (_builder == null) return null;

            BattleRecordBuilder builder = _builder;
            _builder = null;

            BattleRecord record = builder.Build();
            record.Opponent = string.IsNullOrEmpty(opponent) ? "对手" : opponent;
            record.Victory = victory && !draw;
            record.Draw = draw;
            record.Reason = reason ?? string.Empty;
            record.EndedAtTicks = DateTime.UtcNow.Ticks;

            BattleRecordStore.Add(record);
            return record;
        }
    }
}
