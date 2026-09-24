using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 出牌指向箭头（M13）—— 手牌被拖进<strong>判定区</strong>之后出现的一支「我现在要打谁」的箭头。
    ///
    /// <para><b>两种口径</b>：进攻 = 红箭头指向对手；防御 = 蓝箭头指向自己。
    /// 方向不由「鼠标落在谁身上」决定（那是旧做法，角色判定框时灵时不灵），
    /// 而是由<b>当前这一拍问的是哪件事</b>决定 —— 引擎在 <c>ChooseAttackCard</c> 里问进攻、
    /// 在 <c>ChooseDefense</c> 里问防御，<see cref="BattleUi"/> 把这件事翻成一个座位号塞给
    /// <see cref="HandView"/>，本类只管画。</para>
    ///
    /// <para><b>为什么不引第三方箭头图</b>：杆用「无 Sprite 的 Image」（Unity 会画一块纯白矩形，
    /// 直接靠 sizeDelta + color 就能当杆），三角头在运行时用 <c>Texture2D</c> 生成一张
    /// 32×32 的等腰三角形 —— 与工程既有做法一致（<c>UiTween</c> 自研、不引 DOTween）。</para>
    ///
    /// <para><b>坐标系</b>：<c>_root</c> 是一块铺满画布的 RectTransform，
    /// 所以本类只收<b>屏幕坐标</b>，内部统一换算到 <c>_root</c> 的局部坐标再摆位 ——
    /// CanvasScaler 缩放多少都不影响。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DropArrowView : MonoBehaviour
    {
        [Header("节点")]
        [SerializeField] private GameObject _root;
        [SerializeField] private RectTransform _arrow;
        [SerializeField] private Image _shaft;
        [SerializeField] private Image _head;

        [Header("尺寸")]
        [SerializeField] private float _shaftThickness = 14f;
        [SerializeField] private float _headWidth = 46f;
        [SerializeField] private float _headLength = 52f;

        /// <summary>三角头 Sprite（首次用到时才生成，全局共用一张）。</summary>
        private static Sprite _headSprite;

        // ⚠ 故意**没有** Awake 里调 Hide()：根节点在构建器里就是失活的，
        // 首次 Show 调 SetActive(true) 那一刻才会触发 Awake —— 如果 Awake 里再 Hide，
        // 箭头会在「刚亮起来的同一帧」被自己按灭（实测踩过：第一下拖进判定区箭头不出现，
        // 第二下才出现）。初始隐藏由构建器 + Configure 负责，这里不再兜第二遍。

        public bool IsVisible
        {
            get { return _root != null && _root.activeSelf; }
        }

        /// <summary>
        /// 画一支从 <paramref name="fromScreen"/> 指向 <paramref name="toScreen"/> 的箭头。
        /// </summary>
        /// <param name="attack">true = 进攻（红）· false = 防御（蓝）。</param>
        public void Show(Vector2 fromScreen, Vector2 toScreen, bool attack)
        {
            if (_root == null || _arrow == null)
            {
                return;
            }

            Vector2 from;
            Vector2 to;
            if (!ToLocal(fromScreen, out from) || !ToLocal(toScreen, out to))
            {
                return;
            }

            Vector2 dir = to - from;
            float len = dir.magnitude;

            // 牌已经贴到目标身上时杆会退化成 0 —— 给一个下限，
            // 否则 atan2(0,0) 与「杆长 = 负值」都会让箭头糊成一团。
            if (len < UiLayout.DropArrowMinLength)
            {
                len = UiLayout.DropArrowMinLength;
            }

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float headLen = Mathf.Min(_headLength, len);
            float shaftLen = Mathf.Max(0f, len - headLen);

            _root.SetActive(true);
            _arrow.anchoredPosition = from;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, angle);

            Color color = attack ? UiTheme.ArrowAttack : UiTheme.ArrowDefend;

            if (_shaft != null)
            {
                _shaft.color = color;
                _shaft.rectTransform.sizeDelta = new Vector2(shaftLen, _shaftThickness);
            }

            if (_head != null)
            {
                _head.color = color;
                _head.rectTransform.sizeDelta = new Vector2(headLen, _headWidth);
                // 三角紧接杆尾
                _head.rectTransform.anchoredPosition = new Vector2(shaftLen, 0f);
            }
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>构建器接线用（Prefab 重建后字段是空的，走这里一次填好）。</summary>
        public void Configure(GameObject root, RectTransform arrow, Image shaft, Image head,
            float shaftThickness, float headWidth, float headLength)
        {
            _root = root;
            _arrow = arrow;
            _shaft = shaft;
            _head = head;
            _shaftThickness = shaftThickness;
            _headWidth = headWidth;
            _headLength = headLength;

            if (_head != null)
            {
                _head.sprite = HeadSprite();
                _head.type = Image.Type.Simple;
                _head.preserveAspect = false;
            }

            Hide();
        }

        /// <summary>屏幕坐标 → <c>_root</c> 局部坐标。</summary>
        private bool ToLocal(Vector2 screenPos, out Vector2 local)
        {
            local = Vector2.zero;
            RectTransform rt = _root.transform as RectTransform;
            return rt != null
                   && RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out local);
        }

        /// <summary>
        /// 生成「右手边尖、左手边满」的等腰三角（白色，交给 <see cref="Image.color"/> 上色）。
        ///
        /// <para>边缘做了半像素羽化：纯 0/1 的斜边在 UI 上放大之后是明显的锯齿，
        /// 箭头这种细长物体上尤其扎眼。</para>
        /// </summary>
        private static Sprite HeadSprite()
        {
            if (_headSprite != null)
            {
                return _headSprite;
            }

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "DropArrowHead";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                float ny = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f;   // 0 = 中线，1 = 上下边

                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;                       // 0 = 左（满），1 = 右（尖）
                    float half = 1f - nx;                               // 该列的半宽
                    float alpha = Mathf.Clamp01((half - ny) * size * 0.5f + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply(false, false);
            _headSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0f, 0.5f), 100f);
            _headSprite.name = "DropArrowHead";
            return _headSprite;
        }
    }
}
