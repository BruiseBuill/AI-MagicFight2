using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>强化卡的落盘器</b>（2026-09-30）：把「基础力量 +2」之后的定义写成一份新的
    /// <see cref="CardDefinitionAsset"/>，并登记进唯一那份 <c>CardCatalog</c>（动态卡清单）。
    ///
    /// <para><b>为什么是「新资产」而不是改原卡</b>：用户 2026-09-30 口径 ——
    /// 「强化后的本质是生成一张全新的卡，即生成一个全新的 ScriptableObject」。
    /// 内置 42 张是 Core 里的不可变 <see cref="CardDef"/>（纯类），<b>不能也不该被改</b>
    /// （敌方同名牌指着同一份定义）；所以强化卡走「动态卡」这条已有的资产链。</para>
    ///
    /// <para><b>⚠ 一个基础卡只对应一个资产</b>：路径是 <c>Card_&lt;基础ID&gt;_Up.asset</c>，
    /// 反复强化同一张牌是<b>就地更新</b>它（力量一路涨到 9），不是每强化一次多一个文件 ——
    /// 否则「8 → 9」会留下 <c>Card_a_Up</c> / <c>Card_a_Up2</c> 两个资产，
    /// 而卡池里到底用哪一个没人说得清。</para>
    ///
    /// <para><b>⚠ 落盘之后要校对一遍</b>：资产里的效果是通过
    /// <see cref="CardEffectAsset"/> 反序列化回来的，字段映射写错时效果会静默走样
    /// （典型症状：效果栏写着「加速」但数值是 0）。所以这里把资产再读回一个
    /// <see cref="CardDef"/> 与内存那份逐项比对，不一致就<b>放弃资产版、用内存版</b>并打警告 ——
    /// 宁可这次没落盘，也不能让玩家拿到一张效果坏掉的牌。</para>
    /// </summary>
    public static class GeneratedUpgradeCardUtility
    {
        /// <summary>
        /// ⚠ <b>2026-10-02 起不再自动挂钩子</b>（原先是 <c>[InitializeOnLoadMethod]</c> 里
        /// <c>CardUpgradeWriter.Handler = Persist</c>）。
        ///
        /// <para>强化改成「存档里的配方 + 读的时候合成」之后，<b>强化不再写卡资产</b> ——
        /// 资产是<b>全局</b>的，表达不了「同一个基础卡在 A 存档是 +2 力量、在 B 存档是 −1 冷却」，
        /// 怪物侧的强化更是无处安放。这一整条路径（<see cref="Persist"/> 及其校验）
        /// 因此保留为<b>手工调试出口</b>：真要导出一份可以在 Inspector 里看的强化卡，
        /// 从这里调；正常流程不再经过它。</para>
        ///
        /// <para>旧资产（<c>Card_a_Up.asset</c> 等）<b>仍然读得进</b>：它们还在
        /// <c>CardCatalog.generatedCards</c> 里，<c>CardUpgrade.PreferUpgraded</c> 照常认。
        /// 只有当同一张牌又有了<b>新配方</b>时，配方才顶掉资产版
        /// （<c>UpgradeBook.BuildCatalog</c> 的去重规则）。</para>
        /// </summary>
        public static CardDef Persist(CardDef upgraded, CardDef source, out string note)
        {
            if (upgraded == null || source == null)
            {
                note = "参数不全，未落盘";
                return upgraded;
            }

            CardCatalogAsset catalog = GeneratedCardAssetUtility.EnsureCatalog();
            string baseId = CardUpgrade.BaseId(source);
            string path = GeneratedCardAssetUtility.Folder + "/Card_" + baseId + "_Up.asset";

            CardDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<CardDefinitionAsset>(path);
            bool created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<CardDefinitionAsset>();
            }

            Fill(asset, upgraded, source);

            if (created)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            GeneratedCardAssetUtility.Register(catalog, asset);
            AssetDatabase.SaveAssets();

            // ── 校对：把资产读回来，与内存那份逐项比 ──────────────────
            var all = new List<CardDef>();
            all.AddRange(CardLibrary.All);
            foreach (CardDefinitionAsset card in catalog.cards ?? new CardDefinitionAsset[0])
            {
                if (card != null)
                {
                    all.Add(card.CreateDefinition(all.Count + 1000));
                }
            }

            CardDef fromAsset = asset.CreateDefinition(all.Count + 10000);

            string diff = DescribeDifference(upgraded, fromAsset);
            if (diff != null)
            {
                note = "✘ 落盘校验未通过（" + diff + "）→ 本次改用内存定义，资产已写出但先别用：" + path;
                Debug.LogError("[GeneratedUpgradeCard] " + note);
                return upgraded;
            }

            note = "已落盘 " + path;
            return fromAsset;
        }

        /// <summary>把强化后的定义写进资产（卡面插画沿用**源卡**的 <c>artId</c>）。</summary>
        private static void Fill(CardDefinitionAsset asset, CardDef upgraded, CardDef source)
        {
            asset.cardId = upgraded.Id;
            asset.displayName = upgraded.Name;
            asset.power = upgraded.Power;
            asset.cooldown = upgraded.Cooldown;
            asset.hiddenPower = upgraded.HiddenPower;

            // ⚠ 卡面插画是按 **ArtId** 查 CardArtLibrary 的。强化卡的 ID 是 "a+"，
            //   插画表里只有 "a" —— 所以这里必须继承源卡的 ArtId，
            //   否则强化后的牌会变成一块纯色板（零报错）。
            asset.artId = string.IsNullOrEmpty(source.ArtId) ? source.Id : source.ArtId;
            asset.version = upgraded.Version;
            asset.effects = BuildEffects(upgraded.Effects);
        }

        private static CardEffectAsset[] BuildEffects(IReadOnlyList<EffectDef> effects)
        {
            if (effects == null)
            {
                return new CardEffectAsset[0];
            }

            var list = new List<CardEffectAsset>(effects.Count);
            for (int i = 0; i < effects.Count; i++)
            {
                EffectDef ef = effects[i];
                if (ef == null)
                {
                    continue;
                }

                var item = new CardEffectAsset
                {
                    trigger = ef.Trigger,
                    operation = ef.Op,
                    handlerId = ef.HandlerId,
                    amount = ef.A,
                    secondary = ef.B,
                    cooldownAdjustment = ef.C,
                    aura = ef.Aura,
                    mandatory = ef.Mandatory,
                    targets = ef.Targets,
                    specialEvent = ef.SpecialEvent,
                    distinctTargetGroup = ef.DistinctTargetGroup,
                    text = ef.Text,
                };

                if (ef.Conditions != null && ef.Conditions.Count > 0)
                {
                    var conditions = new List<CardEffectConditionAsset>(ef.Conditions.Count);
                    for (int c = 0; c < ef.Conditions.Count; c++)
                    {
                        EffectCondition condition = ef.Conditions[c];
                        conditions.Add(new CardEffectConditionAsset { id = condition.Id, value = condition.Value });
                    }

                    item.conditions = conditions.ToArray();
                }

                list.Add(item);
            }

            return list.ToArray();
        }

        /// <summary>逐项比对（不一致时返回一句人话，一致返回 null）。</summary>
        private static string DescribeDifference(CardDef expected, CardDef actual)
        {
            if (actual == null)
            {
                return "资产没有产出定义";
            }

            if (expected.Id != actual.Id)
            {
                return "ID：" + expected.Id + " ≠ " + actual.Id;
            }

            if (expected.Name != actual.Name)
            {
                return "卡名：" + expected.Name + " ≠ " + actual.Name;
            }

            if (expected.Power != actual.Power)
            {
                return "力量：" + expected.Power + " ≠ " + actual.Power;
            }

            if (expected.Cooldown != actual.Cooldown)
            {
                return "冷却：" + expected.Cooldown + " ≠ " + actual.Cooldown;
            }

            if (expected.AuraTokenCount != actual.AuraTokenCount)
            {
                return "光环指示物数：" + expected.AuraTokenCount + " ≠ " + actual.AuraTokenCount;
            }

            if (expected.ForbidsAtkBuff != actual.ForbidsAtkBuff)
            {
                return "「进攻力量不能增加」标记对不上";
            }

            if (expected.EffectTextWithSymbols != actual.EffectTextWithSymbols)
            {
                return "效果文案：" + expected.EffectTextWithSymbols + " ≠ " + actual.EffectTextWithSymbols;
            }

            if (expected.Effects.Count != actual.Effects.Count)
            {
                return "效果条数：" + expected.Effects.Count + " ≠ " + actual.Effects.Count;
            }

            for (int i = 0; i < expected.Effects.Count; i++)
            {
                string diff = DescribeEffectDifference(i, expected.Effects[i], actual.Effects[i]);
                if (diff != null)
                {
                    return diff;
                }
            }

            return null;
        }

        private static string DescribeEffectDifference(int index, EffectDef expected, EffectDef actual)
        {
            string tag = "效果 #" + index + "：";
            if (expected.Trigger != actual.Trigger)
            {
                return tag + "触发时机 " + expected.Trigger + " ≠ " + actual.Trigger;
            }

            if (expected.HandlerId != actual.HandlerId)
            {
                return tag + "算子 " + expected.HandlerId + " ≠ " + actual.HandlerId;
            }

            // ⚠ 比的是**主参数键名下的那个数**，不是 A 字段本身 —— A 是构造时按算子取出来的，
            //   键名写错时两边都会是 0，只比 A 会「一致地错」而查不出来。
            string primary = EffectDef.PrimaryArgumentName(expected.Op);
            int expectedPrimary = expected.Arg(primary);
            int actualPrimary = actual.Arg(primary);
            if (expectedPrimary != actualPrimary)
            {
                return tag + "主参数（" + primary + "）：" + expectedPrimary + " ≠ " + actualPrimary;
            }

            if (expected.A != actual.A || expected.B != actual.B || expected.C != actual.C)
            {
                return tag + "参数 A/B/C：" + expected.A + "/" + expected.B + "/" + expected.C
                       + " ≠ " + actual.A + "/" + actual.B + "/" + actual.C;
            }

            if (expected.Aura != actual.Aura)
            {
                return tag + "光环类型 " + expected.Aura + " ≠ " + actual.Aura;
            }

            if (expected.Targets != actual.Targets)
            {
                return tag + "作用范围 " + expected.Targets + " ≠ " + actual.Targets;
            }

            if (expected.Mandatory != actual.Mandatory)
            {
                return tag + "是否强制对不上";
            }

            if (expected.SpecialEvent != actual.SpecialEvent)
            {
                return tag + "特殊事件 " + expected.SpecialEvent + " ≠ " + actual.SpecialEvent;
            }

            int expectedConditions = expected.Conditions == null ? 0 : expected.Conditions.Count;
            int actualConditions = actual.Conditions == null ? 0 : actual.Conditions.Count;
            if (expectedConditions != actualConditions)
            {
                return tag + "条件数 " + expectedConditions + " ≠ " + actualConditions;
            }

            return null;
        }

        // ── 菜单：看一眼当前落盘了哪些强化卡（排查用）────────────────

        [MenuItem("魔法乱斗/卡牌/列出强化卡资产", false, 41)]
        private static void ListMenu()
        {
            CardCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(
                GeneratedCardAssetUtility.CatalogPath);
            var log = new StringBuilder("[GeneratedUpgradeCard] 强化卡清单：");

            if (catalog == null || catalog.generatedCards == null || catalog.generatedCards.Length == 0)
            {
                log.AppendLine("（还没有）");
            }
            else
            {
                log.AppendLine(catalog.generatedCards.Length + " 张");
                for (int i = 0; i < catalog.generatedCards.Length; i++)
                {
                    CardDefinitionAsset card = catalog.generatedCards[i];
                    log.AppendLine("  " + (card == null ? "<空>" : card.cardId + " " + card.displayName
                        + " 力量 " + card.power + " 冷却 " + card.cooldown + " artId=" + card.artId));
                }
            }

            Debug.Log(log.ToString());
        }
    }
}
