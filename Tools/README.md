# 工具入口

命令默认从仓库根目录执行。Python 工具从脚本位置解析根目录，不依赖个人机器盘符。
检查工具不写 Unity 资产；切图 / 重建工具会写资源，使用前先阅读其 `--help` 或文件头。

## 常用检查

```powershell
python Tools/Repo/verify_repo.py
python Tools/art-audit/verify_layout.py
dotnet run --project Tools/ArchitectureSmoke
dotnet run --project Tools/RuleSelfTest -- 100 20261700
```

前两项需要 Python 3.10+ 标准库；后两项需要 .NET 9 SDK。规则自测的已知僵局限制见 [当前基线](../Docs/00-当前基线.md)。

| 目录 | 职责 | 输出 |
|---|---|---|
| `Repo/` | 文档链接、目录约定、旧路径、Python 语法检查 | 终端；可用 `--json` 指定报告 |
| `art-audit/` | 资源引用检查、切图、核对图 | `Assets/Art/`、`Captures/art-review/`、`Artifacts/work/art/` |
| `font-audit/` | 字体审计、卡图清单与核对图 | `Artifacts/audits/font/`、`Captures/art-review/` |
| `RuleSelfTest/` | 链接 Core 的规则与多局自测 | 终端输出 |
| `ArchitectureSmoke/` | 独立牌堆、角色、控制器定向检查 | 终端输出 |
| `Mcp/` | Unity MCP 启动辅助 | 本地服务；见 [运行指南](../Docs/operations/Unity-MCP.md) |
| `QA/` | Play 模式临时交互探针 | 见 [探针说明](QA/README.md) |
| `archive/` | 一次性历史脚本 | 仅追溯，见 [归档说明](archive/README.md) |

## 资源处理前置条件

美术脚本依赖 Pillow / numpy；字体审计还需要 fonttools。

```powershell
python -m pip install -r Tools/requirements-art.txt
```

源表放在相应 `Assets/Art/*/_Source/`，成品放同类目录；工具不得把大图重新写入 Docs。
`slice_handpick_kit.py` 的原 UUID 源图已不在当前仓库，重切前需通过 `--source` 指定源图；现有成品继续可用。
早期 `rename_cards.py` / `migrate_to_indexed.py` 只服务旧命名阶段，不能当作日常重建命令。
历史审计输出不代表本次重新运行，见 [产物索引](../Artifacts/README.md)。
