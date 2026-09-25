# 仓库结构检查

```powershell
python Tools/Repo/verify_repo.py
python Tools/Repo/verify_repo.py --json Artifacts/validation/结构检查.json
python -m unittest discover -s Tools/Repo -p test_*.py -v
```

检查本仓库 Markdown 的本地文件链接、Docs 的非文本文件、活动工具中的旧机器路径 / 旧资源路径、
Python 语法、文档是否进入索引，以及禁止重建的旧目录。报告使用非零退出码表示失败。
模板占位符、外部 URL、历史原始产物和归档脚本不作为现行文件路径校验。
链接检查不验证 Markdown 标题锚点和外部网站，也不替代 Unity 编译或 GUID 引用检查。

Unity 资源检查另运行 `python Tools/art-audit/verify_layout.py`。
