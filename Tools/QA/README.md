# Unity 交互探针

仅用于调试，可通过 Unity MCP `execute_code` 执行 `.cs.txt` 内容。它们会暂停表现桥协程、布置临时测试数据，必须在 Play 模式中运行，不保存测试局面。

1. 打开 SampleScene，进入 Play，等待初始换牌决策。
2. 顺序执行 `attack-input-probe.cs.txt`、`aura-input-probe.cs.txt`、`defense-input-probe.cs.txt`。
3. 检查返回的布尔值均为 true；光环数量应从 3 变为 2。
4. 退出 Play，恢复编辑状态。

这些探针验证运行中组件的事件入口与展示接线；规则结算由 EditMode 测试独立验证。完整结果见 [回归报告](../../Docs/validation/2026-09-18-交互与光环回归.md)。
