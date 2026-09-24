# 项目文档 · 入口

最后核对：**2026-09-23**（M34 落地当日）。历史实现记录里的数字 / 坐标只代表当时，
**不代表当前版本** —— 判断「现在是什么样」一律以现行档为准。

## 怎么读（三层，别一次全读）

| 层 | 什么时候读 | 读什么 |
|---|---|---|
| ① 现状 | 每轮开工先看 | [`00-当前基线.md`](00-当前基线.md) —— 状态板：落地了哪些模块、哪些结论能信、还差什么 |
| ② 路由 | 要动某块之前 | 下面的「按任务找文档」；实现类还会再进 [`implementation/README.md`](implementation/README.md)（模块 → 文档） |
| ③ 追溯 | 只想搞清「当初为什么这么长」 | `implementation/` 里标注为**历史**的那几篇 + [`archive/`](archive/)（已替代草稿，不作实现依据） |

⚠ **工程经验不在这个目录**：踩坑手册 / 环境与命令 / 逐日工作日志在 WorkBuddy 工作区的
`.workbuddy/memory/`（`MEMORY.md` / `PITFALLS.md` / `YYYY-MM-DD.md`），绝对路径见
[`00-当前基线.md`](00-当前基线.md) §6。动内核或经 MCP 改资产前先查 `PITFALLS.md`。

## 按任务找文档

| 要做什么 | 读这个 |
|---|---|
| **改玩法 / 卡表 / 效果触发时机** | [`rules/01-规则基线.md`](rules/01-规则基线.md)（§0 就是 α/β/γ 的判定）、[`rules/02-卡牌图鉴.md`](rules/02-卡牌图鉴.md)、[`rules/05-决策记录.md`](rules/05-决策记录.md) |
| **改代码结构 / 数据模型 / 接口** | [`engineering/04-架构与接口.md`](engineering/04-架构与接口.md)、[`engineering/03-工程规划.md`](engineering/03-工程规划.md)（§4 批次 / §9 里程碑 / §12 下一步） |
| **启动工程 / 连 MCP / 出问题怎么查** | [`operations/Unity-MCP.md`](operations/Unity-MCP.md) |
| **新增或改造一个 UI 模块** | 先 [`implementation/16-运行时UI预建与行动提示.md`](implementation/16-运行时UI预建与行动提示.md)（节点必须预建）→ 再[模块索引](implementation/README.md)找最近邻的那一篇 |
| **改卡面版式 / 图文混排** | [`implementation/15-M17组成式卡面与图文混排.md`](implementation/15-M17组成式卡面与图文混排.md) |
| **改手牌 / 光环的拖拽与落区** | [`implementation/光环与拖动交互.md`](implementation/光环与拖动交互.md) + [`20-M21光环摆位与取消手势.md`](implementation/20-M21光环摆位与取消手势.md) + [`32-M34取消光环飞回动画与齿轮接线修复.md`](implementation/32-M34取消光环飞回动画与齿轮接线修复.md) |
| **改「选择弹窗」家族**（看手牌 / 选牌 / 选冷却区） | `implementation/` 的 [23](implementation/23-M25查看对方手牌弹窗.md) · [25](implementation/25-M27手牌选择弹窗.md) · [28](implementation/28-M30手牌选择弹窗美术化与多选修复.md) · [29](implementation/29-M31漩涡选择弹窗与确认键固定宽度.md) |
| **整理美术 / 字体 / 目录** | [`engineering/06-美术与字体规范.md`](engineering/06-美术与字体规范.md)、[`engineering/项目目录规范.md`](engineering/项目目录规范.md) |
| **看某次到底验了什么** | [`validation/`](validation/)（按日期） |

## 目录说明

| 目录 | 内容 | 性质 |
|---|---|---|
| `00-当前基线.md` | 状态板（模块 / 验证程度 / 遗留 / 现行表现层速查） | 现行 |
| `rules/` | 规则基线、卡牌图鉴、决策记录 | **现行**（唯一规则来源） |
| `engineering/` | 规划、架构与接口、美术与字体规范、目录规范 | **现行** |
| `implementation/` | 实现记录 + [索引](implementation/README.md) | 混合：**索引里逐篇标了「现行 / 历史」** |
| `operations/` | 启动、连接、故障定位 | 现行 |
| `validation/` | 按日期的验证范围 / 结果 / 限制 | 终态存档 |
| `archive/` | 已被替代的草稿 | **不作为实现依据** |
| `art-review/` | 只剩一个指针：截图已迁至 `Captures/art-review/`（约 285 MB） | 见该目录的 README |

## 维护约定（新增文档时照这个来）

1. **截图不放进 `Docs/`**：运行 / 核对截图一律落 `Captures/art-review/`，
   命名 `m<模块>_<主题>_<序号>_<状态>.png`，在文档里用相对路径引用。
2. **实现记录**：新建 `implementation/NN-<M号><主题>.md`，`NN` 接着现有最大编号往下
   （当前最大 `32-`，下一个 **`33-`**；模块号与编号不同步，取号前先 `ls`）。
   做完必须回改：`00-当前基线.md` 的模块表与验证程度、`rules/05-决策记录.md`（新口径逐条）、
   `engineering/03-工程规划.md` §12、以及索引。
3. **一件事只有一个来源**：规则改 `rules/`，接口改 `engineering/04`，别在 README / 实现记录里复述。
4. **验收写清楚**：每条改动都写「怎么验的」。**点击类必须真点过**，
   没跑过的（例如端到端真机链路）要在 `00-当前基线.md` 的「已知遗留」里挂着。
