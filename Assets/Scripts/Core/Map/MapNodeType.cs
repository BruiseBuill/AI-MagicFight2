namespace MagicBrawl.Core
{
    /// <summary>
    /// 地图节点的类型（2026-10-02 · 冒险地图）。
    ///
    /// <para><b>口径</b>（用户 2026-10-02）：起点<b>固定</b>是 <see cref="Camp"/>、
    /// 终点<b>固定</b>是 <see cref="Boss"/>，中间随机。所以这个枚举既是「节点是什么」，
    /// 也是「进去之后会发生什么」的**唯一分类依据** ——
    /// 表现层只按它查表决定「有没有对应场景」（见 <c>MapSceneRoutes</c>），
    /// <b>不在别处再判一遍</b>（铁律 3 的落法）。</para>
    ///
    /// <para><b>为什么没有 <c>Rest</c>（休息点）</b>：用户这轮只给了 7 张节点图
    /// （营地 / 战斗 / 商店 / 女巫 / 石台 / 未知 / Boss），其中「营地」被指定为<b>起点</b>。
    /// 冒险的休息机制还没做，所以这里不凭空造第 8 种类型 ——
    /// 将来做休息点时再往这个枚举里加一项，并在 <c>MapSceneRoutes</c> 里给它一个场景。</para>
    /// </summary>
    public enum MapNodeType
    {
        /// <summary>营地 —— <b>起点</b>专用。没有对应场景（玩家从这里出发）。</summary>
        Camp = 0,

        /// <summary>普通战斗 —— 进战斗场景。</summary>
        Battle = 1,

        /// <summary>商店 —— 进商店场景。</summary>
        Shop = 2,

        /// <summary>女巫的工坊 —— 进女巫工坊场景（献祭一张牌换效果转移）。</summary>
        Witch = 3,

        /// <summary>石台 —— 进强化场景（力量 +2）。</summary>
        Altar = 4,

        /// <summary>未知 —— 事件占位。<b>本轮没有对应场景</b>，进去只是「到此一游」。</summary>
        Unknown = 5,

        /// <summary>Boss —— <b>终点</b>专用。进战斗场景，赢了算通关。</summary>
        Boss = 6,
    }
}
