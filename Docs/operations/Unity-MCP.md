# Unity 与 MCP 运行指南

## 项目与前置条件

- 实际项目：`E:\UnityProject\Unity_AI_CardFight2`。
- 场景：`Assets/Scenes/SampleScene.unity`。
- 编辑器：Unity `2022.2.15f1c1`。
- MCP 服务：`http://127.0.0.1:8080`，HTTP 传输。
- `E:\UnityProject\Unity\_AI\_CardFight2` 不存在，不能作为本项目路径。

## 启动 MCP 服务

用户提供的 PowerShell 命令（URL 使用普通字符串，不要粘贴 Markdown 链接）：

```powershell
& 'D:\Python\Scripts\uvx.exe' --offline --from 'mcpforunityserver==10.1.2' mcp-for-unity --transport http --http-url 'http://127.0.0.1:8080' --project-scoped-tools
```

`--offline` 要求该版本已经在 uv 本机缓存内；可先确认入口可用：

```powershell
& 'D:\Python\Scripts\uvx.exe' --offline --from 'mcpforunityserver==10.1.2' mcp-for-unity --help
```

也可运行项目脚本 `Tools/Mcp/Start-UnityMcp.ps1`。脚本在 8080 已有监听服务时提示检查连接并退出，避免重复启动。
启动后保持终端运行，在 Unity 的 MCP 窗口中使用同一 HTTP 地址连接。

## 连接成功的判据

仅有端口监听不代表 Unity 已连接。必须通过 MCP 读取：

1. `mcpforunity://instances`：实例列表中有目标项目。
2. `mcpforunity://project/info`：`projectRoot` 与实际项目一致。
3. `mcpforunity://editor/state`：`data.advice.ready_for_tools == true`，编译未进行。
4. 执行只读场景查询、读取 Console，确认编辑器实际响应。

多个实例时先用 `set_active_instance` 选定目标。项目未连通时停止项目修改，先报告连接问题。

## 排查与编译

- 服务未启动：使用上述命令，检查 uvx 路径和离线缓存。
- 端口被占用：检查现有进程，不重复启动或自动结束未知进程。
- 服务可读但实例数为 0：检查 Unity MCP 插件中的 HTTP 地址和连接状态。
- 外部新建 C# 文件：调用 `refresh_unity`，`scope=all`、`mode=force`，等待编译及域重载完成后读取 Console。
- 域重载中短暂断连：等编辑器恢复后重新读状态，不反复并行触发编译。

## 本次验证

2026-09-18 已通过 MCP 实际读取目标项目、场景和编辑器状态，并运行 Unity 测试。开始任务时服务已运行，没有重复启动第二个服务。具体回归结果见 [验证报告](../validation/2026-09-18-交互与光环回归.md)。

离线入口实际验证：上述 `--help` 命令退出码为 0；`Start-UnityMcp.ps1` 检测到现有 8080 监听，按预期退出，没有启动第二个服务。
