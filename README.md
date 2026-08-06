# Emby Watch Together 插件

这是一个运行在 Emby Server 内的双人一起看插件。它通过 Emby 的会话快照和远程控制接口，让同一房间的两位参与者在**起播、暂停/继续、用户手动拖动进度、切换视频和停止播放**这些明确操作上保持一致。

插件的核心原则是：**正常播放不做周期性 Seek**。播放过程中两端存在小幅速度差、网络延迟或 SessionInfo 更新延迟时，插件不会为了追求每一轮完全相同而反复跳转；只有检测到相对于预计播放位置的明显、单次位置跳变，才把它当作用户手动 Seek。

## 当前行为

- 两位参与者打开同一 Item 后，房间自动进入起播 Barrier：暂停两端、以主用户位置为基准对齐另一端、恢复原来的播放状态。
- 主用户负责解决同时发生的冲突；非主用户的暂停/继续和手动 Seek仍可被识别，但不会覆盖主用户在同一轮的操作。
- 正常播放按“上一次快照位置 + 经过时间 × 上一次播放速度”计算预计位置，不执行周期性漂移纠偏。
- 一方手动 Seek 时，只向另一方发送一次 Seek；Pending acknowledgement 和 Suppressed 窗口用于避免命令回环和重复 Seek。
- 一方切换到下一集或其他 Item 时，房间回到 `Waiting`，不跨 Item Seek；检测到两端都在播放时会暂停活跃会话，等待两端重新打开同一 Item。只有一端独自播放时保留单人保护，不会被自动暂停。
- 一方停止或退出视频时，默认暂停另一方并发送文字提示；这两个行为都可以在插件设置页单独关闭。
- 起播命令没有及时被客户端确认时，插件会进行有限次数重试；仍失败则进入等待状态并设置冷却，避免命令风暴。双方再次可用后会自动重新尝试，并向客户端发送“正在自动重新同步，请稍候”。

## 目录

```text
src/EmbyWatchTogether/       插件源码、同步状态机、REST API、Emby 配置页
  SyncEngine.cs               房间轮询与 Barrier/Watching 状态机
  SessionBridge*.cs           Emby 会话快照和远程命令适配
  Room*.cs                    房间持久化、运行时状态和权限边界
  Web/Configuration/          Emby 插件设置页

tests/EmbyWatchTogether.Tests/ 单元测试和同步边界回归测试
scripts/build.ps1             构建、测试、发布 DLL 并生成 ZIP
docs/                         设计和运行说明
reference/python-watch-together/ Python 参考实现，仅用于对照
```

## 构建与测试

要求：可用的 .NET SDK，以及能够还原 Emby `MediaBrowser.Server.Core` 包的 NuGet 源。

```powershell
dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal
dotnet build src/EmbyWatchTogether.sln -c Release --nologo
.\scripts\build.ps1 -Configuration Release
```

打包脚本会把插件发布到 `dist/EmbyWatchTogether/Emby.Plugins.WatchTogether.dll`，并生成 `dist/EmbyWatchTogether.zip`。插件按单 DLL 交付，DLL 应直接放在 Emby 的 `plugins` 目录中，不要再套一层插件子目录。更新后重启 Emby Server，使入口点和设置页重新加载。

## Emby 设置

在 Emby 管理后台的插件页面打开 **Watch Together**。配置由 Emby 保存，修改后点击保存即可；同步服务会在插件入口点重启时读取配置。

| 配置项 | 默认值 | 作用 |
| --- | ---: | --- |
| `Enabled` | `true` | 是否启用插件功能 |
| `PollIntervalSeconds` | `0.5` | 会话轮询间隔；只影响检测响应速度，不会启用周期性漂移 Seek |
| `PauseOtherOnPlaybackStop` | `true` | 一方停止/退出视频时暂停另一方 |
| `NotifyOtherOnPlaybackStop` | `true` | 一方停止/退出视频时向另一方发送文字提示 |
| `MaxRuntimeDifferenceSeconds` | `3` | 起播时允许的媒体时长差 |
| `SeekToleranceSeconds` | `2` | 远程 Seek acknowledgement 的容差 |
| `BarrierSeekTimeoutSeconds` | `10` | 兼容配置字段；起播 Barrier 的实际命令确认仍受有限重试和超时保护 |
| `StaleSessionTimeoutSeconds` | `60` | 会话陈旧判断配置字段 |

“停止/退出行为”两个开关位于设置页下方，和播放行为放在同一组。默认值为：暂停另一方、发送文字提示。

## 房间生命周期

房间需要两名参与者和一名主用户。房间状态为：

- `Waiting`：等待双方加入、上线或打开同一 Item；不同 Item 也停留在此状态。
- `Barrier`：起播或失败恢复的暂停—Seek—恢复握手。
- `Watching`：双方已经完成起播，对手动播放操作做单次传播。
- `Unavailable`：房间属于另一台 Emby Server，当前实例不负责处理。

房间数据保存在插件数据目录的 `rooms.json` 中；轮询中的 Pending、Suppressed、上一轮快照等运行时状态只保存在内存，插件重启后会重新等待并建立 Barrier。

## 同步规则

### 起播

当两位参与者都在线、具备远程控制能力并打开同一 Item 时：

1. 向双方发送 Pause；
2. 等两端快照确认暂停；
3. 重新读取主用户位置，避免暂停命令传播期间主用户继续前进造成旧锚点；
4. 向非主用户发送 Seek；
5. 恢复双方起播前的暂停/播放状态；
6. 必要时做一次起播最终对齐，然后进入 `Watching`。

每条命令都有确认、超时、有限重试和失败冷却。确认失败不会在每轮无限发送相同命令。

### 播放、暂停和继续

`Watching` 状态只响应快照中观察到的状态变化。主用户优先作为冲突裁决者；向另一端发送 Pause 或 Unpause 后，Pending/Suppressed 机制会阻止远端回传造成重复控制。

### 手动 Seek

插件不会用“两次快照的原始位置差”判断 Seek，而是先估算：

```text
预计位置 = 上一次位置 + 经过时间 × 上一次播放速度
```

只有当前快照相对预计位置发生明显单次跳变（默认阈值 5 秒）才判定为手动 Seek。这样可以避免长轮询间隔、网络延迟和不同播放速度造成误判。对自然播放速度差异不做周期性纠偏，因此不会频繁跳转。

### 切换下一集或打开不同视频

不同 Item 之间不发送 Seek。房间回到 `Waiting`，并在双方都处于播放状态时暂停活跃会话，等待双方打开同一 Item 后重新进入 Barrier。若当前只有一个在线播放者，单人保护生效，不会因为等待另一人而暂停它。

### 停止或退出

在 `Watching` 状态下，以下情况会被视为停止/退出：会话消失、会话标记为 stopped，或位置从较大值重置到接近零。房间会回到 `Waiting`，默认暂停仍在播放的一方，并向其发送“对方已停止播放，请重新打开视频”。通知和暂停均可独立关闭。

起播 Barrier 中的会话退出只会取消本次 Barrier，不会错误地产生持久的“播放已停止”状态。

## REST API

插件服务注册以下接口，管理页使用这些接口：

- `GET /WatchTogether/Users`
- `GET /WatchTogether/Rooms`
- `POST /WatchTogether/Rooms`
- `DELETE /WatchTogether/Rooms/{id}`
- `GET /WatchTogether/Rooms/{id}/State`
- `POST /WatchTogether/Rooms/{id}/Join`
- `POST /WatchTogether/Rooms/{id}/Leave`
- `POST /WatchTogether/Rooms/{id}/Action`：支持 `resync` 等房间控制
- `POST /WatchTogether/Rooms/{id}/Message`

接口权限由服务端校验：管理员可管理房间，参与者只能读取和操作自己参与的房间。

## 已知限制

- 同步依赖 Emby 客户端实际暴露的远程控制能力。不同客户端对 Pause、Seek 和 DisplayMessage 的支持和确认延迟可能不同。
- 插件只协调同一 Emby Server 上的两位用户，不负责跨服务器或多人房间。
- SessionInfo 是轮询快照，不是播放器内部时钟；本实现优先避免打断播放，因此不会保证播放期间每一帧都完全相同。
- 真实客户端、不同网络线路和 STRM/CMS 场景仍需在目标 Emby 环境进行人工验收。

## 相关文档

- `docs/watch-together-emby-plugin-plan.md`：当前实现的架构、状态机、验收和排错说明。
- `docs/pr-stack-workflow.md`：本仓库的 Stack/Worktree 协作流程。
