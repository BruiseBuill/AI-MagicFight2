namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>卡 ID 的纯字符串工具</b>（2026-10-28 · 大改后从 <c>CardUpgrade</c> 里剥出来的那一小块）。
    ///
    /// <para>强化系统已经整个删掉了，所以「<c>a+</c> → <c>a</c>」这件事在正常流程里不会再发生。
    /// 但卡池 / 优先级表 / 特效来源里仍然可能留着带后缀的旧 ID（旧资产、手写表），
    /// 一比对不上就会<b>静默失配</b>（那张牌永远不出现 / 优先级永远不生效）。
    /// 所以把这条归一化留在 Core 里，只有一处实现。</para>
    /// </summary>
    public static class CardIds
    {
        /// <summary>去掉强化后缀（第一个 <c>+</c> 及其之后的一切）。null / 空串原样返回。</summary>
        public static string BaseOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return id;
            }

            int cut = id.IndexOf('+');
            return cut < 0 ? id : id.Substring(0, cut);
        }
    }
}
