# Watch Together 的 Emby 插件方案（构想）

> 本文档是独立构想，供另一个会话按此实现。当前仓库 `watch_together` 分支已有可运行的 Python 参考实现，本文档描述将其解耦为 Emby 服务端插件的目标架构、协议要点、拆解顺序、验收标准与风险。

## 目标

把"同 Emby 服务器下双人同步观看"（管理员建房间、拉入用户、两人打开同一视频判定开始、同步暂停/播放/进度）从 embyToLocalPlayer（Python/toLocal）解耦，做成 Emby 服务端插件。所有参与方（包括主机）使用支持 Emby 会话远程控制的客户端（已实测小秘版 Windows 客户端在控制台可控制暂停/播放/消息），不再需要 toLocal、mpv 代理和油猴脚本。

## 现状（Python 参考实现）

Python 版已在 `watch_together` 分支跑通并验证：

- `utils/watch_together_client.py`：客户端侧（WebSocket 连接、能力声明、进度上报、命令处理）
- `utils/watch_together_coordinator.py`：房间协调与同步状态机（约 1500 行，可作移植蓝本）
- `utils/emby_session_api.py`：Emby Session 命令封装（Pause/Unpause/Seek/Message 等）
- `utils/watch_together_store.py`：房间 JSON 持久化
- `user_script/embyToLocalPlayer.user.js`：嵌入 Emby 网页的 UI
- `scripts/package_watch_together.ps1`：打包脚本

已验证：同服两台 mpv 同步暂停/播放可用；单人房间播放不会误暂停；控制台远程控制能力存在但 Python 侧当前未占用。

局限：依赖 etlp 常驻、需要嵌入式 Python、参与者也要装 toLocal、只支持 mpv/IINA。

## 目标架构

### Emby Server 插件（C#/.NET）

- RoomManager：房间 CRUD、邀请、成员、状态机（等待中 → 同步中 → 暂停 → 结束）
- SyncEngine：以发起人为基准的 leader-follower 同步；漂移超阈值向跟随者发 Seek；暂停/播放广播
- SessionBridge：封装 ISessionManager 下发命令、订阅播放事件
- RoomStore：JSON 持久化
- ApiController：REST API（房间创建/加入/状态/控制命令/消息）
- Web UI：插件管理页面（房间列表、创建、邀请、状态、控制按钮、消息输入）

### 客户端侧

官方 Emby Windows / 小秘版等任何支持控制台遥控的客户端，零安装。

## 协议要点（目标 Emby 4.9.0.60）

- 下发：`POST /Sessions/{Id}/Playing/{Command}`（Pause/Unpause/PlayPause/Seek/Stop）、`/Sessions/{Id}/Command`、`/Sessions/{Id}/Message`
- 状态收集：WebSocket 的 PlaybackStart/PlaybackProgress/PlaybackStopped 事件，或 SessionManager 事件
- 能力探测：Session Capabilities 的 SupportsRemoteControl、SupportedCommands（含 Pause/PlayPause/Seek/DisplayMessage）
- 同视频判定：同一房间内两人会话 NowPlayingItem 的 Id 相同，且房间状态为等待中 → 判定开始

## 实现拆解（建议顺序）

1. 插件骨架 + 能力探测
2. SessionBridge + 命令封装（移植 emby_session_api.py 语义）
3. RoomManager + 状态机（移植 coordinator 判定与防抖逻辑）
4. SyncEngine（暂停/播放/seek/漂移阈值/单人保护）
5. API + 持久化
6. Web UI（房间管理页 + 参与者视图 + 消息框）
7. 打包与安装（插件 DLL 放入 plugins 目录）

## 验收标准

- 管理员建房间、拉入两名用户；两人用小秘版打开同一视频 → 自动判定开始
- 任一方暂停/播放，另一方跟随；差距超阈值自动 seek 对齐
- 插件页面可对任一成员发送暂停/播放/文字消息
- 单人房间播放不出现自动暂停
- 双方退出后房间状态正确回收，无残留会话
- 主机与参与者机器均不运行 etlp、不安装 Python

## 风险与开放问题

- 小秘版/官方客户端的 DisplayMessage、Seek 行为需实测（部分客户端消息不弹窗、seek 会短暂重载画面）
- Emby 插件 SDK 兼容性：需按 4.9.0.60 确认 NuGet 包版本与 TargetFramework；未来 Emby 5.x 可能破坏接口
- 插件原生无法直接嵌入主界面详情页按钮，UI 入口形态需定：独立页面 + 快捷入口，或保留轻量油猴入口（业务仍在插件）
- 建房间者需为管理员，权限校验在服务端做
- 多人房间、跨服务器同步本期不做

## 起点

- 本分支 `codex/emby-plugin-watchtogether` 即插件开发分支
- 参考实现：`watch_together` 分支的 `utils/watch_together_coordinator.py` 与 `utils/emby_session_api.py`
- 流程规范：仓库 `AGENTS.md` 与 `docs/pr-stack-workflow.md` 的 PR Stack 工作流
