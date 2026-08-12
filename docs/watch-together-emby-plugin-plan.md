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

程序集目标框架为 `netstandard2.0`，项目版本为 `1.3.0.4`，NuGet 依赖是 `MediaBrowser.Server.Core` `4.9.0.52-beta`。版本号命名和递增以 [`docs/versioning.md`](versioning.md) 为准；C# 行为、公共 API 和当前版本值不由本文档改变。

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
- `SessionSelector` 为每位已加入用户选择当前有效会话，过滤停止/陈旧记录，并尽量保留两端共同 Item；后续同步继续绑定所选 session identity。
- `RoomManager` 管理房间元数据和每个房间的 `RoomRuntime`；只有已知房间才会创建 runtime，`RoomStore` 将房间元数据写入插件数据目录的 `rooms.json`。
- `SyncEngine` 是轮询驱动的状态机；每个房间通过独立 gate 串行处理，单房间异常被记录并隔离；`WatchTogetherService` 提供管理页使用的 REST API，并在服务端再次校验权限。

## 3. 房间、资格和运行时状态

### 房间约束

一个房间必须有两名不同的参与者和一名主用户，主用户必须是参与者之一。每名用户不能同时属于其他房间。创建房间时两名参与者默认标记为已加入；参与者可以在管理页执行加入或退出。

房间保存 `ServerId`、名称、管理员、主用户、参与者、加入状态和创建时间。写入时先生成候选文件，再使用 `File.Replace` 替换现有 `rooms.json` 并保留 `.bak` 备份；损坏文件会报告错误，不会静默覆盖。运行时的上一轮快照、Pending 命令、Suppressed 窗口、Barrier 阶段和错误冷却不写入 `rooms.json`；插件重启后会从 `Waiting` 重新开始。重复 `Leave` 不改变成员状态，也不会重复触发暂停。

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

播放开始、进度、停止、会话开始/结束和能力变化事件只调用 `RequestImmediatePoll`，具体状态仍由同步线程读取会话快照确认。配置事件会更新 `PollIntervalSeconds`、`PauseOtherOnPlaybackStop` 和 `NotifyOtherOnPlaybackStop`，并唤醒等待中的循环，因此保存后下一轮轮询即可看到新策略。内部唤醒事件会合并突发通知，轮询间隔仍是兜底，不会因为事件风暴创建多个线程。

## 5. 起播 Barrier

Barrier 的三个阶段按顺序执行，每条远程命令都等待 SessionInfo 确认：

### Pause

向双方发送 `Pause`，直到两端快照都显示暂停。确认后重新读取主用户位置，避免暂停命令传播期间锚点继续前进。

### Seek

只向非锚点用户发送 `Seek`，目标为 Pause 确认后重新读取的锚点位置。锚点和另一端都在目标容差内且保持暂停时，才进入 Restore；不会把两端都 Seek 到旧的起始快照。Seek 未确认时保留 Barrier、固定目标和原播放意图，初次发送与重试共享同一绝对预算。若锚点出现不能由当前远程命令解释的明显新位置操作，则显式重建 Barrier；新位置候选绑定当前 session、Item 和暂停/播放意图，这不是周期性追帧。

### Restore

按 Barrier 开始时锚点记录的暂停/播放意图向双方发送 `Pause` 或 `Unpause`。双方状态确认后直接进入 `Watching`，不再执行额外的最终 Seek。

Pending 命令默认等待约 3 秒，Barrier 内允许 1 次重试；仍未确认时错误为 `playback command was not acknowledged`，约 3 秒冷却后在条件仍满足时自动重新开始 Barrier。远程命令和提示消息都支持取消，并有约 5 秒的外部调用超时；引擎停止等待线程结束的时间有 10 秒上限。自动重试提示是尽力发送的消息，消息失败不会阻塞状态机。

## 6. Watching 阶段的同步规则

### 暂停和继续

插件比较当前快照与上一轮快照的 `IsPaused`。同一轮同时检测到明显手动 Seek 和暂停/继续时，Seek 优先，并以该轮最终快照的暂停/播放状态保存 Barrier 的恢复意图；只有没有 Seek 时才传播暂停/继续。Pending 和 Suppressed 防止远端回传再次产生同一命令。

### 手动 Seek 判定

不能直接把两次快照的位置差当成 Seek，因为轮询间隔和播放速率会造成自然位移。当前逻辑先估算预期位置，并用每位用户已观测的命令确认延迟 EMA 提高判定阈值：

```text
threshold = max(4 秒, user_ack_latency_ema) × TicksPerSecond
if previous.IsPaused == current.IsPaused:
    expected = previous.PositionTicks
    if previous 未暂停:
        expected += elapsedSeconds × previous.PlaybackRate × TicksPerSecond
    manualSeek = abs(current.PositionTicks - expected) >= threshold
else:
    natural = [previous.PositionTicks,
               previous.PositionTicks + elapsedSeconds × previous.PlaybackRate × TicksPerSecond]
    manualSeek = current.PositionTicks 在 natural 区间外，且到区间的距离 >= threshold
```

播放速率变化本身不会被误判为 Seek；暂停/继续时自然位置区间内的移动也不会被误判。Pending 或 Suppressed 的远程 `Pause`/`Unpause` 回传会继续抑制同轮位置变化的 Seek 检测。只有明显的单次位置跳变才向另一端发送一次 Seek；长期的小幅速度差不会触发周期性纠偏。刚完成远程 Seek 后 15 秒校准窗口内的小幅回退也不会重复触发 Seek，窗口外则按普通位置跳变判断。

### 不同 Item

两端 `ItemId` 不同即回到 `Waiting`，设置错误“`两位参与者打开了不同视频，暂不发送同步指令`”，不发送跨 Item Seek。若两端都在播放，插件会按安全规则暂停活跃会话；当只能确认一端独自播放时，单人保护不会打断它。两端重新打开相同 Item 后会建立新的 Barrier。

### 停止或退出

只有在 `Watching` 状态才产生持久停止处理。停止判断按以下顺序执行：

1. Emby 的 `PlaybackStopped` 事件只唤醒同步轮询，不直接触发停止副作用。
2. 仅依据 SessionSelector 为参与者选出的当前会话判断停止；同一用户的旧 Session 即使标记 `stopped` 或活动时间更新，也不能覆盖仍有效的当前播放。当前会话标记 `stopped`、离线或缺失时先记录疑似停止时间，异常状态连续达到 2 秒 debounce 后才确认，期间恢复有效快照会清除计时并保持 `Watching`。
3. 位置归零不是停止条件；合法的 seek-to-zero 不会单独触发停止副作用。
4. 仅在停止状态确认的转换上执行副作用，避免每轮重复；`PauseOtherOnPlaybackStop=true` 时暂停仍在线播放的另一方，`NotifyOtherOnPlaybackStop=true` 时向另一方发送文字提示。
5. 清理运行时并回到 `Waiting`，要求双方重新打开同一视频。

Barrier 尚未完成时的离开只取消本次握手，不会被记录成持久的播放停止。

### 会话身份和命令生命周期

`SessionSelector` 选择会话后，`Watching`、Barrier、Pending、Suppressed 和暂停对齐状态都会记录对应的 session identity 与 Item。即使新设备继续播放相同 Item 和位置，只要 session identity 变化也会回到 `Waiting`，不会把旧快照当成手动 Seek。Pending 命令遇到不同会话、不同 Item 或设备重连时直接丢弃，不跨身份确认或重试。

每个房间通过独立 gate 串行完成快照处理和状态迁移；一个房间的异常只记录到该房间并隔离，不会终止同步线程或影响其他房间。远程命令和消息调用带取消与约 5 秒超时，停止引擎等待后台线程的上限为 10 秒。

## 7. 设置页和 REST API

### 管理页

嵌入式页面显示房间创建表单、房间状态卡片、加入/退出、暂停、继续、重新同步和删除操作。每个房间卡片维护独立的操作反馈和忙碌状态，5 秒轮询只刷新状态，不覆盖操作结果；成功提示约 8 秒后清除，错误提示保留到下一次同房间操作或手动刷新。`StatusReason` 会映射为安全、可执行的中文说明，不直接显示后端错误文本。

页面还显示三个设置复选框：

重新同步操作会先确认“会暂时暂停双方并重新对齐”；删除成功结果显示在页面顶部的短提示区，避免房间卡片消失后反馈不可见。

| 配置项 | 默认值 | 当前作用 |
| --- | ---: | --- |
| `PauseOtherOnPlaybackStop` | `true` | 会话快照持续确认停止后暂停另一方 |
| `NotifyOtherOnPlaybackStop` | `true` | 会话快照持续确认停止后发送 DisplayMessage |
| `NotifyOnSyncActions` | `true` | 暂停、继续、进度调整、重新同步、加入、退出、视频不一致和同步完成时向播放端发送约 3 秒提示 |

Emby 负责保存配置；设置页只允许管理员修改。三个设置默认均为 `true`，保存后热更新；提示发送失败不会影响同步状态。`PollIntervalSeconds` 默认 `0.5` 秒并由入口点传给同步引擎。配置事件会把 `PollIntervalSeconds`、`PauseOtherOnPlaybackStop`、`NotifyOtherOnPlaybackStop` 和 `NotifyOnSyncActions` 热更新到同步引擎，并唤醒等待中的循环，保存后下一轮轮询生效。`Enabled`、`MaxRuntimeDifferenceSeconds`、`SeekToleranceSeconds`、`BarrierSeekTimeoutSeconds` 和 `StaleSessionTimeoutSeconds` 仍是配置模型字段，但不作为实时同步策略，关键阈值按本节所述固定策略运行。

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

### 签名正式版流程

正式版检查不是只看程序集版本或 MD5。更新任务会从 GitHub 下载三个固定资产：

- `Emby.Plugins.WatchTogether.dll`
- `EmbyWatchTogether.release.manifest`
- `EmbyWatchTogether.release.manifest.sig`

三个资产可通过各自的 `releases/latest/download/<asset>` 地址发现。manifest 使用严格 UTF-8、无 BOM、LF 换行且无尾部换行或额外空白的 canonical 字段顺序：`schema`、`keyId`、`tag`、`version`、`assetName`、`size`、`sha256`。detached signature 使用 RSA PKCS#1 v1.5 + SHA-256 验签；未知 `keyId` 或任一 canonical 规则不满足时 fail closed。

DLL 校验包含流式 SHA-256、文件大小、程序集名 `Emby.Plugins.WatchTogether` 和程序集版本，并要求它们与 manifest 一致。校验成功后，传给 Emby installer 的 `sourceUrl` 使用 manifest `tag` 的精确地址：

```text
https://github.com/hope140/EmbyWatchTogether/releases/download/<tag>/Emby.Plugins.WatchTogether.dll
```

插件计算的 MD5 只作为 Emby installer 的二次校验，不是签名信任依据。正式版 GitHub Release 固定发布四个资产：DLL、`EmbyWatchTogether.zip`、manifest 和 detached signature；tag 必须与 `Version`、`FileVersion`、`AssemblyVersion` 三项一致。

### 生产 key bootstrap 与 workflow

`src/EmbyWatchTogether/ReleaseTrustStore.cs` 已完成生产 key bootstrap，`ReleaseTrustStore` 包含已审核的公开 `keyId` `prod-2026-08`，并以不可变的 Ordinal 映射提供信任根。首次信任引导版本 `1.2.0.9` 必须人工部署完成信任引导，之后版本方可使用签名自动更新。

1. 使用 `scripts/release/New-ReleaseSigningKey.ps1` 在仓库外生成 RSA 密钥，并审核公钥；
2. 将 `keyId => RSAKeyValue` 映射提交到 `ReleaseTrustStore`；
3. 将匹配的 PKCS#8 base64 私钥放入 GitHub Environment `release` 的 `WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64` secret。

公钥映射是用于验签的公开材料，不需要保密。匹配 Secret 缺失或错误、未知 key 或签名失败时仍然 fail closed。不得把真实私钥、GitHub secret 值、token、本机服务器信息或私人路径写入文档或提交；示例不得包含真实生产私钥或 secret 值。

签名发布相关文件及职责如下：

- `scripts/release/New-ReleaseSigningKey.ps1`：生成仓库外的 PKCS#8 base64 私钥和 `RSAKeyValue` 公钥。
- `scripts/release/Sign-ReleaseManifest.ps1`：校验 DLL 名称/程序集/版本，流式计算大小和 SHA-256，并生成 manifest 与 signature。
- `tests/release-signing.tests.ps1`：验证密钥、canonical manifest、RSA 签名和 DLL 校验。
- `tests/release-workflow.tests.ps1`：验证触发条件、输入、版本检查、固定资产、签名步骤和发布命令。
- `.github/workflows/release.yml`：仅 `workflow_dispatch`，输入 `tag` 和 `key_id`；checkout 对应 tag，校验三项版本，构建、测试、生成测试签名并验证四个固定资产，最后使用 `--verify-tag` 创建 Release；workflow 不部署服务器。

## 9. 验证方案

### 自动化测试

最小验收命令：

```powershell
dotnet build src/EmbyWatchTogether.sln -c Release --nologo
dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal
pwsh -NoProfile -File tests/release-workflow.tests.ps1
pwsh -NoProfile -File tests/release-signing.tests.ps1
git diff --check
```

测试覆盖：

- 房间创建、成员互斥、持久化和服务路由权限；
- 会话选择、远程能力探测和命令工厂；
- Barrier 的 Pause → Seek → Restore、暂停后重新锚定、目标容差、Seek 失败冻结与共享绝对预算；
- Pending acknowledgement、一次重试、失败冷却、Pending 最终未确认与 immediate issue failure 区分，以及消息失败隔离；
- 主用户冲突裁决、同轮 Seek 优先并保留最终播放意图、手动 Seek 去重、长轮询和自然速率差不误判；
- 资格失败原因仅在变化时记录，日志身份摘要使用截短的 session/Item 标识且不记录认证参数；
- 不同 Item 的安全暂停、单人保护、停止检测和重复通知抑制；
- 嵌入式设置页资源和配置默认值；
- 发布密钥生成、canonical manifest、RSA 签名校验和手动发布 workflow 约束。

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
9. 管理页逐房间执行加入、退出、暂停、继续、重新同步和删除，确认反馈不会被轮询清掉；切换三个设置并确认保存摘要显示实际开启/关闭值。

## 10. 排错与残余风险

- `Waiting` 通常表示参与者未加入、Item 不同、媒体时长差过大、播放速率不为 1 或远程控制能力不足；先查看 `/WatchTogether/Rooms/{id}/State` 的 `Eligible`、会话和 `Error`。
- Barrier 错误通常与客户端不确认 Pause/Seek/Unpause 或 SessionInfo 更新滞后有关；冷却结束后会自动重试，也可由管理员执行 `resync`。
- 正常播放期间出现反复跳转时，优先排查其他插件、客户端或遥控器；本实现只有检测到明显单次跳变才发 Seek。
- 停止后的暂停/提示是尽力行为：目标客户端必须支持对应远程命令，消息失败不会阻止房间回到等待状态。
- 房间元数据文件损坏时 `RoomStore` 会报告错误而不会静默覆盖；恢复前请备份 Emby 插件数据目录。
- `ReleaseTrustStore` 只信任已审核的生产公钥；匹配 Secret 缺失或错误、未知 key 或签名失败时所有正式版更新都会 fail closed。首次信任引导版本 `1.2.0.9` 需要人工部署，之后版本方可使用签名自动更新。
- 真实网络、客户端实现和媒体上游可能导致确认延迟或会话短暂缺失，发布前仍需在实际 Emby 环境完成人工验收。

仓库协作与 Stack/Worktree 约定见 [`docs/pr-stack-workflow.md`](pr-stack-workflow.md)。
