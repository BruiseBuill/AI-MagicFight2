using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>编辑器「内置字体图集索引失效」自修复</b>（2026-10-05 · 该坑的第二次复发）。
    ///
    /// <para><b>历史</b>：同一个坑 2026-09-30 排查过一次（
    /// <c>Docs/validation/2026-09-30-编辑器内置字体图集索引失效排查.md</c>）。
    /// 当时手点一次 <c>ClearFontAssetData(true)</c> 就完了 —— 而它会<b>复发</b>
    /// （2026-10-05 又刷了一遍，症状逐字相同），所以这次做成自动钩子。</para>
    ///
    /// <para><b>症状</b>：一进 Play / 一点编辑器画面就刷
    /// <c>MissingReferenceException: The variable m_AtlasTextures of FontAsset doesn't exist anymore</c>，
    /// 堆栈<b>全部</b>在 <c>UnityEngine.UIElements</c> 里
    /// （<c>MaterialManager.GetFallbackMaterial → UITKTextHandle → GUIUtility.ProcessEvent</c>）。</para>
    ///
    /// <para><b>根因</b>：编辑器为「粗体 CJK 回退」准备的那份 OS 字体资产
    /// —— <c>Yu Gothic UI Bold SDF</c>（<c>assetPath = Library/unity editor resources</c>、
    /// <c>hideFlags = HideAndDontSave</c>、<c>m_AtlasPopulationMode = DynamicOS</c>、
    /// 多图集，<b>不在 Assets 下</b>）—— 在多图集扩容路径上留下了悬空索引，
    /// 之后编辑器界面里任何中文 / 日文量尺寸时走到那个槽位取名字 → 抛异常。</para>
    ///
    /// <para><b>⚠ 别按「工程字体坏了」那条路去查</b>：<c>Assets/Art/Fonts</c> 下 20 张
    /// <c>TMP_FontAsset</c> 的图集引用逐一查过全是好的；而且 <c>t:TMP_FontAsset</c>
    /// <b>搜不到</b>编辑器内置字体（它们走 <c>UnityEngine.TextCore.Text.FontAsset</c> 基类），
    /// 必须用 <c>Resources.FindObjectsOfTypeAll&lt;FontAsset&gt;()</c> 才看得见。</para>
    ///
    /// <para><b>修法：只用「有效图集去填满空槽」，保持数组长度不变。</b> 实测有效的路径是
    /// 「数组扩容时 Unity 自动复制末项」—— 所以这里显式赋值后要 <c>Update()</c> 回读校验。</para>
    ///
    /// <para><b>⚠⚠ 三条弯路（都实测过，别走）</b>：</para>
    /// <list type="number">
    /// <item><b>把空槽裁掉（<c>arraySize--</c>）</b>：TextCore 内部仍按原来的槽数索引
    /// （<c>MaterialManager.GetFallbackMaterial(..., atlasIndex)</c>），裁完报错从
    /// <c>MissingReferenceException</c> 变成 <c>IndexOutOfRangeException</c> —— 只换了个名字。</item>
    /// <item><b><c>ClearFontAssetData(true)</c></b>：2026-09-30 那次它有效（坏槽位 → 重建成功），
    /// 但 2026-10-05 复发时它<b>抛异常并半途停下</b>，把状态从「一好一坏」变成「唯一的槽也空了」
    /// —— <b>越修越坏</b>。所以本脚本不再用它。</item>
    /// <item><b>造一张新 <c>Texture2D</c> 填进去</b>：对 <c>HideAndDontSave</c> 的运行时对象
    /// 显式赋值<b>写不进去</b>（<c>ApplyModifiedProperties</c> 后回读仍是 null）。</item>
    /// </list>
    ///
    /// <para><b>2026-10-05 · 第三次复发补齐的第二种状态</b>：上面三条说的是「一好一坏」。
    /// 这次遇到的是<b>整条数组只有一个槽、而且它是 null</b>
    /// （<c>Yu Gothic UI Bold SDF</c>），报错也换了名字：
    /// <c>UnassignedReferenceException: The variable m_AtlasTextures of FontAsset has not been assigned</c>。
    /// 这种状态<b>修法一（拿有效槽填空槽）什么都不做</b>（没有有效槽可用），
    /// 所以补了<b>修法二：认领游离图集</b> —— 详见 <see cref="AdoptOrphanAtlases"/>。</para>
    ///
    /// <para><b>全空怎么办</b>（连游离图集也找不到）：脚本<b>不动它</b>，只打一条警告
    /// —— 实测这种状态<b>不报错</b>（TextCore 在 DynamicOS 模式下会按需自建图集），
    /// 真出问题就重启 Unity 编辑器（那份字体是会话内按需生成的，重启即重建）。
    /// 强行造假图集只会得到「画不出来的字」，比报错更难查。</para>
    ///
    /// <para><b>时机</b>：编辑器加载后延迟一次 + <b>每次进 Play 之前</b>一次
    /// + <b>每次打开场景之后</b>一次（2026-10-05 用户报「点一下场景又刷出来」，
    /// 所以把场景打开也接上）。</para>
    /// </summary>
    [InitializeOnLoad]
    public static class EditorFontAtlasRepair
    {
        static EditorFontAtlasRepair()
        {
            // ⚠ 用 delayCall 而不是在静态构造里直接扫：静态构造跑在资源数据库加载中途，
            //   这时 FindObjectsOfTypeAll 拿到的可能还不全，而且改对象会打断加载。
            EditorApplication.delayCall += delegate { Repair(false); };
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // 用户 2026-10-05 报的路径就是「点一下场景」——打开场景会让 UIElements 重排，
            // 那一刻中日文粗体量尺寸就会走到坏槽位。所以打开场景后再修一次。
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            EditorApplication.delayCall += delegate { Repair(false); };
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                Repair(false);
            }
        }

        [MenuItem("魔法乱斗/工具 · 修复编辑器字体图集", false, 200)]
        private static void MenuRepair()
        {
            Repair(true);
        }

        /// <summary>
        /// 扫一遍所有 <c>FontAsset</c>（含编辑器内置的），把空 / 悬空的图集槽<b>填满</b>。
        /// </summary>
        /// <param name="verbose">真 = 没坏也打一行日志（菜单手点用）；假 = 只有真的修了才出声（自动钩子用）。</param>
        /// <returns>修了几个。</returns>
        public static int Repair(bool verbose)
        {
            var log = verbose ? new StringBuilder() : null;
            int repaired = 0;

            // 整条数组都空的那些（修法一救不了，交给修法二）。
            var allEmpty = new List<UnityEngine.TextCore.Text.FontAsset>();

            UnityEngine.TextCore.Text.FontAsset[] all =
                Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>();

            for (int i = 0; i < all.Length; i++)
            {
                UnityEngine.TextCore.Text.FontAsset fa = all[i];
                if (fa == null || fa.name == null)
                {
                    continue;
                }

                SerializedObject data;
                SerializedProperty slots;
                try
                {
                    data = new SerializedObject(fa);
                    slots = data.FindProperty("m_AtlasTextures");
                }
                catch
                {
                    continue;
                }

                if (slots == null || slots.arraySize == 0)
                {
                    continue;
                }

                int empty = 0;
                UnityEngine.Object firstAlive = null;
                for (int k = 0; k < slots.arraySize; k++)
                {
                    UnityEngine.Object slot = slots.GetArrayElementAtIndex(k).objectReferenceValue;
                    if (IsDead(slot))
                    {
                        empty++;
                    }
                    else if (firstAlive == null)
                    {
                        firstAlive = slot;
                    }
                }

                if (empty == 0)
                {
                    continue;
                }

                // 全空 → 修法一没有可用的有效槽，交给修法二（认领游离图集）。
                if (firstAlive == null)
                {
                    allEmpty.Add(fa);
                    if (log != null)
                    {
                        log.AppendLine("  ⚠ " + fa.name + "：所有图集槽都空了（" + slots.arraySize
                                       + " 个）—— 交给「认领游离图集」。");
                    }

                    continue;
                }

                // 用第一张有效图集把空槽填满（长度不变）。
                for (int k = 0; k < slots.arraySize; k++)
                {
                    if (IsDead(slots.GetArrayElementAtIndex(k).objectReferenceValue))
                    {
                        slots.GetArrayElementAtIndex(k).objectReferenceValue = firstAlive;
                    }
                }

                data.ApplyModifiedPropertiesWithoutUndo();
                data.Update();

                int left = 0;
                for (int k = 0; k < slots.arraySize; k++)
                {
                    if (IsDead(slots.GetArrayElementAtIndex(k).objectReferenceValue))
                    {
                        left++;
                    }
                }

                if (left == 0)
                {
                    repaired++;
                }

                if (log != null)
                {
                    log.AppendLine("  " + (left == 0 ? "✔" : "✘") + " " + fa.name + "：填 "
                                   + empty + " 个空槽（原 " + slots.arraySize + " 个）→ 仍坏 " + left + " 个");
                }
            }

            // 修法二
            if (allEmpty.Count > 0)
            {
                repaired += AdoptOrphanAtlases(allEmpty, log);
            }

            if (repaired > 0)
            {
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                Debug.Log("[EditorFontAtlasRepair] 补好了 " + repaired + " 个字体资产的图集槽。"
                          + (log == null ? string.Empty : "\n" + log));
            }
            else if (verbose)
            {
                Debug.Log("[EditorFontAtlasRepair] 没有可自动修复的图集槽（全空的那类只能重启编辑器）。");
            }

            return repaired;
        }

        // ══════════════════════════════════════════════════════
        //  修法二：认领游离图集（2026-10-05）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>给「整条数组全空」的字体资产认领一张没有主人的图集纹理</b>。
        ///
        /// <para><b>为什么需要它</b>：修法一（拿有效槽填空槽）的前提是「至少有一个槽是好的」。
        /// 2026-10-05 实测到另一种状态：<c>Yu Gothic UI Bold SDF</c> 只有 1 个槽、而且是 null
        /// —— 修法一在这种情况下什么都不做，而 UIElements 一量中日文粗体就抛
        /// <c>UnassignedReferenceException: The variable m_AtlasTextures of FontAsset has not been assigned</c>
        /// （栈全在 <c>UIElements</c> 里）。</para>
        ///
        /// <para><b>关键事实</b>：那张图集纹理 <b>并没有消失</b> —— 它还在内存里
        /// （<c>YUGOTHB Atlas</c>，1024×1024，<c>HideAndDontSave</c>），只是字体资产把引用丢了。
        /// 所以「找一个没有任何字体资产引用的 <c>* Atlas</c> 纹理，认领给那个空槽」就能修好，
        /// 而且没有「造假图集」的风险（不是造图，是把丢掉的引用接回去）。</para>
        ///
        /// <para><b>⚠ 必须用反射写托管字段，不能走 <c>SerializedObject</c></b>：这些字体资产是
        /// <c>HideAndDontSave</c> 的运行时对象，<c>ApplyModifiedPropertiesWithoutUndo()</c>
        /// 之后回读仍然是 null（2026-10-05 实测）。抛异常的那段代码
        /// （<c>FontAsset.TryAddCharacterInternal</c>）读的正是这个托管字段，所以反射写进去就生效。</para>
        ///
        /// <para><b>配对规则</b>：按「名字的相似度」贪心配对（大小写无关、忽略非字母数字，
        /// 取最长公共子序列长度）。实测场景是 1 对 1（唯一空槽 ↔ 唯一游离图集），
        /// 但写成贪心可以顺带处理「同时坏两个」的情况。</para>
        /// </summary>
        private static int AdoptOrphanAtlases(List<UnityEngine.TextCore.Text.FontAsset> broken,
            StringBuilder log)
        {
            if (broken == null || broken.Count == 0)
            {
                return 0;
            }

            // ① 已经被任何字体资产引用的图集纹理（认领只认「没主」的）。
            var usedIds = new HashSet<int>();
            UnityEngine.TextCore.Text.FontAsset[] all =
                Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>();
            for (int i = 0; i < all.Length; i++)
            {
                Texture2D[] slots = ReadAtlasSlots(all[i]);
                if (slots == null)
                {
                    continue;
                }

                for (int k = 0; k < slots.Length; k++)
                {
                    if (slots[k] != null)
                    {
                        usedIds.Add(slots[k].GetInstanceID());
                    }
                }
            }

            // ② 候选：编辑器内置的图集纹理（名字以 " Atlas" 结尾、HideAndDontSave、没被引用）。
            var pool = new List<Texture2D>();
            Texture2D[] textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < textures.Length; i++)
            {
                Texture2D texture = textures[i];
                if (texture == null || texture.name == null)
                {
                    continue;
                }

                if (!texture.name.EndsWith(" Atlas", StringComparison.Ordinal))
                {
                    continue;
                }

                if ((texture.hideFlags & HideFlags.HideAndDontSave) == 0)
                {
                    continue;
                }

                if (usedIds.Contains(texture.GetInstanceID()))
                {
                    continue;
                }

                pool.Add(texture);
            }

            if (pool.Count == 0)
            {
                if (log != null)
                {
                    log.AppendLine("  ✘ " + broken.Count + " 个字体资产的图集槽全空，也没有游离图集可认领"
                                   + " —— 重启 Unity 编辑器（那份字体是会话内按需生成的，重启即重建）。");
                }

                return 0;
            }

            int adopted = 0;
            for (int i = 0; i < broken.Count && pool.Count > 0; i++)
            {
                int best = 0;
                int bestScore = -1;
                for (int k = 0; k < pool.Count; k++)
                {
                    int score = NameScore(broken[i].name, pool[k].name);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = k;
                    }
                }

                Texture2D pick = pool[best];
                pool.RemoveAt(best);
                if (AssignAtlas(broken[i], pick))
                {
                    adopted++;
                    if (log != null)
                    {
                        log.AppendLine("  ✔ " + broken[i].name + "：认领游离图集 " + pick.name
                                       + "（" + pick.width + "×" + pick.height + "，相似度 " + bestScore + "）");
                    }
                }
                else if (log != null)
                {
                    log.AppendLine("  ✘ " + broken[i].name + "：反射写入后回读仍是空 —— 放弃。");
                }
            }

            return adopted;
        }

        /// <summary>反射读一个字体资产的图集槽数组（读不到返回 null）。</summary>
        private static Texture2D[] ReadAtlasSlots(UnityEngine.TextCore.Text.FontAsset fa)
        {
            if (fa == null)
            {
                return null;
            }

            FieldInfo field = AtlasSlotsField;
            if (field == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(fa) as Texture2D[];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 反射把 <paramref name="texture"/> 写进那个字体资产的图集槽（长度保持不变，至少 1）。
        /// 写完立刻回读校验 —— 写不进去就返回假，让调用方知道没成。
        /// </summary>
        private static bool AssignAtlas(UnityEngine.TextCore.Text.FontAsset fa, Texture2D texture)
        {
            FieldInfo slotsField = AtlasSlotsField;
            if (slotsField == null || texture == null)
            {
                return false;
            }

            try
            {
                var current = slotsField.GetValue(fa) as Texture2D[];
                int length = current == null || current.Length == 0 ? 1 : current.Length;
                var next = new Texture2D[length];
                for (int i = 0; i < length; i++)
                {
                    next[i] = texture;
                }

                slotsField.SetValue(fa, next);

                FieldInfo single = SingleAtlasField;
                if (single != null)
                {
                    single.SetValue(fa, texture);
                }

                FieldInfo index = AtlasIndexField;
                if (index != null)
                {
                    index.SetValue(fa, 0);
                }

                var back = slotsField.GetValue(fa) as Texture2D[];
                return back != null && back.Length > 0 && back[0] == texture;
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo _atlasSlotsField;
        private static FieldInfo _singleAtlasField;
        private static FieldInfo _atlasIndexField;

        private static FieldInfo AtlasSlotsField
        {
            get
            {
                if (_atlasSlotsField == null)
                {
                    _atlasSlotsField = FindField("m_AtlasTextures");
                }

                return _atlasSlotsField;
            }
        }

        private static FieldInfo SingleAtlasField
        {
            get
            {
                if (_singleAtlasField == null)
                {
                    _singleAtlasField = FindField("m_AtlasTexture");
                }

                return _singleAtlasField;
            }
        }

        private static FieldInfo AtlasIndexField
        {
            get
            {
                if (_atlasIndexField == null)
                {
                    _atlasIndexField = FindField("m_AtlasTextureIndex");
                }

                return _atlasIndexField;
            }
        }

        private static FieldInfo FindField(string name)
        {
            try
            {
                return typeof(UnityEngine.TextCore.Text.FontAsset).GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 两个名字像不像（越大越像）：大小写无关、只留字母数字，取<b>最长公共子序列</b>的长度。
        ///
        /// <para>为什么不用「公共前缀」：内置图集名是 OS 字体的内部名
        /// （<c>Yu Gothic UI Bold SDF</c> → <c>YUGOTHB</c>），公共前缀只有 <c>YUGO</c>，
        /// 而 <c>YUGOTHB</c> 整串都是 <c>YUGOTHICBOLDSDF</c> 的子序列 —— 子序列长度能把它挑出来。</para>
        /// </summary>
        private static int NameScore(string a, string b)
        {
            string x = Letters(a);
            string y = Letters(b);
            if (x.Length == 0 || y.Length == 0)
            {
                return 0;
            }

            var row = new int[y.Length + 1];
            for (int i = 1; i <= x.Length; i++)
            {
                int diagonal = 0;
                for (int k = 1; k <= y.Length; k++)
                {
                    int previous = row[k];
                    row[k] = x[i - 1] == y[k - 1]
                        ? diagonal + 1
                        : (row[k - 1] > row[k] ? row[k - 1] : row[k]);
                    diagonal = previous;
                }
            }

            return row[y.Length];
        }

        private static string Letters(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 这个槽位是不是「坏了」。<b>⚠ 「已销毁」在 C# 里不是 <c>null</c>「不等于」</b>：
        /// <c>objectReferenceValue == null</c> 对已销毁对象同样返回真（Unity 的假 null），
        /// 所以先用它过滤；再对活着的对象<b>故意读一次 <c>.name</c> 并 try/catch</b>
        /// —— 那正是用户看到的那句异常（历史文档「沉淀的判据」那条）。
        /// </summary>
        private static bool IsDead(UnityEngine.Object slot)
        {
            if (slot == null)
            {
                return true;
            }

            try
            {
                string unused = slot.name;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
