# 维护经验

本文件只收录已验证、可复用且不属于单次任务日志的经验。写入前搜索去重；发现失效时直接更新当前事实并注明依据。

## 1. 不要把自然播放位移当成手动 Seek

- 现象：轮询快照中的位置持续变化，若直接比较两次位置会产生误报。
- 原因：播放速率和轮询间隔会造成预期位移。
- 结论：`SyncEngine` 先按上一快照、经过时间和播放速率估算 expected，再以阈值判断明显跳变。
- 规则：同步逻辑不得通过周期性 Seek 消除小幅速度差；修改判定时必须覆盖暂停、速率变化和远程 Seek 回传。
- 验证：`SyncEngineTests` 覆盖自然速率差不误判、手动前进/后退和远程 Seek 去重。

## 2. 命令确认必须绑定会话身份

- 现象：设备重连后仍播放同一 Item，旧 Pending 命令可能被新会话快照“确认”。
- 原因：Item 和位置相同不足以证明是同一播放器会话。
- 结论：Pending、Suppressed 和 Barrier 状态记录 session identity；身份或 Item 变化时丢弃旧命令并回到等待。
- 规则：新增命令生命周期逻辑不得跨 session identity 重试或确认。
- 验证：`SessionSelectorTests`、`SyncEngineTests` 覆盖会话切换、旧命令丢弃和重新 Barrier。

## 3. 签名更新失败必须 fail closed

- 现象：版本号或 MD5 正确不代表发布资产可信。
- 原因：未签名或未知 key 的资产可能被替换。
- 结论：更新前必须验证 canonical manifest、大小、SHA-256、程序集身份、tag 和 RSA detached signature；未知 key 或任一失败都拒绝更新。
- 规则：不得把 installer 的 MD5 二次校验当作信任根，也不得把私钥或 secret 写入仓库文档。
- 验证：`ReleaseTrustStoreTests`、`release-signing.tests.ps1` 与 `release-workflow.tests.ps1`。

## 4. 签名自动更新必须先完成可信引导

- 现象：验签程序随待安装版本一起分发时，该版本无法仅凭自身证明内置公钥可信。
- 原因：签名只能证明发布资产由对应私钥持有者签发，不能自行建立公钥最初来自可信维护者这一前提。
- 结论：首个内置生产公钥和验签逻辑的版本必须通过人工安装或既有可信渠道部署；完成信任引导后，后续版本才能依赖签名自动更新。
- 规则：待验证的更新包不得携带并决定新的信任根；公钥可以提交，私钥只能保存在受控 Secret 或加密离线介质中。轮换公钥时必须由当前已信任版本或新的人工可信引导授权。
- 验证：`ReleaseTrustStore` 固定保存已审核公钥；`ReleaseSignatureVerifier` 对未知 `keyId` 拒绝更新；`ReleaseTrustStoreTests` 与 `release-signing.tests.ps1` 覆盖已知、未知及签名不匹配场景。

## 5. 不要用暂时无输出判定 Luna 未执行

- 现象：耗时较长的子代理可能暂时无输出、没有 commit，主工作区也保持干净，但独立 worktree 中仍在运行或稍后产生修改。
- 原因：代理状态、主工作区状态和目标 worktree 状态是不同信号；工具拒绝也可能只影响某个复合步骤，而非整个任务。
- 结论：判定“未产生写入”必须同时核对代理已终止或明确失败、目标 worktree 的 `HEAD` 未变化、工作区和未跟踪文件为空、相对基础分支无 diff；超时后还要检查迟到提交。
- 规则：工具拒绝先区分认证缺失、接口限制、安全策略、Shell 解析和外部状态冲突，再把操作拆成可审计的最小步骤或改用安全替代路径；不得通过换壳命令绕过安全限制。修正任务优先返回原代理、原分支和原 worktree。
- 验证：主线程按 `docs/pr-stack-workflow.md` 的审核命令检查 `git worktree list`、分支、状态、提交历史和完整 diff；子代理文字报告、单次超时或主工作区干净均不单独作为完成或失败证据。

## 6. 播放停止必须由持续快照确认

- 现象：部分 Emby 客户端在同步暂停、恢复或重建播放会话期间会短暂报告 `PlaybackStopped`，并暂时显示 `Stopped=true` 或缺失会话，随后恢复同一播放。
- 原因：服务端停止事件和单轮 SessionInfo 是播放状态转换中的瞬时信号，不是权威终态；立即处理会错误暂停另一方并形成重新 Barrier 的循环。
- 结论：`PlaybackStopped` 只用于唤醒轮询；停止只依据 SessionSelector 选中的当前会话判断。同一用户的旧 stopped Session 不能覆盖当前有效播放；当前会话的 `Stopped=true`、离线和缺失统一要求连续 2 秒快照异常，恢复有效快照时清除计时并保持 `Watching`。
- 规则：任何新增停止信号或未选中的候选会话都不得绕过当前会话选择与确认窗口；位置归零仍按正常 Seek 处理。确认停止后，暂停和通知副作用只执行一次。
- 验证：真实 Emby Theater 与 embyToLocalPlayer 日志复现了短暂停止后恢复，以及旧 stopped Session 与当前播放并存导致的误判；`SyncEngineTests` 覆盖同用户同 Item 的旧 stopped Session、短暂 stopped/缺失恢复、持续异常确认和 seek-to-zero。

## 7. Seek 失败必须冻结原目标并有界重试

- 现象：Seek 无确认后从方保持暂停而主方继续前进；若重试目标持续追逐主方位置，或未确认 Seek 被 Pause/Unpause 覆盖，双方会在快进、暂停和重新同步之间反复分叉。
- 原因：失败时丢失 Barrier 目标会让后续重试重新采样；同一 Session 和 Item 只证明命令目标相同，不代表不同命令可以安全替换。
- 结论：无新的、且不能由当前远程命令解释的锚点位置操作时，Seek 冷却期间保留 Barrier、固定目标和原播放意图；同一 identity 上的不同 Pending 命令拒绝覆盖。若明确观察到锚点的新位置操作，则显式重建 Barrier，候选位置绑定当前 session、Item 和暂停/播放意图。Barrier Seek 的初次发送和重试共享绝对期限，Waiting Pause 仅按 session、item 和能力条件做有界重试。
- 规则：无新操作时不得在 Seek 冷却期重新采样目标，也不得刷新同一失败序列的绝对预算；需要改变序列时必须显式重建 Barrier。该重建针对明确的新操作，不是周期性追帧。
- 验证：`SyncEngineTests` 覆盖固定目标重试、锚点新位置显式重建、候选身份与播放意图、Restore 前双方容差、Pending 冲突、Seek 期间暂停变化、Seek 绝对预算、Waiting Pause 次数上限及条件变化恢复。

## 8. 房间副作用必须在 gate 内重新授权

- 现象：REST 请求在读取快照后，成员可能退出、房间可能删除重建、ServerId 或会话 identity 也可能变化；继续使用旧快照会向错误目标发送暂停或消息。
- 原因：锁外快照只能描述历史状态，不能授权之后的外部副作用。
- 结论：控制、消息和离开后的暂停必须在每房间 gate 内重新确认当前房间引用、joined 成员、ServerId 和目标 Session/Item；退出者不能再次成为副作用目标。
- 规则：任何来自 gate 外的快照只能用于前后 identity 对比，不得单独决定发送命令。
- 验证：`RoomManagerTests` 与 `WatchTogetherServiceRoomTests` 覆盖成员退出、重新加入、房间删除重建、跨服务器和会话切换竞态。

## 9. 启动和更新状态必须按已完成事实发布

- 现象：入口启动失败时提前发布半初始化对象，或更新检查失败后仍保留旧 release，会让 API 和安装动作使用过期状态；安装成功后的保存或重启通知失败也不能按完整成功报告。
- 原因：对象构造、后台启动、release 校验、包安装、状态持久化和重启通知是不同完成阶段。
- 结论：入口仅在同步引擎启动成功后发布运行时对象，并只清理自己拥有的引用；当前版本不可读时更新 fail closed，每次检查先失效旧 release；安装后的持久化或通知失败必须持续保留可见诊断。
- 规则：不得用管理器程序集版本替代不可读的当前插件版本，也不得用新的检查结果掩盖“已安装但待处理”的状态。
- 验证：`WatchTogetherEntryPointTests`、`PluginUpdateManagerTests` 与 `WatchTogetherUpdateTaskTests` 覆盖重复启动/释放、失败清理、版本尾零等价、版本读取失败、旧 release 失效和安装后诊断。

## 10. 嵌入式插件页必须兼容宿主主题变量

- 现象：仅假定完整色 CSS 变量，或用 `prefers-color-scheme` 推断宿主主题时，Emby Theater 3.0.20 的嵌入式页面会出现卡片、按钮、占位符和悬停态对比度异常。
- 原因：宿主可能只提供 HSL 分量变量；系统偏好媒体查询也不等同于 Emby 当前主题，硬编码白底或深色文字会覆盖宿主浅色/深色配色。
- 结论：主题变量应优先使用完整色并兼容 HSL 分量（text/secondary/primary、theme background、button/card、line），由宿主变量决定页面配色。
- 规则：不得用 `prefers-color-scheme` 代替宿主主题判断，不得为卡片、按钮、placeholder 或 hover 硬编码白底/深色文字；必须分别检查宿主浅色和深色下的计算样式。
- 验证：实际客户端主题文件与当前插件实现交叉确认，`PluginPagesTests` 通过，并以浅/深主题本地渲染计算样式复核；尚未在真实客户端加载新 DLL。

## 11. 会话选择必须传入当前时间

- 现象：在线但 `LastActivity` 已过期的旧会话可能与另一位参与者组成无效配对并触发同步。
- 原因：`SessionSelector.Select` 只有传入 `now` 时才执行 `RemoveExpired`；运行时调用必须显式提供采样时间。
- 结论：`SyncEngine.PollOnce` 将本轮 `now` 传入会话选择；管理快照在候选采样后为本次选择捕获当前时间并传入，既有 60 秒过期策略因此在两条路径都生效。
- 规则：默认过期策略仍为 60 秒（`StaleSessionTimeoutSeconds`），`staleTimeoutSeconds` 仍是可传入参数；不得改变 15 秒相对过滤、默认 `LastActivity` 处理或其他选择语义。
- 验证：`SessionSelectorTests` 覆盖过期候选清理；`SyncEngineTests.PollOnce_ExpiredGhostSessionCannotFormPairOrTriggerSync` 覆盖过期幽灵会话不能组成有效配对或触发同步；管理接口路径复用 `BuildSnapshots` 的当前时间采样逻辑，未新增可稳定注入 `LastActivity` 的端到端服务夹具。

## 12. 多会话选择诊断必须低噪声且来自同一决策管线

- 约束：仅在同一房间参与者存在多个原始在线候选时记录 `multiple-session selection`；候选集合、处置阶段或最终选择不变时按签名去重，位置和 `LastActivity` 年龄不进入签名。
- 结论：诊断与 `SessionSelector` 的过期、相对落后、共同 Item、能力排序和歧义处理共用一条管线，记录短 Session/Item identity、位置、暂停状态、年龄（未知时写 `unknown`）及每用户最终选择；离开多会话状态后清除签名，复发可再次记录。
- 验证：`SessionSelectorTests` 与 `SyncEngineTests` 保持既有选择语义，并覆盖首次记录、位置/年龄变化去重、候选或处置变化重记及离开后复发；这些测试不等同于真实客户端复现。

## 13. 多共同 Item 必须全局一致评分并在同分时安全等待

- 现象：参与者各自拥有多个共同 Item 时，若直接按用户分别选择最新会话，可能出现双方交叉选择不同 Item。
- 结论：完成过期 60 秒和用户内 15 秒落后清理后，对每个共同 Item 取每位参与者的最佳 `SelectionKey`，将这些 key 排序为与用户及输入枚举顺序无关的评分向量，按 maximin 选择唯一胜者；其余 Item 走 `common-item-filtered`。全局评分完全相同时标记相关候选 `ambiguous` 并清空本轮选择，禁止按字符串、用户 ID 或输入顺序猜测。
- 规则：评分只比较 `SelectionKey` tuple，不累加 `activityTicks`，保持能力排序、同 session 去重及单用户同分歧义语义不变。
- 验证：`SessionSelectorTests` 覆盖多共同 Item 的全局一致选择、交换输入/用户顺序、能力差异、同分安全失败、过期/落后候选排除及诊断处置；`SyncEngineTests.PollOnce_MultipleCommonItems_SelectsOneItemForBothParticipants` 证明首轮进入 Barrier 时两端命令只绑定全局胜出 Item 的 session。

## 14. Watching 同分会话只沿用唯一有效历史身份

- 结论：运行时在同一用户的最佳 `SelectionKey` 同分时，仅当候选集合中恰有一个候选同时匹配上一轮已绑定的 `SessionId+ItemId` 才沿用；历史身份过期、落后、消失或 Item 不同均不得复活，仍按同分歧义安全等待。
- 规则：公开无偏好的 `SessionSelector.Select` 语义不变；沿用候选标记为 `selected`，其余同分候选标记为 `previous-selection-filtered` 并沿用多会话诊断签名去重。
- 验证：`SessionSelectorTests.SelectWithPreviousDiagnostics_EqualTieReusesUniquePreviousIdentity`、`SelectWithPreviousDiagnostics_DoesNotReuseExpiredOrDifferentItemIdentity` 与 `SyncEngineTests.WatchingTie_ReusesPreviousSessionIdentityWithoutRestartingOrIssuingCommands` 通过；未覆盖真实客户端重连行为。

## 15. 房间副作用部分失败必须如实反馈

- 约束：退出后的自动暂停和群发提示都按目标统计成功、失败与未尝试；单个目标失败不得阻断后续目标，且响应和管理页不得把部分成功伪装成全部成功。
- 规则：目标缺少可信 session、能力或在线状态属于未尝试；命令/提示异常只返回稳定错误代码和汇总计数，不回传底层异常详情。退出状态 `Changed` 始终只反映成员关系转换结果。
- 验证：`WatchTogetherServiceRoomTests` 覆盖暂停返回 false、issuer 缺失、session identity 变化和首个群发目标异常后继续发送；`PluginPagesTests` 覆盖退出确认与成功、失败、中性三类文案。

## 16. 管理结果和底层异常必须走不同反馈通道

- 现象：普通 `Message` 不能稳定显示为 Emby Web 管理页短横条；把底层异常文本写入房间状态又会向参与者暴露传输细节。
- 结论：更新检查、发现版本、安装成功或失败及待重启结果统一通过 `SendMessageToAdminSessions("GeneralCommand", ...)` 发送 `DisplayMessage`，并使用 3 秒超时；命令发送器对外只返回稳定错误码，完整异常只进入服务器私有日志。
- 规则：提示发送失败不得改变已经完成的检查或安装事实，真实取消仍须传播；房间 API 只返回稳定错误码和当前房间两名参与者的受限摘要，不得开放全站用户目录或回传 `Exception.Message`。
- 验证：`PluginUpdateManagerTests`、`WatchTogetherUpdateTaskTests`、`SessionBridgeCommandIssuerTests`、`WatchTogetherServiceRoomTests` 与 `PluginPagesTests` 覆盖结果提示、取消传播、错误脱敏、启动期服务不可用、参与者摘要和非管理员页面边界。
