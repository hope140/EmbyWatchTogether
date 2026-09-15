# Watch Together 路线图

## 已完成

- Phase A-C：双人同步观看基础功能、房间生命周期和邀请流程。
- Phase D：Media Handoff。主用户切换媒体时，参与者确认打开同一 Item 后再进入 Barrier。

## 当前阶段

### Phase E — Playback Stability

- Transient same-session recovery：`Watching` 中原绑定 Session 短暂消失时，最多等待 10 秒；只有原 User、原 SessionId 和原 ItemId 同时恢复，才经过 Barrier 回到 `Watching`。
- Drift telemetry：仅在双方同 Item、actively playing、正常倍速且没有进行中的同步操作时记录时间轴偏移。
- Conservative drift auto repair：绝对漂移至少 3 秒并持续 5 秒时，只触发一次现有 Barrier；自动纠偏之间保持 120 秒冷却。
- 管理页和诊断接口展示 `Recovering`、恢复窗口、预期身份摘要、当前 drift、最大 drift、最近自动纠偏和次数。

## 长期边界

当前路线不包含设备迁移、Session migration、Primary transfer、连续追帧式 Seek、多人房间或跨服务器同步。

## 远期候选

- 可选的 strict-follow 模式。
- 观看历史和更丰富的房间界面。
- 多人同步观看。
