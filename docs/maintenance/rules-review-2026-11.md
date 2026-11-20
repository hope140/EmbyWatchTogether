# 协作与发布规则审视（2026-11-16）

范围是仓库自设规则、文档索引和发布流程；平台权限与安全政策独立适用，不由仓库文档放宽。本次以 beta 的 `v1.7.3.1` 源码为基线，不修改发布、合并、标签或安全门禁。`LOCAL_OPERATIONS.md` 和仓库 `.agents/skills` 在本次环境中不存在；没有推断其内容。

## 来源和判断

- [`AGENTS.md`](../../AGENTS.md) 为全仓库职责、授权及审核入口；[PR Stack](../pr-stack-workflow.md)、[模型策略](../ai-model-policy.md)、[验证与交接](../validation-and-handoff.md)分别细化执行方式。`6cd5800` 已集中整理这些入口，低风险任务允许主线程单代理闭环。执行时仍须服从当前用户指令及运行时能力，仓库中的代理配置不证明实际可用。
- [版本规则](../versioning.md)和[发布请求](../release-requests.md)定义约定；[release workflow](../../.github/workflows/release.yml)、[请求控制器](../../scripts/release/release-request.cjs)及其测试才是实际自动门禁。历史缘由见 [ADR-004](../adr/004-reviewed-release-requests.md)：在当时工具缺少 tag/dispatch 接口的条件下，专用 PR 提供可审核的源码、CI 和发布内容绑定。保留签名、固定来源、版本一致、不可覆盖和失败关闭约束。
- 发布与验收不是同一证据：[1.7.3.1 说明](../releases/v1.7.3.1.md)记录界面语言和房间名称已验收，播放通知及菜单语言宿主行为仍待实测。

## 已确认的处理

小改动只需简短范围与验证，复杂任务保留完整计划和必要测试；模型与服务模式按任务风险、用户偏好及运行时选择，不改平台或用户自定义配置；stable 版本段规则保持不变。手动入口与审核式发布请求均保留，签名、固定来源、CI、tag 和资产不可覆盖等保障保持不变。

## Release 标题

GitHub 当前标题为 `v1.7.3.0 正式版` 和 `v1.7.3.1 测试版`。此前手动入口回退为 `Release <tag>`，审核式请求标题由 JSON 指定。此分支将手动入口更新为 `<tag> 正式版`（stable）或 `<tag> 测试版`（beta）；新请求必须提供同样的标题。旧请求的 retry 仍需与可信历史原始请求逐字节一致，因此沿用其已审核标题，不静默改写。发布 workflow 与控制器修改仅在此 PR 审核合并后生效，历史 Release 不由本 PR 改动。
