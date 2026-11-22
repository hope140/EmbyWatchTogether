# Emby Watch Together（同映）

中文 · [English](README.en.md)

在同一台 Emby Server 上和另一人一起看视频。两人打开同一视频后，插件会协调暂停、继续和播放进度。插件直接装在 Emby Server 上。

每间房两人，双方使用不同账号，每个账号同时只能参加一个房间。双方的客户端须支持远程暂停、继续和进度跳转，建议使用 1× 播放。

首次安装请选择正式版。在发布页的 **Assets** 中下载 `EmbyWatchTogether.zip`。

**[下载正式版](https://github.com/hope140/EmbyWatchTogether/releases/latest)** · [安装与首播](#安装) · [客户端兼容性](docs/client-compatibility.md)

## 安装

1. 停止 Emby Server。解压正式版安装包，将 `Emby.Plugins.WatchTogether.dll` 直接放入服务器数据目录的 `plugins` 文件夹。
2. 启动 Emby Server，在 **Dashboard → Plugins → Watch Together** 确认插件和设置页可用。

升级前备份旧 DLL，以便回滚。

### 开始一起观看

1. 两人用各自账号登录同一台服务器，在 Emby 用户菜单打开 **Watch Together**（可能显示为「同映」）。
2. 请管理员建房，或由一人创建一次性邀请码，另一人在「邀请」页接受。
3. 双方在各自客户端打开同一视频。必要时在「我的房间」点击「加入房间」，等待播放对齐。

![Watch Together 房间页示意图](docs/images/watch-together-preview.png)

## 能做什么

在「我的房间」可以查看双方状态、暂离或重新同步。管理员可以查看全部房间和播放状态，并结束同步。主用户换片时，插件会尝试让另一方跟随并重新对齐。

<a id="界面语言与翻译"></a>

界面提供简体中文、繁体中文和英文。[使用指南](docs/user-guide.md#中文)介绍邀请、房间操作、设置和语言规则。

## 兼容性与帮助

- 插件基于 Emby Server 4.9 API 构建。具体客户端的实测与待验证场景见[兼容性记录](docs/client-compatibility.md)。同步精度取决于客户端上报的播放状态。
- 房间一直显示等待时，确认双方在线、已加入房间、打开了同一视频，且客户端支持所需的远程控制。必要时在房间页点击「重新同步」。
- 报告问题时，请附脱敏诊断 JSON、插件和双方客户端版本、问题发生时间、操作步骤、预期及实际结果。分享前移除令牌及日志中的私人信息。

## 选择版本

| 通道 | 当前版本 | 适合谁 |
| --- | --- | --- |
| 正式版 | [v1.8.0.0](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.8.0.0) · [说明](docs/releases/v1.8.0.0.md) | 首次安装和日常使用 |
| 测试版 | [v1.8.0.1](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.8.0.1) · [说明](docs/releases/v1.8.0.1.md) | 供主动测试的用户使用 |

[全部发布日志](docs/releases/README.md) · [文档索引](docs/README.md) · [翻译贡献](docs/translations.md)

项目采用 [Apache License 2.0](LICENSE)。截图来源及许可见[图像说明](docs/images/README.md)。
