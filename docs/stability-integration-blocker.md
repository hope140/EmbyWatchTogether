# S112 主用户会话缺失策略与整合记录

日期：2026-10-18。

本文记录已解决的整合阻塞和验证依据，保留供追溯，不代表当前待办或发布阻塞。用户于 2026-10-18 确认本轮验收通过，相关整合已随 [`v1.6.1.0`](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.6.1.0) 正式发布。

## 当前已确认决策

用户已选择“保留 beta 晚到换集行为”。主用户缺失继续走原有 2 秒停止观察和 10 秒晚到换集候选；Phase E 恢复不抢占该路径。参与者短暂缺失仍可使用绑定原 Session/Item 的恢复窗口。若参与者恢复期间主用户缺失或身份/媒体发生变化，先退出 Recovery，再由现有 beta 停止/Handoff 处理；不在 Recovery 内迁移会话身份。

## 决策落实与验证

S112 已按上述决策修正并补齐交叉测试：Primary 无原始快照或明确 stopped 时，均退出参与者 Recovery，2.1 秒停止确认只暂停另一方一次，首次主用户缺失后 4 秒的新 Session/Item 可进入原有 Handoff；参与者 Recovery 期间 Primary 替换会话直接换到新 Item 时也交还 beta Handoff。参与者替换 Session 仍拒绝继承 Recovery。

完整 .NET 测试 544/544 通过；原六项晚到换集失败均已解决，原有 drift、cooldown、停止及 S111 trace 测试保留。以下 535/541 和六项失败仅为首次集成的历史记录。真实客户端与最终 UI 汇合验收另列于当前开发排期。

当时的后续 Stack 计划（历史记录）：

- S112（恢复实施）：基础 S111 `98ace2b` 与已暂存的 Phase E 合并；允许修改 `SyncEngine.cs`、`SyncEngineTests.cs`、Phase E 的 ADR/架构/技术/路线图/当前实现说明及本记录，独占这些文件。禁止修改会话选择、命令协议、更新器、版本、依赖和发布工作流。执行 Luna High，主线程审查并完成 merge commit；不允许并行写入。验收为全部原 beta 晚到换集测试保留、参与者 Recovery/身份边界与 drift hold/cooldown 通过。命令为完整 `dotnet test` 和 `git diff --check`。回滚为 revert 该 merge commit（mainline 1）。
- S115（最终汇合）：基础为当前本地 beta `e0f3a02`，前置依赖为 S112 审核通过提交；主线程独占集成、允许修改两条分支交叉的 UI、页面测试及文档，既有同步产品代码仅以审核后的 S112 为准。UI 继续原生风格 HTML/AMD，接入 Recovering/drift 的现有只读诊断字段。独立 worktree，串行，UI 窄修改交给 Luna High；最终构建、全量 .NET/浏览器、四组 PowerShell 门禁及完整 diff 由主线程验收。回滚为逆序 revert 该 Stack 的集成提交。版本、发布、远端和 NAS 写入不属于本步骤。

以下冲突和首轮失败记录作为历史保留，不再表示决策待定。

## 首轮整合状态（历史）

- 分支：`codex/stack-112-stability-integration`。
- 基础提交：S111 `98ace2b26bab2fc7cfe5a6675e1381728b34d8c9`。
- 待合并分支：`codex/playback-stability`，`5730084b791a63867ebf50571681c7eb230db08d`。
- 三方合并的文本冲突已解决；`MERGE_HEAD` 保留，尚未提交或并入 beta。
- 当前候选保留 beta 的晚到主端换片候选和 Phase E 的 Recovering 两条实现，不存在版本/Token/Release 文件回退。
- 验证命令：`dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal`。
- 实际结果：541 项中 535 通过、6 项失败；无需在策略确认前反复运行相同测试。

## 同一输入的冲突

双方已进入 Watching；随后主用户完全没有原始 SessionSnapshot，而参与者仍以原会话播放原 Item。

- 当前 beta：先保留 Watching；2 秒停止观察到期后确认停止并按现有配置暂停另一方；最多 10 秒的候选窗口内，可信新主端会话打开新 Item 时可进入既有 Handoff。
- Phase E：立即进入 Recovering；最多等待 10 秒，只接受原 User/Session/Item 恢复；替换会话返回 Waiting，不进入 Handoff。

这不是纯文本冲突。不能在未确认策略时通过改断言或放宽 Session 身份校验掩盖差异。

失败用例：

1. `MediaHandoff_PrimaryStartsNewItemAfterStopGrace_RecoversWithinCandidateWindow`。
2. `MediaHandoff_PrimaryStartsNewItemAfterStopConfirmation_RecoversWithinTenSeconds(4)`。
3. `MediaHandoff_PrimaryStartsNewItemAfterStopConfirmation_RecoversWithinTenSeconds(7)`。
4. `MediaHandoff_LatePrimaryItemTransitionExpiresWithoutPlayItem`。
5. `MediaHandoff_LatePrimaryTransitionPrimaryReturnsToSource_ClearsCandidate`。
6. `PollOnce_ServerMismatch_ClearsLatePrimaryTransitionCandidate`。

Phase E 自身还包含等待原 Primary Session 恢复、替换 Session 不成为 Handoff 的测试；策略确定后必须重新评估两组覆盖，不能把其中一组默默删除。

## 后续验收入口（历史记录）

策略已经确认并实现于 S112 `f294c67`，由 S115 与原生风格 UI 汇合；后续整合和验收已按 `docs/development-roadmap.md` 收口，保留参与者恢复、drift hold/cooldown、既有停止与 Handoff 的安全边界。

S115 产品源码 `bcfb1fd` 的最终自动验证为 .NET 545/545、Playwright 37/37、Release build/publish、单 DLL ZIP/哈希及四组 PowerShell 门禁通过。此策略阻塞已经解决；真实客户端验收仍独立记录。

独立审核未发现除该路径之外的明确集成缺陷。原始 S110 和 Phase E 分支保持原状；自动验证通过不替代发布候选的真实客户端验收。
