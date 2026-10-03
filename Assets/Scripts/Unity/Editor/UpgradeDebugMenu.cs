using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>强化（多轴）的调试出口</b>（2026-10-02）。
    ///
    /// <para><b>为什么需要菜单而不是加界面</b>：强化场景（<c>Upgrade.unity</c>）那一版只发放
    /// 一个方向（力量 +2），而框架现在支持三轴（力量 / 冷却 / 词条）。给弹窗加「选方向」
    /// 是一批界面工作（要动被用户手调过的 <c>UpgradeCanvas.prefab</c>），
    /// 但<b>「框架到底能不能叠三轴」这件事现在就该能验</b> —— 所以先把三条轴做成菜单，
    /// 每条都走与界面完全相同的写入口（<c>SaveStore.TryAppendUpgrade</c>）与同一套规则
    /// （<c>CardUpgrade.CanApply</c>）。</para>
    ///
    /// <para><b>⚠ 它动的是真存档</b>（主存档 <c>save-main.json</c>）。用之前先想清楚，
    /// 或者先复制一份文件。最后一个菜单项可以把强化册清空复原。</para>
    /// </summary>
    public static class UpgradeDebugMenu
    {
        private const string Root = "魔法乱斗/调试/强化（主存档）";

        // ── 三个轴各一条：作用在卡池里第一张「还能被这个方向强化」的牌上 ──

        [MenuItem(Root + "/力量 +2", false, 100)]
        private static void UpgradePower()
        {
            ApplyToFirstEligible(CardUpgradeMod.Power(CardUpgrade.PowerStep), "力量 +2");
        }

        [MenuItem(Root + "/冷却 −1（加速）", false, 101)]
        private static void UpgradeCooldown()
        {
            ApplyToFirstEligible(CardUpgradeMod.Cooldown(-1), "冷却 −1");
        }

        [MenuItem(Root + "/追加词条「光环：快速回填」", false, 102)]
        private static void UpgradeAuraHaste()
        {
            ApplyToFirstEligible(CardUpgradeMod.Effect(UpgradeTraits.AuraHaste),
                "追加词条「" + UpgradeTraits.LabelOf(UpgradeTraits.AuraHaste) + "」");
        }

        [MenuItem(Root + "/追加词条「加速」", false, 103)]
        private static void UpgradeHasteKeyword()
        {
            ApplyToFirstEligible(CardUpgradeMod.Effect(UpgradeTraits.Haste),
                "追加词条「" + UpgradeTraits.LabelOf(UpgradeTraits.Haste) + "」");
        }

        // ── 查看 / 复原 ──

        [MenuItem(Root + "/打印强化册", false, 120)]
        private static void PrintBook()
        {
            UpgradeBook book;
            string error;
            if (!SaveStore.TryLoadUpgradeBook(SaveSlot.Main, out book, out error))
            {
                Debug.LogError("[UpgradeDebug] 读强化册失败：" + error);
                return;
            }

            if (book.IsEmpty)
            {
                Debug.Log("[UpgradeDebug] 主存档没有强化：" + SaveStore.FilePathFor(SaveSlot.Main));
                return;
            }

            ICardCatalog catalog = SaveStore.WithSavedUpgrades(SaveSlot.Main, ResolvedBaseCatalog());
            var log = new System.Text.StringBuilder("[UpgradeDebug] 主存档强化册：" + book.Count + " 张");
            for (int i = 0; i < book.ToRecords().Count; i++)
            {
                CardUpgradeRecord record = book.ToRecords()[i];
                CardDef current = Resolve(catalog, record.baseId);
                log.AppendLine();
                log.Append("  ").Append(record.baseId).Append(" → ").Append(record.Describe());
                if (current != null)
                {
                    log.Append("　[").Append(current.Id).Append(' ').Append(current.Name)
                        .Append(" 力量 ").Append(current.PowerText)
                        .Append(" 冷却 ").Append(current.Cooldown)
                        .Append(" 光环 ").Append(current.AuraTokenCount).Append(']');
                }
            }

            Debug.Log(log.ToString());
        }

        [MenuItem(Root + "/清空强化册（复原）", false, 121)]
        private static void ClearBook()
        {
            UpgradeBook book;
            string error;
            if (!SaveStore.TryLoadUpgradeBook(SaveSlot.Main, out book, out error))
            {
                Debug.LogError("[UpgradeDebug] 读主存档失败：" + error);
                return;
            }

            if (!SaveStore.TryReplaceUpgrades(SaveSlot.Main, null, out error))
            {
                Debug.LogError("[UpgradeDebug] 清空失败：" + error);
                return;
            }

            Debug.Log("[UpgradeDebug] 已清空主存档的强化册（原 " + book.Count + " 张）："
                      + SaveStore.FilePathFor(SaveSlot.Main));
        }

        // ── 实现 ────────────────────────────────────────────────

        private static void ApplyToFirstEligible(CardUpgradeMod mod, string label)
        {
            PlayerData player;
            string error;
            if (!SaveStore.TryLoadPlayer(SaveSlot.Main, out player, out error) || player == null)
            {
                Debug.LogError("[UpgradeDebug] 读主存档失败：" + error);
                return;
            }

            UpgradeBook book = UpgradeBook.FromRecords(player.upgrades);
            ICardCatalog catalog = SaveStore.WithSavedUpgrades(SaveSlot.Main, ResolvedBaseCatalog());

            for (int i = 0; i < player.cardIds.Count; i++)
            {
                string baseId = CardUpgrade.BaseIdOf(player.cardIds[i]);
                CardDef current = Resolve(catalog, baseId);
                if (current == null)
                {
                    continue;
                }

                CardUpgradeRecord existing;
                if (!book.TryGet(baseId, out existing))
                {
                    existing = null;
                }

                string reason;
                if (!CardUpgrade.CanApply(current, mod, existing, out reason))
                {
                    continue;
                }

                if (!SaveStore.TryAppendUpgrade(SaveSlot.Main, baseId, mod, out error))
                {
                    Debug.LogError("[UpgradeDebug] 写入失败：" + error);
                    return;
                }

                ICardCatalog after = SaveStore.WithSavedUpgrades(SaveSlot.Main, ResolvedBaseCatalog());
                CardDef result = Resolve(after, baseId);
                Debug.Log("[UpgradeDebug] " + label + " → 《" + current.Name + "》(" + baseId + ")："
                          + "力量 " + current.Power + " → " + result.Power
                          + " · 冷却 " + current.Cooldown + " → " + result.Cooldown
                          + " · 光环 " + current.AuraTokenCount + " → " + result.AuraTokenCount
                          + " · 卡面 " + result.Id + " / " + result.Name
                          + "　（" + SaveStore.FilePathFor(SaveSlot.Main) + "）");
                return;
            }

            Debug.LogWarning("[UpgradeDebug] 主存档里没有一张牌还能被「" + label + "」强化。");
        }

        /// <summary>基础卡目录（用来合成强化版）。</summary>
        private static ICardCatalog ResolvedBaseCatalog()
        {
            CardCatalogAsset asset = Resources.Load<CardCatalogAsset>("CardCatalog");
            return asset == null ? CardCatalog.Builtin() : asset.CreateCatalog();
        }

        /// <summary>在（已叠过强化册的）目录里取这张牌<b>当前</b>的定义。</summary>
        private static CardDef Resolve(ICardCatalog catalog, string baseId)
        {
            IReadOnlyList<string> ids = new List<string> { baseId };
            IReadOnlyList<string> resolved = CardUpgrade.PreferUpgraded(ids, catalog);
            if (resolved.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < catalog.All.Count; i++)
            {
                if (catalog.All[i] != null
                    && string.Equals(catalog.All[i].Id, resolved[0], System.StringComparison.Ordinal))
                {
                    return catalog.All[i];
                }
            }

            return null;
        }
    }
}
