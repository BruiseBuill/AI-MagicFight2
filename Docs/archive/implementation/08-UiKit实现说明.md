# 08 · UiKit 实现说明（M7）

> 归档说明：本篇是已被替代的初版；当前入口见 [实现索引](../../implementation/README.md)。目录迁移不代表重新验收。

> **性质**：历史实现记录 · **最后核对** 2026-09-23（未逐条复核，**别照它改代码**）

> 历史实现记录；当前交互与规则以 [规则基线](../../rules/01-规则基线.md) 和 [当前交互说明](../../design/战斗交互.md) 为准。

> 承接 `Docs/engineering/03-工程规划.md` §7 的界面要求。本文是 **M8 BattleUi 的交接文档** ——
> 界面骨架已经搭好并通过截图验收，M8 要做的是「把真数据接上去」。
>
> 验收截图：`Captures/art-review/m7_uikit_01.png`、`Captures/art-review/m7_uikit_02.png`

---

## 1. 边界

| 做 | 不做 |
|---|---|
| Canvas 结构与分辨率适配 | 接引擎数据（M8） |
| `CardView` 两种形态 + Prefab | 交互状态机 / 点击选牌（M8） |
| 版式与配色常量 | 动画演出（M8 用 UiTween 写具体动画） |
| 动画基类 `UiTween` | 发牌界面（M9）、设置界面（M10） |

**M7 完全不依赖 M4 引擎**：所有视图只消费 `Core/Battle/Snapshot.cs` 里的只读快照
（`CardSnapshot` / `PlayerSnapshot` / `BattleSnapshot` / `DecisionSnapshot`），
所以界面能脱离规则内核独立开发与验收 —— 这也顺手把分层铁律第 2 条（依赖只能向下）落成了结构。

---

## 2. 产出物

| 类型 | 路径 |
|---|---|
| Prefab | `Assets/Prefabs/Ui/BattleCanvas.prefab`（场景骨架） |
| Prefab | `Assets/Prefabs/Ui/CardView_Hand.prefab`（手牌大卡） |
| Prefab | `Assets/Prefabs/Ui/CardView_Mini.prefab`（冷却区迷你卡） |
| 资产 | `Assets/Resources/CardArtLibrary.asset`（卡 ID → 整张卡面 Sprite，40 张） |
| 运行时代码 | `Assets/Scripts/Unity/Ui/` |
| 编辑器工具 | `Assets/Scripts/Unity/Editor/UiKitBuilder.cs` |

场景：`Assets/Scenes/SampleScene.unity` —— 含 `BattleCanvas`（Prefab 实例）+ `EventSystem`。

---

## 3. 节点结构

```
BattleCanvas                     Canvas(Overlay) + CanvasScaler(1920×1080, match 0.5) + GraphicRaycaster
├─ Backdrop                      Image，深板岩底
├─ TopArea                       高 736（= 1080 − 手牌区 344）
│  ├─ PlayerCoolingPanel         anchor(0,1) 左上，宽 224
│  │  └─ Grid                    GridLayoutGroup + MiniCardGrid ← 运行时塞 CardView_Mini 实例
│  ├─ EnemyCoolingPanel          anchor(1,1) 右上，宽 224（与左面板对称）
│  │  └─ Grid                    同上
│  └─ Stage
│     ├─ PlayerBar               anchor 居中，偏移 x = −360；Accent/Panel/Name/HpDots(Dot0–3)/HpNumber/HandCount/Aura
│     ├─ EnemyBar                anchor 居中，偏移 x = +360（对称）
│     └─ StageMark               TMP 文本，居中
└─ HandArea                       底部全宽，高 344
   └─ HandRow                    HorizontalLayoutGroup，居中 ← 运行时塞 CardView_Hand 实例

EventSystem                      独立根节点（StandaloneInputModule）
```

**CardView_Hand**：`Hit(透明点击区, 最底)` / `HandRoot(Art, Dim)` / `Glow(4 根细条拼的选中边框)`
**CardView_Mini**：`Hit` / `MiniRoot(Face, Border, Name, Power, Cooldown, CooldownBase, AuraPips)` / `Glow`

> `Glow` 刻意不是单张 Image 而是 4 根细条 —— 没有 9-slice 边框贴图时，
> 用整块 Image 会盖住卡面，用细条拼框最省事且随尺寸自适应。

---

## 4. 常量都在哪（改版式只改这两处）

| 文件 | 内容 |
|---|---|
| `Ui/UiLayout.cs` | **全部尺寸与字号**。参考分辨率、手牌卡 208×289、迷你卡 104×140、每列 4 张折列、双方信息条偏移 360、字号（对齐 `Docs/engineering/06-美术与字体规范.md` §3.6） |
| `Ui/UiTheme.cs` | **全部颜色**。深板岩底、双方主色（你=青 / AI=橙）、冷却数字蓝、光环亮/灰、生命三态色 |

改完这两个文件 → 跑菜单 `魔法乱斗/M7 · 构建 UiKit（Canvas + Prefab）` → 结构按新数值重建。

### 几个不得不解释的数值

- **手牌卡 208×289**：卡面是 760×1056（比例 0.7197），高按比例算出；
  8 张 + 7 个 16 px 间距 = 1776 ≤ 1920，刚好是「8 张上限放得下」的最大尺寸。
- **迷你卡每列 4 张折列**：一个玩家最多 8 张牌，8 张竖排要 8×140 = 1120 px，顶穿 736 px 的上部区域。
  折成 2 列后是 584 px，留足余量。
- **迷你卡的「剩余冷却」用浅蓝**：卡面右上角本来就是浅蓝的冷却圆点，延续这个视觉语言；
  否则「力量」和「剩余冷却」两个白字数字在 104 px 宽的卡上极易看混。
- **生命圆点三态**：剩余（红）/ 已失去（暗）/ 上限被削掉（更深）—— 圆点表达不了「上限减少」，
  再配一个 `3/3` 数字。

---

## 5. 组件职责

| 组件 | 职责 |
|---|---|
| `CardView` | 一张牌的视图。`Bind(CardSnapshot, ViewMode, slot)` / `RefreshCooldown` / `SetInteractable` / `SetSelected` / `SetDimmed` / `Clear`，`Clicked` 事件 |
| `PlayerBarView` | 一方信息条。`Bind(PlayerSnapshot)`，`SetAccent`，`ShowHandCount`（玩家侧关掉） |
| `MiniCardGrid` | 冷却区排布，子节点数变化时自动重算列数（>4 张折第二列） |
| `CardArtLibrary` | ScriptableObject，卡 ID → 卡面 Sprite。放 Resources 下是为了运行时能取到 |
| `UiTween` + `TweenScale/TweenAnchoredPosition/TweenCanvasAlpha` | **动画基类**。零第三方依赖（不走 DOTween），协程 + `unscaledDeltaTime`，带缓动与 `Snap()` 跳帧 |
| `UiKitPreview` | **M7 自检装置**：铺样本卡验版式，不连引擎。M8 之后只留在预览用 |

---

## 6. 编辑器菜单

| 菜单 | 作用 |
|---|---|
| `魔法乱斗/M7 · 构建 UiKit（Canvas + Prefab）` | 全量重建：TMP 字体配置 → 卡面映射表 → 三个 Prefab → 场景里的 Canvas + EventSystem。**幂等**，可反复跑 |
| `魔法乱斗/重建卡面映射表（CardArtLibrary）` | 美术改名 / 补图后单独重建映射 |
| `魔法乱斗/配置 TMP 默认字体与 fallback` | 设 TMP Settings 默认字体 = 正文（Noto Sans SC），给仓耳渔阳体 W03 与 ZCOOL Addict 挂正文 fallback（它们缺 `α β γ ≤ ≥`） |

> 卡面映射是按文件名里的 `_<卡ID>_` 段匹配的（规范见 `Docs/engineering/06-美术与字体规范.md`），
> 所以美术文件必须保持 `Card_<两位序号>_<卡ID>_<卡名>.png` 的命名，改名后重跑菜单即可。

---

## 7. 分辨率适配

`CanvasScaler`：`ScaleWithScreenSize` + 参考分辨率 `1920×1080` + `match = 0.5`。

`match = 0.5` 是刻意的：`0`（按宽）在超宽屏会把手牌顶出屏幕，`1`（按高）在窄屏会把冷却区挤扁，
取中间值让两个方向都留有余量。手牌区宽度按 1776/1920 = 92.5% 占满，横向余量只有 7.5%，
**如果将来手牌上限从 8 张放宽，必须先回到 `UiLayout` 调小卡宽**。

---

## 8. M8 要接的地方

```
BattleEngine.OnEvent ──► BattleDriver（M6，把事件流切成有节奏的演出）
                              │
                              ├─► HandView      ：读 CardSnapshot[]，池化实例化 CardView_Hand 到 HandRow
                              ├─► CooldownView  ：读 CardSnapshot[]，池化实例化 CardView_Mini 到两个 Grid
                              ├─► StageView     ：读 PlayerSnapshot[]，Bind 到 PlayerBar / EnemyBar
                              └─► TargetPicker  ：读 DecisionSnapshot，把 Option[] 画成可点选项
```

三个**必须一起解决**的问题（都在 M8）：

1. **手牌效果文字不可读** —— 208 px 宽下手牌图上的效果文字只有约 11 px。
   要么选中/悬停时放大到 ~380 px，要么在信息条下加一条详情浮层。
2. **`HorizontalLayoutGroup` 与「放大上浮」冲突** —— 布局组会用子节点的 preferredSize 分配位置，
   `localScale` 不影响布局（这点已经设计好了：CardView 根节点带 `LayoutElement`，pivot 在底边，
   放大只会向上向外长，不会挤动别的牌）。若要「抬起来」，改 `anchoredPosition` 而不是 `localScale`，
   那时就需要把 `HandRow` 换成自定义排布。
3. **点击已就绪** —— `EventSystem` 与每张卡的 `Button` 都挂好了，`CardView.Clicked` 直接订阅即可；
   `SetInteractable(false)` 会压暗并吃掉点击。

### 8.1 整理后新增的可用素材（2026-09-16）

M7 时期手边只有卡面整图，现在多出一批可用的：

| 素材 | 位置 | M8 能怎么用 |
|---|---|---|
| **插画原图** 40 张 | `Assets/Art/CardArt/Card_<序号>_<卡ID>_<卡名>.<ext>` | 迷你卡要插画时直接用（不必再从卡面里裁）；也可做详情浮层的背景图 |
| 无对应卡草稿 9 张 | `Assets/Art/CardArt/_Draft/` | ⛔ **勿使用** —— 已确认为未实装素材（无对应卡、规则未定义），仅保留备用 |
| **触发图标** 剑 / 盾 / 感叹号 | `TriggerIconLibrary.Instance.GetIcon(EffectTrigger.Attack)` | ⭐ **效果文字要带符号时直接取**：α 剑 / β 盾 / γ 感叹号。三张 1024² Sprite（24 px 下仍可辨认），按枚举取值，不必自己拼路径 |
| **PolySprite 形状** 35 张 | `Assets/ThirdParty/PolySprite/` | 低多边形几何形状，与卡面调性一致，可做底纹/箭头/分隔线（`DashLine_*`、`Hex_*`、`Hoop*`） |
| 魔法卡 UI 材质 11 + 着色器 5 | `Assets/ThirdParty/MagicCardKit/` | `RoundBox` 圆角面板、`EdgeFade/EdgeEnhance` 描边发光、`RandomCircle` 随机圆环 —— 想升级面板质感时的现成方案 |
| 爆炸光效 | `Assets/Art/Fx/Fx_Explosion_Light.png` | 结算/受击打击感 |

> ⚠ 取图标前先判 `HasIcon()`：`EffectTrigger.Passive`（常驻）本批没有图标，`GetIcon` 返回 `null`，
> 要**不留符号位**，别画成空白方块。
>
> 若 M8 打算让效果文字**用 TMP 内联 Sprite** 渲染（`<sprite name="attack">加速`），
> 还需在这三张 Sprite 之上再做一个 `TMP_SpriteAsset`；当前只做到「按时机取到 Sprite」。

> 统一的映射与命名规则见 `Docs/engineering/06-美术与字体规范.md` §2.6–§2.9；
> 素材总览图见 `Captures/art-review/`，可先看图再决定用哪张。
