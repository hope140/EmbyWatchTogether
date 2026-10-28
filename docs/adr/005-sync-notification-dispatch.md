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
