# 发布日志

## 发布通道

- `main` 是正式发布分支，对应 `stable` 通道。维护者从 `main` 手动运行发布 workflow 后创建普通 GitHub Release。
- `beta` 是测试发布分支，对应 `beta` 通道。维护者从 `beta` 手动运行发布 workflow 后创建 GitHub prerelease。
- 两个通道共用规范数字 tag，例如 `v1.3.0.6`，不得添加 `-beta`、`-rc` 或其他后缀。workflow 会拒绝 channel 与手动触发分支不匹配的请求。
- GitHub prerelease 不会成为 `releases/latest`。正式版更新器继续只读取 `releases/latest`；测试版如需安装，必须由维护者手动获取并验证对应 prerelease 资产。
- 测试版手动获取或安装不等于真实客户端验收；本目录的 Release Notes 不得将静态测试或维护者验证描述为真实客户端验收。

每个 stable 或 beta 四段数字 tag 都必须对应一个 `docs/releases/v<version>.md` 文件。正文使用简体中文，面向普通用户说明新增功能、改进、修复、配置变化和升级注意事项，不包含内部协作过程、敏感信息或未经验证的夸大描述。

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

版本提交和 tag 创建前，维护者必须审核对应日志文件；文件必须为非空 UTF-8（无 BOM），并至少包含中文内容。发布 workflow 会在创建 GitHub Release 前再次执行这些门禁。
