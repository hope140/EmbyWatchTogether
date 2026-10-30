# Watch Together 路线图

当前正式版与测试版见[发布日志索引](releases/README.md)，详细实施状态见 [`development-roadmap.md`](development-roadmap.md)。

## 已整合能力

- Phase A-C：双人同步观看、房间生命周期和一次性邀请。
- Phase D：Media Handoff。主用户换片时，参与者确认打开同一 Item 后再进入 Barrier。
- Phase E：参与者原会话短暂缺失时的有限恢复，以及持续明显漂移的低频保守重新对齐。
- “房间 / 创建与邀请 / 设置”三页保留诊断、脱敏导出、更新通道和 GitHub Token 状态；普通用户可访问房间与邀请。
- 界面沿用 Emby 原生标签和控件，并保留房间操作语义。

## 当前交付状态

原生控件、同步稳定性整合、文档受众整理和 stable 签名发布已完成。README 提供用户入口，开发者从 [`docs/README.md`](README.md) 定位实现、构建和维护资料。后续观察以真实客户端反馈为依据；多跳集诊断按现场证据记录，不把日志能力写成根因修复。

## 长期边界

当前路线不包含设备迁移、Session migration、Primary transfer、连续追帧式 Seek、多人房间或跨服务器同步。

## 无限期延期

2026-10-19 用户决定以下两项无限期延期，暂不排期；恢复需另行确认。

- 可选的 strict-follow 模式。
- 多人同步观看。
