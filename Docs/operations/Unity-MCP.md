# Unity 与 MCP 运行指南

## 项目与前置条件

- 实际项目：`E:\Unity_Project\Unity_AI_MagicFighting2`。
- 场景：`Assets/Scenes/SampleScene.unity`。
- 编辑器：Unity `2022.2.15f1c1`。
- MCP 服务：`http://127.0.0.1:8080`，HTTP 传输。
- 不要使用旧的 `E:\UnityProject\Unity_AI_CardFight2` 路径；本机实际项目目录以上述路径为准。

## 启动 MCP 服务

用户提供的 PowerShell 命令（URL 使用普通字符串，不要粘贴 Markdown 链接）：

```powershell
& 'C:\Users\Lenovo\.local\bin\uvx.exe' --offline --from 'mcpforunityserver==10.1.2' mcp-for-unity --transport http --http-url 'http://127.0.0.1:8080' --project-scoped-tools
```

`--offline` 要求该版本已经在 uv 本机缓存内；可先确认入口可用：

```powershell
& 'C:\Users\Lenovo\.local\bin\uvx.exe' --offline --from 'mcpforunityserver==10.1.2' mcp-for-unity --help
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

## ⚠ 坑：用 MCP 进 Play 模式会让工具在会话里失效（2026-09-25 实测）

`manage_editor(action=play)` 会触发域重载。实测这一次重载之后：

- Unity 侧桥**重连了**（`mcpforunity://instances` 的 `connected_at` 是一条新记录）；
- 但**客户端会话里的工具索引没有恢复** —— 之后所有 `ToolSearch` / 工具调用都报
  `not found in the deferred tools index`，**`manage_editor(stop)` 也调不到**；
- **资源通道仍然可用**（`ReadMcpResource` 能读到 `mcpforunity://editor/state`，
  里面明明写着 `play_mode.is_playing = true`）。

后果与做法：

- **优先不要进 Play 模式**：表现层的定点验证在**编辑期**就能跑完（见下），
  这样既不会掉线、也不会把编辑器留在 Play 里。
- 真要进 Play：**进去之前就要把整段脚本化冒烟想清楚**（一次 `execute_code` 跑完、自带清理），
  不要指望「先进 Play，再慢慢调代码」。
- 一旦掉线，编辑器会**留在 Play 模式**，只能请用户手动按 Stop；
  下一次用户消息之后工具索引会自己恢复（2026-09-25 实测：掉线只持续到下一轮）。

## ✅ 编辑期定点冒烟配方（2026-09-25 起，优先用这条）

不需要进 Play —— `AddComponent` 在编辑期不跑 `Awake`，而本项目的表现层组件
（`CardView` / `CooldownView` / `CardInteractor` / `ZoneSlotClickCatcher` …）的字段初始化与
`Awake` 里的接线都是 null-safe 的，所以直接造节点、直接调方法即可。

```csharp
// 1) 临时空场景 → 不碰用户正在编辑的场景
var prev  = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Additive);

// 2) 自建 GameObject 全打 DontSave → 场景永不脏，收尾关场景不会弹保存框
var go = new GameObject("Smoke", typeof(RectTransform));
go.hideFlags = HideFlags.DontSave;

// 3) …造层级 / 反射注入私有集合 / 订阅事件 / 调私有方法…

// 4) 收尾：退订 + 复位显示态 + SetActiveScene(prev) + 关场景
if (!scene.isDirty) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
```

几个要点：

- **`EditorSceneManager` 没有 `MarkSceneClean`**（只有 `MarkSceneDirty` / `MarkAllScenesDirty`）——
  所以「不脏」要靠 `HideFlags.DontSave` 从源头保证，而不是事后擦。
- 假卡要贴到**真实层级的样子**下（例如挂在 `SlotL_CD4/Cards` 里），
  这样 `CooldownView.RowIndexFor` 那条**按祖先名解析**的路才走得通。
- 断言尽量读**只有目标方法会写的私有字段**（例：`ScrollRect.m_Dragging` /
  `m_PointerStartLocalCursor`）——比读最终位置更硬，也不受插值 / 钳位影响。
- 顺手加一组**反向对照**（把开关翻到旧值再跑一次），能直接证明「旧行为确实是坏的」。

## 本次验证

2026-09-18 已通过 MCP 实际读取目标项目、场景和编辑器状态，并运行 Unity 测试。开始任务时服务已运行，没有重复启动第二个服务。具体回归结果见 [验证报告](../validation/2026-09-18-交互与光环回归.md)。

离线入口实际验证：上述 `--help` 命令退出码为 0；`Start-UnityMcp.ps1` 检测到现有 8080 监听，按预期退出，没有启动第二个服务。
