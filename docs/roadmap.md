# Watch Together 路线图

当前项目版本目标为 `1.6.1.0`。用户已于 2026-10-18 确认本轮验收通过，当前工作进入 stable 发布阶段；发布结果以主线程的 tag、GitHub Release 和发布后 readback 为准。详细排期见 [`development-roadmap.md`](development-roadmap.md)。

## 已整合能力

- Phase A-C：双人同步观看、房间生命周期和一次性邀请。
- Phase D：Media Handoff。主用户换片时，参与者确认打开同一 Item 后再进入 Barrier。
- Phase E：参与者原会话短暂缺失时的有限恢复，以及持续明显漂移的低频保守重新对齐。
- 管理页诊断、脱敏导出、更新通道和 GitHub Token 状态保留在原服务器单页中。
- 原生控件调整保留原区域、房间布局、宿主内容宽度和既有操作语义。

## 当前发布阶段

S119 已完成 `1.6.1.0` 版本候选准备，S120 正在整理用户入口、开发者入口、历史记录和 Release Notes。主线程随后负责最终构建、tag、签名 Release 与发布后状态同步。真实客户端的具体型号、版本和时长不在本文臆测；多跳集诊断仍按证据记录，不把日志能力写成根因修复。

## 长期边界

当前路线不包含设备迁移、Session migration、Primary transfer、连续追帧式 Seek、多人房间或跨服务器同步。

## 远期候选

- 可选的 strict-follow 模式。
- 观看历史和更丰富的房间界面。
- 多人同步观看。
