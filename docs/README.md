# Watch Together 文档索引

本文面向开发者、维护者和发布人员。普通用户请先阅读仓库根目录的 [`README.md`](../README.md)。

## 当前实现

- [当前架构](architecture.md)：运行时组件、状态、嵌入式 UI、持久化和信任边界。
- [技术文档](technical.md)：API、运行时约束、构建、测试、打包、签名发布和排错。
- [版本号规则](versioning.md)：项目版本、Git tag、stable/beta 通道与发布检查。
- [客户端兼容性记录](client-compatibility.md)：已验证事实、验收边界和问题报告所需信息。
- [换集诊断日志](handoff-diagnostics.md)：`WT_HANDOFF` 字段含义和证据解释。

## 开发与维护

- [当前开发排期](development-roadmap.md)：正式版、测试版、CI 状态和历史 Stack 入口。
- [路线图](roadmap.md)：已整合能力与延期事项。
- [项目协作规则](../AGENTS.md)：主线程职责、授权与审核门禁的总入口。
- [贡献与 Stack/Worktree 流程](pr-stack-workflow.md)：分支、隔离、派发、停止和回滚约定。
- [AI / Codex 模型与任务 Tier 策略](ai-model-policy.md)：风险分层、保留的模型路由及 Terra 候选依据。
- [验证与交接](validation-and-handoff.md)：EWT 验证入口、证据边界、准确 CI SHA 与报告模板。
- [浏览器测试与 GitHub CI](../tests/ui/README.md)：本地 fixture 回归、CI 约束和报告查看。
- [经验记录](lessons-learned.md)：已由代码、测试或实际运行证实的约束。
- [v1.7.0.1 beta 发布记录](maintenance/v1.7.0.1-beta-publication.md)：V16 界面反馈、签名发布与独立核验。
- [ADR 索引](adr/README.md)：需要长期保留的架构决策。

## 历史记录

以下文档保留实施过程和证据，用于追溯，不代表当前待办：

- [原生控件调整与验证记录](native-controls-plan.md)
- [v1.6.1.0 发布准备记录](release-preparation-v1.6.1.0.md)
- [S112 稳定性整合阻塞记录](stability-integration-blocker.md)
- [UI VNext 历史实施契约](ui-vnext-plan.md)
- [UI VNext 历史验证记录](ui-vnext-verification.md)
- [Watch Together 插件历史实现说明](watch-together-emby-plugin-plan.md)

## 发布日志

- [发布日志索引](releases/README.md)
- [v1.7.1.0 正式版说明](releases/v1.7.1.0.md)
- [v1.7.2.6 测试版说明](releases/v1.7.2.6.md) · [发布核验记录](maintenance/v1.7.2.6-beta-publication.md)
- [v1.7.2.5 测试版说明](releases/v1.7.2.5.md) · [发布核验记录](maintenance/v1.7.2.5-beta-publication.md)
- [v1.7.2.4 测试版说明](releases/v1.7.2.4.md)
- [v1.7.2.3 测试版说明](releases/v1.7.2.3.md)
- [v1.6.1.0 Release Notes](releases/v1.6.1.0.md)
- [v1.7.0.0 测试版说明](releases/v1.7.0.0.md)
- [v1.7.0.0 测试版发布记录](maintenance/v1.7.0.0-beta-publication.md)

发布操作、tag、GitHub Release 和发布后状态同步由维护者按维护记录执行；本索引只提供文档入口，相关边界见 [`v1.6.1.0 发布维护记录`](maintenance/v1.6.1.0-publication.md)。
