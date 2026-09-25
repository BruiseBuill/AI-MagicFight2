using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 卡牌静态定义（`Docs/rules/02-卡牌图鉴.md` 的录入结果）。
    ///
    /// 为什么不用 ScriptableObject：Core 必须能在 <c>dotnet</c> 下独立编译，
    /// ScriptableObject 会把 UnityEngine 拖进来、破坏 asmdef 的 No Engine References 铁律。
    /// 后续若需要在 Inspector 里编辑卡表，再加一层只读适配器即可。
    /// </summary>
    public sealed class CardDef
    {
        /// <summary>卡 ID："a" .. "an"。</summary>
        public readonly string Id;

        /// <summary>卡名，如「暴风雪」。</summary>
        public readonly string Name;
        public readonly string ArtId;
        public readonly int Version;

        /// <summary>力量值。模仿（x）此字段恒为 1，卡面显示见 <see cref="HiddenPower"/>。</summary>
        public readonly int Power;

        /// <summary>基础冷却值。</summary>
        public readonly int Cooldown;

        /// <summary>卡面力量显示为 "?"（仅模仿）。</summary>
        public readonly bool HiddenPower;

        /// <summary>效果列表，按卡面书写顺序 —— 同优先级效果的默认结算顺序。</summary>
        public readonly IReadOnlyList<EffectDef> Effects;

        /// <summary>卡表序号 0–39（a=0 … an=39），与美术资源编号一一对应。</summary>
        public readonly int Index;

        public CardDef(
            int index,
            string id,
            string name,
            int power,
            int cooldown,
            IReadOnlyList<EffectDef> effects,
            bool hiddenPower = false, string artId = null, int version = 1)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new System.ArgumentException("Card ID and name are required.");
            if (cooldown < 1 || power < 0 || version < 1) throw new System.ArgumentException("Invalid card values.");
            Index = index;
            Id = id;
            Name = name;
            Power = power;
            Cooldown = cooldown;
            var copy = new List<EffectDef>(effects ?? new EffectDef[0]);
            if (copy.Contains(null)) throw new System.ArgumentException("Null card effect.");
            Effects = copy.AsReadOnly();
            HiddenPower = hiddenPower;
            ArtId = string.IsNullOrWhiteSpace(artId) ? id : artId;
            Version = version;
        }

        /// <summary>卡面力量文案（模仿显示 "X"）。</summary>
        public string PowerText
        {
            get { return HiddenPower ? "X" : Power.ToString(); }
        }

        /// <summary>本牌拥有的光环指示物总数（双光环 = 2）。</summary>
        public int AuraTokenCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Effects.Count; i++)
                {
                    if (Effects[i].Op == EffectOp.Aura)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>是否带光环。</summary>
        public bool HasAura
        {
            get { return AuraTokenCount > 0; }
        }

        /// <summary>
        /// 本牌的<b>进攻力量不可增加</b>（卡面带 <see cref="EffectOp.NoAtkBuff"/>，目前只有沉重打击 h）。
        ///
        /// <para><b>两处都要看它</b>：</para>
        /// <list type="bullet">
        /// <item><b>规则侧</b>：引擎把进攻光环 / 力量增益一律记在
        /// <c>AttackContext.NoAtkBuff</c> 上，最终 <c>BonusPower</c> 恒为 0 ——
        /// 也就是「用了光环也照样按不变算」；</item>
        /// <item><b>显示侧</b>：手牌的力量预览（把已准备光环的加值画到卡面上）必须<b>跳过它</b>，
        /// 否则卡面会画出一个规则上并不存在的数（用户 2026-09-25 口径：
        /// 「实际逻辑按不变算，显示时也应当不变」）。</item>
        /// </list>
        ///
        /// <para>放在 <see cref="CardDef"/> 而不是表现层各自判断卡 ID：这是**规则事实**
        /// （卡表这一格写了这条效果），表现层只消费布尔量，不需要认卡。</para>
        /// </summary>
        public bool ForbidsAtkBuff
        {
            get
            {
                for (int i = 0; i < Effects.Count; i++)
                {
                    if (Effects[i].Op == EffectOp.NoAtkBuff)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 本牌「触发符号 = <paramref name="trigger"/>」的光环指示物数 ——
        /// <b>第 ⑤ 步真正会点亮几枚就看它</b>。
        ///
        /// <para>规则依据：卡面上的 α / β / γ 就是结算时机（`Docs/rules/01-规则基线.md` §0）——
        /// 本牌作为<b>进攻牌</b>打出时只有 α 光环激活、作为<b>防御牌</b>打出时只有 β 光环激活。
        /// 本批 40 张卡的光环<b>全部标成 α</b>（见 `02-卡牌图鉴.md`），所以拿光环卡去防御
        /// 一枚指示物都不会亮。</para>
        ///
        /// <para>⚠ <see cref="AuraResolver"/> 把「剩余指示物」建模成「光环效果列表的尾部 N 条」，
        /// 所以<b>同一张牌的光环效果必须同符号</b> —— 由
        /// <see cref="CardLibrary.ValidateAuraTriggers"/> 在自测里断言。</para>
        /// </summary>
        public int AuraTokenCountOf(EffectTrigger trigger)
        {
            int n = 0;
            for (int i = 0; i < Effects.Count; i++)
            {
                if (Effects[i].Op == EffectOp.Aura && Effects[i].Trigger == trigger)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>取某一时机下的全部效果（模仿复制时用得上）。</summary>
        public List<EffectDef> EffectsOf(EffectTrigger trigger)
        {
            var list = new List<EffectDef>();
            for (int i = 0; i < Effects.Count; i++)
            {
                if (Effects[i].Trigger == trigger)
                {
                    list.Add(Effects[i]);
                }
            }

            return list;
        }

        /// <summary>能力描述文本：把各效果的 <see cref="EffectDef.Text"/> 拼起来。</summary>
        public string EffectText
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < Effects.Count; i++)
                {
                    if (!string.IsNullOrEmpty(Effects[i].Text))
                    {
                        parts.Add(Effects[i].Text);
                    }
                }

                return string.Join("；", parts.ToArray());
            }
        }

        /// <summary>
        /// 效果文案的**卡面口径**：每条效果前面带上自己的触发符号（α / β / γ），
        /// <b>换行</b>分隔。
        ///
        /// <para>与 <see cref="EffectText"/> 的区别只在「怎么排版」：那个是一句话摘要
        /// （分号连接、无符号），给日志和自测用；这个是几条独立条目，
        /// 给卡面的效果栏用 —— 卡面上每一条效果本来就是独立一行、行首一个符号。</para>
        ///
        /// <para>符号是**字符**（<see cref="TriggerSymbol"/>），表现层拿到之后
        /// 自行决定是直接排版还是换成图标（手牌走 TMP `<sprite>` 图文混排）。</para>
        /// </summary>
        public string EffectTextWithSymbols
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < Effects.Count; i++)
                {
                    EffectDef e = Effects[i];
                    if (string.IsNullOrEmpty(e.Text))
                    {
                        continue;
                    }

                    // Passive 没有符号（TriggerSymbol.Of 返回空串）→ 不加前缀，也不留空格
                    parts.Add(TriggerSymbol.Of(e.Trigger) + e.Text);
                }

                return string.Join("\n", parts.ToArray());
            }
        }

        public override string ToString()
        {
            return Id + " " + Name + " " + PowerText + "/" + Cooldown;
        }
    }
}
