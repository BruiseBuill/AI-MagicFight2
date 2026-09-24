using System;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>角色的动作姿态。</summary>
    public enum CharacterPose
    {
        Idle = 0,
        Attack = 1,
        Defend = 2,

        /// <summary>挨打（掉血）。插播一次，播完回待机。</summary>
        BeHit = 3,

        /// <summary>战败（生命归零）。**播完停在最后一帧**，不回待机 —— 见 <see cref="CharacterView.PlayPoseHold"/>。</summary>
        Death = 4,
    }

    /// <summary>
    /// 战斗界面新美术的<strong>唯一取图入口</strong>（M11 美术层）。
    ///
    /// <para>和 <see cref="CardArtLibrary"/> / <see cref="TriggerIconLibrary"/> 同一套约定：
    /// 素材放在 `Assets/Art/` 下（不在 Resources 里，运行时加载不到），
    /// 用一张 ScriptableObject 把 Sprite 引用烘进去，UI 只认这张表。</para>
    ///
    /// <para><b>帧画布与锚点</b>：人物动画的每一帧都被切成<b>同一尺寸的画布</b>，
    /// 并且「身体中心 / 脚底」落在画布上的同一像素（切分口径见
    /// `Tools/art-audit/slice_char_sheets.py`）。所以这里记下
    /// <see cref="Clip.Pivot"/>（锚点在画布里的归一化位置）与 <see cref="Clip.Size"/>（画布像素尺寸），
    /// 播放时按帧切 pivot 即可让角色原地不动。</para>
    ///
    /// 生成方式：菜单 `魔法乱斗/整理 · 配置新美术导入` 扫目录重建。
    /// </summary>
    [CreateAssetMenu(fileName = "BattleArtLibrary", menuName = "魔法乱斗/战斗美术映射表")]
    public sealed class BattleArtLibrary : ScriptableObject
    {
        /// <summary>约定的 Resources 路径（不含扩展名）。</summary>
        public const string ResourcePath = "BattleArtLibrary";

        /// <summary>一组动画帧。</summary>
        [Serializable]
        public sealed class Clip
        {
            /// <summary>帧序列（同一动作的所有帧画布尺寸相同）。</summary>
            public Sprite[] Frames = new Sprite[0];

            /// <summary>播放帧率（人眼舒适区间 6–12；出招比待机快）。</summary>
            public float Fps = 8f;

            /// <summary>帧画布像素尺寸（= 单帧 Sprite 的原始宽高）。</summary>
            public Vector2 Size = new Vector2(100f, 100f);

            /// <summary>锚点（身体中心 / 脚底）在画布里的归一化位置，Unity 口径（0 = 下 / 左）。</summary>
            public Vector2 Pivot = new Vector2(0.5f, 0f);

            public int FrameCount
            {
                get { return Frames == null ? 0 : Frames.Length; }
            }

            public bool IsValid
            {
                get { return FrameCount > 0; }
            }
        }

        /// <summary>一个角色的全部动作。</summary>
        [Serializable]
        public sealed class CharacterSet
        {
            public string Name;

            /// <summary>立绘（HUD 头像 / 结算页用；没有对应卡时可为空）。</summary>
            public Sprite Portrait;

            public Clip Idle;
            public Clip Attack;
            public Clip Defend;
            public Clip BeHit;
            public Clip Death;

            public Clip Get(CharacterPose pose)
            {
                switch (pose)
                {
                    case CharacterPose.Attack:
                        return Attack;
                    case CharacterPose.Defend:
                        return Defend;
                    case CharacterPose.BeHit:
                        return BeHit;
                    case CharacterPose.Death:
                        return Death;
                    default:
                        return Idle;
                }
            }
        }

        [Header("顶部状态栏")]
        [SerializeField] private Sprite _hudBar;
        [SerializeField] private Sprite _hudAvatar;

        [Tooltip("状态图标（火焰 / 空环 / 药水 / 符文 / 剑印），按顺序对应 UI 上的 5 个槽位。")]
        [SerializeField] private Sprite[] _buffIcons = new Sprite[0];

        [Header("冷却槽（下标 0 = 冷却区4 … 3 = 冷却区1）")]
        [SerializeField] private Sprite[] _slotPlayerRow = new Sprite[0];
        [SerializeField] private Sprite[] _slotEnemyRow = new Sprite[0];

        [Header("舞台")]
        [SerializeField] private Sprite _background;
        [SerializeField] private Sprite _energyOrb;

        [Header("角色")]
        [SerializeField] private CharacterSet _hero = new CharacterSet();
        [SerializeField] private CharacterSet _monster = new CharacterSet();

        [Header("M25 · 查看对方手牌弹窗（Peek）")]
        [Tooltip("弹窗底板（992×447，纵向九宫格 —— 见 slice_peek_kit.py）。")]
        [SerializeField] private Sprite _peekPanel;

        [Tooltip("同款小面板（504×281）。当前版式没用上，留给后续的提示条 / 确认框。")]
        [SerializeField] private Sprite _peekPanelSmall;

        [Tooltip("两端带缺口的横幅标题牌（499×192）。")]
        [SerializeField] private Sprite _peekBanner;

        [Tooltip("竖向边框（431×556）。当前版式没用上，留给放大查看的单卡展示。")]
        [SerializeField] private Sprite _peekFrame;

        [Tooltip("牌背（186×256）。玩家点选的就是它。")]
        [SerializeField] private Sprite _peekCardBack;

        private static BattleArtLibrary _instance;

        /// <summary>从 Resources 取单例（不存在返回 null，UI 退化成纯色占位）。</summary>
        public static BattleArtLibrary Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<BattleArtLibrary>(ResourcePath);
                }

                return _instance;
            }
        }

        public Sprite HudBar
        {
            get { return _hudBar; }
        }

        public Sprite HudAvatar
        {
            get { return _hudAvatar; }
        }

        public Sprite[] BuffIcons
        {
            get { return _buffIcons ?? new Sprite[0]; }
        }

        public Sprite[] SlotPlayerRow
        {
            get { return _slotPlayerRow ?? new Sprite[0]; }
        }

        public Sprite[] SlotEnemyRow
        {
            get { return _slotEnemyRow ?? new Sprite[0]; }
        }

        public Sprite Background
        {
            get { return _background; }
        }

        public Sprite EnergyOrb
        {
            get { return _energyOrb; }
        }

        public CharacterSet Hero
        {
            get { return _hero; }
        }

        public CharacterSet Monster
        {
            get { return _monster; }
        }

        // ── M25 · 查看对方手牌弹窗 ─────────────────────────────

        /// <summary>弹窗底板（无图时 UI 退化成纯色面板）。</summary>
        public Sprite PeekPanel
        {
            get { return _peekPanel; }
        }

        public Sprite PeekPanelSmall
        {
            get { return _peekPanelSmall; }
        }

        public Sprite PeekBanner
        {
            get { return _peekBanner; }
        }

        public Sprite PeekFrame
        {
            get { return _peekFrame; }
        }

        /// <summary>牌背（玩家在一排牌背里点一张）。</summary>
        public Sprite PeekCardBack
        {
            get { return _peekCardBack; }
        }

        /// <summary>按剩余冷却取某一侧的槽底图；<paramref name="remaining"/> 取 1–4，越界返回 null。</summary>
        public Sprite GetSlotRow(bool playerSide, int remaining)
        {
            Sprite[] rows = playerSide ? SlotPlayerRow : SlotEnemyRow;
            int index = 4 - remaining;              // 冷却区4 → 下标 0
            if (index < 0 || index >= rows.Length)
            {
                return null;
            }

            return rows[index];
        }

        /// <summary>
        /// 批量写入 M25「查看手牌弹窗」那几块（编辑器生成器用）。
        ///
        /// <para><b>为什么不并进 <see cref="SetAll"/></b>：<c>SetAll</c> 是 M11 那批
        /// 顶栏 / 槽位 / 角色美术的写入口，签名一动就要连带改它的每一个调用点；
        /// 而 Peek 这批是后加的一层，单独一个入口既不动老代码，也不会被
        /// 「重跑 ArtImportBuilder」连带清空。</para>
        /// </summary>
        public void SetPeek(
            Sprite panel, Sprite panelSmall, Sprite banner, Sprite frame, Sprite cardBack)
        {
            _peekPanel = panel;
            _peekPanelSmall = panelSmall;
            _peekBanner = banner;
            _peekFrame = frame;
            _peekCardBack = cardBack;
        }

        /// <summary>批量写入（编辑器生成器用）。</summary>
        public void SetAll(
            Sprite hudBar, Sprite hudAvatar, Sprite[] buffIcons,
            Sprite[] slotPlayerRow, Sprite[] slotEnemyRow,
            Sprite background, Sprite energyOrb,
            CharacterSet hero, CharacterSet monster)
        {
            _hudBar = hudBar;
            _hudAvatar = hudAvatar;
            _buffIcons = buffIcons ?? new Sprite[0];
            _slotPlayerRow = slotPlayerRow ?? new Sprite[0];
            _slotEnemyRow = slotEnemyRow ?? new Sprite[0];
            _background = background;
            _energyOrb = energyOrb;
            _hero = hero ?? new CharacterSet();
            _monster = monster ?? new CharacterSet();
        }
    }
}
