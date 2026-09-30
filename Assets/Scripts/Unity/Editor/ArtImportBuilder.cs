using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicBrawl.App;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// 新美术的导入整理器（M11）。菜单：`魔法乱斗/整理 · 配置新美术导入`（幂等可重跑）。
    ///
    /// <para>做三件事：</para>
    /// <list type="number">
    /// <item><b>归位</b> —— 三张 UUID 命名的源表移进各自类目的 `_Source/`；
    /// 背景与版式参考图搬到 `Backgrounds/` 与 `_Reference/`。</item>
    /// <item><b>导入设置</b> —— 成品图统一按 Sprite 导入（无 mipmap、不做压缩、Clamp 包裹）；
    /// `_Source/` 与 `_Reference/` 保持普通贴图，不参与 UI。</item>
    /// <item><b>生成映射表</b> —— 扫目录写 `Assets/Resources/BattleArtLibrary.asset`，
    /// 并从 `Chars/anim_frames.txt` 读入每套动作的<b>画布尺寸与锚点</b>。</item>
    /// </list>
    ///
    /// <para><b>⚠ 搬文件必须走 <see cref="AssetDatabase.MoveAsset"/></b>，不能直接改文件名 ——
    /// Unity 在跑时直接 mv 有被当成「删除 + 新建」而换掉 GUID 的风险
    /// （`Docs/rules/05-决策记录.md`）。</para>
    /// </summary>
    public static class ArtImportBuilder
    {
        private const string ArtRoot = "Assets/Art";
        private const string UiDir = ArtRoot + "/Ui";
        private const string BgDir = ArtRoot + "/Backgrounds";
        private const string CharsDir = ArtRoot + "/Chars";
        private const string IconsDir = ArtRoot + "/Icons";
        private const string ElementsDir = IconsDir + "/Elements";

        /// <summary>
        /// 七元素符号的<b>运行时</b>目录（2026-09-26）。
        ///
        /// <para><see cref="ElementIconLibrary"/> 用
        /// <c>Resources.Load&lt;Sprite&gt;("Elements/Element_XX_Name")</c> 按名取图，
        /// 所以成品图必须落在 Resources 下 —— 那边是**唯一的一份**，
        /// 由 `Tools/art-audit/slice_element_icons.py` 从
        /// <c>Art/Icons/Elements/_Source/Type.png</c> 切出来直接写进去。
        /// ⚠ 别再往 <see cref="ElementsDir"/> 里放成品副本 —— 两份一样名字的图
        /// 就是「改了一份、另一份留着旧的」这种查不出来的错。</para>
        /// </summary>
        private const string ElementResourceDir = "Assets/Resources/Elements";
        private const string RefDir = ArtRoot + "/_Reference";
        private const string LibraryPath = "Assets/Resources/BattleArtLibrary.asset";
        private const string ManifestPath = CharsDir + "/anim_frames.txt";

        /// <summary>源表 → 归档目录（原文件名不动）。</summary>
        private static readonly string[][] SourceMoves =
        {
            new[] { ArtRoot + "/542f45c2-5d2c-4803-bcbd-2cafa10226fc.png", UiDir + "/_Source" },
            new[] { ArtRoot + "/2049a678-64d5-46e6-a342-87eaf3f37435.png", CharsDir + "/_Source" },
            new[] { ArtRoot + "/6e093879-16be-4326-b9e9-c90a767cb587.png", CharsDir + "/_Source" },

            // 2026-09-21 · 「查看对方手牌」弹窗 UI 套件（用户按 Reference-2 的风格出的一张整层图，
            // 切成 5 块 Peek_* 放在 Ui/ 下，切法见 Tools/art-audit/slice_peek_kit.py）。
            new[] { ArtRoot + "/661ca76a-c73d-4618-b6e5-918c75968b58.png", UiDir + "/_Source" },

            // 2026-09-26 · 第三批 UI 套件源表（深蓝金属框卡面 + 标题条 + 按钮底，
            // 与 Reference-3 同一风格族）。先归档，尚未切分接入。
            new[] { ArtRoot + "/148cae8c-29e8-457e-82ec-40f8088a71a2.png", UiDir + "/_Source" },
            new[] { ArtRoot + "/ae67a6sgshbf-bef42ae582f4 (realesrgan-x4plus x3).png", UiDir + "/_Source" },

            // 2026-09-26 · 商店场景（P4）的 UI 套件源表。
            //   它同时是**商店版式参考图**（别的游戏的商店运行截图：左侧巫师 + 4 张带价卡 + 离开按钮 + 背包），
            //   我们从它身上切出了 Shop_PricePlate / Shop_LeaveButton / Shop_BagIcon
            //   （切法见 Tools/art-audit/slice_shop_kit.py）。
            //   放 Ui/_Source 而不是 _Reference：它首先是**可再切一次的 UI 源表**
            //   （与 661ca76a 那张弹窗套件同口径），版参考只是它的副身份。
            new[] { ArtRoot + "/d30f41c7-1d35-43f6-9755-ca54cc6f4a6a.png", UiDir + "/_Source" },
        };

        /// <summary>
        /// 根目录上「与已归档正本逐字节相同」的副本 → 正本。
        ///
        /// <para>判据：正本存在、且两份文件长度相同（同长同源的表在这里就够了；真要比内容
        /// 得读盘哈希，而这条通路每次 RunAll 都会跑，不值得）。</para>
        ///
        /// <para><b>为什么不能登记进 <see cref="SourceMoves"/></b>：那会把副本搬进 `_Source/`，
        /// 得到两份同名同内容的东西 —— 正是「改了一份、另一份留着旧的」这种查不出来的错。
        /// 正本已经在位，副本没有存在的理由。</para>
        /// </summary>
        private static readonly string[][] DuplicateDeletes =
        {
            // 2026-09-26 落的七元素源表副本。此前 2026-09-30 前的注释是「留着等用户确认后再删」；
            // 用户 2026-09-30 明确要求「Art 根目录散图一并归类」→ 按归类处理：删副本，留正本。
            new[] { ArtRoot + "/407da584-b124-4bb2-b070-240f7f88dd0b.png", IconsDir + "/_Source/Type.png" },
        };

        /// <summary>
        /// 第二批主角动画源表（2026-09-18）。五张都丢在 `Assets/Art/` 根目录、
        /// 且带语义名，归档时统一加 `Hero_Anim_` 前缀 ——
        /// 一个光秃秃的 `Idle.png` 躺在 `_Source/` 里，下次谁也不知道它属于哪一批。
        ///
        /// <para>注意 `defense.png` 是小写开头的历史文件名，归档时一并规范成 `Hero_Anim_Defend.png`。</para>
        /// </summary>
        private static readonly string[][] HeroAnimMoves =
        {
            new[] { ArtRoot + "/Idle.png", CharsDir + "/_Source/Hero_Anim_Idle.png" },
            new[] { ArtRoot + "/Attack.png", CharsDir + "/_Source/Hero_Anim_Attack.png" },
            new[] { ArtRoot + "/defense.png", CharsDir + "/_Source/Hero_Anim_Defend.png" },
            new[] { ArtRoot + "/BeHit.png", CharsDir + "/_Source/Hero_Anim_BeHit.png" },
            new[] { ArtRoot + "/Death.png", CharsDir + "/_Source/Hero_Anim_Death.png" },
        };

        /// <summary>其它归位：旧路径 → 新路径（含改名）。</summary>
        private static readonly string[][] RenameMoves =
        {
            new[] { ArtRoot + "/dungeon_background_clean.png", BgDir + "/Bg_Dungeon.png" },
            new[] { ArtRoot + "/Reference.png", RefDir + "/Reference.png" },
            // 2026-09-21：「查看手牌」弹窗的第二张参考图（同样是别的游戏的运行截图，
            // 只用来看那个弹窗长什么样，不进游戏）。
            new[] { ArtRoot + "/Reference-2.png", RefDir + "/Reference-2.png" },
            // 2026-09-26：第三张版式参考（战斗界面全貌，含左侧冷却区 / 中央选牌弹窗 / 底部弧形手牌）。
            new[] { ArtRoot + "/Reference-3.png", RefDir + "/Reference-3.png" },

            // 2026-09-26 · 七元素符号表。⚠ 它已经在
            //     Tools/art-audit/slice_element_icons.py 里切成了 7 张成品图放在
            //     Assets/Art/Icons/Elements/ 下，这里只把**源表**归档 ——
            //     否则一张 1536×1024 的源图留在 Art 根目录，会被当成散落图反复提醒。
            new[] { ArtRoot + "/Type.png", IconsDir + "/_Source/Type.png" },

            // 2026-09-26 · **商店场景的成品背景**（木质内景，无任何烘焙 UI）。
            //     ⚠ 文件名 `ReferenceShop.png` 有误导性 —— 它不是参考图，
            //     它是商店唯一在用的背景（`UiLayout.ShopBgSpritePath` 指向这里）。
            //     真正的「商店版式参考」是上面的 `d30f41c7-…`。
            new[] { ArtRoot + "/ReferenceShop.png", BgDir + "/Bg_Shop.png" },

            // 2026-09-30 · **强化场景（卡牌强化）的成品背景**（石质台面 + 悬浮石板 + 蓝晶，1671×941，无烘焙 UI）。
            //     ⚠ 落到 Art 根目录时叫光秃秃的 `Background.png` —— 那个名字进 Backgrounds/ 会
            //     变成「第二张 Background」，跟已有的 Bg_Dungeon / Bg_Shop 不是一个口径，
            //     所以归类时一并按 `<用途>_背景` 规范成 `Bg_Upgrade.png`。
            new[] { ArtRoot + "/Background.png", BgDir + "/Bg_Upgrade.png" },

            // 2026-09-30 · 强化场景的**版式参考图**（同一张背景 + 左下角站着的巫师 = 目标构图）。
            //     与 Reference / Reference-2 / Reference-3 同族，编号接着排。
            new[] { ArtRoot + "/Reference4.png", RefDir + "/Reference-4.png" },
        };

        // ── 菜单入口（带确认弹窗，不能给脚本化调用）─────────────────────

        [MenuItem("魔法乱斗/整理 · 配置新美术导入", false, 21)]
        private static void MenuEntry()
        {
            if (!EditorUtility.DisplayDialog(
                    "配置新美术导入",
                    "会做：\n\n" +
                    "① 把 UUID 源表移进 Ui/_Source 与 Chars/_Source\n" +
                    "② 第二批主角动画源表（Idle / Attack / defense / BeHit / Death）\n" +
                    "    → Chars/_Source/Hero_Anim_*.png\n" +
                    "③ 背景 → Backgrounds/Bg_Dungeon.png、商店背景 → Backgrounds/Bg_Shop.png、\n" +
                    "    强化背景 → Backgrounds/Bg_Upgrade.png，参考图 → _Reference/\n" +
                    "④ 清掉 Art 根目录上与已归档正本重复的散图（搬进 Artifacts/backups/）\n" +
                    "⑤ 重设全部新图的导入参数\n" +
                    "⑥ 重建 Assets/Resources/BattleArtLibrary.asset\n\n" +
                    "全部操作幂等，可反复重跑。",
                    "执行", "取消"))
            {
                return;
            }

            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[ArtImport] ==== 开始 ====");

            EnsureFolders();
            RemovePythonSideArchives(log);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            MoveAll(log);
            DeleteDuplicates(log);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            ConfigureImporters(log);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            BuildLibrary(log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine("[ArtImport] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ── 目录 ─────────────────────────────────────────────────────

        private static void EnsureFolders()
        {
            string[] dirs = { UiDir, UiDir + "/_Source", BgDir, CharsDir, CharsDir + "/_Source",
                              IconsDir, IconsDir + "/_Source", ElementsDir,
                              ElementResourceDir, RefDir };
            foreach (string d in dirs)
            {
                if (AssetDatabase.IsValidFolder(d))
                {
                    continue;
                }

                string parent = Path.GetDirectoryName(d).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
            }
        }

        /// <summary>
        /// 删掉 Python 切图脚本顺手写进 `_Source/` 的副本。
        ///
        /// <para>那些副本是 PIL 重新编码的，字节和原图不同。归档要留<b>原图</b>，
        /// 所以先清掉副本，再让 <see cref="AssetDatabase.MoveAsset"/> 把原件搬进来
        /// （MoveAsset 在目标已存在时会直接失败）。</para>
        ///
        /// <para><b>⚠ 判定条件必须是「原件还在原位」</b>。第一版只看了「副本存在」就删 ——
        /// 于是重跑一遍菜单会把<b>上一次刚搬进 `_Source/` 的原件</b>当成副本删掉，
        /// 三张源表直接没了（GUID 和 `.meta` 一起没，也不进回收站）。
        /// 加上 <c>File.Exists(from)</c> 之后，第二次重跑走的是
        /// <see cref="MoveOne"/> 的「已在位」分支，是幂等的。</para>
        /// </summary>
        private static void RemovePythonSideArchives(StringBuilder log)
        {
            foreach (string[] pair in SourceMoves)
            {
                string from = pair[0];
                string fileName = Path.GetFileName(from);
                string copy = pair[1] + "/" + fileName;

                if (!File.Exists(from) || !File.Exists(copy))
                {
                    // 原件已搬走 → 这个「副本」其实就是归档成品，绝不能删
                    continue;
                }

                AssetDatabase.DeleteAsset(copy);
                log.AppendLine("  清掉 Python 副本 " + copy);
            }
        }

        private static void MoveAll(StringBuilder log)
        {
            foreach (string[] pair in SourceMoves)
            {
                string from = pair[0];
                string to = pair[1] + "/" + Path.GetFileName(pair[0]);
                MoveOne(from, to, log);
            }

            foreach (string[] pair in RenameMoves)
            {
                MoveOne(pair[0], pair[1], log);
            }

            foreach (string[] pair in HeroAnimMoves)
            {
                MoveOne(pair[0], pair[1], log);
            }
        }

        /// <summary>
        /// 删掉根目录上「与已归档正本重复」的散图（见 <see cref="DuplicateDeletes"/>）。
        ///
        /// <para><b>⚠ 只在正本确实在位时才删</b>：判据是「<c>to</c> 存在」。
        /// 少了这一条，一旦正本被人挪走 / 删掉，这一遍就会把<b>唯一的一份</b>当副本清掉
        /// —— 与 <see cref="RemovePythonSideArchives"/> 第一版栽的是同一个坑。</para>
        /// </summary>
        private static void DeleteDuplicates(StringBuilder log)
        {
            foreach (string[] pair in DuplicateDeletes)
            {
                string copy = pair[0];
                string canonical = pair[1];

                if (!File.Exists(copy))
                {
                    log.AppendLine("  ⚠ 副本已不在，跳过 " + copy);
                    continue;
                }

                if (!File.Exists(canonical))
                {
                    log.AppendLine("  ✘ 正本不在位，拒绝删副本 " + copy + "（正本：" + canonical + "）");
                    continue;
                }

                if (new FileInfo(copy).Length != new FileInfo(canonical).Length)
                {
                    log.AppendLine("  ✘ 长度不同，不当重复处理 " + copy + "（正本：" + canonical + "）");
                    continue;
                }

                // 搬进 Artifacts/backups/ 而不是就地删：那一层在 Assets **之外**，Unity 不导入，
                // 但文件还在盘上、随时能捞回来。`AssetDatabase.DeleteAsset` 是直接删、不进回收站，
                // 对一次「归类」来说代价太大。
                string backupDir = "Artifacts/backups/" + System.DateTime.Now.ToString("yyyy-MM-dd")
                                   + "-artroot-duplicates";
                Directory.CreateDirectory(backupDir);
                string backup = backupDir + "/" + Path.GetFileName(copy);
                if (File.Exists(backup))
                {
                    backup = backupDir + "/" + Path.GetFileNameWithoutExtension(copy)
                             + System.DateTime.Now.ToString("-HHmmss") + Path.GetExtension(copy);
                }

                File.Move(copy, backup);
                if (File.Exists(copy + ".meta"))
                {
                    File.Move(copy + ".meta", backup + ".meta");
                }

                log.AppendLine("  删重复副本 " + copy + "（正本在位：" + canonical + "，备份 → " + backup + "）");
            }
        }

        private static void MoveOne(string from, string to, StringBuilder log)
        {
            if (File.Exists(to) && !File.Exists(from))
            {
                log.AppendLine("  已在位 " + to);
                return;
            }

            if (!File.Exists(from))
            {
                log.AppendLine("  ⚠ 源不存在，跳过 " + from);
                return;
            }

            string err = AssetDatabase.MoveAsset(from, to);
            log.AppendLine(string.IsNullOrEmpty(err)
                ? "  移动 " + from + " → " + to
                : "  ✘ 移动失败 " + from + "：" + err);
        }

        // ── 导入设置 ─────────────────────────────────────────────────

        private static void ConfigureImporters(StringBuilder log)
        {
            int sprite = 0;
            foreach (string path in CollectPngs(UiDir, true))
            {
                Apply(path, TextureImporterType.Sprite, 2048);
                sprite++;
            }

            foreach (string path in CollectPngs(BgDir, true))
            {
                Apply(path, TextureImporterType.Sprite, 4096);
                sprite++;
            }

            foreach (string path in CollectPngs(CharsDir, true))
            {
                Apply(path, TextureImporterType.Sprite, 2048);
                sprite++;
            }

            // 七元素符号（2026-09-26）：成品 UI 图，按 Sprite 导入。
            foreach (string path in CollectPngs(IconsDir, true))
            {
                Apply(path, TextureImporterType.Sprite, 512);
                sprite++;
            }

            // 七元素符号的**运行时那份**（Resources/Elements）—— 由切图脚本直接写进去，
            // 与上面 Art/Icons/Elements 下那份**不是同一批文件**（后者只剩 _Source/Type.png）。
            // 导入参数必须与 Art 侧对齐，否则同一张图在两处看起来不一样。
            foreach (string path in CollectPngs(ElementResourceDir, true))
            {
                Apply(path, TextureImporterType.Sprite, 512);
                sprite++;
            }

            int plain = 0;
            // ⚠ 这三个调用必须传 excludeSourceSubdir = false —— 它们扫的**就是** `_Source/` 本身，
            //   传 true 会把自己全部过滤掉，归档贴图就不会被降级成普通贴图。
            foreach (string path in CollectPngs(CharsDir + "/_Source", false))
            {
                Apply(path, TextureImporterType.Default, 2048);
                plain++;
            }

            foreach (string path in CollectPngs(UiDir + "/_Source", false))
            {
                Apply(path, TextureImporterType.Default, 2048);
                plain++;
            }

            // ⚠ 必须传 false —— 与上面 UiDir/_Source 同理，它扫的就是 _Source 本身。
            foreach (string path in CollectPngs(IconsDir + "/_Source", false))
            {
                Apply(path, TextureImporterType.Default, 2048);
                plain++;
            }

            foreach (string path in CollectPngs(RefDir, false))
            {
                Apply(path, TextureImporterType.Default, 4096);
                plain++;
            }

            log.AppendLine("  Sprite 导入 " + sprite + " 张，普通贴图 " + plain + " 张");
        }

        private static void Apply(string path, TextureImporterType type, int maxSize)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                return;
            }

            bool isSprite = type == TextureImporterType.Sprite;
            bool changed =
                ti.textureType != type
                || (isSprite && ti.spriteImportMode != SpriteImportMode.Single)
                || ti.mipmapEnabled
                || ti.maxTextureSize != maxSize
                || ti.textureCompression != TextureImporterCompression.Uncompressed
                || (isSprite && !ti.alphaIsTransparency);

            ti.textureType = type;
            ti.spriteImportMode = isSprite ? SpriteImportMode.Single : ti.spriteImportMode;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = isSprite;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            // 界面图不压缩：这批图里大量细节靠 alpha 边缘，压完会有明显块噪，而总量只有几 MB
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = maxSize;

            if (changed)
            {
                ti.SaveAndReimport();
            }
        }

        private static List<string> CollectPngs(string dir, bool excludeSourceSubdir)
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(dir))
            {
                return list;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (excludeSourceSubdir && p.Contains("/_Source/"))
                {
                    continue;
                }

                list.Add(p);
            }

            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        // ── 映射表 ───────────────────────────────────────────────────

        private static void BuildLibrary(StringBuilder log)
        {
            BattleArtLibrary lib = AssetDatabase.LoadAssetAtPath<BattleArtLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<BattleArtLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            var buffs = new List<Sprite>();
            for (int i = 1; i <= 9; i++)
            {
                Sprite s = LoadSprite(UiDir + "/Buff_" + i.ToString("00") + "_" + BuffSuffix(i) + ".png");
                if (s == null)
                {
                    break;
                }

                buffs.Add(s);
            }

            var slotPlayer = new List<Sprite>();
            var slotEnemy = new List<Sprite>();
            for (int cd = 4; cd >= 1; cd--)
            {
                slotPlayer.Add(LoadSprite(UiDir + "/SlotL_CD" + cd + ".png"));
                slotEnemy.Add(LoadSprite(UiDir + "/SlotR_CD" + cd + ".png"));
            }

            Dictionary<string, int[]> manifest = ReadManifest(log);

            lib.SetAll(
                LoadSprite(UiDir + "/Hud_Bar.png"),
                LoadSprite(UiDir + "/Hud_Avatar.png"),
                buffs.ToArray(),
                slotPlayer.ToArray(),
                slotEnemy.ToArray(),
                LoadSprite(BgDir + "/Bg_Dungeon.png"),
                LoadSprite(UiDir + "/Orb_Energy.png"),
                BuildCharacter("Hero", manifest, log),
                BuildCharacter("Monster", manifest, log));

            // M25 · 查看对方手牌弹窗（2026-09-21）
            lib.SetPeek(
                LoadSprite(UiDir + "/Peek_Panel.png"),
                LoadSprite(UiDir + "/Peek_PanelSmall.png"),
                LoadSprite(UiDir + "/Peek_Banner.png"),
                LoadSprite(UiDir + "/Peek_Frame.png"),
                LoadSprite(UiDir + "/Peek_CardBack.png"));

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            log.AppendLine("  映射表 → " + LibraryPath
                           + "（buff " + buffs.Count + " · 槽 " + slotPlayer.Count + "×2）");
        }

        private static string BuffSuffix(int index)
        {
            string[] names = { "Fire", "RingEmpty", "Potion", "RuneBoost", "BladeStack" };
            return index >= 1 && index <= names.Length ? names[index - 1] : "X";
        }

        /// <summary>
        /// 角色动作目录 → pose 名。顺序必须与下面 <c>clips[i]</c> 的赋值一一对应。
        ///
        /// <para>`behit` / `death` 是 2026-09-18 第二批主角素材新增的动作；
        /// 怪物目前没有这两套（目录不存在 → 空 clip，UI 侧会安静退回待机）。</para>
        /// </summary>
        private static readonly string[] Poses = { "idle", "attack", "defend", "behit", "death" };

        /// <summary>动作 → 帧率。数值的**唯一来源是 <see cref="UiLayout"/>**，这里只做映射。</summary>
        private static float FpsFor(string pose)
        {
            switch (pose)
            {
                case "attack":
                    return UiLayout.CharAttackFps;
                case "defend":
                    return UiLayout.CharDefendFps;
                case "behit":
                    return UiLayout.CharBeHitFps;
                case "death":
                    return UiLayout.CharDeathFps;
                default:
                    return UiLayout.CharIdleFps;
            }
        }

        private static BattleArtLibrary.CharacterSet BuildCharacter(
            string who, Dictionary<string, int[]> manifest, StringBuilder log)
        {
            var set = new BattleArtLibrary.CharacterSet();
            set.Name = who;

            var clips = new BattleArtLibrary.Clip[Poses.Length];

            for (int i = 0; i < Poses.Length; i++)
            {
                var clip = new BattleArtLibrary.Clip();
                clips[i] = clip;

                string dir = CharsDir + "/" + who + "/" + Poses[i];
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    // 没有这套动作（怪物还没有受击 / 倒地）→ 留空 clip。
                    // ⚠ 先判目录，别直接进循环逐帧 LoadSprite：那会为每次缺失刷一条
                    //   「缺图」警告，日志里看着像出了问题。
                    continue;
                }

                var frames = new List<Sprite>();
                for (int n = 1; n <= 24; n++)
                {
                    Sprite s = LoadSprite(dir + "/" + who + "_" + Poses[i] + "_" + n.ToString("00") + ".png");
                    if (s == null)
                    {
                        break;
                    }

                    frames.Add(s);
                }

                clip.Frames = frames.ToArray();
                clip.Fps = FpsFor(Poses[i]);

                int[] meta;
                if (manifest.TryGetValue(who + "/" + Poses[i], out meta))
                {
                    clip.Size = new Vector2(meta[0], meta[1]);
                    // 清单里锚点是「图像坐标（左上原点）」，Unity 的 pivot 是左下原点
                    clip.Pivot = new Vector2(
                        meta[2] / (float)meta[0],
                        (meta[1] - meta[3]) / (float)meta[1]);
                }
                else if (frames.Count > 0)
                {
                    clip.Size = new Vector2(frames[0].rect.width, frames[0].rect.height);
                    clip.Pivot = new Vector2(0.5f, 0f);
                    log.AppendLine("  ⚠ 清单缺 " + who + "/" + Poses[i] + "，锚点退回底部居中");
                }
            }

            set.Idle = clips[0];
            set.Attack = clips[1];
            set.Defend = clips[2];
            set.BeHit = clips[3];
            set.Death = clips[4];
            set.Portrait = LoadSprite(CharsDir + "/" + who + "/" + who + "_portrait.png");

            var parts = new List<string>();
            for (int i = 0; i < Poses.Length; i++)
            {
                if (clips[i].FrameCount > 0)
                {
                    parts.Add(Poses[i] + " " + clips[i].FrameCount + " 帧");
                }
            }

            log.AppendLine("  " + who + "：" + string.Join(" · ", parts.ToArray()));
            return set;
        }

        /// <summary>读 `anim_frames.txt`：`角色/动作 画布宽 画布高 锚点X 锚点Y 帧数`（# 开头为注释）。</summary>
        private static Dictionary<string, int[]> ReadManifest(StringBuilder log)
        {
            var map = new Dictionary<string, int[]>();
            if (!File.Exists(ManifestPath))
            {
                log.AppendLine("  ⚠ 找不到帧清单 " + ManifestPath);
                return map;
            }

            foreach (string raw in File.ReadAllLines(ManifestPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6)
                {
                    continue;
                }

                var nums = new int[5];
                bool ok = true;
                for (int i = 0; i < 5; i++)
                {
                    if (!int.TryParse(parts[1 + i], out nums[i]))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    map[parts[0]] = nums;
                }
            }

            return map;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null)
            {
                Debug.LogWarning("[ArtImport] 缺图：" + path);
            }

            return s;
        }
    }
}
