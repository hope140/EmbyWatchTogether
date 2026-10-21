# ADR-004: 通过审核的 PR 请求驱动云端签名发布

- 状态: accepted
- 日期: 2026-10-21
- 相关组件: GitHub Actions 发布控制器、签名发布工作流

## 背景

现有官方 connector 能提交及合并 PR，但本会话工具没有 tag-create 或 dispatch
接口；云端 CLI 授权未验证可用。签名私钥已保存在 GitHub `release` Environment。

## 问题

让已审核发布不再依赖本地电脑，同时保留明确的版本、源码、通道和正文授权，
不能把每次 main 提交都变成发布，也不能为接口缺口复制凭据或私钥。

## 可选方案

- 等待官方平台提供仓库范围 tag/dispatch 接口或正式 CLI 代理。
- 引入另一个持久 PAT 或 GitHub App 凭据。
- 使用已有 connector 创建专用请求 PR，由受信任 Actions 解析并发布。

## 最终选择

采用第三种方案。基础设施与每次请求分开审核；请求 PR 只新增一个严格 JSON
文件，合并 main 后由 `pull_request_target: closed` 入口解析。固定 source SHA、
成功 CI、版本、标题、正文路径及 hash，复用原签名发布逻辑和 Environment。

## 选择原因

无需新增持久凭据；请求内容与实际发布一一绑定，普通提交不构成发布授权。
构建源码与可信控制器分开 checkout；tag 不移动、已有 Release 不覆盖。

## 已知代价

请求需要满足固定格式及分支约定；CI 历史运行必须可查询。合并是审核执行边界，
不能替代尚未配置的 reviewer/保护规则。tag 创建后若构建失败会保留 tag，重跑
只允许同 SHA 且无 Release 的状态；部分发布须人工调查。

## 后续影响

可复用 workflow 显式声明并按名称传递唯一签名 secret；Environment 配置仍是值的来源，创建 tag 前检查非空。失败后若需修复控制器，使用新审核的、内容绑定原始不可变新增 blob 的 retry 请求，不修改旧请求。

保留原手动入口。未来签名 key ID 或请求协议变更必须通过基础设施审核。
禁用通道或回滚代码不会删除历史 tag 或签名资产。

## 验证依据

- [控制器](../../scripts/release/release-request.cjs)
- [行为测试](../../tests/release-request.tests.cjs)
- [请求入口](../../.github/workflows/release-request.yml)
- [共用发布逻辑](../../.github/workflows/release.yml)
- [维护流程](../release-requests.md)、[架构](../architecture.md)
