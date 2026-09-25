using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 一方的信息条：名字 + 生命圆点 + 生命数字 + 未用光环数 +（可选）手牌张数。
    ///
    /// 工程规划 §7：对方手牌<b>只显示数量、不显示内容</b>；未使用的光环在卡角显示亮点。
    /// 生命用「圆点 + 数字」两种读法 —— 圆点便于一眼看出还剩几点，
    /// 数字能表达「上限被削」这种圆点不好表达的情况（4 上限削到 3 时，第 4 个点画成「已被削掉」的深色）。
    /// </summary>
    public sealed class PlayerBarView : MonoBehaviour
    {
        [Header("文字")]
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _hpNumber;

        [Header("生命圆点")]
        [SerializeField] private Image[] _hpDots = new Image[0];

        [Header("侧色条")]
        [SerializeField] private Image _accent;

        [Header("手牌张数（对方侧才显示）")]
        [SerializeField] private GameObject _handCountRoot;
        [SerializeField] private TMP_Text _handCount;

        [Header("未使用光环")]
        [SerializeField] private GameObject _auraRoot;
        [SerializeField] private TMP_Text _auraCount;

        /// <summary>是否显示「手牌 ×N」。玩家自己一侧关掉（手牌就在屏幕上摆着）。</summary>
        [SerializeField] private bool _showHandCount = true;

        public bool ShowHandCount
        {
            get { return _showHandCount; }
            set
            {
                _showHandCount = value;
                if (_handCountRoot != null)
                {
                    _handCountRoot.SetActive(value);
                }
            }
        }

        /// <summary>设置本方主色（玩家青 / 对手橙）。</summary>
        public void SetAccent(Color color)
        {
            if (_accent != null)
            {
                _accent.color = color;
            }

            if (_nameLabel != null)
            {
                _nameLabel.color = color;
            }
        }

        public void Bind(PlayerSnapshot p)
        {
            if (_nameLabel != null)
            {
                _nameLabel.text = p.Name;
            }

            if (_hpNumber != null)
            {
                _hpNumber.text = p.Hp + "/" + p.MaxHp;
                _hpNumber.color = p.Hp <= 1 ? UiTheme.HpFull : UiTheme.TextPrimary;
            }

            BindHpDots(p);

            if (_handCountRoot != null)
            {
                _handCountRoot.SetActive(_showHandCount);
            }

            if (_handCount != null)
            {
                _handCount.text = "手牌 ×" + p.HandCount;
            }

            if (_auraRoot != null)
            {
                _auraRoot.SetActive(p.AuraTokensReady > 0);
            }

            if (_auraCount != null)
            {
                _auraCount.text = "未用光环 ×" + p.AuraTokensReady;
            }
        }

        private void BindHpDots(PlayerSnapshot p)
        {
            if (_hpDots == null)
            {
                return;
            }

            for (int i = 0; i < _hpDots.Length; i++)
            {
                Image dot = _hpDots[i];
                if (dot == null)
                {
                    continue;
                }

                bool withinLimit = i < p.MaxHp;
                dot.gameObject.SetActive(p.MaxHp <= _hpDots.Length && i < UiLayout.HpDotCount);

                if (!withinLimit)
                {
                    dot.color = UiTheme.HpGone;
                }
                else if (i < p.Hp)
                {
                    dot.color = UiTheme.HpFull;
                }
                else
                {
                    dot.color = UiTheme.HpEmpty;
                }
            }
        }
    }
}
