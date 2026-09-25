# 魔法乱斗 · MagicBrawl

Unity 2022.2.15f1c1 / URP 2D / UGUI + TextMeshPro 的人机对战卡牌项目。
本文中的路径均相对本仓库根目录；Unity 版本以 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) 为准。

## 开始使用

1. 在 Unity Hub 中添加本仓库目录，使用上述编辑器版本打开。
2. 打开 [SampleScene](Assets/Scenes/SampleScene.unity)，进入 Play。
3. 玩法与交互见 [文档入口](Docs/README.md)；验证范围和遗留问题见 [当前基线](Docs/00-当前基线.md)。

## 常用入口

| 任务 | 入口 |
|---|---|
| 理解玩法、接口和模块 | [项目文档](Docs/README.md) |
| 新增或修改文档 | [文档规范](Docs/engineering/文档规范.md) |
| 放置代码、美术或工具 | [项目目录规范](Docs/engineering/项目目录规范.md) |
| 运行工具、检查资源 | [工具入口](Tools/README.md) |
| 启动 Unity MCP、排查连接 | [运行指南](Docs/operations/Unity-MCP.md) |

## 验证

在仓库根目录运行；.NET 自测需要 .NET 9 SDK：

```powershell
python Tools/Repo/verify_repo.py
python Tools/art-audit/verify_layout.py
dotnet run --project Tools/RuleSelfTest -- 100 20261700
```

Unity 测试入口：Test Runner → EditMode → Run All。
规则自测和 EditMode 中存在已记录的僵局问题，历史测试结果不能替代本次执行；详见 [当前基线](Docs/00-当前基线.md)。
