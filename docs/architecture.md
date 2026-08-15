# 当前架构

本文只描述当前代码已实现的结构；行为细节和人工验收步骤见 [`watch-together-emby-plugin-plan.md`](watch-together-emby-plugin-plan.md)。架构变更应先更新本文，再评估是否需要 [`adr/README.md`](adr/README.md) 中的 ADR。

## 运行时边界

插件目标为 `netstandard2.0`，由 Emby 服务器加载。`Plugin` 暴露插件元数据和嵌入式配置页；`WatchTogetherEntryPoint` 负责启动、配置热更新和停止时释放运行时对象。仓库不依赖外部同步服务。

## 组件与数据流

```text
Emby SessionManager
        │
        ▼
SessionBridge ──> SessionBridgeSnapshotProvider ──> SessionSelector
        │                                                   │
        └──── SessionBridgeCommandIssuer <──────── SyncEngine

Plugin ──> WatchTogetherEntryPoint ──> RoomManager ──> RoomStore (rooms.json)
                                  └──> WatchTogetherService + embedded Web UI
```

- `SessionBridge` 将 Emby 会话和事件适配为快照、命令和立即轮询唤醒。
- `SessionSelector` 为参与者选择有效会话并绑定 session identity，避免旧会话确认新设备命令。
- `RoomManager` 管理房间元数据和每房间 `RoomRuntime`；房间命令、消息和离开后的播放副作用在每房间 gate 内重新校验当前房间、成员关系、服务器和会话身份；`RoomStore` 只持久化房间元数据。
- `SyncEngine` 按轮询驱动每房间状态机，使用独立 gate 串行处理；状态包括 `Waiting`、`Barrier`、`Watching`、`Unavailable`。
- `WatchTogetherService` 提供 REST 管理接口并在服务端检查身份、管理员权限和成员关系；管理页按钮不是安全边界。

## 状态与持久化

起播 `Barrier` 按 Pause → Seek（仅非锚点用户）→ Restore 执行，远程命令等待会话快照确认；进入 Restore 前，锚点和另一端都必须在固定 Seek 目标的容差内。Seek 未确认时保留原目标和原播放意图，并在同一 Barrier 的绝对预算内重试；只有检测到锚点有当前远程命令无法解释的明显新位置操作时，才显式重建 Barrier。正常 `Watching` 期间只传播明确暂停/继续和明显手动 Seek，不做周期性追帧。运行时快照、Pending 命令和 Barrier 阶段不写入 `rooms.json`；房间文件采用候选文件替换并保留备份，损坏时报告错误而不静默覆盖。

## 发布信任边界

更新组件由 `GitHubReleaseClient`、`PluginUpdateManager`、`ReleaseSignatureVerifier` 和 `ReleaseTrustStore` 协作：默认 `stable` 通道从固定的 `releases/latest` 资产读取，管理员选择 `beta` 后先从公开 Releases API 选择最高版本的非 draft prerelease，再构造本仓库规范数字 tag 的固定资产地址；API 返回的下载地址不作为安装来源。两条通道最终都校验 manifest 的版本、大小、SHA-256、tag 与 detached RSA 签名，未知 key、当前插件版本不可读或校验失败时 fail closed；GitHub 资产下载允许其官方 CDN 重定向，但不会因此放宽内容验签。每次检查会使旧的已验证 release 缓存失效。发布 workflow 负责构建和资产发布，不负责服务器部署。

## 证据与维护

上述结论来自 `src/EmbyWatchTogether` 的入口、房间、会话、同步和发布信任实现，以及对应 `tests/EmbyWatchTogether.Tests` 测试。新增跨组件约束时先补测试或文档证据，再更新本文；稳定决策另建 ADR。
