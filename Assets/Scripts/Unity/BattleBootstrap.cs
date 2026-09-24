using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 表现桥（M6）的最小骨架。
    ///
    /// 本阶段（批次 0 / 批次 1）只做到「在 Unity 里把内核跑起来、把事件流打到 Console」——
    /// 它的作用是<strong>证明 App 程序集 → Core 程序集的引用链真的通了</strong>，
    /// 并作为批次 3 接 <c>BattleDriver</c> + UI 的落点。
    ///
    /// 用法：在任意场景里挂到空物体上，进 Play 即可看到一局 AI 互殴的完整事件流。
    /// </summary>
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [Header("对局")]
        [Tooltip("随机种子 —— 同种子必然复现同一局（Core 用自实现的确定性 Rng）。")]
        [SerializeField] private int seed = 20260916;

        [Tooltip("AI 座位是否使用光环。规划 §6：最简 AI 默认不使用。")]
        [SerializeField] private bool aiUseAuras = false;

        [Header("输出")]
        [Tooltip("是否把每一步事件打到 Console。")]
        [SerializeField] private bool logEvents = true;

        private readonly List<string> _lines = new List<string>();

        private void Start()
        {
            RunSelfPlay();
        }

        /// <summary>跑一局 AI 互殴，并把结果汇总打到 Console。</summary>
        [ContextMenu("跑一局 AI 互殴")]
        public void RunSelfPlay()
        {
            _lines.Clear();

            BattleEngine engine = BattleEngine.Create(seed);
            var agent = new SimpleAiAgent { UseAuras = aiUseAuras };

            if (logEvents)
            {
                engine.OnEvent += e => _lines.Add(e.Describe());
            }

            engine.Start();

            int guard = 0;
            while (!engine.IsOver)
            {
                engine.Advance();
                if (engine.IsOver)
                {
                    break;
                }

                if (engine.Pending == null)
                {
                    Debug.LogError("[MagicBrawl] 引擎既未终局也没有待决策 —— 状态机卡住");
                    return;
                }

                engine.Submit(agent.Decide(engine.Pending));

                if (++guard > 20000)
                {
                    Debug.LogError("[MagicBrawl] 决策轮次超过 20000，疑似死循环");
                    return;
                }
            }

            if (logEvents && _lines.Count > 0)
            {
                Debug.Log("[MagicBrawl] seed " + seed + " 事件流：\n" + string.Join("\n", _lines.ToArray()));
            }

            BattleState s = engine.State;
            int winner = s.WinnerSeat;
            string who = winner == BattleState.SeatPlayer ? "你（先手）" : winner == BattleState.SeatAi ? "AI（后手）" : "平局";

            Debug.Log("[MagicBrawl] seed " + seed + " 结束：回合 " + s.TurnNumber
                      + " · 事件 " + s.EventSeq + " 条 · 胜者 " + who + "（" + s.EndReason + "）"
                      + " · 终局 " + s.Describe());
        }
    }
}
