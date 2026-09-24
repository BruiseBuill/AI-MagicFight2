# 09 · BattleUi 实现说明（M6 + M8）

> **性质**：历史实现记录 · **最后核对** 2026-09-23（未逐条复核，**别照它改代码**）

> 历史实现记录；当前交互与规则以 [规则基线](../rules/01-规则基线.md) 和 [当前交互说明](光环与拖动交互.md) 为准。

> 承接 `Docs/engineering/03-工程规划.md` §7（界面要求）与 `Docs/implementation/08-UiKit实现说明.md` §8（M8 接入指引）。
> 本批把 M7 搭好的界面骨架接上了真数据，并把规则引擎真正驱动了起来。
>
> 验收截图：`Captures/art-review/m8_uikit_01_replace.png` ~ `m8_uikit_04_result.png`

---

## 1. 本批交付

| 项 | 内容 |
|---|---|
| **M6** `BattleDriver` | 引擎驱动 + 事件节拍回放 + 决策分发（AI 即时应答 / 玩家挂起等点击） |
| **M8** `HandView` | 手牌区：池化 + **自定义排布** + 悬停上浮 + 点选出牌 |
| **M8** `CooldownView` | 双方冷却区：池化到两个 Grid，剩余冷却只改数字不重建 |
| **M8** `StageView` | 双方信息条 + 中央标记 + 顶部提示条 + 受击闪红 |
| **M8** `TargetPicker` | 选项浮层：把「不挂在手牌上」的选项画成按钮 |
| **M8** `CardDetailView` | **详情浮层：解决手牌效果文字不可读** |
| **M8** `BattleUi` | 总装：唯一同时认识 driver 与各视图的类 |
| 编辑器 | `BattleUiBuilder.cs`，菜单 `魔法乱斗/M8 · 构建 BattleUi（接线 + 浮层）`，幂等可重跑 |

---

## 2. M6 BattleDriver：把同步结算切成有节奏的演出

### 2.1 为什么需要它

规则结算天生**同步顺序** —— `BattleEngine.Advance()` 一次可能连发几十条事件；
而 Unity UI 要**异步等点击**。`BattleDriver` 做两件事：

1. 把同步事件流**缓冲成一条条 beat**，按节拍逐个回放；
2. 把「AI 座位即时应答 / 玩家座位等点击」这套暂停-恢复循环真正跑在 Unity 里。

主循环（`CoRun`）：

```
engine.Start()
loop until IsOver:
    if engine.Pending == null: engine.Advance()      # 产生事件进 buffer
    yield CoPlayBeats()                              # 把 buffer 逐拍演完
    req = engine.Pending
    if req.Seat == AI 或 autoPlay:  engine.Submit(ai.Decide(req))
    else:                           yield CoWaitPlayer(req)   # 挂起，等 UI 回填
```

### 2.2 节拍表（`BeatDuration`）

| 事件 | 秒 | 理由 |
|---|---|---|
| `AttackDeclared` | 0.55 | 打牌要看得清 |
| `DefenseResolved` | 0.50 | 挡没挡住是信息量最大的一拍 |
| `DamageTaken` | 0.60 | 掉血配闪红 |
| `AttackPowerResolved` | 0.28 | |
| `TurnStarted` | 0.32 | |
| `CooldownChanged` | **0.06** | 批量发生，只是为了让状态逐条刷新，不是在等人看 |
| `CardDrawn` / `CardReturned` | 0.18 | |
| `GameOver` | 0.85 | |

`_beatScale` 是总倍率；设为 **0** 即「跳过演出」（`SetFastForward(true)`），
此时每个事件只 `yield return null` 让出一帧，避免一帧内把 UI 重绑上千次。

### 2.3 对外接口

```csharp
// 事件
event Action<BattleEvent>        OnBeat;             // 逐拍演出
event Action                     OnStateChanged;     // 快照可能变了，UI 重绑
event Action<DecisionSnapshot>   OnPlayerDecision;   // 轮到你（或 AI 位玩家）决策
event Action<int, string>        OnFinished;         // (胜者座位, 结束原因)
event Action                     OnStarted;

// 只读快照（UI 唯一的数据来源）
BattleSnapshot GetBattle();
PlayerSnapshot GetPlayer(int seat);
void CollectHand(int seat, List<CardSnapshot> sink);
void CollectCooling(int seat, List<CardSnapshot> sink);

// 玩家回填
void SubmitPlayerDecision(params int[] optionIndices);
void SubmitSkip();
```

`CollectHand` / `CollectCooling` 用 sink 参数是为了**零分配** —— 每拍都要重绑，不能新建 List。

---

## 3. M8 的五个视图 + 总装

### 3.1 数据流（严格单向）

```
BattleEngine ──OnEvent──► BattleDriver ──OnBeat/OnStateChanged──► BattleUi ──► 各 View
                                             │
                                        OnPlayerDecision
                                             │
玩家点牌 ──► HandView / CooldownView / TargetPicker ──Option──► BattleUi ──► driver.SubmitPlayerDecision
```

**`BattleUi` 里唯一的「业务判断」是选项分类，而它不是规则判断**：
引擎给的 `Options` 已经全部合法，`BattleUi` 只是按「有没有实体卡在屏幕上」把它们分成
「点那张卡」和「点选项按钮」两拨 —— 目的是不出现「同一件事有两个入口」的重复。
分类结果：

| 选项 | 归到哪 |
|---|---|
| `Option.Card != null` 且 `Zone != Cooling` | 手牌（点那张卡） |
| `Option.Card != null` 且 `Zone == Cooling` | 冷却区（点那张迷你卡） |
| `Option.Card == null`（Skip / ZoneValue / Done…） | 选项浮层按钮 |

### 3.2 `HandView`：为什么必须换掉 `HorizontalLayoutGroup`

规划 §7 的遗留问题②。`HorizontalLayoutGroup` 用子节点的 `preferredSize` 重新分配
`anchoredPosition`，而「选中上浮」改的正是这个值 —— 两者每帧打架。
放大（`localScale`）不影响布局，所以只有「上浮」这一半会坏。

改法是**自定义排布**：每帧自己算目标位置与缩放，再按 `1 - exp(-speed·dt)` 平滑趋近。
顺带白捡一个好处：手牌增删时卡片会**滑到新位置**（布局组只会瞬移）。

```csharp
// HandView.Update()
for each shown card i:
    targetX     = startX + i * (W + spacing)
    targetRaise = (i == hover) ? HandHoverRaise : 0
                | (uid == selected) ? HandSelectRaise : 0
    st.X/Raise/Scale ← Lerp(cur, target, k)
```

每张牌的状态存在 `HandCardState` 组件上（跟着卡实例走），
所以池化复用时位置不会跳。

**位置预算**：手牌区 344 − 底边距 22 − 卡高 289 = **33 px 余量**，
所以选中上浮定在 26 px（`UiLayout.HandSelectRaise`），再大就顶进上部区域了。

### 3.3 `CardDetailView`：效果文字不可读的解法

规划 §7 的遗留问题①。手牌卡面缩到 208 px 后，烘焙的效果文字只剩约 **11 px**；
而 8 张手牌横排要 1776 px（占屏宽 92.5%），**没有横向余量去放大**。

解法是**在舞台中央开一层浮层，用 TMP 重新排版**，而不是放大卡面：

- 卡面即使放大到 420 px 宽，效果文字也只有 ~22 px，而且换行位置是烘焙死的；
- 用 `CardLibrary.Get(id).Effects[i].Text` 现场排版，字号 19 px（下限自适应到 14），
  还能在每条效果前挂上触发符号（剑 α / 盾 β / 感叹号 γ，取自 `TriggerIconLibrary`）。

> ⚠ `EffectTrigger.Passive` 本批**没有图标**，`GetIcon` 返回 null。
> `BindEffects` 会检查这一点并把文字左移让出符号位，而不是画一个空白方块。

最长的一条效果是「模仿」（47 字），在 340 px 宽下靠 `enableAutoSizing` 缩到 14 px
正好挤进两行 —— 这也是 `UiLayout.DetailRowHeight = 50` 的由来。

### 3.4 `TargetPicker`：只画「非卡牌选项」

带 `Card` 的选项已经有实体卡在屏幕上，直接点那张卡才符合直觉，再列一份按钮是重复。
所以浮层最多只有 4 行（`UiLayout.PickerMaxRows`），不会顶到详情浮层。
标题也不显示（决策文案已经由顶部提示条承担），一个可点按钮都没有时整个浮层收起。

### 3.5 `StageView`：中央 `StageMark` 停用

M7 的 `StageMark` 是 200×90 且居中，而中央净空只有 420 px 宽：
选项浮层（最高 380 px 高）会从上面压过来。所以 M8 把叙事文案统一改走
**顶部提示条**（`PromptBar` 640×58，能放 22 个字），`StageMark` 在构建时停用。
`StageView.SetMark` 能力保留，留给以后（比如真正的「VS」开场演出）。

---

## 4. M8 的节点结构

```
TopArea                          ← 高 736（= 1080 − 手牌区 344）
├─ PromptBar                     640×58，顶部中央；叙事 / 决策文案都在这
├─ PickerPanel                   420 宽，锚顶；Body 内是 ContentSizeFitter 自适应的按钮列表
│   └─ Body / Backdrop / Frame / Title(隐藏) / Rows / Row0..3
├─ DetailPanel                   420 宽，锚底（手牌上方）；Body 同样自适应高度
│   └─ Body / Backdrop / Frame / Name / Meta / Effects / Row0..3
└─ Flash                         全 TopArea 的红闪覆盖层（alpha 0，raycastTarget=false）

BattleCanvas
├─ ResultRoot                    全屏：Veil（遮罩）+ Panel（标题 / 副文本 / 再来一局）
└─ LogPanel                      右下角，默认关（M10 从设置里开）

组件落点
  HandArea          → HandView（_cardPrefab / _row / _detail）
  TopArea           → CooldownView（_cardPrefab / _playerRoot / _enemyRoot）
  Stage             → StageView（_playerBar / _enemyBar / _promptRoot / _flash）
  PickerPanel       → TargetPicker
  DetailPanel       → CardDetailView
  BattleCanvas      → BattleDriver + BattleUi
```

---

## 5. 中央 420 px 净空的三段分配

中央能用的横向空间被左右信息条卡死：双方 `PlayerBar` 各占 `±360` 宽 300，
中间只剩 **x ∈ [−210, 210]**。三块浮层按垂直方向排：

| 区块 | 顶部起算 | 高度 |
|---|---|---|
| `PromptBar` | 16 | 58 |
| `PickerPanel` | 84 | ≤ 296（标题 40 + 4×52 + 3×6 + padding 24） |
| `DetailPanel` | 底边距 10（顶边落在 398） | 自适应，最多约 250 |

两者之间留 18 px 间隙 —— **改任何一块的高度前先回来核对这张表**。

---

## 6. 构建顺序（重要）

```
改 UiLayout.cs / UiTheme.cs
   ↓
菜单 魔法乱斗/M7 · 构建 UiKit（Canvas + Prefab）     ← 会推倒重建整个 Canvas
   ↓
菜单 魔法乱斗/M8 · 构建 BattleUi（接线 + 浮层）      ← 重新接线 + 建 M8 节点
```

**M7 会把 Canvas 整个重建，M8 的接线随之丢失**，所以两者必须连跑。
M8 的构建器是幂等的：它先删掉自己上次建的 6 个根节点（`Flash` `PromptBar`
`PickerPanel` `DetailPanel` `ResultRoot` `LogPanel`）再重建，不会重复堆节点。

另外 M8 构建时还会做三件「修环境」的事：

1. 删掉 `HandRow` 上的 `HorizontalLayoutGroup`（否则自定义排布被压回去）；
2. 把 `UiKitPreview._buildOnAwake` 设为 false（M7 的自检装置不该在真对局里铺样本卡）；
3. 藏掉 M7 的 `StageMark`。

---

## 7. 验收

| 项 | 方式 | 结果 |
|---|---|---|
| 编译 | Unity MCP：`refresh_unity` + 反射确认 8 个新类型进程序集 | ✅ 0 错 0 警告 |
| 构建 | Unity MCP `execute_menu_item` 跑 M8 菜单 | ✅ |
| 开局 | Play → 发牌 → 替换决策（手牌 6 张 / 提示条 / 选项浮层） | ✅ `m8_uikit_01_replace.png` |
| 详情 | 选中「瀑流」→ 浮层显示卡名 / 力量冷却 / 2 条效果（含剑图标） | ✅ `m8_uikit_02_detail.png` |
| 对局中 | 冷却区迷你卡 + 剩余冷却 + 光环亮点 + 叙事提示条 | ✅ `m8_uikit_03_midgame.png` |
| 手牌打空 | 双方手牌归零后靠冷却回手继续打 | ✅ `m8_uikit_03b_emptyhand.png` |
| 结算 | 胜负 + 遮罩 + 再来一局；点了确实开新局 | ✅ `m8_uikit_04_result.png` |
| 内核回归 | `dotnet run -- 3000 20260916` | ✅ 全绿，0 异常 / 0 违规 |

---

## 8. 本批踩到的坑（都值得记住）

### 8.1 `LayoutGroup.childControlHeight = false` 会让 `LayoutElement.preferredHeight` 完全失效

**症状**：详情浮层里效果文字跑到浮层**外面**、压在手牌上；选项浮层面板比内容高一倍。

**原因**：`childControlHeight = false` 时，`VerticalLayoutGroup` 不去控制子节点高度，
而是**直接用子节点自己的 `sizeDelta`** —— `LayoutElement.preferredHeight` 根本不参与计算。
而 `new GameObject(name, typeof(RectTransform))` 建出来的节点默认是 **100×100**。

**修法**：凡是要用 `preferredHeight` 的布局，父组必须 `childControlHeight = true` +
`childForceExpandHeight = false`。

### 8.2 改 `LayoutElement.preferredHeight` 不会自动把布局标脏

`ContentSizeFitter` 不会自己发现子节点的 `preferredHeight` 变了，
必须显式 `LayoutRebuilder.MarkLayoutForRebuild(rect)`。
所以「浮层高度按实际条数自适应」这件事，改值之后还得喊那一嗓子。

### 8.3 `Selectable.ColorTint` 是「替换颜色」不是「乘算」

`targetGraphic.color` 会被状态色**直接覆盖**。所以底图要建成白色，
把常态色交给 `colors.normalColor` —— 否则构建时设的底色在下一次状态切换就被抹掉了。

### 8.4 新建 `.cs` 后 `refresh_unity` 报 `refresh_triggered: false`

Unity 没把新文件当「脏」。得先 `AssetDatabase.Refresh(ForceUpdate)` 让它导入并生成 `.meta`，
再 `CompilationPipeline.RequestScriptCompilation()`。
**判断编译是否真的完成的唯一可靠方法是反射查类型**（`Type.GetType("全名, 程序集名")`），
不要只看 refresh 的返回值。

---

## 9. 还没做的

| 项 | 归属 |
|---|---|
| 开局发 6 张的**图形化**替换交互（现在只有「不替换」按钮 + 点牌换牌） | M9 `DealUi` |
| 设置界面（战斗日志开关；`LogPanel` 已经建好、默认关） | M10 `SettingsUi` |
| 40 张卡的逐张行为用例 | 需先给引擎加 `TestSetup.SetHand(...)` 注入钩子 |
| 开卡动画 / 出牌飞行动画 / 音效 / 结算演出 | 批次 4 |
| `TargetPicker` 超过 4 个选项时的分页（当前只显示前 4 个） | 视需要 |
