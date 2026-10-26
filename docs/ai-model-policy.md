# AI / Codex 模型与任务 Tier 策略

本页是模型和推理选择的唯一详述入口。它描述选择建议，不声明本机当前可用能力；主线程每次派发都必须按运行时实际支持、显式配置和当前授权核验。任务 Tier 与具体 model、effort、role 名称彼此独立。

职责与授权见 [`AGENTS.md`](../AGENTS.md)，派发与隔离见 [PR Stack 工作流](pr-stack-workflow.md)，验收证据见 [验证与交接](validation-and-handoff.md)。

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
| GPT-6.1 Terra（新增候选，`gpt-6.1-terra`） | 有明确输入与验收标准的视觉检查、电脑操作、基于材料的文档整理和小范围代码修正 | 仅作新任务建议，尚未经 EWT 对照验证；不自动替换既有 Luna/Sol/Astra 路由或主线程职责 |

型号名称来自候选能力族，不是当前额度、部署或可用性的声明。不要按旧型号推断当前额度，也不要自动切换模型。主线程仍须遵守 `AGENTS.md`、`docs/pr-stack-workflow.md`、架构文档和当前用户授权。

### Terra 的依据与边界

截至 2026-10-26，官方 [Terra 模型页](https://developers.openai.com/api/docs/models/gpt-6.1-terra)给出名称 GPT-6.1 Terra、ID `gpt-6.1-terra`，定位为视觉推理、精准电脑操作和清晰写作；[模型选择指南](https://developers.openai.com/api/docs/guides/model-selection)也列出边界明确的小范围代码修正。[发布说明](https://learn.chatgpt.com/docs/whats-new/october-26-30-2026)说明产品入口和管理员控制会影响可用性。上表是据此提出的项目建议，不是 EWT 实测结论，也没有全面替代复杂开发、整合或高风险审查的依据。

使用前核对当前客户端、账号、工具和模型列表；不可用时保持原已确认路由，不强制升级客户端。API 页的 `reasoning.effort` 为 `none`、`low`、`medium`（默认）、`high`、`xhigh`、`max`；Codex 或其他编排入口的档位以各自运行时为准，不能把产品模式或某个入口的 `ultra` 直接当作 API 枚举。

## 推理、服务模式与现有配置

当前执行约定为 **Standard，Fast 关闭**。服务模式与推理档位分开记录；任务 Tier、候选模型或模型发布都不授权自动改变模型、档位或服务模式。

已有 [Codex 项目配置](../.codex/config.toml)及固定 worker 入口保持原样：

| 入口 | 文件中声明的 model / effort |
| --- | --- |
| [`luna_worker`](../.codex/agents/luna-worker.toml) | `gpt-5.6-luna` / `medium` |
| [`luna_high_worker`](../.codex/agents/luna-high-worker.toml) | `gpt-5.6-luna` / `high` |
| [`luna_max_worker`](../.codex/agents/luna-max-worker.toml) | `gpt-5.6-luna` / `max` |
| [`deepseek_worker`](../.codex/agents/deepseek-worker.toml) | `deepseek-v4-flash` / `max` |

这是仓库配置事实，不是可用性测试。候选族表与旧固定入口并不一一对应，不得只改派发文字就声称入口已迁移。旧入口中的本地 commit 许可仍需由当前用户授权和派发 contract 限定，不能据此自行提交；其中按旧型号或额度描述的回退条件，以本页要求的运行时核验为准。后续如需迁移配置，应作为另行授权、可验证的变更处理。

官方[子代理说明](https://learn.chatgpt.com/docs/agent-configuration/subagents)区分继承与显式选择：相应模型/档位未被自定义代理文件、显式派发或 `[agents]` 默认值覆盖时，才继承父设置；实际采用值以当前运行时的解析结果为准。显式换模型可能采用该模型的默认档位。派发时记录已核实的实际 model、effort 和服务模式；需要显式选择时一并确认模型与档位，不擅自切换。子代理继承父权限，代理文件中的 `sandbox_mode` 不覆盖父会话设置；“只读任务”文字不等于已建立独立只读沙箱。

## DeepSeek 回退

当运行时确认推荐的 Luna 族不可用时，可以使用已配置的 DeepSeek v4 备用 worker。该回退需要在派发记录中说明“运行时不可用”证据；不得仅凭历史型号或额度猜测。DeepSeek 与 Tier 1/2/3 不自动等价，仍须使用相同的 worktree、文件所有权、contract、测试和主线程审核门禁。

当前在途 Stack 继续按原 contract 完成；本策略从新任务开始生效，不要求中途拆分或重构既有工作。
