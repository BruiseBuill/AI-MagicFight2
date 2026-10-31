# 魔法乱斗 · MagicBrawl

Unity 卡牌对战项目，支持标准随机牌组与自定义牌组。

## 启动

1. 用 Unity Hub 打开仓库，编辑器版本见 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)（当前为 2022.2.15f1c1）。
2. 打开 [Home](Assets/Scenes/Home.unity)，进入 Play。直接打开 [SampleScene](Assets/Scenes/SampleScene.unity) 可调试战斗。
3. 玩法见 [规则基线](Docs/rules/01-规则基线.md)，实现状态与已知差异见 [当前基线](Docs/00-当前基线.md)。

## 文档与工具

- [项目文档](Docs/README.md)：规则、卡表、交互、架构与历史记录。
- [文档规范](Docs/engineering/文档规范.md) · [目录规范](Docs/engineering/项目目录规范.md)。
- [工具入口](Tools/README.md) · [Unity MCP 运行指南](Docs/operations/Unity-MCP.md)。

## 检查

在仓库根目录执行；规则自测需要 .NET 9 SDK。

```powershell
python Tools/Repo/verify_repo.py
dotnet run --project Tools/RuleSelfTest -- --quick
```

按改动选择其中一项即可。默认只编译及少量定向检查，不运行大量随机对局、完整 EditMode 或全面验收；详见 [轻量检查原则](Docs/engineering/文档规范.md)。
