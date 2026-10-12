using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <see cref="CardPoolConfig"/> 的 Inspector 增强（2026-10-07）：
    /// 在默认面板下方补一行「<b>中文名</b>」，把 <c>cardIds</c> 里那串字母 ID
    /// （<c>a</c> / <c>ao</c> / <c>au</c>…）翻成人看得懂的名字。
    ///
    /// <para><b>为什么用 CustomEditor 而不是往资产里加一个序列化字段</b>：
    /// 字段是要落盘的 —— 卡表改了名、资产不重存就永远显示旧名字，而且零报错。
    /// 这里<b>每次重绘都按当前卡表实时翻译</b>（<see cref="CardPoolConfig.NameOf"/>），
    /// 改卡名立刻跟着变。</para>
    ///
    /// <para>⚠ <b>只读面板，不碰任何数据</b>：<see cref="EditorGUILayout"/> 画的全是临时控件，
    /// 没有一个写回 <see cref="SerializedObject"/> ——「卡池是内容，构建器不覆盖」那条口径
    /// （见 <see cref="CardPoolBuilder"/>）在这里同样成立。</para>
    /// </summary>
    [CustomEditor(typeof(CardPoolConfig))]
    public sealed class CardPoolConfigInspector : Editor
    {
        /// <summary>中文名一栏最多列几张卡的逐条明细（再多就只给汇总，免得把面板撑爆）。</summary>
        private const int MaxListedCards = 12;

        private bool _expanded = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var pool = (CardPoolConfig)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("中文名（只读，实时按卡表翻译）",
                EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                if (pool.useAllCards)
                {
                    EditorGUILayout.HelpBox(
                        "本卡池勾了「使用全部卡」—— cardIds 不生效，\n"
                        + "实际内容 = 卡表全部 " + CardLibrary.Count + " 张。",
                        MessageType.Info);
                }

                IReadOnlyList<string> names = pool.CardNames;
                if (names.Count == 0)
                {
                    EditorGUILayout.LabelField("（cardIds 为空）", EditorStyles.miniLabel);
                    return;
                }

                EditorGUILayout.LabelField("张数", names.Count.ToString());

                // 一眼能看全的短清单直接铺开；长的默认收起，点一下才展开。
                if (names.Count <= MaxListedCards)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        EditorGUILayout.LabelField(pool.cardIds[i] + "  " + names[i],
                            EditorStyles.miniLabel);
                    }
                }
                else
                {
                    _expanded = EditorGUILayout.Foldout(_expanded,
                        "逐条明细（" + names.Count + " 张）");
                    if (_expanded)
                    {
                        for (int i = 0; i < names.Count; i++)
                        {
                            EditorGUILayout.LabelField(pool.cardIds[i] + "  " + names[i],
                                EditorStyles.miniLabel);
                        }
                    }
                }

                EditorGUILayout.Space(4);

                // 一个可复制的整条字符串（a = 暴风雪, b = 冰风暴, …）——
                // 排查「资产里这个字母到底是哪张牌」时直接抄走。
                EditorGUILayout.LabelField("配对串（ID = 中文名）", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(pool.CardNamesWithIds,
                    EditorStyles.textField, GUILayout.MinHeight(32));

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("纯中文名串（逗号分隔）", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(pool.NamesText,
                    EditorStyles.textField, GUILayout.MinHeight(32));
            }

            EditorGUILayout.Space(10);
            DrawManualPriority(pool);
        }

        /// <summary>
        /// <b>「手写出牌优先级」一节</b>（2026-10-08）—— 核对 <see cref="CardPoolConfig.priorities"/>
        /// 这张表。表本体由 <see cref="DrawDefaultInspector"/> 直接编辑，这里只做两件
        /// 它做不到的事：
        /// <list type="number">
        /// <item>把字母 ID 翻成中文名（<see cref="CardPoolConfig.NameOf"/>，**每次重绘实时按卡表翻译**）；</item>
        /// <item>标出**写错 / 用不到**的行：空 ID、卡表里不认识的 ID、不在这份卡池里的 ID。</item>
        /// </list>
        ///
        /// <para>⚠ 与上面那节一样，<b>只读，不写回任何数据</b> —— 资产里的内容只由人来改
        /// （「卡池是内容，构建器不覆盖」，见 <c>CardPoolBuilder</c>）。</para>
        /// </summary>
        private static void DrawManualPriority(CardPoolConfig pool)
        {
            EditorGUILayout.LabelField("手写出牌优先级（数值越小越先打）", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                if (!pool.useManualPriority)
                {
                    EditorGUILayout.HelpBox(
                        "开关没勾 —— 这份卡池的出牌顺序仍由 AI 自动计算"
                        + "（四流派档位 + 怪物子类的偏爱）。\n"
                        + "勾上 useManualPriority 之后，下面的表**完全接管**出牌顺序："
                        + "没登记在表里的卡一律并列最低，不会再回落到自动档位。",
                        MessageType.Info);
                    return;
                }

                if (pool.priorities == null || pool.priorities.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "开关勾了、但一行都没填 —— 这个卡池的每一张牌都会并列最低"
                        + "（等于「随便打一张」）。\n请在 priorities 里给每张在意顺序的卡写一行。",
                        MessageType.Warning);
                    return;
                }

                int valid = 0;
                for (int i = 0; i < pool.priorities.Length; i++)
                {
                    CardPriorityEntry entry = pool.priorities[i];
                    string id = entry == null ? string.Empty : entry.cardId;

                    if (string.IsNullOrEmpty(id))
                    {
                        EditorGUILayout.LabelField("第 " + i + " 行：ID 是空的（这一行不生效）",
                            EditorStyles.miniLabel);
                        continue;
                    }

                    valid++;
                    string baseId = CardUpgrade.BaseIdOf(id);
                    CardDef def;
                    bool known = CardLibrary.TryGet(baseId, out def);

                    string warn;
                    if (!known)
                    {
                        warn = "   ⚠ 卡表里没有这个 ID";
                    }
                    else if (!pool.useAllCards && !pool.Contains(baseId))
                    {
                        warn = "   ⚠ 不在本卡池里（写了也不会被打出）";
                    }
                    else
                    {
                        warn = string.Empty;
                    }

                    string name = def != null ? def.Name : id;
                    EditorGUILayout.LabelField(
                        pool.priorities[i].priority + "  " + id + "  " + name + warn,
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.LabelField("生效 " + valid + " 行 · 未登记的卡 = 并列最低",
                    EditorStyles.miniLabel);

                IReadOnlyList<string> outside = pool.PriorityIdsOutsidePool();
                if (outside.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        "以下 ID 写在表里但不在本卡池：" + string.Join(", ", ToArray(outside))
                        + "\n（不影响运行，只是永远不会被打出 —— 确认是不是卡池那边漏勾了。）",
                        MessageType.Warning);
                }
            }
        }

        /// <summary><c>IReadOnlyList&lt;string&gt;</c> → <c>string[]</c>（老 Unity 的 string.Join 只吃数组）。</summary>
        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                array[i] = list[i];
            }

            return array;
        }
    }
}
