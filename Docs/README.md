# 项目文档

> 状态：现行索引 · 核对日期：2026-09-25 · 范围：导航与文档归属

先读 [当前基线](00-当前基线.md)，了解已落地内容、验证边界和遗留问题；再按任务进入下表。
规则、当前规范、变更记录和历史资料分开维护；日期较新不等于经过完整验收。

| 要做什么 | 入口 |
|---|---|
| 改玩法、卡牌或效果 | [规则基线](rules/01-规则基线.md) · [卡牌图鉴](rules/02-卡牌图鉴.md) · [决策记录](rules/05-决策记录.md) |
| 理解当前交互 | [战斗交互](design/战斗交互.md) |
| 改架构或接口 | [架构与接口](engineering/04-架构与接口.md) · [工程规划](engineering/03-工程规划.md)；新模块结合 [实现索引](implementation/README.md) 核对 |
| 修改 UI 或查实现原因 | [实现与补丁索引](implementation/README.md) |
| 放置代码、资源或产物 | [目录规范](engineering/项目目录规范.md) · [美术与字体规范](engineering/06-美术与字体规范.md) |
| 写文档或整理历史 | [文档规范](engineering/文档规范.md) · [模板](templates/README.md) |
| 启动、连接、排障 | [Unity MCP](operations/Unity-MCP.md) |
| 跑工具与检查 | [工具入口](../Tools/README.md) |
| 查验证证据 | [验证索引](validation/README.md) · [截图](../Captures/README.md) · [原始产物](../Artifacts/README.md) |
| 追溯废弃方案 | [历史档案](archive/README.md) |

## 目录边界

- `rules/`、`design/`、`engineering/`、`operations/`：按主题维护的规范或指南。
- `implementation/`：单次实现快照；旧参数不自动代表当前行为。
- `validation/`：实际执行过的验证报告；原始日志和 JSON 在 `Artifacts/validation/`。
- `archive/`：已被替代的内容，保留来由与替代入口。
- `templates/`：新建记录的骨架，不计作完成或验收证据。

截图不进入 Docs，工具源码不与生成报告混放。维护细则只在 [文档规范](engineering/文档规范.md) 更新。
仓库内的文档应足够让新成员开始工作，个人工具记忆不作为必备依赖。
