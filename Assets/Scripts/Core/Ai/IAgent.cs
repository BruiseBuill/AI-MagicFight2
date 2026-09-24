using System;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 决策者接口（M5）。AI 与（未来的）网络对手都实现它；
    /// 玩家座位由 UI 渲染 <see cref="DecisionRequest"/> 后回填 <see cref="DecisionResponse"/>。
    ///
    /// 升级位：换成 <c>HeuristicAgent</c>（局面评估）→ <c>SearchAgent</c>（浅层搜索）时，
    /// 内核一行都不用改 —— 评估所需特征在 <see cref="BattleState"/> 里已齐备。
    /// </summary>
    public interface IAgent
    {
        /// <summary>对一个决策请求立即给出应答（必须是 <paramref name="request"/> 里的合法选项）。</summary>
        DecisionResponse Decide(DecisionRequest request);
    }

    /// <summary>永远放弃 / 不执行的决策者（自测与调试用）。</summary>
    public sealed class PassiveAgent : IAgent
    {
        public DecisionResponse Decide(DecisionRequest request)
        {
            return DecisionResponse.Skip(request.Seat);
        }
    }
}
