using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// 把 <see cref="TriggerIconLibrary"/> 那三张触发图标（剑 / 盾 / 感叹号）合成一张
    /// <see cref="TMP_SpriteAsset"/>，让 α / β / γ 能在 TMP 里做**图文混排**。
    ///
    /// <para><b>为什么不能直接用三张 Sprite</b>：TMP 的 sprite 资产只有一份材质、一张
    /// <c>spriteSheet</c>，所有 inline sprite 都从这一张贴图里按 <c>glyphRect</c> 取区域。
    /// 三张独立 PNG 拼不出图文混排，必须先合成图集。</para>
    ///
    /// <para>图集由 `Tools/art-audit/build_tmp_icon_sheet.py` 生成（取三者的并集包围盒当统一
    /// 取景框，保住设计稿里的相对大小）。本类只负责把它登记成 TMP 资产 —— 幂等，可重跑。</para>
    ///
    /// <para>菜单：`魔法乱斗/整理 · 建触发符号 SpriteAsset`
    /// （MCP 触发请反射调 <see cref="RunBuild"/>：菜单项里没有弹窗，但保持同一个入口习惯）。</para>
    /// </summary>
    public static class TriggerSpriteAssetBuilder
    {
        /// <summary>图集（脚本产出）。</summary>
        public const string SheetPath = "Assets/Art/Icons/IconSheet_触发符号_TMP.png";

        private const string ResourcesDir = "Assets/Resources";

        /// <summary>产出资产路径（= <see cref="TriggerSpriteLibrary.ResourcePath"/>）。</summary>
        public const string AssetPath = ResourcesDir + "/" + TriggerSpriteLibrary.ResourcePath + ".asset";

        // ══════════════════════════════════════════════════════════════
        //  ⚠ 下面四个数必须与 Tools/art-audit/build_tmp_icon_sheet.py 完全一致。
        //    改脚本后重跑它，脚本会把这几行直接打印出来，照抄过来即可。
        //    不一致的后果是**安静错位**：图标被从相邻格里取样（半个盾牌/半个感叹号），
        //    TMP 不会报任何错。
        // ══════════════════════════════════════════════════════════════

        /// <summary>单元格宽（= 并集内容宽 + 两侧留白）。</summary>
        private const int CellWidth = 189;

        /// <summary>单元格高（= 并集内容高 + 上下留白）。</summary>
        private const int CellHeight = 240;

        /// <summary>单元格内的留白（四边等宽）。</summary>
        private const int Pad = 4;

        /// <summary>
        /// 希望图标**内容**渲染出来的高度 = 字号 × 本值。
        ///
        /// <para>1.12 是从成品卡面上量出来的，不是拍的：`Card_08_h_沉重打击.png`（760×1056）里
        /// 第一行那把剑的墨迹高 42 px、同行的汉字墨迹高 33 px，汉字墨迹约等于 0.88 em
        /// → em ≈ 37.5 px → 剑 ≈ <b>1.12 em</b>。图标比汉字**高一头**是卡面设计本身的口径，
        /// 不要按「和汉字一样高」去调。</para>
        /// </summary>
        private const float IconEm = 1.12f;

        /// <summary>
        /// 图标内容**顶边**相对基线的位置（em）。
        ///
        /// <para>同样量自卡面：剑的墨迹顶在基线上方 1.04 em、底在下方 0.08 em，
        /// 与同行汉字墨迹（0 ~ 0.88 em）**几乎同心**（0.48 em vs 0.44 em）。
        /// 拿「图标和汉字各占一半」去对齐会明显偏高。</para>
        /// </summary>
        private const float IconTopEm = 1.04f;

        /// <summary>图集里格子的顺序 —— 必须与脚本的 SOURCES 一致。</summary>
        private static readonly EffectTrigger[] Order =
        {
            EffectTrigger.Attack,
            EffectTrigger.Defend,
            EffectTrigger.Special,
        };

        /// <summary>写入 TMP 的 sprite 名 —— 必须与 <see cref="TriggerSpriteLibrary"/> 的常量一致。</summary>
        private static readonly string[] SpriteNames =
        {
            TriggerSpriteLibrary.SpriteAttack,
            TriggerSpriteLibrary.SpriteDefend,
            TriggerSpriteLibrary.SpriteSpecial,
        };

        [MenuItem("魔法乱斗/整理 · 建触发符号 SpriteAsset", priority = 30)]
        public static void BuildFromMenu()
        {
            Debug.Log("[触发符号] " + RunBuild());
        }

        /// <summary>
        /// 重建 SpriteAsset（幂等）。返回一行摘要，供菜单/MCP 日志使用。
        /// 没有 DisplayDialog —— MCP 脚本化触发弹窗会把主线程挂死。
        /// </summary>
        public static string RunBuild()
        {
            ApplySheetImporterSettings();

            Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
            if (sheet == null)
            {
                return "⚠ 找不到图集：" + SheetPath
                       + "\n    先跑：python Tools/art-audit/build_tmp_icon_sheet.py --apply";
            }

            int cells = sheet.width / CellWidth;
            if (cells < Order.Length || sheet.height < CellHeight)
            {
                return "⚠ 图集尺寸对不上：" + sheet.width + "×" + sheet.height
                       + "，需要至少 " + (CellWidth * Order.Length) + "×" + CellHeight
                       + "\n    多半是脚本生成后没有重新 Refresh，或 CellWidth/CellHeight 被改过。";
            }

            EnsureFolder(ResourcesDir);

            TMP_SpriteAsset asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AssetPath);
            bool created = false;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
                created = true;
            }

            asset.name = TriggerSpriteLibrary.ResourcePath;
            asset.spriteSheet = sheet;

            // 下面四个成员在 TMP 里是 `internal set` —— 跨程序集赋不了值（编译期就报 CS0200），
            // 只能落到它们的序列化字段上。写成一个小工具而不是 SerializedObject 逐元素搬，
            // 是因为 faceInfo 是结构体、两张表是 List<T>，逐元素搬要几十行且极易漏字段。
            SetField(asset, "m_Version", "1.1.0");
            SetField(asset, "m_FaceInfo", BuildFaceInfo());
            SetField(asset, "m_SpriteGlyphTable", BuildGlyphs());

            // ⚠ 必须在 glyphTable 之后：character 靠 glyphIndex 去表里取字形
            SetField(asset, "m_SpriteCharacterTable", BuildCharacters(asset));

            asset.material = EnsureMaterial(asset, sheet);
            asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);

            // 名字表 / unicode 表 / glyph 索引表都在这里建；不调的话
            // <sprite name="..."> 查不到东西（而且是静默查不到，只剩空白）
            SetField(asset, "m_IsSpriteAssetLookupTablesDirty", true);
            asset.UpdateLookupTables();
            SetField(asset, "m_IsSpriteAssetLookupTablesDirty", false);

            EditorUtility.SetDirty(asset);
            if (asset.material != null)
            {
                EditorUtility.SetDirty(asset.material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath);

            return (created ? "✔ 新建 " : "✔ 更新 ") + AssetPath
                   + "\n  · 图集 " + sheet.width + "×" + sheet.height
                   + "，格 " + CellWidth + "×" + CellHeight + "，共 " + Order.Length + " 张"
                   + "\n  · pointSize " + FacePointSize.ToString("0.##")
                   + "（图标内容 = " + IconEm.ToString("0.00") + " em）"
                   + "\n  · sprite 名 " + string.Join(" / ", SpriteNames);
        }

        /// <summary>
        /// 往 TMP 的**私有序列化字段**上写值。
        /// 为什么不用公开属性：`version` / `faceInfo` / `spriteGlyphTable` / `spriteCharacterTable`
        /// 在 TMP 里是 <c>internal set</c>，跨程序集不可写（`PropertyInfo.CanWrite` 是 true，
        /// 但那个 setter 不可见 —— 很容易被误判成「可以赋值」）。
        /// </summary>
        private static void SetField(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (f == null)
            {
                Debug.LogWarning("[触发符号] TMP 改版了？找不到字段 " + field + "，本次跳过");
                return;
            }

            f.SetValue(target, value);
        }

        // ── 资产拼装 ────────────────────────────────────────────

        /// <summary>
        /// faceInfo.pointSize 不是「随便填一个」：TMP 渲染 sprite 时
        /// <c>spriteScale = 字号 / pointSize</c>，所以这里由「图标内容要多高」反解出来 ——
        /// 内容高（px）× scale = 字号 × <see cref="IconEm"/>。
        /// </summary>
        private static float FacePointSize
        {
            get { return (CellHeight - Pad * 2) / IconEm; }
        }

        private static FaceInfo BuildFaceInfo()
        {
            float ps = FacePointSize;
            FaceInfo fi = default(FaceInfo);
            fi.familyName = "MagicBrawl Trigger Icons";
            fi.styleName = "Regular";
            fi.pointSize = Mathf.RoundToInt(ps);
            fi.scale = 1f;
            fi.lineHeight = ps;
            fi.ascentLine = ps * IconTopEm;
            fi.capLine = ps * IconTopEm;
            fi.meanLine = ps * 0.44f;
            fi.baseline = 0f;
            fi.descentLine = -ps * 0.12f;
            fi.tabWidth = ps;

            // FaceInfo 是结构体，m_FaceIndex / m_UnitsPerEM 没有公开属性 →
            // 装箱改字段再拆箱。不改也能用，但 unitsPerEM=0 会让某些 TMP 版本
            // 在算 sprite 缩放时除零，留下很难查的坑。
            object boxed = fi;
            SetField(boxed, "m_FaceIndex", 0);
            SetField(boxed, "m_UnitsPerEM", Mathf.RoundToInt(ps));
            return (FaceInfo)boxed;
        }

        private static List<TMP_SpriteGlyph> BuildGlyphs()
        {
            float ps = FacePointSize;

            // bearingY 让**内容**顶边落在 baseline + IconTopEm：
            // 格子上边距是 Pad，所以内容顶边比格子顶边低 Pad 像素。
            float bearingY = Pad + IconTopEm * ps;

            var glyphs = new List<TMP_SpriteGlyph>();
            for (int i = 0; i < Order.Length; i++)
            {
                // 取整格的区域，不是紧贴内容 —— 三个符号的相对大小就靠这一点保住
                var metrics = new GlyphMetrics(CellWidth, CellHeight, 0f, bearingY, CellWidth);

                // ⚠ TMP 的贴图坐标原点在**左下**：本图集只有一行且格子占满高度 → y = 0
                var rect = new GlyphRect(i * CellWidth, 0, CellWidth, CellHeight);

                glyphs.Add(new TMP_SpriteGlyph((uint)i, metrics, rect, 1f, 0));
            }

            return glyphs;
        }

        private static List<TMP_SpriteCharacter> BuildCharacters(TMP_SpriteAsset owner)
        {
            var chars = new List<TMP_SpriteCharacter>();
            for (int i = 0; i < Order.Length; i++)
            {
                // unicode 直接从 Core 的符号常量推出来 —— 这样「符号字符」和
                // 「能触发替换的码位」永远是同一个，不需要两处手抄码表
                string symbol = TriggerSymbol.Of(Order[i]);
                uint unicode = symbol.Length > 0 ? (uint)symbol[0] : (uint)('a' + i);

                // 走三参构造：它会把 glyph / glyphIndex / spriteAsset 一起接好
                var ch = new TMP_SpriteCharacter(unicode, owner, owner.spriteGlyphTable[i]);
                ch.name = SpriteNames[i];
                ch.scale = 1f;
                chars.Add(ch);
            }

            return chars;
        }

        /// <summary>材质挂在资产内部当子资产（不进 Resources 根目录，避免多一个散落文件）。</summary>
        private static Material EnsureMaterial(TMP_SpriteAsset owner, Texture2D sheet)
        {
            Shader shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null)
            {
                Debug.LogError("[触发符号] 找不到 TextMeshPro/Sprite 着色器，TMP 基础资源没导入？");
                return null;
            }

            Material mat = null;
            Object[] subs = AssetDatabase.LoadAllAssetsAtPath(AssetPath);
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i] is Material)
                {
                    mat = (Material)subs[i];
                    break;
                }
            }

            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = TriggerSpriteLibrary.ResourcePath + " Material";
                AssetDatabase.AddObjectToAsset(mat, owner);
            }

            mat.shader = shader;
            mat.SetTexture("_MainTex", sheet);
            return mat;
        }

        // ── 导入设置 ────────────────────────────────────────────

        /// <summary>
        /// 图集不吃压缩：它最终会以 16–40 px 显示，DXT 的 4×4 块会把图标边缘糊成色斑。
        /// 768 级的 RGBA 未压缩贴图只有几百 KB，不值得为这点体积牺牲可读性。
        /// </summary>
        private static void ApplySheetImporterSettings()
        {
            var ti = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
            if (ti == null)
            {
                return;
            }

            bool dirty = false;

            if (ti.textureType != TextureImporterType.Default)
            {
                ti.textureType = TextureImporterType.Default;
                dirty = true;
            }

            if (ti.mipmapEnabled)
            {
                ti.mipmapEnabled = false;
                dirty = true;
            }

            if (!ti.alphaIsTransparency)
            {
                ti.alphaIsTransparency = true;
                dirty = true;
            }

            if (ti.wrapMode != TextureWrapMode.Clamp)
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                dirty = true;
            }

            if (ti.filterMode != FilterMode.Bilinear)
            {
                ti.filterMode = FilterMode.Bilinear;
                dirty = true;
            }

            if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (ti.npotScale != TextureImporterNPOTScale.None)
            {
                ti.npotScale = TextureImporterNPOTScale.None;
                dirty = true;
            }

            if (ti.maxTextureSize < 1024)
            {
                ti.maxTextureSize = 1024;
                dirty = true;
            }

            if (dirty)
            {
                ti.SaveAndReimport();
            }
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'),
                    System.IO.Path.GetFileName(path));
            }
        }
    }
}
