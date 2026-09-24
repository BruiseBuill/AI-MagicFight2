# 截图不在这里了

`Docs/art-review/` 下的 **92 个文件 / 约 285 MB** 截图已整体迁到工程内：

```
Captures/art-review/
```

**原因**：知识库（`Docs/`）是给人和 AI **读**的地方，一次目录遍历 / 全库检索不该为 285 MB 的
PNG 买单。迁移后 `Docs/` 只剩约 **1.3 MB** 纯文本。文件一个没删，路径只是从 `Docs/` 挪到了
`Captures/` —— 工程里 `Captures/` 本来就是「运行截图，不导入 Unity」那一档（见
[目录规范](../engineering/项目目录规范.md)）。

- 文档里的引用已同步改成 `Captures/art-review/...`（21 篇）。
- 新截图也请直接落 `Captures/art-review/`，命名沿用 `m<模块>_<主题>_<序号>_<状态>.png`。
- 工具脚本 `Tools/font-audit/render_font_samples.py` 的输出目录已同步。
