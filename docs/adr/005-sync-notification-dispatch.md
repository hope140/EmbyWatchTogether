# ADR-005：将同步提示移出播放控制线程

- 状态：accepted
- 日期：2026-10-27
- 相关组件：SyncEngine、SyncNotificationDispatcher、SessionBridgeCommandIssuer

## 背景

同步提示原本在房间 gate 内同步发送。自动重试先向两人发送提示，再执行 Barrier 的暂停；提示网络等待会累加到播放控制延迟。独立的发送超时限制单次等待，但不能消除这一顺序依赖。

## 决策

1. 同步引擎的提示只入队，每个引擎最多运行一个发送 worker。等待队列上限为 64，队满丢弃新提示；实际排队时间使用单调时钟，超过 10 秒即丢弃。
2. 每条消息绑定原 Room 引用、用户、SessionId 和 ItemId。发送前重新采样、选择当前会话，在 room gate 内核验当前房间、服务器、成员、快照保护、身份、DisplayMessage 能力及对应通知设置；释放 gate 后执行网络发送。
3. 提示发送和失败均不改变 Pending、Barrier 或其他播放状态。停止时拒绝新消息、清空等待队列、取消在途请求；不等待忽略取消的传输或阻塞的取消回调，资源在退出后释放。
4. 播放控制及 PlayItem 使用 `playback:` 会话槽，DisplayMessage 使用 `message:` 会话槽。前缀只用于内部准入，Emby API 仍接收原 SessionId。两类槽共享每个 issuer 最多 64 个未完成传输的总量限制，同一通道同一会话保留既有迟到任务保护。

## 选择原因

固定 worker 和容量限制使网络故障不会产生无界消息任务；重新授权限制排队消息对旧目标的影响。分开传输槽避免在途提示使同会话的 Pause、Seek 或 PlayItem 被误判为忙碌；总量限制继续约束不协作传输。

## 已知代价

提示可能延后或被丢弃，不构成命令执行确认。网络发送前的校验不能撤回已经进入底层的物理发送。底层忽略取消时，最多占住该引擎的一个提示 worker；播放线程和停止调用继续返回。房间播放控制仍逐个串行处理。

## 验证依据

- [同步引擎](../../src/EmbyWatchTogether/SyncEngine.cs)
- [提示队列](../../src/EmbyWatchTogether/SyncNotificationDispatcher.cs)
- [有界发送器](../../src/EmbyWatchTogether/SessionBridgeCommandIssuer.cs)
- [同步性能回归](../../tests/EmbyWatchTogether.Tests/SyncEnginePerformanceTests.cs)
- [队列回归](../../tests/EmbyWatchTogether.Tests/SyncNotificationDispatcherTests.cs)
- [发送器回归](../../tests/EmbyWatchTogether.Tests/SessionBridgeCommandIssuerTests.cs)

本地测试证明对应的模拟时序和资源边界；真实双客户端播放响应由候选包实机验收确认。

## 2026-10-29 notification policy refinement

Routine pause/seek/resume stage notices are omitted in favor of one completion per participant. Completion and retry notices bind an in-memory alignment operation token and room state; retry also binds its barrier stage and is attempted once per operation. Reset/new Barrier retires the old token. Existing identity, expiry and cancellation checks remain. Already dispatched network work still cannot be recalled. Snapshot freshness protection only handles strictly older LastActivity in stable eligible Watching without Pending; it is not an event sequence or a general replay detector.

## 2026-11-10：停止提示短合并与发送时语义复核

- 停止后的真实 Pause 仍按原 2 秒观察窗执行。仅停止文字增加 0.5 秒合并窗，提示改为“对方已停止播放，同步已暂停”，不在可能自动跟随的过渡期要求用户重新打开视频。合并窗不是送达期限；原有排队、传输等待和丢弃规则仍适用。
- 延迟与立即消息共用 64 条等待容量和原 10 秒单调寿命。每引擎最多一个发送 worker 和一个单次唤醒 Timer；尚未到期的条目让出 worker，不阻塞后续 ready 消息或房间 gate。Stop 释放 Timer、清空等待消息，保留原在途取消规则。
- 停止提示使用独立的内存通知代次，在停止确认的 reset 退休 alignment 代次后仍保留真实停止提醒；重复 stopped poll 不重复提醒。发送时重新检查该代次、Waiting/停止原因、原停止成员仍缺失；进入 Handoff、Barrier 或一般 reset 退休旧停止提示。通知字段不参与播放决策。
- 不同视频提示绑定通知操作和 Waiting 状态，发送时复核原两端 Session/Item 仍成立且仍不同。Handoff 开始退休旧通知操作，避免旧完成提示发给仍留在旧 Item 的一端。
- 完成提示仍只从有效 Watching 入口发出，发送时复核当前两端身份及可同步条件。FromMediaHandoff 使用“已跟随切换，同步完成”；不新增换集开始或阶段提示，不把 PlayItem 传输返回当作成功。原已暂停时仍保持暂停，不改变播放恢复意图、超时或错误指引。
- `HandoffNotificationTests` 使用直接构造的快照和真实 SessionBridge/SDK 发送器验证临界时序、通知开关、正常停止/不同视频、超时、代次和命令序列；`StoppedPlaybackTests.TheaterNotifications_*` 进一步覆盖真实 SDK 的 Stop/Start/Progress、SessionBridgeSnapshotProvider 和 Theater 能力清单。`SyncNotificationDispatcherTests.DeferredEntries_*` 覆盖边界、容量、唤醒和寿命。这些是自动化模拟传输证据，不证明客户端实际展示或用户已看到消息；已进入底层的发送不能撤回。
