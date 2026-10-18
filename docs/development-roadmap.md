# Watch Together 当前排期

更新日期：2026-10-18。

## 当前已确认目标

以本轮部署前 NAS 的单页界面为 UI 基线，保留原区域顺序、房间布局和交互，只调整既有按钮、输入框、选择框、checkbox 等原生控件及其样式。已确认的同步、诊断和后台整合保留，完成 UI 和真实双端验收后统一准备正式发布。

当前 UI 来源为 `a1408e0` 的原服务器页面，`f294c67` 仅提供其上的 Phase E 诊断增量；S116 控件契约见 [`native-controls-plan.md`](native-controls-plan.md)。UI VNext 的三页布局为已撤回的历史候选，现行 Token 设置、更新通道及已确认同步修复继续保留。

当前排期：S116 原页面控件调整 → S117 原生内容宽度/主题适配 → S118 按钮居中与轮廓 → UI 初步验收 → S119 `v1.6.1.0` 正式版发布前准备 → 双端验收与发布门禁收口。具体人工项目见 [`client-compatibility.md`](client-compatibility.md)。

当前进度：S118 产品源码 `d4b724d` 已于 2026-10-18 21:54（东八区）部署到 NAS，.NET 545/545、UI 40/40、构建和四组脚本门禁通过。真实 Web 的房间按钮文字中心偏移约 -0.005px，原生描边清晰且 80ch 宽度保持；模拟主题代表点文字 >=4.5:1、边界 >=3:1。用户初步确认当前 UI 可接受并要求发布前准备。NAS 仍为 `1.6.0.5` 本地候选，正式版沿已有预定目标 `1.6.1.0` 在独立分支准备；双端播放验收单列，不以 UI 反馈代替。构建身份与验证边界见 `native-controls-plan.md`。

## 基线与证据

S119 本地正式候选已整理为 `1.6.1.0`，当前 UI/业务源码与 S118 相同，只调整版本元数据与发布文档。候选在独立发布准备分支冻结；本地最终构建及可发布状态以该 worktree 的 `dist/release-readiness.json` 为准。公开 main/beta、tag、Release 和 NAS 均不由这次准备操作提升。准备清单见 [`release-preparation-v1.6.1.0.md`](release-preparation-v1.6.1.0.md)。

- 远端 `beta`：`a1408e03203201501690544df64509d0926164ec`，版本 `1.6.0.5`。
- 远端 `main` / Latest stable：`c6568f56b16e03fa62a45265ddb7589006418847` / `v1.5.0.0`。
- Phase E：`codex/playback-stability`，`5730084b791a63867ebf50571681c7eb230db08d`；已有历史部署记录，自动测试与真实客户端验收分别记录。
- UI VNext：`codex/stack-97-ui-native-tabs-acceptance`，`68908b0`；已实现 Emby 原生分页、主体控件与本地浏览器门禁，历史 mock 结果不替代当前集成状态的真实宿主验收。
- S110：`codex/stack-110-handoff-diagnostics` 中未提交的诊断候选；2026-10-18 只读核验 NAS 当前 DLL SHA-256 与原部署记录一致，为 `513dda4032d3c06c1d66091610e83e5569fcc810c2982abb05aed2adcb36d535`。此证据证明候选文件身份，不证明重复跳集根因已修复。

## 执行约定

各 Stack 串行、独立分支及 worktree；后继从直接前置已审核 commit 建立。主线程拥有跨 Stack 合并、冲突解决、完整 diff 审核、回归及本地 beta 集成。子代理仅在完整派发契约及真实隔离核验后写入。所有步骤保留原独立分支和原工作区改动。

执行调整：S112 首轮集成曾发现 beta 晚到换片与 Phase E 主用户缺失恢复对同一输入存在行为冲突，因此 S113/S114 先从 S111 独立完成 UI 整合。用户现已确认保留 beta 主用户晚到换集行为；S112 `f294c67` 已按此修正并通过 544 项测试，S115 负责最后汇合与验收。

共用知识输入：`AGENTS.md`、`docs/pr-stack-workflow.md`、`docs/architecture.md`、`docs/lessons-learned.md`；Phase E 另读 ADR-003，UI 另读原 UI VNext 契约与验证记录。

## PR Stack 计划

### 当前 S116：原服务器单页上的原生控件调整

基础为本地 beta `90feffd`，按用户最新确认恢复 UI 基线，后端保持原已审核实现。实现、所有权、验证和回滚的完整契约见 `native-controls-plan.md`。以下 S111–S115 为历史整合过程；其三页 UI 布局不再是当前交付方案。

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
- 基础分支：S111 已审核 commit `98ace2b`；前置依赖：S111；S112 策略确认后由 S114 汇合。
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

### S115：Playback Stability 与原生风格 UI 汇合

- 目标：将已审核 S112 与本地 beta 的 S111/S113/S114 合流，接通 Recovery/drift 的只读界面。
- 基础分支：`beta@e0f3a02`；前置依赖：S112 `f294c67`，完整 .NET 544/544、主线程交叉窄测 5/5。
- 主线程文件所有权：跨分支 merge、README、架构、技术/版本说明、当前排期、经验及策略记录；同步产品代码保持 S112 审核结果。
- UI 子任务允许修改及所有权：`Plugin.cs` 的页面缓存键、`Web/Configuration/watchtogether.html` / `watchtogether.js`、`PluginPagesTests.cs`、`tests/ui/fixture/server.js` / `specs/watchtogether.spec.js`、`docs/ui-vnext-plan.md` / `ui-vnext-verification.md`。
- 禁止修改：同步引擎、Session/Room/邀请/Token/更新器 API、依赖和锁文件、程序集版本、发布工作流、远端及 NAS。
- 实现边界：Emby 原生风格、控件与现有 HTML/AMD 不变；接入 `Recovering` 标签和 `transient_recovery` 原因，保留两种恢复窗口、Handoff、drift 的各自诊断与脱敏导出；页面/控制器/route 缓存键统一 V5。
- 验收：保留 Token、权限、账号及页面生命周期门禁；参与者 Recovery 的房间和诊断信息一致，主用户缺失仍显示原 beta 等待/停止状态；新诊断字段不导出原始身份，320px 展开诊断无横向溢出，浅深主题可读。
- 测试命令：`node --check src/EmbyWatchTogether/Web/Configuration/watchtogether.js`；页面/诊断 .NET 过滤测试；`npm ci` / `npm test`（`tests/ui`）；最终 `scripts/build.ps1 -Configuration Release` 和四组 PowerShell 门禁；`git diff --check`。
- 回滚：先 revert UI 收口及文档提交，再 revert 本 Stack merge（mainline 1）；不覆盖旧分支。
- 允许并行：否，共享文件和最终回归串行；子代理：Luna High 负责限定 UI 收口，主线程负责集成、审核、最终回归及本地 beta fast-forward；Max 理由不适用。
- Knowledge context：`AGENTS.md`、当前架构/经验、ADR-003、S112 策略记录和 UI 契约/验证记录。

## 正式版验收与停点

1. 本地集成与 UI 浏览器门禁。
2. 最终构建在真实 Emby 中验证桌面/手机、浅深主题、管理员/普通用户、缓存、邀请/设置、Recovering/换片/漂移；记录具体构建身份及结果。
3. UI 设计与集成功能验收完成后，统一准备正式版版本号、发布说明和签名发布。

当前 S116 本地门禁和 NAS 部署完成后，由用户执行步骤 2 的真实双端验收；步骤 3 等验收收口。当前页面沿用原服务器内容，不增加媒体展示区。

## S111–S115 历史整合状态

- S111：`98ace2b`，已收录原 S110 六项精确文件；issuer/SyncEngine 相关 173 项测试通过。原 S110 工作区未提交改动完整保留。
- 部署前 NAS 文件身份：原 S110 安装 DLL 与本机产物及历史部署 receipt 的 SHA-256 三者一致。之后部署过 S115 候选；当前候选以 S116 记录为准。
- S112：`f294c67`，按用户已确认的 beta 主用户规则整合 Phase E，完整 .NET 544/544、主线程交叉窄测 5/5 通过；首次 535/541 和六项失败为已解决的历史记录。
- 已确认策略：主用户缺失走 beta 的 2 秒停止观察与 10 秒晚到换集识别；Recovery 仅处理非主用户参与者缺失。参与者恢复期间主用户发生变化，先交还原有停止/Handoff 路径，不在 Recovery 内迁移身份。
- S113：`2f3f481`，从 S111 整合既有原生分页、表单、开关、列表及按钮，保留 beta Token 功能并完成生命周期收口。子代理执行的 .NET 页面/服务测试 56 项通过，UI 浏览器 35 项通过，最后的账号立即操作隔离窄测 1 项通过；随后由 S114 独立完成回归。
- S114：最终源码基线 `2f3f481` 已由主线程独立验证：.NET 519/519、Playwright 35/35、Release build/publish、单 DLL ZIP 和四组 PowerShell 门禁全部通过，`git diff --check` 通过。本地 beta 整合包含 S111/S113 及本轮文档同步；远端与已发布版本保持原基线。
- S115：源码 `bcfb1fd` 已汇合 S112 与原生风格 UI，缓存键统一 V5。主线程独立验证 .NET 545/545、Playwright 37/37、Release build/publish、单 DLL ZIP 布局/哈希及四组 PowerShell 门禁全部通过，工作区与 diff 检查通过。本地 beta 包含该汇合及验证记录提交。
- 以上记录对应历史整合构建；当前 UI 与部署进度见 S116 契约。正式版仍等待最终 UI 的真实 Emby/桌面/手机/双账号验收、统一版本和签名发布。
