# Watch Together 路线图

当前 Stack 状态与统一正式版排期见 [`development-roadmap.md`](development-roadmap.md)。本文说明已整合能力和后续验收，本地整合与已发布版本、NAS 当前运行构建分别记录。

## 已完成

- Phase A-C：双人同步观看基础功能、房间生命周期和邀请流程。
- Phase D：Media Handoff。主用户切换媒体时，参与者确认打开同一 Item 后再进入 Barrier。

## 已完成本地整合

### Phase E — Playback Stability

- Transient same-session recovery：`Watching` 中非 Primary 参与者的原绑定 Session 短暂消失时，最多等待 10 秒；原 User、原 SessionId 和原 ItemId 同时恢复时经过 Barrier 回到 `Watching`。Primary 缺失继续沿用既有 2 秒停止观察和 10 秒晚到换集 Handoff 候选。
- Drift telemetry：仅在双方同 Item、actively playing、正常倍速且没有进行中的同步操作时记录时间轴偏移。
- Conservative drift auto repair：绝对漂移至少 3 秒并持续 5 秒时，只触发一次现有 Barrier；自动纠偏之间保持 120 秒冷却。
- 管理页和诊断接口展示 `Recovering`、恢复窗口、预期身份摘要、当前 drift、最大 drift、最近自动纠偏和次数。

### Emby 原生风格 UI 与诊断

- 保留原服务器单页 HTML/AMD、区域顺序和房间布局，复用宿主 input/select/checkbox/button 控件；管理员和普通用户按原页面规则显示内容。
- 保留 beta 更新通道和 Token 设置；权限与离页行为沿用原服务器页面。
- 换集诊断与 Playback Stability 诊断统一纳入页面及脱敏导出。

## 下一阶段

当前 UI 已获用户初步认可，正式版 `1.6.1.0` 候选在独立分支完成版本与说明整理。发布前继续按清单收口真实双账号、换片、参与者恢复及低频漂移纠偏验收，随后执行正式签名发布；本地验证与生产签名分开记录。

## 长期边界

当前路线不包含设备迁移、Session migration、Primary transfer、连续追帧式 Seek、多人房间或跨服务器同步。

## 远期候选

- 可选的 strict-follow 模式。
- 观看历史和更丰富的房间界面。
- 多人同步观看。
