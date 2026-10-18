# 发布日志

本文按发布状态列出版本说明。普通用户先看对应版本的 Release Notes；开发者和维护者还应阅读 [`docs/versioning.md`](../versioning.md) 与 [`docs/technical.md`](../technical.md)。

## 当前正式版

- [v1.6.1.0 发布说明](v1.6.1.0.md) · [GitHub Release 与下载](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.6.1.0)：2026-10-18 正式发布，当前 Latest stable。

## 历史发布

### Stable

- [v1.5.0.0](v1.5.0.0.md)
- [v1.4.5.0](v1.4.5.0.md)
- [v1.4.4.0](v1.4.4.0.md)
- [v1.4.3.0](v1.4.3.0.md)
- [v1.4.2.0](v1.4.2.0.md)
- [v1.4.1.0](v1.4.1.0.md)
- [v1.4.0.0](v1.4.0.0.md)
- [v1.3.0.0](v1.3.0.0.md)
- [v1.2.0.1](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.2.0.1)
- [v1.1.0.0](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.1.0.0)
- [v1.0.0.0](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.0.0.0)

### Beta

- [v1.6.0.5](v1.6.0.5.md)
- [v1.6.0.4](v1.6.0.4.md)
- [v1.6.0.3](v1.6.0.3.md)
- [v1.6.0.2](v1.6.0.2.md)
- [v1.6.0.1](v1.6.0.1.md)
- [v1.5.0.3](v1.5.0.3.md)
- [v1.5.0.2](v1.5.0.2.md)
- [v1.5.0.1](v1.5.0.1.md)
- [v1.4.5.2](v1.4.5.2.md)
- [v1.4.5.1](v1.4.5.1.md)
- [v1.4.4.6](v1.4.4.6.md)
- [v1.4.4.5](v1.4.4.5.md)
- [v1.4.4.4](v1.4.4.4.md)
- [v1.4.4.3](v1.4.4.3.md)
- [v1.4.4.2](v1.4.4.2.md)
- [v1.4.4.1](v1.4.4.1.md)
- [v1.4.3.2](v1.4.3.2.md)
- [v1.4.3.1](v1.4.3.1.md)
- [v1.4.2.2](v1.4.2.2.md)
- [v1.4.2.1](v1.4.2.1.md)
- [v1.3.0.6](v1.3.0.6.md)
- [v1.3.0.2](v1.3.0.2.md)
- [v1.3.0.1](v1.3.0.1.md)
- [v1.2.0.16](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.2.0.16)
- [v1.2.0.15](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.2.0.15)
- [v1.2.0.13](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.2.0.13)
- [v1.2.0.9](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.2.0.9)

以上 38 项为本次正式版之前的公开历史 Release，通道分类与 GitHub 元数据一致。历史说明保留当时的版本事实。

`v1.5.0.4` 只对应一次失败的 tag 尝试，没有 GitHub Release 或发布资产，不能列为成功发布。

## 发布通道

- `main` 对应 stable，正式 Release 由维护者手动运行发布 workflow 创建。
- `beta` 对应 prerelease，测试版本从 `beta` 分支手动发布。
- 新 tag 使用四段数字格式，例如 `v1.6.1.0`；不得添加 `-beta` 或 `-rc` 后缀。
- 插件默认使用 `stable`；管理员可在配置页选择 `beta`，更新任务按所选通道获取并安装。
- 静态测试、自动安装和维护者检查不等于真实客户端验收；Release Notes 只描述已有证据。

每个新建的 stable 或 beta tag 必须对应一个 `docs/releases/v<version>.md` 文件。正文使用简体中文，面向普通用户说明用户可见变化、配置兼容性和升级注意事项，不包含内部协作过程、敏感信息或未经验证的夸大描述。历史公开 Release 若仓库没有对应本地文件，索引直接链接其 GitHub Release 页面；这不改变新版本 Release Notes 门禁。

## 模板

```markdown
# Watch Together vX.Y.Z.W

## 新增与改进

- 面向用户的变化。

## 配置与兼容性

- 配置默认值、热更新和兼容性说明。

## 升级注意

- 重启、回滚或人工验收要求。
```
