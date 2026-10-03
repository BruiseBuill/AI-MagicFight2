using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>怪物资产构建器</b>（2026-10-03）—— 一只怪 = 一份
    /// <see cref="CharacterConfig"/> 资产，放在 <c>Assets/Resources/Monsters/</c>。
    ///
    /// <para><b>为什么是「资产」而不是「代码里的一只怪」</b>：用户口径要的是
    /// 「我自己来调这个怪物的卡池 / 血量」。把血量、卡池、AI 档案、强化、行为、发牌口径
    /// 全写在一份资产上，改怪就只是打开它改几个数 —— 不碰代码、不碰 Prefab、
    /// 不碰任何构建菜单。</para>
    ///
    /// <para><b>「缺失才建」而不是「每次覆盖」</b>：构建器跑第二遍时如果<b>重新写一遍</b>资产，
    /// 你在 Inspector 里手调过的东西（血量、卡池勾选、新加的一条行为）就全没了 ——
    /// 那是本项目已经踩过的坑（手改被构建器冲掉）。所以这里只在<b>文件不存在</b>时建；
    /// 想推倒重来请用下面那个显式的「重置」菜单（它会明确告诉你它会覆盖）。</para>
    ///
    /// <para><b>接线只按 Prefab 改</b>：<c>BattleCanvas.prefab</c> 在场景里是
    /// <b>PrefabInstance</b>（见 <c>SampleScene.unity</c>），改 Prefab 会自动同步 ——
    /// 与 WitchUiBuilder 那种「场景里建再 SaveAsPrefabAsset」的拷贝式构建器不同。</para>
    /// </summary>
    public static class MonsterBuilder
    {
        private const string CanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";
        private const string Folder = "Assets/Resources/Monsters";

        /// <summary>本批怪物的定义。加一只怪 = 在这里加一行。</summary>
        private sealed class MonsterSpec
        {
            public string FileName;
            public string CharacterId;
            public string DisplayName;
            public int Hp;
            public string AiProfile;
            public CharacterAbilityConfig[] Abilities;
            public string Note;
        }

        private static readonly MonsterSpec[] Specs =
        {
            new MonsterSpec
            {
                FileName = "Monster_Bomber",
                CharacterId = "monster.bomber",
                DisplayName = "自爆傀儡",
                // ⚠ 3 点血的用意：自爆伤害 = 自己当前生命，所以它炸出来的数
                //   就是「你还剩多少时间」的直接映射。4 血的话玩家满血也必被炸死
                //   （4 − 0 = 4 = 玩家满血），整场只能靠抢先打中它一下；
                //   3 血则「一下都不打也会剩 1 点」，威胁感在、但不必立刻致命。
                //   想让它更狠：把这里改成 4。
                Hp = 3,
                AiProfile = "",
                Abilities = new[]
                {
                    new CharacterAbilityConfig
                    {
                        id = "bomber.damage",
                        trigger = AbilityTrigger.TurnStarted,
                        operation = CharacterAbilityOp.DamageOpponents,
                        amount = 0,
                        maxUses = 0,
                        triggerTurn = 3,
                        amountSource = AbilityAmountSource.OwnHp,
                    },
                    new CharacterAbilityConfig
                    {
                        id = "bomber.die",
                        trigger = AbilityTrigger.TurnStarted,
                        operation = CharacterAbilityOp.SelfDestruct,
                        amount = 0,
                        maxUses = 0,
                        triggerTurn = 3,
                        amountSource = AbilityAmountSource.Fixed,
                    },
                },
                Note = "第 3 回合不出牌 → 强制伤害 = 自己当前生命 → 自身归零",
            },
        };

        // ══════════════════════════════════════════════════════
        //  菜单
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/怪物 · 生成怪物资产 + 接进战斗（缺失才建）", priority = 60)]
        public static void BuildMissing()
        {
            Run(overwrite: false);
        }

        [MenuItem("魔法乱斗/怪物 · 重置怪物资产（会覆盖你在 Inspector 里的手改）", priority = 61)]
        public static void ResetAll()
        {
            if (!EditorUtility.DisplayDialog("重置怪物资产",
                    "这会用构建器里的定义覆盖 " + Specs.Length + " 份怪物资产，\n"
                    + "你在 Inspector 里手调过的血量 / 卡池 / 行为都会丢失。\n\n确定继续？",
                    "覆盖", "取消"))
            {
                return;
            }

            Run(overwrite: true);
        }

        // ══════════════════════════════════════════════════════
        //  主流程
        // ══════════════════════════════════════════════════════

        private static void Run(bool overwrite)
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("请退出 Play 后再构建。");
            }

            EnsureFolder();

            var created = new List<string>();
            var kept = new List<string>();
            CharacterConfig first = null;

            for (int i = 0; i < Specs.Length; i++)
            {
                MonsterSpec spec = Specs[i];
                string path = Folder + "/" + spec.FileName + ".asset";
                CharacterConfig asset = AssetDatabase.LoadAssetAtPath<CharacterConfig>(path);

                if (asset == null || overwrite)
                {
                    if (asset != null)
                    {
                        AssetDatabase.DeleteAsset(path);
                    }

                    asset = ScriptableObject.CreateInstance<CharacterConfig>();
                    Apply(asset, spec);
                    AssetDatabase.CreateAsset(asset, path);
                    created.Add(spec.FileName + "（" + spec.DisplayName + "）");
                }
                else
                {
                    kept.Add(spec.FileName);
                }

                if (first == null)
                {
                    first = asset;
                }
            }

            AssetDatabase.SaveAssets();

            bool wired = Wire(first);

            Debug.Log("[MonsterBuilder] 新建 " + created.Count + " 份、保留 " + kept.Count + " 份；"
                      + (wired ? "已把第一只怪接到 BattleCanvas。" : "⚠ 没接上 BattleCanvas（见下条警告）。"));
            for (int i = 0; i < created.Count; i++)
            {
                Debug.Log("  · 新建 " + created[i]);
            }

            for (int i = 0; i < kept.Count; i++)
            {
                Debug.Log("  · 保留 " + kept[i] + "（想重置用「怪物 · 重置怪物资产」菜单）");
            }
        }

        private static void Apply(CharacterConfig asset, MonsterSpec spec)
        {
            asset.characterId = spec.CharacterId;
            asset.displayName = spec.DisplayName;
            asset.kind = CharacterKind.Monster;
            asset.initialHp = spec.Hp;
            asset.maxHp = spec.Hp;
            asset.minimumCardCount = 8;
            asset.useAllCards = true;
            asset.cardIds = new string[0];
            asset.cardPool = null;
            asset.aiProfile = spec.AiProfile ?? string.Empty;
            asset.upgrades = new CardUpgradeRecord[0];
            asset.abilities = spec.Abilities ?? new CharacterAbilityConfig[0];
            // 美术：本批怪共用 BattleArtLibrary 里那份（不勾自定义），
            // 将来要单独换图时在资产上勾上 useCustomArt 再填动作即可。
            asset.useCustomArt = false;

            // ⚠ 怪物一律用怪物口径（开局 8 张、之后不补、不换牌）——
            //   这正是「怪物不像人类玩家」这条规则的落点。
            asset.dealPreset = DealPreset.Monster;
        }

        private static bool Wire(CharacterConfig monster)
        {
            if (monster == null || AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPath) == null)
            {
                return false;
            }

            GameObject prefab = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                BattleDriver driver = prefab.GetComponent<BattleDriver>();
                if (driver == null)
                {
                    driver = prefab.GetComponentInChildren<BattleDriver>(true);
                }

                if (driver == null)
                {
                    Debug.LogWarning("[MonsterBuilder] BattleCanvas 上找不到 BattleDriver —— 没接线。");
                    return false;
                }

                var data = new SerializedObject(driver);
                SerializedProperty field = data.FindProperty("_monsterConfig");
                if (field == null)
                {
                    Debug.LogWarning("[MonsterBuilder] BattleDriver 上没有 _monsterConfig 字段（脚本未编译？）。");
                    return false;
                }

                field.objectReferenceValue = monster;
                data.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(prefab, CanvasPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Monsters");
            }
        }
    }
}
