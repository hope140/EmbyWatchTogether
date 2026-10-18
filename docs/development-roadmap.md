# Watch Together 当前排期

更新日期：2026-10-18。

## 当前已确认目标

保留现有 HTML/AMD JavaScript 页面和业务接口，将插件 UI 设计统一为 Emby 原生风格与控件。整合此前独立实现并部署到 NAS 的候选，完成 UI 和集成验收后一起评估正式发布。

界面沿用 Emby 宿主导航、分页、主题、字体、表单、列表与按钮。旧 UI VNext 是整合输入；现行 beta 的 Token 设置、更新通道及后续修复必须保留。

## 基线与证据

- 远端 `beta`：`a1408e03203201501690544df64509d0926164ec`，版本 `1.6.0.5`。
- 远端 `main` / Latest stable：`c6568f56b16e03fa62a45265ddb7589006418847` / `v1.5.0.0`。
- Phase E：`codex/playback-stability`，`5730084b791a63867ebf50571681c7eb230db08d`；已有历史部署记录，自动测试与真实客户端验收分别记录。
- UI VNext：`codex/stack-97-ui-native-tabs-acceptance`，`68908b0`；已实现 Emby 原生分页、主体控件与本地浏览器门禁，历史 mock 结果不替代当前集成状态的真实宿主验收。
- S110：`codex/stack-110-handoff-diagnostics` 中未提交的诊断候选；2026-10-18 只读核验 NAS 当前 DLL SHA-256 与原部署记录一致，为 `513dda4032d3c06c1d66091610e83e5569fcc810c2982abb05aed2adcb36d535`。此证据证明候选文件身份，不证明重复跳集根因已修复。

## 执行约定

各 Stack 串行、独立分支及 worktree；后继从直接前置已审核 commit 建立。主线程拥有跨 Stack 合并、冲突解决、完整 diff 审核、回归及本地 beta 集成。子代理仅在完整派发契约及真实隔离核验后写入。所有步骤保留原独立分支和原工作区改动。

共用知识输入：`AGENTS.md`、`docs/pr-stack-workflow.md`、`docs/architecture.md`、`docs/lessons-learned.md`；Phase E 另读 ADR-003，UI 另读原 UI VNext 契约与验证记录。

## PR Stack 计划

### S111：收录已部署的换集诊断候选

- 目标：将 NAS 当前候选对应的 S110 源码纳入可审核本地提交。
- 基础分支：`beta@a1408e0`；前置依赖：无。
- 允许修改及所有权：`HandoffTrace.cs`、`SessionBridgeCommandIssuer.cs`、`SyncEngine.cs`、对应两项测试、`docs/handoff-diagnostics.md`、本排期文档。
- 禁止修改：UI、版本、更新器、持久化、会话桥接协议、发布工作流及其他文件。
- 实现边界：只复制原候选的六项精确文件，不在源工作区提交或改动；诊断失败不能影响播放命令。
- 验收：完整 diff 与原候选一致，相关 tracing/issuer/handoff 测试通过；原工作区保留。
- 测试命令：`dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --filter "FullyQualifiedName~SessionBridgeCommandIssuerTests|FullyQualifiedName~SyncEngineTests"`；`git diff --check`。
- 回滚：revert 本 Stack commit；允许并行：否。
- 执行代理：主线程（已有候选收录及集成）；Max 选择理由：不适用。

### S112：整合 Playback Stability

- 目标：合入 Phase E，并保留 beta 后续换片修复及 S111 日志。
- 基础分支：S111 已审核 commit；前置依赖：S111。
- 允许修改及所有权：Phase E 与基线间完整 diff 中的同步状态、runtime、constants、diagnostics、service、UI 展示、对应测试及文档；以 merge diff 为边界。
- 禁止修改：版本、更新器信任链、依赖、发布工作流、房间持久化格式和其他业务。
- 实现边界：同 Session 恢复、低频 Barrier 漂移修复、晚到主端换片候选兼容；不新增状态机策略。
- 验收：Phase E 与 beta 的相关测试均保留；同 Session/替换 Session、停止、晚到换片、drift hold/cooldown 和日志边界通过。
- 测试命令：`dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal`；`git diff --check`。
- 回滚：revert 本 Stack merge commit（mainline 1）；允许并行：否。
- 执行代理：主线程集成，Luna High 只读审核；Max 选择理由：不适用。

### S113：整合 Emby 原生风格 UI

- 目标：复用既有 UI VNext 的原生分页、表单及列表，接回 beta Token 设置和 Phase E 状态。
- 基础分支：S112 已审核 commit；前置依赖：S112。
- 允许修改及所有权：`Plugin.cs`、`WatchTogetherService.cs` 的只读展示字段、`Web/Configuration/watchtogether.html`、`watchtogether.js`、`PluginPagesTests.cs`、`WatchTogetherServiceRoomTests.cs`、`tests/ui/`、UI 契约/验证文档。
- 禁止修改：同步引擎、会话选择、房间/邀请生命周期、Token 存储/API、更新器、版本及发布工作流。
- 实现边界：保留 HTML/AMD 和现有动作语义；轮询不丢失输入/焦点，离页/换账号清理，服务端权限仍为边界；HTML 与控制器缓存键联动更新。
- 验收：管理员非成员/成员和普通成员入口正确；Token 不回显且保存/清除/失败恢复可用；Recovering/drift 展示与 DTO 一致；浅深主题、320–1920px、44px 控件和诊断脱敏通过本地门禁。
- 测试命令：`node --check src/EmbyWatchTogether/Web/Configuration/watchtogether.js`；`npm ci` / `npm test`（`tests/ui`）；相关 .NET 页面/服务测试。
- 回滚：先 revert UI 收口 commit，再 revert UI merge commit（mainline 1）；允许并行：否。
- 执行代理：主线程合并；独立 Luna High worker 负责边界明确的 UI 收口；Max 选择理由：不适用。

### S114：集成回归与正式版准备

- 目标：在最终集成状态构建、验证并合入本地 beta，记录正式版剩余验收。
- 基础分支：S113 已审核 commit；前置依赖：S113。
- 允许修改及所有权：README、架构、技术说明、路线图、UI 验证记录、相关经验文档；产品修正必须退回对应 Stack。
- 禁止修改：版本、tag、Release、签名、远端、NAS 文件和运行服务。
- 实现边界：本轮完成本地集成；正式版等待 UI 与真实集成验收，不将旧部署或 mock 结果当作新构建验收。
- 验收：完整 .NET tests/build、现有四组 PowerShell 门禁、本地 UI 浏览器测试、完整 diff 审核及干净 Git 状态；本地 beta 包含所有审核提交。
- 测试命令：`pwsh -NoProfile -File scripts/build.ps1 -Configuration Release`；`tests/build-path-guard.tests.ps1`、`tests/release-signing.tests.ps1`、`tests/release-workflow.tests.ps1`、`tests/ci-workflow.tests.ps1`；`npm test`；`git diff --check`。
- 回滚：revert 文档提交及按逆依赖撤销集成提交；允许并行：否。
- 执行代理：主线程；Max 选择理由：不适用。

## 正式版验收与停点

1. 本地集成与 UI 浏览器门禁。
2. 最终构建在真实 Emby 中验证桌面/手机、浅深主题、管理员/普通用户、缓存、邀请/设置、Recovering/换片/漂移；记录具体构建身份及结果。
3. UI 设计与集成功能验收完成后，统一准备正式版版本号、发布说明和签名发布。

当前步骤 2、3 待执行。原生媒体名称、封面、头像沿用权限门禁；未验证的媒体权限继续使用占位。
