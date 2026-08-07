# Watch Together 插件：当前实现说明

本文档说明仓库中独立 C# Emby 插件的实际行为、运行边界和验收方式。插件的取舍是“少打断播放”：正常播放只根据会话快照识别用户明确操作，不在每一轮轮询中强制把两端位置拉齐。

## 1. 范围和非目标

### 支持

- 同一台 Emby Server 上的双人房间；
- 通过起播 Barrier 对齐相同 Item 的播放位置和暂停状态；
- 在播放中传播一次性的暂停/继续和明显的手动 Seek；
- 处理切换 Item、停止、退出、远程命令确认和失败重试；
- 通过 Emby 插件页管理房间和停止行为设置。

### 不支持

- 跨服务器、多人房间或跨 Item 追赶；
- 以服务器轮询快照替代播放器内部时钟；
- 正常播放期间周期性 Seek 或保证逐帧相同；
- 依赖外部服务、脚本或第二份配置文件。

程序集目标框架为 `netstandard2.0`，项目版本为 `1.2.0.3`，NuGet 依赖是 `MediaBrowser.Server.Core` `4.9.0.52-beta`。C# 行为、公共 API 和版本号不由本文档改变。

## 2. 组件和数据流

```text
Emby SessionManager / SessionInfo
             │
             ▼
SessionBridge ──> SnapshotProvider ──> SessionSelector ──> SyncEngine
      │                                                   │
      └────────────── CommandIssuer <── Pending/Suppressed ┘

Plugin ──> WatchTogetherEntryPoint ──> RoomManager ──> RoomStore (rooms.json)
   │
   └──> WatchTogetherService + embedded configuration page
```

- `Plugin` 是 Emby 发现的插件入口，提供插件 ID、名称和嵌入式管理页。
- `WatchTogetherEntryPoint` 在服务器启动时构造存储、会话桥接和同步线程，在停止时释放它们。
- `SessionBridge` 将 Emby `SessionInfo` 和远程命令适配为插件使用的快照和命令；会话事件会请求立即轮询。
- `SessionSelector` 为每位已加入用户选择当前有效会话，过滤停止/陈旧记录，并尽量保留两端共同 Item。
- `RoomManager` 管理房间元数据和每个房间的 `RoomRuntime`；`RoomStore` 将房间元数据写入插件数据目录的 `rooms.json`。
- `SyncEngine` 是轮询驱动的状态机；`WatchTogetherService` 提供管理页使用的 REST API，并在服务端再次校验权限。

## 3. 房间、资格和运行时状态

### 房间约束

一个房间必须有两名不同的参与者和一名主用户，主用户必须是参与者之一。每名用户不能同时属于其他房间。创建房间时两名参与者默认标记为已加入；参与者可以在管理页执行加入或退出。

房间保存 `ServerId`、名称、管理员、主用户、参与者、加入状态和创建时间。运行时的上一轮快照、Pending 命令、Suppressed 窗口、Barrier 阶段和错误冷却不写入 `rooms.json`；插件重启后会从 `Waiting` 重新开始。

### 进入 Barrier 的资格

两位已加入参与者必须同时满足：

1. 存在在线会话，且会话没有标记为 stopped；
2. `ItemId` 相同，媒体时长都有效且相差不超过 3 秒；
3. 播放速率接近 1（允许约 1% 误差）；
4. 两端应报告远程控制能力；实际发出命令时还会检查 `Pause`、`Unpause`、`Seek`，不支持这些命令的客户端会在 Barrier 失败后回到 `Waiting`。

不满足条件时状态保持 `Waiting`，不会向错误的 Item 发送 Seek。

### 状态表

| 状态 | 含义 | 常见进入方式 |
| --- | --- | --- |
| `Waiting` | 条件未满足、Item 不同或上一轮失败后的安全状态 | 初始、退出、不同 Item、命令未确认 |
| `Barrier` | 暂停—Seek—恢复的起播握手 | 双方在线且资格检查通过 |
| `Watching` | 起播完成，只处理明确的播放操作 | Barrier 完成 |
| `Unavailable` | 房间 `ServerId` 与当前 Emby 实例不一致 | 载入或轮询时发现归属不符 |

## 4. 轮询和事件唤醒

同步线程按 `PollIntervalSeconds` 轮询，默认间隔为 `0.5` 秒。每轮大致执行：

1. 校验房间所属服务器；
2. 拉取会话并按参与者选择快照；
3. 在 `Watching` 中先识别停止/退出；
4. 观察 Pending 命令是否已确认、超时或需要一次重试；
5. 判断双方是否满足相同 Item 和远程控制条件；
6. 推进 Barrier 或处理 Watching 中的用户操作；
7. 生成房间状态供 REST API 和管理页显示。

播放开始、进度、停止、会话开始/结束和能力变化事件会调用 `RequestImmediatePoll`。内部唤醒事件会合并突发通知，轮询间隔仍是兜底，不会因为事件风暴创建多个线程。

## 5. 起播 Barrier

Barrier 的四个阶段按顺序执行，每条远程命令都等待 SessionInfo 确认：

### Pause

向双方发送 `Pause`，直到两端快照都显示暂停。确认后重新读取主用户位置，避免暂停命令传播期间锚点继续前进。

### Seek

只向非主用户发送一次 `Seek`，目标为重新读取的主用户位置。目标位置在 Seek 容差内即视为确认；不会把两端都 Seek 到旧的起始快照。

### Restore

按 Barrier 开始前主用户的暂停/播放状态向双方发送 `Pause` 或 `Unpause`。若原本在播放，恢复后进入最终对齐阶段；若原本暂停，双方状态确认后直接进入 `Watching`。

### FinalAlign

播放状态恢复后，如果两端位置差超过 1 秒，只向非主用户做一次最终 Seek，再等待确认。命令未确认或阶段超时会回到 `Waiting`，记录错误并安排自动重试冷却。

Pending 命令默认等待约 3 秒，Barrier 内允许 1 次重试；仍未确认时错误为 `playback command was not acknowledged`，约 3 秒冷却后在条件仍满足时自动重新开始 Barrier。自动重试提示是尽力发送的消息，消息失败不会阻塞状态机。

## 6. Watching 阶段的同步规则

### 暂停和继续

插件比较当前快照与上一轮快照的 `IsPaused`。主用户和另一端同时变化时，主用户优先；否则采用先观察到的变化，只向另一方发送一次相同的 `Pause` 或 `Unpause`。Pending 和 Suppressed 防止远端回传再次产生同一命令。

### 手动 Seek 判定

不能直接把两次快照的位置差当成 Seek，因为轮询间隔和播放速率会造成自然位移。当前逻辑先估算预期位置：

```text
expected = previous.PositionTicks
if previous 未暂停:
    expected += elapsedSeconds × previous.PlaybackRate × TicksPerSecond
manualSeek = abs(current.PositionTicks - expected) >= 5 秒
```

暂停/继续或播放速率变化本身不会被误判为 Seek。只有明显的单次位置跳变才向另一端发送一次 Seek；长期的小幅速度差不会触发周期性纠偏。

### 不同 Item

两端 `ItemId` 不同即回到 `Waiting`，设置错误“`两位参与者打开了不同视频，暂不发送同步指令`”，不发送跨 Item Seek。若两端都在播放，插件会按安全规则暂停活跃会话；当只能确认一端独自播放时，单人保护不会打断它。两端重新打开相同 Item 后会建立新的 Barrier。

### 停止或退出

只有在 `Watching` 状态才产生持久停止处理。以下情况会被识别：会话消失或离线、快照标记 stopped、同一 Item 的位置从明显非零值重置到接近零。处理步骤是：

1. 仅在进入停止状态的转换上执行副作用，避免每轮重复；
2. `PauseOtherOnPlaybackStop=true` 时暂停仍在线播放的另一方；
3. `NotifyOtherOnPlaybackStop=true` 时向另一方发送文字提示；
4. 清理运行时并回到 `Waiting`，要求双方重新打开同一视频。

Barrier 尚未完成时的离开只取消本次握手，不会被记录成持久的播放停止。

## 7. 设置页和 REST API

### 管理页

嵌入式页面显示房间创建表单、房间状态卡片、加入/退出、暂停、继续、重新同步和删除操作。页面还显示两个停止行为复选框：

| 配置项 | 默认值 | 当前作用 |
| --- | ---: | --- |
| `PauseOtherOnPlaybackStop` | `true` | 停止或退出时暂停另一方 |
| `NotifyOtherOnPlaybackStop` | `true` | 停止或退出时发送 DisplayMessage |

Emby 负责保存配置；设置页只允许管理员修改。`PollIntervalSeconds` 默认 `0.5` 秒并由入口点传给同步引擎。`Enabled`、`MaxRuntimeDifferenceSeconds`、`SeekToleranceSeconds`、`BarrierSeekTimeoutSeconds` 和 `StaleSessionTimeoutSeconds` 仍是配置模型字段，但当前页面不暴露它们，关键阈值按本节所述固定策略运行。

### 服务路由和权限

| 方法 | 路由 | 权限和用途 |
| --- | --- | --- |
| `GET` | `/WatchTogether/Users` | 仅管理员；读取用户选择列表 |
| `GET` | `/WatchTogether/Rooms` | 管理员看全部，参与者看自己参与的房间 |
| `POST` | `/WatchTogether/Rooms` | 仅管理员；创建两人房间 |
| `DELETE` | `/WatchTogether/Rooms/{id}` | 仅管理员；删除房间 |
| `GET` | `/WatchTogether/Rooms/{id}/State` | 管理员或参与者；返回状态、资格、Item 和会话摘要 |
| `POST` | `/WatchTogether/Rooms/{id}/Join` | 房间参与者加入 |
| `POST` | `/WatchTogether/Rooms/{id}/Leave` | 房间参与者退出；退出主用户会暂停在线参与者 |
| `POST` | `/WatchTogether/Rooms/{id}/Action` | 仅管理员；`pause`、`resume`、`resync` |
| `POST` | `/WatchTogether/Rooms/{id}/Message` | 仅管理员；向支持消息的在线会话发送文本 |

所有路由要求 Emby 身份认证，服务端会重新检查管理员策略和房间成员关系。管理页隐藏按钮不是安全边界。

## 8. 安装、构建和发布

### 安装 DLL

发布包是单 DLL。停止 Emby Server 后，将 `Emby.Plugins.WatchTogether.dll` 直接放入服务器数据目录的 `plugins` 目录；不要把 DLL 留在 ZIP 内或额外的插件子目录中。启动/重启后从 **Dashboard → Plugins → Watch Together** 打开设置页。升级前应保留旧 DLL，回滚时停止服务器并恢复它。

### 本地构建

需要 PowerShell、.NET SDK 和可还原 `MediaBrowser.Server.Core` `4.9.0.52-beta` 的 NuGet 源。仓库根目录执行：

```powershell
dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal
dotnet build src/EmbyWatchTogether.sln -c Release --nologo
```

### 发布脚本和产物

```powershell
.\scripts\build.ps1 -Configuration Release
```

脚本依次执行 Release 构建、测试和 `dotnet publish`，再把发布目录中的单 DLL复制到：

```text
dist/EmbyWatchTogether/Emby.Plugins.WatchTogether.dll
dist/EmbyWatchTogether.zip
```

ZIP 根目录直接包含 DLL。`.publish/`、`dist/` 和编译输出是临时产物，不应提交。

## 9. 验证方案

### 自动化测试

最小验收命令：

```powershell
dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal
dotnet build src/EmbyWatchTogether.sln -c Release --nologo
git diff --check
```

测试覆盖：

- 房间创建、成员互斥、持久化和服务路由权限；
- 会话选择、远程能力探测和命令工厂；
- Barrier 的暂停、重新锚定、Seek、恢复、最终对齐；
- Pending acknowledgement、一次重试、失败冷却和消息失败隔离；
- 主用户冲突裁决、手动 Seek 去重、长轮询和自然速率差不误判；
- 不同 Item 的安全暂停、单人保护、停止检测和重复通知抑制；
- 嵌入式设置页资源和配置默认值。

### 人工验收

在目标 Emby Server 上至少验证：

1. 两个支持 Pause、Unpause、Seek 的客户端加入同一房间并打开同一视频，确认 `Barrier → Watching`；
2. 连续播放约 10 分钟，不应出现由本插件造成的周期性跳转；
3. 主用户暂停/继续，另一端跟随且不来回切换；
4. 任一端前进或后退，另一端只发生一次对应 Seek；
5. 一端切换下一集或不同视频，双方进入等待且没有跨 Item Seek；
6. 一端停止或退出，分别切换两个停止行为开关，确认暂停和消息互相独立；
7. 暂时阻断命令确认，确认只有限重试、进入冷却并能自动恢复；
8. 在网络延迟、直播/STRM 或 CMS 场景观察 SessionInfo 是否稳定，并记录客户端能力差异。

## 10. 排错与残余风险

- `Waiting` 通常表示参与者未加入、Item 不同、媒体时长差过大、播放速率不为 1 或远程控制能力不足；先查看 `/WatchTogether/Rooms/{id}/State` 的 `Eligible`、会话和 `Error`。
- Barrier 错误通常与客户端不确认 Pause/Seek/Unpause 或 SessionInfo 更新滞后有关；冷却结束后会自动重试，也可由管理员执行 `resync`。
- 正常播放期间出现反复跳转时，优先排查其他插件、客户端或遥控器；本实现只有检测到明显单次跳变才发 Seek。
- 停止后的暂停/提示是尽力行为：目标客户端必须支持对应远程命令，消息失败不会阻止房间回到等待状态。
- 房间元数据文件损坏时 `RoomStore` 会报告错误而不会静默覆盖；恢复前请备份 Emby 插件数据目录。
- 真实网络、客户端实现和媒体上游可能导致确认延迟或会话短暂缺失，发布前仍需在实际 Emby 环境完成人工验收。

仓库协作与 Stack/Worktree 约定见 [`docs/pr-stack-workflow.md`](pr-stack-workflow.md)。
