using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 商店里的<strong>一个货位</strong>（2026-09-26）。
    ///
    /// <para><b>一个货位里有什么</b>：</para>
    /// <list type="bullet">
    /// <item>卡面 —— 一个子节点，挂的正是那份唯一的 <c>CardView_Hand.prefab</c>（铁律 9）；</item>
    /// <item>价格牌 —— <c>Shop_PricePlate.png</c>（木质六边形，金币已烘在图上，数字擦掉了）；
    /// 价格数字是压在金币右侧的一枚 TMP；</item>
    /// <item>「卖光了」—— 同一格上的另一套显示（卡面与价格牌都关掉，只留一块灰字）；</item>
    /// <item>压暗纱 —— 买不起时盖在卡面上，读作「这张现在拿不走」。</item>
    /// </list>
    ///
    /// <para><b>⚠ 成交状态由上层决定，本类只画</b>：买得起 / 买不起是
    /// <see cref="ShopView.ShelfEntry.Affordable"/> 给的，这里不做「余额 ≥ 价格」的判断 ——
    /// 那件事属于 Core（红线 3：规则只收敛在一处）。</para>
    ///
    /// <para><b>⚠ 为什么点选自己实现 <see cref="IPointerClickHandler"/> 而不用 Button</b>：
    /// 货位底是一张透明的命中区，但卡面自己（<c>CardView</c>）也有一层按钮与自己的点击语义。
    /// 用 Button 会叠出两套选中态，还会把未购买状态的颜色刷上去；
    /// 直接吃指针事件最干净 —— 且「买不买得起」不影响能不能点（点不起只是提示，不是禁用）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopSlotView : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>卡面（子节点，来自 <c>CardView_Hand.prefab</c>）。</summary>
        [SerializeField] private CardView _card;

        /// <summary>价格牌底图根节点（含金币与数字）。</summary>
        [SerializeField] private GameObject _pricePlate;

        /// <summary>价格数字。</summary>
        [SerializeField] private TMP_Text _priceText;

        /// <summary>「卖光了」那一行灰字。</summary>
        [SerializeField] private GameObject _soldOut;

        [SerializeField] private TMP_Text _soldOutText;

        /// <summary>买不起时压在卡面上的黑纱。</summary>
        [SerializeField] private GameObject _dim;

        /// <summary>命中区（透明的整格矩形，吃射线）。</summary>
        [SerializeField] private Image _hit;

        /// <summary>被点了（参数是自己）。</summary>
        public event Action<ShopSlotView> Clicked;

        /// <summary>这一格当前对应的卡（卖光了 = null）。</summary>
        public CardDef Card { get; private set; }

        /// <summary>当前价格。</summary>
        public int Price { get; private set; }

        private void Awake()
        {
            // 构建期在整格的透明命中区上把 raycastTarget 打开即可；
            // 这里不改任何东西 —— 货位**始终可点**（买不起也让点，由上层弹提示）。
        }

        /// <summary>整格显示 / 隐藏（货位数少于模板数时，多出来的模板要关掉）。</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 把一格画出来。
        ///
        /// <para>卖光了（<c>Card == null</c>）时：关掉卡面与价格牌，只显示灰字 ——
        /// 且**这一格不响应点击**（空位不是一个「已售罄商品」，见架构文档 §5.2）。</para>
        /// </summary>
        public void Bind(ShopView.ShelfEntry entry)
        {
            Card = entry.Card;
            Price = entry.Price;

            bool soldOut = entry.Card == null;

            if (_soldOut != null)
            {
                _soldOut.SetActive(soldOut);
            }

            if (soldOut)
            {
                if (_card != null)
                {
                    _card.gameObject.SetActive(false);
                }

                if (_pricePlate != null)
                {
                    _pricePlate.SetActive(false);
                }

                if (_hit != null)
                {
                    _hit.raycastTarget = false;      // 空位不可点
                }

                return;
            }

            if (_soldOutText != null)
            {
                _soldOutText.text = "卖光了";
            }

            if (_card != null)
            {
                _card.gameObject.SetActive(true);

                // 卡面唯一来源 = 卡表定义；冷却值取基础冷却（与手牌口径一致）。
                CardSnapshot snap = CardSnapshot.FromDef(entry.Card, 0, entry.Card.Cooldown);
                _card.Bind(snap, CardView.ViewMode.Hand, entry.Index);

                // ⚠ 尺寸一律**运行时给**，不要去信 Prefab 里烘着的那份 CardRoot.localScale
                //   （2026-09-27 实测：构建器在编辑模式里调 SetFaceWidth 写下的缩放
                //   **没有跟着嵌套 Prefab 存进 ShopCanvas.prefab** —— 存下来的是
                //   CardView_Hand.prefab 里那份旧值 0.32，症状是「改了 UiLayout 常量、
                //   卡面纹丝不动」，而且零报错）。这里每次绑定都按当前常量重申一次。
                _card.SetFaceWidth(UiLayout.ShopCardWidth);

                _card.SetInteractable(false);        // 商店里的卡不参与出牌，点击由货位自己接
            }

            if (_pricePlate != null)
            {
                _pricePlate.SetActive(true);
            }

            if (_priceText != null)
            {
                _priceText.text = entry.Price.ToString();

                // 特价 = 暖金；买不起 = 暗红；其余 = 近白。
                // ⚠ 这三个都是**文字色**（不是底图美术），所以写在代码里不违反「提示色不进代码」那条。
                if (!entry.Affordable)
                {
                    _priceText.color = UiTheme.ShopPriceTooExpensive;
                }
                else if (entry.Discount)
                {
                    _priceText.color = UiTheme.ShopPriceDiscount;
                }
                else
                {
                    _priceText.color = UiTheme.ShopPriceText;
                }
            }

            if (_dim != null)
            {
                _dim.SetActive(!entry.Affordable);
            }

            if (_hit != null)
            {
                _hit.raycastTarget = true;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Card == null)
            {
                return;                              // 空位不冒泡
            }

            Clicked?.Invoke(this);
        }
    }
}
