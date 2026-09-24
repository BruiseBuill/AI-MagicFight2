# 魔法乱斗 · Unity CardFight

Unity 2022.2.15f1c1 / URP 2D / UGUI + TextMeshPro。
实际项目：`E:\UnityProject\Unity_AI_CardFight2`。

- [文档入口](Docs/README.md)
- [运行与 MCP 连接](Docs/operations/Unity-MCP.md)
- [当前交互规范](Docs/implementation/光环与拖动交互.md)
- [目录规范](Docs/engineering/项目目录规范.md)

打开 `Assets/Scenes/SampleScene.unity` 后进入 Play。进攻、防御必须拖动手牌；光环从 `ArtLayer/HudBuff` 拖到自己的手牌区使用，长按查看说明。

规则自测：`dotnet run --project Tools/RuleSelfTest -- 10000 20260918`。
Unity 测试：Test Runner → EditMode → Run All。
