# 项目协作规则

## 适用范围

本文件适用于整个仓库。更深目录中的 `AGENTS.md` 可以补充局部规则，但不得放宽这里的任务边界、隔离、审核与发布要求。

本仓库采用“主线程 + 风险分层 worker + PR Stack”工作流。模型与推理选择的唯一详述入口是 [`docs/ai-model-policy.md`](docs/ai-model-policy.md)，执行模板见 [`docs/pr-stack-workflow.md`](docs/pr-stack-workflow.md)。

## 知识读取与复盘

- 按需读取知识：先看本文件，再看任务相关的 [`docs/architecture.md`](docs/architecture.md)，然后读取 [`docs/lessons-learned.md`](docs/lessons-learned.md) 中相关内容，最后按模块读取相关 ADR；不要机械全量加载无关历史。
- 知识库只记录已由真实代码、测试、实际运行或日志、可复现结果，或官方文档与当前实现交叉确认的事实。写入前搜索去重；发现旧经验失效时更新为当前事实并保留验证依据。
- 每个具有实质性代码修改、Bug/架构/兼容性调查的任务结束前执行 Knowledge Review：记录新增约束、隐蔽坑、被证明错误的假设和建议沉淀项；架构变化需评估是否更新 `architecture.md` 或创建 ADR。无发现时可在完成报告明确 Knowledge Findings/Review 为“无”，不为流程强行改知识文档。
- 子代理完成报告必须包含 `Knowledge Findings`，主线程负责审核其事实依据、去重结果和是否需要架构/ADR 更新。

## 变更原则

- 先检查真实代码、仓库文档、Git 状态和现有改动，再设计或修改。
- 只做当前任务要求的最小变更；保留并绕开用户已有改动。
- 未经用户明确授权，不得创建本地 commit、push、创建 PR、合并 PR、强制更新分支或执行不可逆清理；派发 contract 必须明确是否允许本地 commit。
- 不得泄露密钥、令牌、Cookie、个人信息或不必要的本机路径。
- 子代理报告只是线索，不是验收证据；主线程必须检查真实仓库状态和完整 diff。

## 主线程职责

主线程负责需求分析、架构设计、Stack 拆分、依赖与风险判断、文件所有权分配、子代理派发、代码审查、集成回归和最终验收。

复杂需求进入实现前，主线程必须先输出完整 PR Stack 计划。每个 Stack 至少写明：

- Stack 编号和名称
- 目标
- 基础分支
- 前置依赖
- 允许修改的文件或目录
- 禁止修改的文件或目录
- 文件所有权
- 实现边界
- 验收标准
- 测试命令
- 回滚方式
- 是否允许并行
- 计划使用的子代理

主线程应把边界明确、可验证、可回滚的任务交给最低成本且运行时可用的 worker；任务 Tier 由风险、不确定性、跨模块范围和 Playback/Session 影响决定，具体模型建议见 [`docs/ai-model-policy.md`](docs/ai-model-policy.md)。架构取舍、需求澄清、跨 Stack 集成、最终发布和无法明确划界的修改仍由主线程负责。

### 任务 Tier 选择规则

- Tier 1：规格明确的文档、测试、Git、脚本、配置和独立 helper/mapping 实现。优先选择可靠完成所需的最低成本 worker。
- Tier 2：涉及 SyncEngine、RoomManager、SessionBridge、SessionSelector 之间的状态或身份链，异步 race/generation/cancellation、远控恢复、鉴权或签名信任边界的跨组件分析、设计和高风险核心 review。
- Tier 3：Tier 2 已完成一轮证据分析仍未明确的关键问题、重大架构、底层兼容或高风险安全决策；必须记录证据和升级原因。
- 文件数量、任务重要性或单次失败不能单独触发升级。先缩小范围、补证据、拆任务；环境、权限或输入问题不通过升档解决。已明确符合 Tier 2 的任务可直接选择 Tier 2；连续两轮 Tier 1 仍未定位时返回主线程评估升级。
- 任务 Tier 与具体 model、effort 和固定 role 名称彼此独立；不得自动切换模型或把当前 role 名称当成模型能力承诺。原在途工作保持既定边界。

### 模型可用性回退

当运行时确认推荐的 Luna 族不可用时，可使用已配置的 DeepSeek v4 备用 worker；该选择必须由主线程记录为运行时回退，不得仅凭旧型号或额度推断可用性。备用 worker 与任务 Tier 不自动等价，派发契约、worktree 隔离、审核门禁与回滚要求不因模型回退而放宽。

## PR Stack 与并发规则

- 无依赖的 Stack 可以从共同基础分支创建。
- 有依赖的 Stack 必须基于直接前置 Stack 的已审核 commit 创建。
- 修改相同文件、共享生成文件、共享迁移或存在接口依赖的 Stack 必须串行。
- 只有文件所有权不重叠、没有依赖且验证不会争用同一可变资源的 Stack 才允许并行。
- 依赖或文件所有权在执行中发生变化时，暂停相关 Stack，由主线程重新规划，不得由子代理自行协调扩大范围。
- 一个 Stack 原则上对应一个分支、一个独立 worktree、一个执行代理线程和一个可独立审核的 commit 序列。

## 子代理与 Worktree 门禁

- 每个执行写操作的子任务必须使用独立代理线程、独立分支和独立 Git worktree。
- 派发前，主线程必须实际运行 `git worktree list`，确认目标 worktree、分支和基础 commit；不得只在提示或报告中声称已隔离。
- 如果运行环境不能为子代理保证独立 worktree，主线程必须先显式执行 `git worktree add` 并把绝对路径交给代理；仍无法隔离时，停止委派写入，由主线程接管已授权且适合单代理闭环的小任务，或等待隔离就绪，不得让写 worker 使用共享目录 fallback。
- 默认最多并发 3 个子代理线程；主线程应按依赖、文件所有权和机器资源进一步降低并发。低风险任务允许主线程单代理闭环，不为拆分而拆分。
- 子代理必须在收到 `docs/pr-stack-workflow.md` 派发契约中的全部字段后才能写入。字段缺失、需求不清、出现文件冲突或发现架构问题时，停止修改并报告主线程。
- 修正任务优先发送回原代理线程，并继续使用原分支和原 worktree；不得无故新建修复代理。

## 主线程审核门禁

子代理完成后，主线程必须在该代理的真实 worktree 中执行并检查（无论代理是否创建 commit）：

```bash
git worktree list
git branch --show-current
git status --short
git log --oneline --decorate -10
git diff --stat <派发时基础 commit SHA>...HEAD
git diff <派发时基础 commit SHA>...HEAD
git diff --stat
git diff
git diff --cached
git ls-files --others --exclude-standard
git status --short
```

主线程还必须确认：

- 没有修改允许范围之外的文件；审核固定使用派发时基础 commit SHA，覆盖已提交 diff、staged、unstaged 和 untracked，并读取 `git ls-files --others --exclude-standard` 列出的文件内容，不能只依赖漂移的 base...HEAD；
- 没有遗漏未跟踪文件；
- 没有未经批准的依赖或生成文件；
- 没有无关重构；
- 所有验收标准均有代码或测试证据；
- 已按风险重新运行必要的测试、构建、Lint、类型检查或静态检查。

发现问题时，主线程向原代理发送明确、具体、可验证的修正要求。审核通过后，仅由主线程整理最终提交；本地 commit、push、创建 PR 或合并均需用户明确授权。

## 最终集成与回归

全部 Stack 完成后，主线程按任务影响在最终集成状态运行适用的回归，检查 Stack 间接口和行为，并汇总：实际修改文件、测试与构建证据、分支/commit/PR 状态、未执行验证及原因、残余风险。治理文档任务只检查规则、链接、编码和 diff，不承诺产品完整回归；只有全部适用验收条件满足后才能结束任务。

## 本地操作参考（LOCAL_OPERATIONS.md）

本仓库根目录存在一个本机专用操作手册 `LOCAL_OPERATIONS.md`，已通过 `.git/info/exclude` 排除，不提交、不推送。该文件记录本机确认的部署环境、服务器地址、部署/发布流程、发布约定与常用排查步骤。

- 当任务涉及本仓库的部署、发布、回滚、服务器/Emby 操作、插件备份清理或版本发布时，必须先读取 `LOCAL_OPERATIONS.md`（若存在）并按其中约定执行。
- 若该文件不存在（例如其他机器或 CI 环境），直接跳过，不得凭猜测编造其中的内容。
- 该文件中的服务器地址、连接信息等属于本机参考，不得写入任何提交到仓库的文件。
