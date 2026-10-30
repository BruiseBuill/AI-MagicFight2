using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>主菜单的单场景入口</b>（2026-10-28 · 大改）。
    ///
    /// <para><b>它把这条链路串起来</b>：</para>
    /// <list type="bullet">
    /// <item><b>标准战斗</b> → <see cref="BattleLaunch.SetStandard"/> → 直接进战斗场景，
    /// 双方各随机 8 张；</item>
    /// <item><b>自定义战斗</b> → 进牌组编辑场景（<see cref="SceneRoutes.DeckScene"/>），
    /// 挑完两套 8 张再开打。</item>
    /// </list>
    ///
    /// <para><b>⚠ 为什么标准战斗这里要显式清一次</b>：<see cref="BattleLaunch"/> 是静态的、
    /// 换场景不会重置 —— 上一局要是自定义战斗，不清的话再点「标准战斗」会沿用那两套牌，
    /// 症状是「选了标准却拿到自己配的牌组」，零报错。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeSceneEntry : MonoBehaviour
    {
        [Header("视图（构建器接线）")]
        [SerializeField] private HomeView _view;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<HomeView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[HomeSceneEntry] 没有接 HomeView —— 先跑 `魔法乱斗/P0 · 构建 Home 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）。
            _view.StandardClicked += OnStandardClicked;
            _view.CustomClicked += OnCustomClicked;

            _view.SetHint("标准战斗 = 双方各随机 8 张　·　自定义战斗 = 自己挑双方的 8 张");
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.StandardClicked -= OnStandardClicked;
            _view.CustomClicked -= OnCustomClicked;
        }

        /// <summary>「标准战斗」：清掉上一趟的自定义牌组，直接开打。</summary>
        private void OnStandardClicked()
        {
            BattleLaunch.SetStandard();
            Debug.Log("[HomeSceneEntry] 标准战斗 → " + SceneRoutes.BattleScene);
            SceneRoutes.LoadBattle();
        }

        /// <summary>「自定义战斗」：去牌组编辑界面。</summary>
        private void OnCustomClicked()
        {
            Debug.Log("[HomeSceneEntry] 自定义战斗 → " + SceneRoutes.DeckScene);
            SceneRoutes.LoadDeck();
        }
    }
}
