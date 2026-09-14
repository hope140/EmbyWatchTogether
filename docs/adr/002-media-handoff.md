# ADR-002: 将主用户媒体切换与时间轴对齐分层

- 状态: accepted
- 日期: 2026-09-14
- 相关组件: SessionBridge、IPlayItemIssuer、SyncEngine、RoomRuntime、Barrier、Participant Resync

## 背景

Watch Together 的 `Barrier` 已负责双方位于同一 Item 后的 Pause → Seek → Restore。主用户切换到下一集时，参与者可能仍停留在旧 Item，直接进入 Barrier 会把跨 Item 问题误当成时间轴偏差。

## 问题

媒体切换需要一个独立、可确认、有界的运行时流程，同时保留现有 Watching 阶段不周期性 Seek、参与者不反向控制 Primary 以及停止副作用的安全边界。

## 可选方案

1. 把 PlayItem 作为 Barrier 的一种 Pending 命令。
2. 在 SyncEngine 外增加播放列表或下一集推导服务。
3. 增加独立的 Media Handoff runtime，确认 Item 后复用现有 Barrier。

## 最终选择

选择方案 3。`SessionBridge` 使用 Emby 的 `SendPlayCommand` 封装 `SendPlayItemAsync`，通过 additive public `IPlayItemIssuer` 提供给同步引擎。`RoomRuntime` 保存临时 `MediaHandoffState`，绑定目标 Item、Primary/participant Session identity 和 generation。只有当前 participant 用户、Session 和 `SessionSnapshot.ItemId` 均确认目标 Item 后，才清除 Handoff 并进入 Barrier。

主用户的 Item 变化触发 Handoff；participant 自行换片仍进入安全 Waiting。显式 Participant Resync 只作用于非 Primary participant，即使请求由 Primary 发起也不会向 Primary 发送 PlayItem。PlayItem 发送使用取消和 5 秒外部超时，确认等待与重试有界；目标或 Session identity 变化会使旧 generation 失效，最终失败回到 Waiting。

## 选择原因

- 保持 Barrier 对时间轴同步的单一职责，不跨 Item Seek。
- 通过快照确认区分“命令已发出”和“播放器已打开目标媒体”。
- Session identity、generation 和有限重试阻止迟到的旧目标污染新 Handoff。
- 新增接口不改变既有 `ICommandIssuer` 方法签名，也不改变房间持久化格式。

## 已知代价

- Emby 客户端对 PlayItem 的支持和 SessionInfo 更新时序仍需真实客户端验证。
- Handoff runtime 只驻留内存，服务重启后需要重新建立同步。
- 当前 Emby API 的 PlayRequest ItemIds 由 SessionBridge 按其 API 类型转换，非法目标会安全失败。

## 后续影响

维护 Handoff 时不得引入周期性漂移 Seek、播放列表同步、跨服务器或自动 Primary 转移。诊断只输出 Item/Session 短 hash、participant alias、稳定错误和有限事件，不输出原始身份或底层异常。

## 验证依据

- [SessionBridge.cs](../../src/EmbyWatchTogether/SessionBridge.cs)
- [ICommandIssuer.cs](../../src/EmbyWatchTogether/ICommandIssuer.cs)
- [RoomState.cs](../../src/EmbyWatchTogether/RoomState.cs)
- [SyncEngine.cs](../../src/EmbyWatchTogether/SyncEngine.cs)
- [SyncEngineTests.cs](../../tests/EmbyWatchTogether.Tests/SyncEngineTests.cs)
- [SyncDiagnosticsTests.cs](../../tests/EmbyWatchTogether.Tests/SyncDiagnosticsTests.cs)
