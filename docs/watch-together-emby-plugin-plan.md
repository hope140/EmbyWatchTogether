# Watch Together 插件：当前实现说明

> 本文档描述 `codex/emby-plugin-watchtogether` 分支当前实际代码，不是早期设计草案。同步策略以“实际播放不频繁跳转”为第一优先级：只处理用户明确操作或安全状态转换，不在正常播放期间持续纠偏。

## 1. 范围与边界

### 目标

- 在同一 Emby Server 内协调一个房间的两位用户；
- 起播时将两端放到同一 Item 和同一时间位置；
- 对暂停/继续、手动 Seek、切换 Item、停止/退出做安全的单次传播；
- 在命令未确认时有限重试，并在失败后冷却，避免命令风暴；
- 通过 Emby 插件设置页保存停止行为配置。

### 不做

- 不进行跨服务器同步；
- 不支持多人房间；
- 不在普通播放期间持续比较两端位置并周期性 Seek；
- 不把自然播放速度差异、轮询延迟或 SessionInfo 抖动当作用户手动 Seek；
- 不依赖主机或参与者运行外部 Python/etlp 进程。

## 2. 组件关系

```text
Emby SessionManager
        │
        ▼
SessionBridge ──> SessionSnapshotProvider ──> SyncEngine
        │                                      │
        └──> CommandIssuer <── Pending/Retry ──┘

WatchTogetherService ──> RoomManager ──> RoomStore (rooms.json)
Web/Configuration ─────> Emby PluginConfiguration
```

- `SessionBridge`：适配 Emby 的 SessionInfo、会话事件和 DisplayMessage/远程命令。
- `SessionSnapshot`：同步状态机使用的不可变会话快照，包含 Item、位置、暂停状态、播放速度、停止标记和能力信息。
- `SessionSelector`：按参与者选择当前活跃会话，并在存在唯一共同 Item 时优先保留共同 Item，减少 Emby 短暂保留旧会话造成的误判。
- `RoomManager`：维护房间持久化数据和每个房间的 `RoomRuntime`。
- `SyncEngine`：按轮询和事件唤醒推进 `Waiting`、`Barrier`、`Watching`。
- `WatchTogetherService`：提供管理页所需的房间/用户/控制 REST API。

## 3. 状态与运行时数据

`RoomState` 只有四种实际状态：

| 状态 | 含义 | 进入方式 |
| --- | --- | --- |
| `Waiting` | 未满足同步条件，或上一轮同步失败 | 初始、不同 Item、会话退出、命令失败 |
| `Barrier` | 起播/恢复的暂停、Seek、恢复握手 | 双方在线且同一 Item |
| `Watching` | 起播完成，监听用户操作 | Barrier 完成 |
| `Unavailable` | 当前实例不是房间归属服务器 | 服务器 ID 不匹配 |

`RoomRuntime` 保存：

- `Previous` 和 `PreviousAtUtc`：上一轮快照及采样时间；
- `Pending`：等待 Emby 快照确认的远程命令；
- `Suppressed`：命令确认后的短暂抑制窗口，防止远端状态回传再次触发同一命令；
- `Barrier`：起播阶段、锚点位置和各阶段是否已发命令；
- `BarrierRetryAtUtc`：失败后的自动重试冷却。

这些运行时字段不写入 `rooms.json`，插件重启后房间回到等待并重新建立 Barrier。

## 4. 轮询主流程

每轮 `PollOnce` 大致按以下顺序执行：

1. 校验房间所属 Server；
2. 拉取并选择每位参与者的会话快照；
3. 识别正在 Watching 的停止/退出；
4. 观察 Pending 命令是否确认、超时或需要一次重试；
5. 判断双方是否在线、能力足够且打开同一 Item；
6. 根据当前状态推进 Watching 或 Barrier；
7. 生成房间结果供 REST API 和管理页显示。

Session 事件会调用 `RequestImmediatePoll`，同时保留轮询间隔作为兜底。`AutoResetEvent` 会合并连续唤醒，避免事件突发产生轮询风暴。

## 5. 起播 Barrier

Barrier 只在两位参与者满足以下条件时开始：

- 房间有两名参与者；
- 两端有在线会话；
- ItemId 相同；
- 媒体时长差在允许范围内；
- 会话具备可用的远程控制能力。

阶段：

### Pause

向双方发送 Pause。等待快照确认两端均暂停；确认后重新读取主用户位置，因为暂停命令传播期间主用户可能前进。

### Seek

向非主用户发送主用户位置。只发送一次；确认位置在 `SeekToleranceTicks` 内后进入 Restore。

### Restore

按 Barrier 开始时各端的 `PrimaryPaused` 状态恢复 Pause 或 Unpause。恢复完成后，如果仍有超过 `StartupAlignToleranceTicks` 的起播偏差，最多做一次最终对齐。

### 失败与重试

每条命令通过 `PendingCommand` 等待确认。超时后允许一次重试；再次失败进入 `Waiting`，写入 `playback command was not acknowledged`，并设置自动重试冷却。冷却结束且双方仍可用时重新开始 Barrier，同时尽力向双方发送自动同步提示。消息失败不会阻塞状态机。

## 6. Watching：只处理明确操作

### 暂停/继续

比较当前快照和上一轮的 `IsPaused`。发现变化后选择控制源：主用户优先；若主用户没有变化，则采用先观察到的参与者。向另一端发送相同的 Pause/Unpause。Pending 和 Suppressed 防止命令回环。

### 手动 Seek 判定

不能直接使用：

```text
abs(current.PositionTicks - old.PositionTicks)
```

因为轮询间隔可能变长，且不同客户端的自然播放速度可能不同。当前使用：

```text
expected = old.PositionTicks
if old 未暂停:
    expected += elapsedSeconds * old.PlaybackRate * TicksPerSecond
manualSeek = abs(current.PositionTicks - expected) >= DriftThresholdTicks
```

默认 `DriftThresholdTicks` 为 5 秒。这样：

- 正常播放跨越一次较长轮询间隔，不会被当作 Seek；
- 稳定的播放速度差异，不会触发周期性 Seek；
- 用户明显向前或向后拖动，仍会触发一次对另一端的 Seek；
- 已发送但未确认的 Seek 不会反向覆盖，也不会重复发送。

这里的设计取舍是“少打断播放”而不是“每轮强行对齐”。如果两端长期存在小幅自然速度差，插件不会主动跳转修正。

### 切换下一集或不同 Item

一旦两个快照的 ItemId 不同：

1. 不发送 Seek；
2. 当前 Watching/Barrier 回到 Waiting；
3. 对仍在播放的在线会话执行 Pause（若只能确认一端独自播放，则单人保护不暂停）；
4. 等待双方重新打开同一 Item，再启动新的 Barrier。

不同 Item 的错误提示为“ 两位参与者打开了不同视频，暂不发送同步指令 ”。这是一种安全停机，而不是跨集追赶。

## 7. 停止/退出

只在 `Watching` 状态判定持久化停止：

- 快照缺失或离线；
- `Stopped == true`；
- 同一 Item 的位置从明显非零值重置到接近零。

处理步骤：

1. 仅在从未处理过停止的转换上执行副作用；
2. 按 `PauseOtherOnPlaybackStop` 暂停仍在线播放的一方；
3. 按 `NotifyOtherOnPlaybackStop` 发送文字提示；
4. 清理运行时并回到 `Waiting`，要求双方重新打开同一视频。

Barrier 中的离开不会走持久化停止路径，因为此时视频尚未完成起播；它只取消 Barrier，避免错误地卡在播放已停止状态。

## 8. 配置与管理页

`PluginConfiguration` 是 Emby 持久化配置。当前设置项：

- `Enabled = true`
- `PollIntervalSeconds = 0.5`
- `PauseOtherOnPlaybackStop = true`
- `NotifyOtherOnPlaybackStop = true`
- `MaxRuntimeDifferenceSeconds = 3`
- `SeekToleranceSeconds = 2`
- `BarrierSeekTimeoutSeconds = 10`
- `StaleSessionTimeoutSeconds = 60`

管理页把“停止或退出视频时暂停另一方”和“发送文字提示”放在同一组，并默认勾选。设置通过 Emby 的插件配置 API 读写，不额外维护第二份配置文件。

## 9. REST API 与权限

服务路由：

- `GET /WatchTogether/Users`
- `GET/POST /WatchTogether/Rooms`
- `DELETE /WatchTogether/Rooms/{id}`
- `GET /WatchTogether/Rooms/{id}/State`
- `POST /WatchTogether/Rooms/{id}/Join`
- `POST /WatchTogether/Rooms/{id}/Leave`
- `POST /WatchTogether/Rooms/{id}/Action`
- `POST /WatchTogether/Rooms/{id}/Message`

管理员可以创建、删除和控制房间；参与者可以读取并加入/离开自己的房间。服务端再次校验身份，不把管理页的权限判断当作安全边界。

## 10. 测试与验收

### 自动化验证

```powershell
dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal
dotnet build src/EmbyWatchTogether.sln -c Release --nologo
git diff --check
```

测试重点：

- Barrier 的暂停、重新锚定、Seek、恢复和最终对齐；
- 延迟确认、一次重试、自动重试冷却和消息失败隔离；
- 主用户暂停/继续、手动 Seek 去重和 Pending acknowledgement；
- 长轮询正常播放不误判 Seek；
- 不同播放速度不触发周期性 Seek；
- 不同 Item 不 Seek，并暂停活跃会话；
- 单人保护；
- 播放停止检测和重复通知抑制；
- 起播前离开不会产生持久停止错误。

### 人工验收

在目标 Emby Server 上至少验证：

1. 两个支持远程控制的客户端打开同一视频，起播完成后连续播放 10 分钟，不应出现自动跳转；
2. 主用户暂停/继续，另一端跟随且不来回切换；
3. 任一端手动前进/后退，另一端只跳转一次；
4. 一端切换下一集，双方进入等待且不会把上一集 Seek 到下一集；
5. 一端停止或退出，按配置决定是否暂停和提示；
6. 起播命令未确认时客户端能看到自动重试提示，且不会无限刷命令；
7. 在有网络延迟、STRM 和 CMS 的情况下观察 SessionInfo 更新是否稳定。

## 11. 排错建议

- 先看房间状态和 `Error` 字段，再判断是未满足条件、不同 Item 还是命令未确认；
- 起播失败优先检查两端是否真正打开同一 Item、会话是否支持远程控制、Emby 是否返回了更新后的 SessionInfo；
- 出现跳转时记录发生时间、两端 ItemId、位置、暂停状态、播放速度和轮询间隔；
- 如果是自然播放期间的频繁跳转，应优先检查是否有其他代码/客户端在发 Seek，而不是通过降低阈值强行修复；
- 如果是暂停很久后恢复卡住，需同时检查 Emby 播放会话是否仍存在、客户端是否重新建立流，以及 STRM/CMS 上游是否能继续提供 Range 请求。

## 12. 发布与回滚

构建产物是单 DLL ZIP。升级前保留旧 DLL；发生回归时停止 Emby，恢复上一版 DLL 后再启动。不要把本地测试输出、`.publish`、`dist` 或包含凭据的配置文件提交到仓库。
