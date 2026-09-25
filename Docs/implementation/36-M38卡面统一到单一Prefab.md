# 36 · M38 卡面统一到「一份 Prefab」

> **性质**：现行实现记录 —— 与当前代码 / 资产一致，可照此改（2026-09-25 落地）。
> **最后核对**：2026-09-25

## 0. 这一版解决了什么

| # | 需求（用户 2026-09-25） | 落点 |
|---|---|---|
| 1 | 手牌 Prefab 上的改动（卡名加粗、名条加宽加深、力量/冷却数字加粗）要**同步到冷却区的牌与长按放大的牌** | 冷却区与放大卡面改为**共用 `CardView_Hand.prefab`**，不是抄数值 |
| 2 | **所有**出现的卡面显示都应当统一（含未提及的其他地方） | 「一份卡面 = 一份 Prefab」+ `CardView.SetFaceWidth()` 服务所有尺寸 |

## 1. 先看根因：一份卡面曾经有 **9 个副本 / 3 个来源**

改之前实测（编辑器探针）：

| 位置 | 来源 | 是否跟随手牌的手改 |
|---|---|---|
| 手牌 | `Assets/Prefabs/Ui/CardView_Hand.prefab` | —— （用户改的就是它） |
| 冷却迷你卡 | `Assets/Prefabs/Ui/CardView_Mini.prefab` | ❌ |
| 长按放大（`TopArea/DetailPanel/Face`） | **`BattleCanvas.prefab` 里内嵌的独立副本** | ❌ |
| `HandArea/HandRow` 下 6 个 `HandCard` | 内嵌副本（`UiKitPreview` 样本卡的化石） | ❌ 而且运行时才被 `HandView.PurgeStrayRowChildren` 删掉 |

**为什么会各存一份**：组成式卡面整棵树画在固定设计空间里，对外的缩放只有 `CardRoot` 一个节点，
而那个 `localScale` 是**构建期**按目标卡宽算出来的
（`UiKitBuilder.BuildCardSurface` → `UiLayout.CardSpaceScale(cardWidth)`）。
「一份卡宽 = 一份 Prefab」于是成了既定结构 —— 用户在**手牌**那份上手工调好的样式，
在结构上就没有任何路径能传到另外两份。

## 2. 解法：把「尺寸」从构建期挪到运行时

```csharp
// CardView.cs（新增）
public void SetFaceWidth(float cardWidth)   // 改 CardRoot.localScale
```

- 缩放口径不变（还是 `UiLayout.CardSpaceScale`），只是**谁在什么时候算**变了：
  从「构建这份 Prefab 时」变成「实例化它的人决定」。
- 根节点的 `sizeDelta`（占位尺寸）**不在这里改** —— 谁实例化谁决定：
  冷却区用 `SlotMiniCard*`、放大查看用 `DetailFace*`。两者分工明确：
  `sizeDelta` = 占位，`SetFaceWidth` = 卡面内部按这个宽度重排缩放。

### 2.1 改了什么

| 文件 | 改动 |
|---|---|
| `Ui/CardView.cs` | 新增 `FaceRoot`（惰性按名字找，不加序列化字段）+ `SetFaceWidth(float)` |
| `Ui/CooldownView.cs` | `Take()` 里对每一张取出来的牌下 `SetFaceWidth(UiLayout.SlotMiniCardWidth)`（池化复用的那些也一样要下） |
| `Ui/CardDetailView.cs` | `Show()` 第一次调用时下 `SetFaceWidth(UiLayout.DetailFaceWidth)`（用 `_faceSized` 兜住，避免每帧写 `localScale` 让 TMP 反复重排） |
| `Ui/UiLayout.cs` | `CardNameWidth 301.2 → 356.6`、`CardNameOffsetX 50.7 → 52.5`（**固化**用户的手改） |
| `Ui/UiTheme.cs` | `CardNameBarBackdrop` 的 α `0.62 → 0.76862746`（**固化**用户的手改） |
| `Editor/UiKitBuilder.cs` | 卡名 / 力量 / 冷却三个 TMP 写死 `FontStyles.Bold`（**固化**）；不再产出 `CardView_Mini.prefab`；删除 `BuildMiniCardPrefab` |

> **为什么要「固化进构建器」而不是只改资产**：这正是 M35 给 `CardNumberFontPath` 写下的理由 ——
> 用户是先在 Prefab 上手工改好的，而 TMP 的 `fontStyle` / 尺寸 / 颜色都是**构建器重跑时会整体覆盖**
> 的东西。不写进构建器，哪天跑一次 M7 就会静默变回原样（零报错）。
> 固化之后，「手工改的」与「重建出来的」永远是同一组数。

### 2.2 资产侧怎么改的（**没有重跑构建器**）

`BattleCanvas.prefab` / `CardView_Hand.prefab` 里有用户的手改，**重跑 M7 会把它冲掉**，
所以资产改动一律走 Unity 编辑器 API（`PrefabUtility.LoadPrefabContents` → 改 → `SaveAsPrefabAsset`）：

1. `CooldownView._cardPrefab` → `CardView_Hand.prefab`
2. `UiKitPreview._miniCardPrefab` → `CardView_Hand.prefab`
3. `TopArea/DetailPanel/Face`：删掉内嵌副本，换成 `CardView_Hand.prefab` 的**嵌套 Prefab 实例**
   （原矩形 396×550.23 / pos (0,0) 保留），`CardDetailView._card` 重指到它；
   顺手拆掉实例上的 `Button` / `CardInteractor` / `HandCardState`
   （放大卡面**不参与交互**，见 `CardDetailView.ConfigureFaceOnly` 的说明）
4. 删掉 `HandArea/HandRow` 下 6 个卡面化石（运行时本来就会被清，不该留在资产里）
5. 删除 `Assets/Prefabs/Ui/CardView_Mini.prefab`（已无引用）

**用嵌套实例而不是运行时 Instantiate** 的理由：M18 的口径是「静态 UI 一律场景预建」，
而放大卡面是静态位置上的东西；嵌套实例既满足这一条，又能**自动继承**基 Prefab 的后续改动。

备份：`E:/UnityProject/_CardFight2_Backups/m38-2026-09-25/`（三个 Prefab 的改动前副本）。

## 3. 验证

**资产探针**（编辑器）：

```
BattleCanvas 内卡面实例数 = 1
  BattleCanvas/TopArea/DetailPanel/Face  nameBold=1  band=356.6/52.5  bandAlpha=0.769  嵌套Prefab来源=CardView_Hand
CooldownView._cardPrefab = CardView_Hand
UiKitPreview._miniCardPrefab = CardView_Hand
HandRow 子节点数 = 0
```

→ 放大卡面确实**继承**了手牌那份的值（加粗 / 名条 356.6 / α 0.769），不再是副本。

**Play 截图**（`Captures/art-review/`）：

| 截图 | 看什么 |
|---|---|
| `m38_unified_cards_01_play.png` | 手牌 6 张：卡名加粗、名条加宽、力量/冷却数字加粗（组成式卡面正常） |
| `m38_unified_cards_02_mini.png` | 把同一批卡临时缩到**冷却迷你尺寸**（`SetFaceWidth(SlotMiniCardWidth)`）后的样子 —— 冷却区从此就是这个外观 |

⚠ **已知噪声**：改 Prefab 的过程里 TMP 抛了 18 条 `NullReferenceException`
（`TMP_SubMeshUI.UpdateMaterial` ← `OnValidate`）。这是**编辑器期**加载 / 保存含
`<sprite>` 图文混排的 TMP 时已知的 TMP 行为，资产数据完好 —— 随后的探针与 Play 截图
都能正常读到并渲染这些 TMP。

## 4. ⚠ 遗留：**还有一族卡面没统一**（下一步要先定口径）

盘出来的「会显示一张牌」的地方里，还有一族是**整图 / 插画**，不是组成式卡面：

| 位置 | 现在拿的是什么 | 能不能同步「加粗 / 名条」 |
|---|---|---|
| 头顶出牌展示 `PlayedCardView` | `CardArtLibrary.GetArt` = `Art/Cards/` 里 **760×1056 的成品整图**（卡名 / 数值 / 文字**烘焙在图上**） | ❌ 图上没有可改的元素 |
| 飞行卡 `CardTransitView` | 同上（`GetArt`） | ❌ |
| 选牌弹窗槽 `HandPickSlot` | `GetIllustration` = `Art/CardArt/` 784×1168 **插画原图**（无框无字） | ❌ |
| 查看对方手牌 `PeekCardSlot` | 同上（牌背 → 翻面用插画） | ❌ |
| 怪物手牌 `MonsterHandSlot` | 同上 | ❌ |

**要统一只能把它们也换成组成式卡面**（那才是「同一份 Prefab 画出来」）。这不是一次抄数值的活：
每处都要改渲染方式（`Image` → `CardView`）、定尺寸、并在预置节点上回填引用，
而且**出牌演出与飞行卡是动画路径**（`PlayedCardView.Layout` 每帧改 `sizeDelta`、
`CardTransitView` 用 ghost 池做插值），改成组成式需要一并处理缩放与「埋掉旧节点」。
—— 这一条**等用户确认**再动（是改观感的活，且必须真机看效果）。
