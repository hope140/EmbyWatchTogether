# AI / Codex 模型与任务 Tier 策略

本页是模型和推理选择的唯一详述入口。它描述选择建议，不声明本机当前可用能力；主线程每次派发都必须按运行时实际支持、显式配置和当前授权核验。任务 Tier 与具体 model、effort、role 名称彼此独立。

## 任务 Tier

开始任务时评估 Task Risk、Task Uncertainty、Cross-module Scope 和 Playback/Session Impact：

| Tier | 适用范围 | 处理原则 |
| --- | --- | --- |
| Tier 1 | 规格明确的文档、测试、Git、脚本、配置、fixture、日志脱敏、公开资产只读核查和独立 helper/mapping | 选择可靠完成所需的最低成本 worker；低风险任务可由主线程单代理闭环 |
| Tier 2 | SyncEngine、RoomManager、SessionBridge、SessionSelector 间状态/身份链，异步 race/generation/cancellation、远控恢复、鉴权或签名信任边界的跨组件分析、设计和高风险核心 review | 由主线程保留架构决策；worker 只验证明确假设或执行已批准局部实现 |
| Tier 3 | Tier 2 完整分析一轮仍不明的关键问题、重大架构、底层兼容或高风险安全决策 | 必须保留证据、说明升级原因和未决风险；不得让模型自行发明架构 |

文件数量、任务重要性或一次失败不能单独触发升档。先缩小范围、补齐输入和证据、拆分任务；环境、权限、依赖和输入不足不通过升档解决。已明确符合 Tier 2 的任务可直接选择 Tier 2；连续两轮 Tier 1 仍未定位时，返回主线程评估升级。

## 模型族候选

下表仅是候选建议，具体型号和推理档位必须由运行时能力与主线程选择确认：

| 候选族 | 可考虑的工作 | 约束 |
| --- | --- | --- |
| GPT-6 Luna（默认建议 High，Max 可用时考虑） | Tier 1；已完成设计、contract 清楚的普通实现或独立测试 | 只在运行时实际可用且显式配置支持时选择；不代表固定 role 已具备对应能力 |
| GPT-6.1 Sol（Medium/High） | Tier 2 的跨组件分析、兼容性问题和高风险实现 | 依据 Tier 2 的风险、不确定性、跨模块范围或 Playback/Session 影响选择；保留主线程架构决策 |
| GPT-6 Astra | Tier 3 的重大架构、底层兼容或高风险安全决策 | 仅在 Tier 2 已完整分析仍不足时建议；必须记录升级原因 |

型号名称来自候选能力族，不是当前额度、部署或可用性的声明。不要按旧型号推断当前额度，也不要自动切换模型。主线程仍须遵守 `AGENTS.md`、`docs/pr-stack-workflow.md`、架构文档和当前用户授权。

## DeepSeek 回退

当运行时确认推荐的 Luna 族不可用时，可以使用已配置的 DeepSeek v4 备用 worker。该回退需要在派发记录中说明“运行时不可用”证据；不得仅凭历史型号或额度猜测。DeepSeek 与 Tier 1/2/3 不自动等价，仍须使用相同的 worktree、文件所有权、contract、测试和主线程审核门禁。

## 委派边界

主线程负责分析、拆 Stack、固定 contract、授权判断、跨层架构、Playback/Session 身份链、复杂异步和凭据信任边界，以及最终验收。worker 适合文档、测试/fake snapshot/issuer/fixture、构建与静态检查、日志脱敏、签名公开资产只读核查和已明确 contract 的 helper/mapping。

worker 发现设计与真实行为冲突、测试与静态结论冲突、需要改变 contract 或出现跨层生命周期风险时，必须停止扩大范围并使用以下结构回报：

```text
Observed:
Expected:
Evidence:
Files involved:
Tests:
Why current contract may be insufficient:
Recommended escalation:
```

## 记录与审核

每个写任务的派发 contract 必须明确提交授权：未经当前用户明确授权禁止 commit，获授权时也只允许包含本 Stack 的本地 commit。主线程审核必须覆盖固定 base commit SHA、已提交 diff、staged、unstaged 和 untracked；worker 的完成消息、局部测试或静态结果都不构成最终通过。

证据分层记录静态/单元/构建、隔离 Emby 测试、服务器部署/加载和真实双客户端播放远控。缺少更高层证据时保持 `UNKNOWN`，不要把局部通过写成真实运行验收。

当前在途 Stack 继续按原 contract 完成；本策略从新任务开始生效，不要求中途拆分或重构既有工作。
