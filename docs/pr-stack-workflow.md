# 主线程 + 风险分层 worker + PR Stack 工作流

## 目的与适用条件

本流程用于复杂、可拆分的软件变更，按 [`AGENTS.md`](../AGENTS.md) 的职责与门禁执行；worker 只执行已固化 contract 的窄任务。任务 Tier、模型建议和回退规则见 [`ai-model-policy.md`](ai-model-policy.md)，验证范围和报告格式见 [`validation-and-handoff.md`](validation-and-handoff.md)。低风险任务可由主线程单代理闭环，不为拆分而拆分。

PR Stack 是按依赖排列、可独立审核和回滚的分支/worktree/提交序列。创建 PR、push、合并、发布和部署仍须当前用户明确授权；派发模板中的“提交要求”必须明确写出是否允许本地 commit，不能由 worker 自授。

## 阶段一：现状与知识检查

主线程先读取相关 `AGENTS.md`、README、构建与测试配置，并执行：

```bash
git rev-parse --show-toplevel
git branch --show-current
git status --short
git worktree list
git log --oneline --decorate -10
```

记录用户已有改动、基础分支和 commit、构建工具、测试入口、共享生成物与不可并发资源。已有改动不得覆盖、移动或混入 Stack。按范围读取 [`architecture.md`](architecture.md)、[`lessons-learned.md`](lessons-learned.md) 和相关 ADR；不要机械加载无关历史。

任务要求从最新 main 开始时，正常 `git fetch origin main`，核对 `origin/main` 的准确 SHA，再从该 SHA 建立独立分支或 worktree；保存环境中的旧 HEAD 不算最新 main。工作区有未知改动时保留原处，通过独立 worktree 隔离，不能用 reset、清理或未获授权的 stash 腾空工作区。

## 阶段二：输出 Stack 计划

复杂需求实现前必须逐项输出：

```text
Stack: S<n> - <名称>
目标: <单一可审查结果>
基础分支: <branch>
基础 commit SHA: <固定 SHA>
前置依赖: <无 | S<n>；使用已审核 commit>
允许修改: <精确文件或目录>
禁止修改: <精确文件或目录>
文件所有权: <本 Stack 独占的文件/目录>
实现边界: <要做与明确不做>
任务 Tier: <Tier 1 | Tier 2 | Tier 3，附风险/不确定性/跨模块/Playback-Session 依据>
模型建议: <仅作为候选，运行时核验后由主线程选择>
执行设置: <已核实的 model / effort；Standard，Fast 关闭>
验收标准: <可观察、可验证条件>
测试命令: <可直接运行的命令>
回滚方式: <revert 独立 commit 或移除补丁>
允许并行: <是/否及依据>
子代理: <主线程或运行时可用的明确 worker>
提交授权: <禁止 commit | 允许仅包含本 Stack 的本地 commit>
Knowledge context: <已读取的架构/经验文档及适用约束>
升级原因: <无；Tier 2/3 时写证据和原因>
```

无依赖且所有权不重叠的 Stack 才可并行；共享文件、生成物、迁移、公共接口或可变测试环境必须串行。Tier 选择不能仅由文件数量、任务重要性或一次失败决定；环境、权限、依赖和输入问题先补证据、缩范围或重新规划。

## 阶段三：建立真实隔离

所有 worker 写任务必须使用独立线程、分支和 worktree；低风险主线程单代理闭环可直接在主线程工作区完成。主线程实际核验：

```bash
git worktree add -b codex/stack-<nn>-<slug> <绝对路径> <base-branch-or-commit>
git worktree list
git -C <绝对路径> branch --show-current
git -C <绝对路径> status --short
```

worktree 放在仓库外，不能在现有工作区内嵌套；Windows 下使用已核验的绝对路径，删除或移动前再次核验目标。不能确认隔离时，停止 worker 写入，由主线程接管已授权且适合单代理闭环的小任务，或等待隔离就绪；不得使用共享目录 fallback。只读研究可不建 worktree。任何依赖或所有权变化都由主线程重新规划。

## 阶段四：固定 contract 并派发

派发消息必须包含基础分支/commit、绝对 worktree、分支、允许与禁止文件、任务边界、输入输出、不变量、验收标准、测试命令、提交授权、停止条件和报告要求。只给完成任务所需的最小上下文，并区分事实与假设；说明主线程是否等待结果、结果用于哪个后续步骤。只并行独立部分，不把同一问题重复交给多个写代理，也不让主线程同时修改 worker 拥有的文件。

适合 worker 的范围包括文档、测试/fake snapshot/issuer/fixture、日志脱敏、构建和签名公开资产只读核查，以及已明确 contract 的 helper/mapping。涉及 SyncEngine、RoomManager、SessionBridge、SessionSelector 的核心身份链、复杂异步 race/generation/cancellation、远控恢复、鉴权/签名信任边界和整体架构时，worker 只可在主线程固化的 contract 内收证据、补测试或执行已批准的局部实现，不得自行作架构、身份或信任边界决定。

Worker contract 示例：

```text
执行 Stack: S<n> - <名称>
目标: <单一可审查结果>
基础分支/commit: <...>
worktree/分支: <绝对路径> / <branch>
允许修改: <...>
禁止修改: <...>
任务边界: <实现内容与不做内容>
任务 Tier/原因: <Tier 1/2/3 及风险依据>
执行设置: <实际 model / effort；Standard，Fast 关闭；不可用时返回主线程>
文件所有权: <本 Stack 独占文件>
依赖/并行/回滚: <前置依赖、是否并行及回滚方式>
上下文事实/假设: <分别列出>
输入/输出: <...>
等待/消费结果: <主线程是否等待；结果用于哪个后续步骤>
必须保持的不变量: <...>
验收标准: <...>
测试命令: <...>
提交授权: <禁止 commit | 允许仅本 Stack 的本地 commit>
停止条件: <输入缺失、范围/所有权/行为冲突、权限或环境阻塞等；不得自行扩大范围>
完成报告: <按 validation-and-handoff.md 的交接模板，附可复核证据>
```

## 阶段五：worker 停止与主线程实证审核

Worker 遇到输入缺失、所有权或文件冲突、contract 与真实行为冲突、测试与静态结论冲突、需要改变架构、跨层生命周期风险，或环境/权限阻塞，应停止受影响操作并返回；独立且仍在范围内的工作可继续：

```text
Observed:
Expected:
Evidence:
Files involved:
Tests:
Why current contract may be insufficient:
Recommended escalation:
Blocked action/target:
Outcome: <已拒绝 | 待处理 | 结果不确定>
Completed work:
```

Worker 完成不等于通过。主线程在真实 worktree 执行 [`AGENTS.md` 的审核门禁](../AGENTS.md#主线程审核门禁)，使用派发时固定 base SHA，覆盖已提交、staged、unstaged 和 untracked，并读取未跟踪文件内容。确认所有权、依赖、生成物、不变量及 Knowledge Findings 后，按[验证与交接](validation-and-handoff.md)复核验收证据。发现问题时回原线程、原分支和原 worktree 修正；不能只看漂移的 base 分支、worker 摘要或局部测试。

## 阶段六：推进、PR 管理与最终回归

只有 Stack 审核通过后，才可从其已审核 commit 创建后继 Stack。远端 PR 必须标注前置 PR，按依赖从底到顶合并；底层 Stack 变化后，主线程重新规划依赖并重测、重审后继 Stack。无当前用户授权不得执行 push、创建 PR 或 merge。默认回滚是 revert 独立 commit 或移除本次补丁，禁止用 reset --hard、强制 push 或清理其他 worktree 替代可审计回滚。

全部 Stack 完成后，主线程在最终集成状态执行适用回归，不能仅汇总各 Stack 的局部测试。验证命令、准确 SHA、CI 与未执行项按[验证与交接](validation-and-handoff.md)记录。完成 Knowledge Review；无新增事实时报告 `Knowledge Findings: 无`，不强行修改架构或 ADR。

## 能力边界

运行时提供的模型、推理档位、代理角色和权限才是实际能力；本文不会更改全局配置或自动切换模型。固定代理入口不能替代文件所有权、独立 worktree、Stack 依赖和授权核验。主线程必须实际检查隔离、权限和完整 diff，不能把配置或 worker 报告当成门禁已被自动执行。
