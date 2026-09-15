# ADR-003：Playback Stability 的恢复与漂移策略

- 状态：accepted
- 日期：2026-09-16
- 相关组件：SyncEngine、RoomRuntime、Barrier、SyncDiagnostics、Watch Together 管理页

## 背景

SessionInfo 轮询和客户端连接都可能出现短暂缺失。长期播放时，两端也可能逐步产生小幅时间轴偏移。立即把缺失当作停止，或每轮通过 Seek 追赶，都会把短暂网络波动放大成播放中断和命令风暴。

## 决策

1. `Recovering` 是独立的 transient runtime state，只能从 `Watching` 中已绑定 Session 的短暂缺失进入。
2. 缺失会话恢复只接受同一 User、同一 SessionId、同一 ItemId 的在线会话，并且必须再次经过现有 `Barrier`；同一已绑定 Primary Session 的显式 Item transition 可以取消 Recovery 并复用既有 `Handoff`。
3. Recovery 只驻留内存，不写入 `rooms.json`；超时、替换 SessionId、Participant Item 变化和其他身份变化回到既有安全 `Waiting` 路径。
4. Drift 使用 `Participant.PositionTicks - Primary.PositionTicks` 计算 signed telemetry，同时保留绝对值最大值。
5. 只有绝对 drift 至少 3 秒并持续 5 秒，且双方 actively playing、无 Pending/Handoff/Barrier/Recovering 时，才触发一次现有 Barrier。
6. 不实现 continuous drift chasing；自动纠偏完成或失败后使用 120 秒 time-based cooldown。
7. explicit user action、Handoff、Recovery 和 Barrier 优先于自动纠偏；所有 hold/cooldown 都使用 UTC 时间，不依赖 poll 次数。

## 诊断与安全边界

诊断 DTO 只输出 `userA`/`userB` 别名、SessionId/ItemId 短 hash、位置和稳定时间字段，不输出原始身份。Recovery、drift 和事件环均为有界内存状态，服务重启或房间重建后清空。

## 验证依据

- `src/EmbyWatchTogether/SyncEngine.cs`
- `src/EmbyWatchTogether/RoomRuntime.cs`
- `src/EmbyWatchTogether/SyncDiagnostics.cs`
- `tests/EmbyWatchTogether.Tests/SyncEngineTests.cs`
- `tests/EmbyWatchTogether.Tests/SyncDiagnosticsTests.cs`

Phase E 的真实双客户端、真实媒体和长时间网络环境验收仍由维护者按最终清单执行；自动测试不替代该验收。
