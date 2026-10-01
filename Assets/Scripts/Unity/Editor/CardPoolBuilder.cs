using System.Text;
using MagicBrawl.App;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>三份场景卡池</b>的构建器（2026-09-30）。菜单：<c>魔法乱斗/P5a · 构建卡池资产</c>。
    ///
    /// <para>建 <c>Assets/Resources/Pools/</c> 下的三份 <see cref="CardPoolConfig"/>：</para>
    /// <list type="bullet">
    /// <item><b>BattlePool</b>（战斗）—— <c>useAllCards = true</c>，保持与改造前<b>逐字一致</b>
    /// 的行为（42 张全开）；</item>
    /// <item><b>ShopPool</b>（商店）—— 8 张起始牌池；</item>
    /// <item><b>UpgradePool</b>（强化）—— 同一批 8 张（用户 2026-09-30 口径「强化沿用那 8 张」）。</item>
    /// </list>
    ///
    /// <para><b>⚠ 只在缺失时创建，不在位就不覆盖</b>：卡池是**内容**，玩家/策划会直接改资产。
    /// 每次跑构建器都按代码里的清单重写，等于把编辑器里的改动静默抹掉 ——
    /// 与「用户手调过的节点不要重跑构建器」是同一条口径（铁律 16）。</para>
    ///
    /// <para>顺带把 <c>Resources/Characters/DefaultPlayer.asset</c> 的卡池指到 <c>BattlePool</c>
    /// （只在它是空的时候指）—— 否则「三场景卡池分离」只做了两份，战斗那份还挂在旧字段上。</para>
    /// </summary>
    public static class CardPoolBuilder
    {
        /// <summary>起始牌池的 8 张（唯一来源：从 <c>ShopSceneEntry</c> 里搬出来的那份常量）。</summary>
        private static readonly string[] StartingPool =
        {
            "a",    // 暴风雪
            "b",    // 冰风暴
            "c",    // 凝固
            "d",    // 寒流
            "f",    // 滚石冲击
            "k",    // 电弧
            "s",    // 喷泉
            "ag",   // 淬火
        };

        private const string DefaultPlayerPath = "Assets/Resources/Characters/DefaultPlayer.asset";
        private const string DefaultMonsterPath = "Assets/Resources/Characters/DefaultMonster.asset";

        [MenuItem("魔法乱斗/P5a · 构建卡池资产", false, 39)]
        private static void MenuEntry()
        {
            // ⚠ 无确认弹窗：带弹窗的菜单在脚本 / MCP 调用时会挂住主线程。
            Debug.Log(RunAll());
        }

        /// <summary>无弹窗核心（<see cref="UpgradeUiBuilder.RunAll"/> 会先调它）。</summary>
        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[CardPool] ==== 开始 ====");

            if (Application.isPlaying)
            {
                log.AppendLine("✘ 请先退出 Play 再构建（Play 模式下改资产不会落盘）");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            EnsureFolder("Assets/Resources");
            EnsureFolder(CardPoolConfig.PoolFolder);

            CardPoolConfig battle = Ensure(CardPoolConfig.BattlePoolResourcePath, "BattlePool", log);
            if (battle != null && battle.cardIds.Length == 0 && !battle.useAllCards)
            {
                battle.useAllCards = true;
                battle.poolId = "battle";
                battle.displayName = "战斗卡池（全部卡表）";
                EditorUtility.SetDirty(battle);
                log.AppendLine("  BattlePool：补上 useAllCards = true");
            }

            CardPoolConfig shop = Ensure(CardPoolConfig.ShopPoolResourcePath, "ShopPool", log);
            FillStarting(shop, "shop", "商店卡池（起始 8 张）", log);

            CardPoolConfig upgrade = Ensure(CardPoolConfig.UpgradePoolResourcePath, "UpgradePool", log);
            FillStarting(upgrade, "upgrade", "强化卡池（起始 8 张）", log);

            PointCharacter(DefaultPlayerPath, battle, log);
            // 怪物不接卡池资产：它是「全部卡」的默认行为，接了反而多一处要维护的引用。
            if (AssetDatabase.LoadAssetAtPath<CharacterConfig>(DefaultMonsterPath) != null)
            {
                log.AppendLine("  DefaultMonster：保持原样（useAllCards）");
            }

            // 顺手把「卡目录 → 动态卡清单」与磁盘对齐：目录损坏 / 被删过时，
            // 强化卡会凭空从卡池里消失且零报错，这一步是它的自愈点。
            CardCatalogAsset catalog = GeneratedCardAssetUtility.RebuildCatalog();
            log.AppendLine("  卡目录：" + (catalog.generatedCards == null ? 0 : catalog.generatedCards.Length)
                           + " 张动态卡（" + GeneratedCardAssetUtility.CatalogPath + "）");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.AppendLine("[CardPool] ==== 完成 ====");

            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        /// <summary>取（缺失则建）一份卡池资产。</summary>
        private static CardPoolConfig Ensure(string resourcePath, string assetName, StringBuilder log)
        {
            string path = CardPoolConfig.PoolFolder + "/" + assetName + ".asset";
            CardPoolConfig pool = AssetDatabase.LoadAssetAtPath<CardPoolConfig>(path);
            if (pool != null)
            {
                log.AppendLine("  已在位 " + path + "（保留现有内容，不覆盖）");
                return pool;
            }

            pool = ScriptableObject.CreateInstance<CardPoolConfig>();
            AssetDatabase.CreateAsset(pool, path);
            log.AppendLine("  新建 " + path + "（对照 Resources 路径 " + resourcePath + "）");
            return pool;
        }

        /// <summary>把起始 8 张写进一份卡池（**只在它还是空的时候**）。</summary>
        private static void FillStarting(CardPoolConfig pool, string poolId, string displayName, StringBuilder log)
        {
            if (pool == null)
            {
                return;
            }

            if (pool.cardIds != null && pool.cardIds.Length > 0)
            {
                log.AppendLine("  " + pool.name + "：已有 " + pool.cardIds.Length + " 张，不覆盖");
                return;
            }

            pool.poolId = poolId;
            pool.displayName = displayName;
            pool.useAllCards = false;
            pool.SetIds(StartingPool);
            EditorUtility.SetDirty(pool);
            log.AppendLine("  " + pool.name + "：写入起始 " + StartingPool.Length + " 张（"
                           + string.Join(" ", StartingPool) + "）");
        }

        /// <summary>把角色配置的卡池指到某份资产（只在它为空时指）。</summary>
        private static void PointCharacter(string assetPath, CardPoolConfig pool, StringBuilder log)
        {
            CharacterConfig character = AssetDatabase.LoadAssetAtPath<CharacterConfig>(assetPath);
            if (character == null)
            {
                log.AppendLine("  ⚠ 找不到角色配置 " + assetPath + "（跳过）");
                return;
            }

            if (character.cardPool != null)
            {
                log.AppendLine("  " + character.name + "：卡池已指向 " + character.cardPool.name + "（不动）");
                return;
            }

            if (pool == null)
            {
                return;
            }

            character.cardPool = pool;
            EditorUtility.SetDirty(character);
            log.AppendLine("  " + character.name + "：卡池 → " + pool.name);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
