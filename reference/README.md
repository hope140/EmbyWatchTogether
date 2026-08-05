# Python 参考实现（只读蓝本）

本目录是从 `watch_together` 分支 `530932a` 原样复制的 Python 参考实现，用于 C# 插件
移植时的行为对照与测试用例翻译。**此目录不可运行**：源码依赖 etlp 仓库的
`utils/configs.py`、`utils/players.py`、`utils/net_tools.py` 等模块，这些依赖
不随迁到本分支（本分支已完全脱离 toLocal）。

## 内容

- `utils/watch_together_client.py`：客户端侧 WebSocket 连接、能力声明、进度上报、命令处理
- `utils/watch_together_coordinator.py`：房间协调与同步状态机（移植蓝本）
- `utils/watch_together_store.py`：房间 JSON 持久化
- `utils/emby_session_api.py`：Emby Session 命令封装（Pause/Unpause/Seek/Message 等）
- `tests/`：对应单元测试（翻译为 C# 测试用例的蓝本）
- `scripts/package_watch_together.ps1`：原 etlp 打包脚本（仅参考打包思路）

## 约定

- 本目录文件不得修改；需要更新时从 `watch_together` 分支对应 commit 重新复制。
- 关键移植参数：轮询间隔 1s；媒体时长差阈值 `MAX_RUNTIME_DIFFERENCE_TICKS = 3s`；
  seek 容差 2s；状态机 `waiting → syncing → paused → ended`。
