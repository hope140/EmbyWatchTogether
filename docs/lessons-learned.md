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
- 独立下载核验时，`.release.manifest.sig` 为严格 Base64 ASCII 文件，必须先按项目格式解码，再对 manifest 原始字节执行 RSA PKCS#1 v1.5/SHA-256 验证；不能把签名文件的文本字节直接当作 RSA 签名。此规则由签名脚本、验证器和 v1.7.0.1 公开资产交叉核对。
- 规则：不得把 installer 的 MD5 二次校验当作信任根，也不得把私钥或 secret 写入仓库文档。
- 验证：`ReleaseTrustStoreTests`、`release-signing.tests.ps1` 与 `release-workflow.tests.ps1`。
- 补充规则：beta 更新检查应先在 GitHub API 返回的非 draft stable 与 prerelease 中选择最高规范版本，再验证该版本的固定资产；历史低版本缺少 manifest 或 signature 不得阻断最高版本选择，但最高版本自身缺少固定资产仍必须 fail closed。当前 GitHub API 的 35 个 Release、早期 `v1.0`/`v1.1`/`v1.2` 资产情况，以及 `GitHubReleaseClientTests` 的两个选择/缺资产测试共同确认了这一边界。
- 补充规则：Releases API 查询与固定资产下载使用不同的有界超时；固定资产（DLL、manifest、signature）应允许 60 秒，Releases API 仍为 20 秒。该边界由服务器日志、pinned `MediaBrowser.Common` 反射确认的默认 `TimeoutMs=20000`、NAS 上官方 DLL 下载耗时 28.34 秒，以及 S5 候选实际运行更新检查任务完成共同验证；签名、哈希、来源/身份校验和失败清理仍必须保持不变。

- 补充规则：beta 枚举应跳过历史非规范 tag，再选择最高规范非 draft 版本；只含非法或 draft 候选时仍拒绝更新。最终最高候选缺资产或签名错误不能回退低版本，规范 tag 和固定下载地址的校验规则不变。验证：GitHubReleaseClientTests 的历史标签混合/反序、全非法或 draft、最高签名失败及最高资产缺失用例。

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
- 结论：`PlaybackStopped` 只用于唤醒轮询；停止只依据 SessionSelector 选中的当前会话判断。同一用户的旧 stopped Session 不能覆盖当前有效播放；当前会话的 `Stopped=true`、离线和缺失统一要求连续 2 秒快照异常，恢复有效快照时清除计时并保持 `Watching`。主用户缺失开始后，额外保留最多 10 秒的主用户换片候选；停止确认后若主用户以可信会话打开新 Item、参与者仍以原可信会话停留在旧 Item，则恢复既有 Media Handoff。
- 规则：任何新增停止信号或未选中的候选会话都不得绕过当前会话选择与确认窗口；位置归零仍按正常 Seek 处理。确认停止后，暂停和通知副作用只执行一次。迟到换片候选必须绑定房间成员、用户/session identity、原 Item 和远控能力；参与者换 Item/会话、能力不可信、房间生命周期改变或候选超过 10 秒时立即失效。
- 验证：真实 Emby Theater 与 embyToLocalPlayer 日志复现了短暂停止后恢复，以及旧 stopped Session 与当前播放并存导致的误判；`SyncEngineTests` 覆盖同用户同 Item 的旧 stopped Session、短暂 stopped/缺失恢复、持续异常确认、seek-to-zero，以及主用户换片在 2 秒同轮、停止确认后 4/7 秒恢复和 10 秒过期。

## 7. Seek 失败必须冻结原目标并有界重试

- 现象：Seek 无确认后从方保持暂停而主方继续前进；若重试目标持续追逐主方位置，或未确认 Seek 被 Pause/Unpause 覆盖，双方会在快进、暂停和重新同步之间反复分叉。
- 原因：失败时丢失 Barrier 目标会让后续重试重新采样；同一 Session 和 Item 只证明命令目标相同，不代表不同命令可以安全替换。
- 结论：无新的、且不能由当前远程命令解释的锚点位置操作时，Seek 冷却期间保留 Barrier、固定目标和原播放意图；同一 identity 上的不同 Pending 命令拒绝覆盖。若明确观察到锚点的新位置操作，则显式重建 Barrier，候选位置绑定当前 session、Item 和暂停/播放意图。Barrier Seek 的初次发送和重试共享绝对期限；Waiting Pause 必须按参与者自身的 session、item 和能力条件确认，即使双方 Item 不同也不能误清有效 Pending，并对暂停确认后的再次播放保持有界重试。
- 规则：无新操作时不得在 Seek 冷却期重新采样目标，也不得刷新同一失败序列的绝对预算；需要改变序列时必须显式重建 Barrier。该重建针对明确的新操作，不是周期性追帧。
- 验证：`SyncEngineTests` 覆盖固定目标重试、锚点新位置显式重建、候选身份与播放意图、Restore 前双方容差、Pending 冲突、Seek 期间暂停变化、Seek 绝对预算、Waiting Pause 次数上限、session/item/能力变化恢复，以及同一 Item 恢复后重新进入 Barrier。

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

## 17. 缓存能力不能证明远控连接仍然可用

- 现象：已进入 `Watching` 的 embyToLocalPlayer 会话在远控 WebSocket 被重置后，原始 `SupportsRemoteControl` 先变为 `false`，缓存命令列表仍让有效能力报告为 `true`；普通 HTTP 进度继续上报，但 Pause 在重连前无法送达，插件立即暂停另一端会造成短暂分叉。
- 结论：缓存能力只证明该会话曾声明远控命令，不能证明当前 WebSocket 可用；全局资格和初始 Barrier 继续使用严格规则。只有身份连续、同一有效媒体、没有 Pending 且仍有有效能力证据的既有 `Watching` 会话可以等待最多 8 秒恢复，窗口内不发送命令。
- 规则：恢复窗口必须绑定 SessionId、ItemId 和受影响用户集合，不得刷新同一故障的起始时间；身份、Item、有效能力、Pending 或用户集合变化立即退出，超时仍执行现有安全暂停。窗口内真实暂停或 Seek 在能力恢复后按保留快照最多处理一次。
- 验证：两次真实 ETLP/Emby 日志均显示 HTTP 进度正常、WebSocket `ConnectionResetError`、重连后重新声明能力并恢复命令；`SyncEngineTests` 覆盖短暂恢复、8 秒超时、初始 Waiting、Session/Item 变化、Pending、有效能力消失、恢复后的 Pause/Seek 以及重置清理。真实测试版客户端验收仍待执行。

## 18. 嵌入式页面更新时必须同步页面缓存键

- 现象：只替换嵌入式 HTML 或 JavaScript 资源时，Emby Web 可能继续使用旧缓存，导致新界面与旧控制器不匹配。
- 结论：修改嵌入式管理页时，同时递增 `Plugin.GetPages()` 中 HTML 页面和 JavaScript 控制器的 `Name`，并让 HTML 的 `data-controller` 使用新的控制器键；对应页面测试必须检查三者一致。
- 验证：GitHub Token 管理区加入后，页面键和控制器键同步更新为 `WatchTogetherDiagnosticsV2` / `WatchTogetherDiagnosticsV2.js`，`PluginPagesTests` 检查嵌入资源与控制器声明。

## 19. 页面账号变化必须同步使读取和操作失效

此节的 generation/menu 实现与浏览器证据属于历史 UI VNext。S116 按用户确认恢复原服务器单页控制器，不将这些历史证据记为当前页面已实现或已验收。

- 现象：为 Token 加入迟到响应隔离后，单独清空输入框仍会保留旧账号的房间、邀请和管理员视图；若只在读取回调中检查账号，切换后的第一下操作还可能使用旧页面对象。
- 规则：页面 mutation 入口与异步回写都核对当前账号，区分 SDK 身份未知与已知登出；身份变化使整个页面 generation 失效、停止轮询、清空旧数据并禁止交互，正常 resume 后重新加载权限。document 级菜单监听在 pause 时解绑，不能在每次子视图切换时重复注册。
- 验证：UI 浏览器测试覆盖账号变化后立即触发旧邀请/房间动作且零新增 POST/DELETE、迟到 Token 响应、正常 resume 恢复，以及十次 pause/resume 后仅有一组活跃菜单监听。模拟宿主测试不等于真实 Emby 多账号验收。

## 20. 参与者恢复不能抢占主用户停止与晚到换集

- 现象：独立 Phase E 的任意会话缺失恢复与 beta 主用户晚到换集对同一输入有冲突；只解决 Git 文本冲突会使原晚到换集场景停在 `Recovering`。
- 当前决策：主用户缺失继续使用 2 秒停止观察与 10 秒晚到换集候选。Recovery 只处理非主用户参与者缺失；其间主用户缺失、停止或身份/媒体改变时，在当轮停止/Handoff 判断前退出 Recovery，保留原有 Previous 身份供 beta 路径判断，不通过 Recovery 接管替换会话。
- 验证：原晚到换集用例、参与者替换 Session 拒绝用例保留；交叉测试覆盖参与者恢复期间主用户原始记录缺失或明确 stopped、停止确认单次暂停，以及 4 秒内新 Session/Item 回到 Handoff。ADR-003 记录 2026-10-18 的角色边界修订。

## 21. 原生控件调整先核对现有声明与外观覆盖

- 事实：原服务器单页已有 `emby-input/select/checkbox/button` 声明；按钮背景、边框、圆角、hover 和输入颜色的自绘 CSS 覆盖了宿主外观。动态诊断导出按钮尚需补原生初始化。
- 规则：控件外观任务应在确认的 UI 基线上调整属性和样式；原生声明已存在时优先剥离对应覆盖，保留原容器结构、区域顺序和交互。
- 验证：S116 与 `f294c67` 的 HTML/JS 差异限定为控件外观、动态按钮初始化、缓存键和恢复原因文案；页面/浏览器测试区分原结构与历史三页布局。真实宿主与人工双端验收另行记录。

## 22. 动态原生控件必须保留构造器添加的样式类

- 现象：真实 Emby 中静态按钮含 `emby-button emby-button-focusscale`，动态房间按钮只有 `button-flat wt-action`，出现 Arial 和浏览器默认 `2px outset` 边框，即使其 `is="emby-button"` 属性正确。
- 原因：动态控件构造后再赋 `className` 覆盖了原生构造器添加的类。仅断言 `is` 属性或使用不模拟构造器的夹具不能发现这个问题。
- 规则：为动态原生控件添加业务类时使用 `classList.add`，保留构造器状态；夹具应模拟原生类的构造行为，验收还需核对真实宿主中的类和计算样式。
- 验证：S116 首次候选真实宿主的静态/动态按钮类、font 和 border 对比定位此问题；修正与后续宿主结果见 `native-controls-plan.md`。

## 23. 原生宽度和按钮必须复用当前宿主实际定义的类

- 事实：此次 NAS 宿主的 `modules/layout/layout.css` 将 `form, .readOnlyContent` 限为 `80ch`，`.auto-center` 负责双侧 auto margin，路由模板在 padded 容器内使用 `readOnlyContent auto-center`。只有 padded 容器不会自动套用此限宽。`layout_nontv.css` 的 `.page-withDockedDrawer .auto-center` 将侧栏布局的 margin 覆盖为 0；真实 Web 测得宽 727.19px 且靠左，这是宿主规则，不能凭类名宣称所有宿主状态均居中。
- 事实：当前宿主 `emby-button.css` 提供控件基础结构并继承文字色；浅色及深色渐变主题由 `.raised` 同时定义背景和文字，`.button-submit` 进一步应用主题强调色。旧 `button-flat` 没有提供相同的主题背景配对，用户截图出现浅底白字。
- 规则：复用原生结构和类之前核对当前宿主 CSS；避免只保留原生属性而缺失主题类。主题按钮含透明色时，对比度检查应合成 alpha 与下层表面，不能把透明白色当不透明白色，也不能忽略文字 alpha。
- 验证：真实宿主文件只读检查与用户截图、S116 当前类交叉确认；S117 通过回归及真实宿主复核分别记录，模拟渐变代表区域不等同于所有客户端背景位置验收。

## 24. 原生 flex 按钮的文字居中与边界需要独立验证

- 事实：宿主 `.emby-button` 使用 inline-flex 和 align-items:center，但没有 justify-content；`.raised` 的 text-align:center 不负责 flex 主轴分布。设置最小宽度或窄屏拉伸后，真实房间按钮文字横向偏左约 4.16px。
- 修正：只补插件作用域的 justify-content/align-items/text-align:center，保留宿主 display、字体、padding、焦点与禁用行为。普通按钮采用已有原生 raised-outline，通过宿主派生的文字色强化轮廓，不需要自绘按钮背景。
- 验证：原 V6 源码快照在真实 inline-flex 夹具中出现 7.71px 文字偏移，新增对齐回归失败；描边与文字分别使用 RGBA 合成后测量边界和文字对比度，不能用文字清晰代替轮廓可辨。S118 的最终检查与真实宿主证据见 `native-controls-plan.md`。

## 25. 原生标签必须在权限确定后注册并核对所有权

- 事实：当前宿主 `mainTabsManager.setTabs` 对相同 owner 返回 `replaced:false`，不会重建标签列表。权限未知时先注册两页，随后确定管理员再调用相同 owner，不会自动增加设置页。
- 规则：权限判定后首次注册；重建或离页前以 `getTabsElement()` 的元素引用确认当前标签仍属于本页，再调用 `setTabs(null)`。不能清掉其他页面已经接管的导航。
- 验证：真实宿主模块只读核对，S122 fixture 模拟 owner 行为，浏览器覆盖管理员三页、普通用户两页、权限失败与重复进入。

## 26. 房间片名展示需要状态、权限与新鲜度共同约束

- 事实：`/State` 是重新选择 Session 的非原子读，Waiting 停止观察期间可保留旧 SyncItemId。媒体元数据慢响应也可能晚于下一次房间状态变化。
- 规则：State/StatusReason/Eligible/两名已加入成员/在线同 Item/SyncItemId 必须共同满足；通过当前调用用户的 Emby 媒体接口取名，禁止空 userId 导致全局 Items fallback。State 与媒体请求独立，旧媒体目标、身份或页面代次不得回写；过期或失败清除片名。
- 验证：S122 浏览器用例覆盖失效 gate、403/404、错误响应 Id、账号/服务器切换、pause、慢媒体、换 Item、房间删除、10 秒状态新鲜度及文本注入。媒体只进入界面，不加入诊断导出；未将模拟媒体数据记为真实双客户端验收。

## 27. 背景图片加载也必须绑定当前媒体与权限

- 事实：当前 SDK 的 BaseItemDto 提供自身及父背景标签；宿主 `getImageUrl` 构造图片地址。URL 构造器会将 null/undefined 转成相对路径，不能用解析成功替代返回值类型检查。
- 规则：只从当前用户获准读取、Id 匹配的 DTO 选取背景；检查 SDK 返回非空字符串、协议、origin 和嵌入式凭据后才启动图片请求。load/error 必须核对页面、view、当前目标及 URL；状态失效和离页清除 src。图加载成功后才应用图上文字配色，失败保留有效片名。
- 验证：V10 浏览器检查包含自身/父背景、缺图/404、非法地址零网络请求、迟到图片、权限撤回和图片标识不进入诊断导出；不将模拟图片成功等同于真实双端播放。

## 28. 媒体区域的比例与最小高度会影响窄屏宽度

- 事实：V10 初次布局使用 16:9 比例与 255px 最小高度时，390px 页面中媒体区最小宽度约 453px，产生横向溢出。
- 修正：使用 width:100%、min-width:0、height:auto 和最小高度；长标题允许撑高，图像与遮罩单独继承圆角。不通过隐藏横向溢出掩盖内容裁剪。
- 验证：修正后 320–1920px、390px 长标题和浅深三页布局检查通过。

## 29. 房间头像使用成员身份与独立图片生命周期

- 事实：等待播放时成员仍有效，头像不能依赖媒体同步资格；房间状态更新会重建参与者节点，缺失头像若仅在节点内记录失败会反复请求。
- 规则：仅对当前房间两名成员与受限摘要共同包含的用户生成同服务器头像地址，不额外查询用户资料。图片事件校验页面代次、账号、服务器、view、当前成员与 URL；离页或移除清理 src。代次内记录失败，下一代次允许重新尝试；失败保留首字。
- 验证：V11 七项浏览器检查覆盖授权成员、等待、403/404 与失败抑制、无效接口/URL、摘要缺失、旧事件与身份切换和诊断隔离；真实 Emby 已显示一位参与者的头像以及另一位的首字回退。

## 30. 拒绝旧请求回调时必须同步清理静态操作按钮

- 事实：页面 generation 守卫会阻止旧请求链的成功、失败和最终清理回调。静态创建/接受按钮复用 DOM 时，仅依赖请求链尾部解除 busy，会在离页返回后永久禁用。
- 规则：生命周期清理恢复静态按钮文字并移除 aria-busy，再由新页面的权限与表单规则判断可用性；保留旧回调隔离，不能用允许旧 finally 回写来解锁按钮。
- 验证：创建邀请码、接受邀请、管理员创建房间三项浏览器回归修正前均失败、修正后通过；同时确认返回后可重试，旧响应不会解除新请求的 busy，也不会自动重发 POST。
- 同类事件边界：DOM 节点移除后仍可能有已排队的 details toggle；读写新页面状态前需要身份、generation 和当前 panel 引用校验。新增回归修正前检测到旧面板触发的 Diagnostics 查询。

## 31. 插件分页不能依赖旧宿主的 Dashboard 全局

- 事实：2026-10-19 只读打开当前 Emby Web 4.10.0.40 的 V12 页面，控制台出现 `ReferenceError: Dashboard is not defined`；调用链为 `nativeTabs → applyRoute → setAdminVisibility → clearPageData → onResume`。初始化中断时保留“正在加载…”；同一清理函数也由 onPause 调用，会阻断离页。原 fixture 主动注入 Dashboard，掩盖了这个兼容问题。
- 修正：S124 V14 候选构造与当前宿主 `pluginManager.getConfigurationPageUrl` 相同的相对链接 `configurationpage?name=`，并编码当前账号和服务器参数；默认 fixture 不再注入 Dashboard。页面、控制器注册、HTML 声明与浏览器测试同步更新缓存键。
- 真实宿主返修：首次 V13 部署后，页面初始化恢复，但 emby-linkbutton 将 `#!/configurationpage` 链接按 anchor pathname 改写为 `#!/index.html/configurationpage`，切页无效。V14 改为宿主相对链接；fixture 导航改为解析 anchor pathname/query 后交给路由，不能再原样复制 href 到地址栏掩盖这种差异。
- 下拉规则：`fillSelect` 仅在选项 ID、名称或顺序变化时重建 option，避免 5 秒刷新、窗口 focus 刷新和房间名称输入替换打开中的主用户菜单。保留当前有效选择，参与者改变使旧选择失效时回退首项；继续使用宿主 emby-select，不自绘箭头或选择菜单。
- 验证：缺少 Dashboard 的初始化/切页/离页回归、保留未变化选项及参与者变化回归，均验证修正前失败、修正后通过；完整浏览器 84/84、.NET 545/545，Release 构建打包通过。
- 边界：用户观察到客户端三个下拉框当时均不能打开、重启后恢复；本轮没有复现该客户端卡住的原始过程，不能把选项重建修正记为其已证实根因。V13 曾部署并由真实宿主检查发现上述切页问题，V14 的部署/验收以本地 receipt 为准；没有架构或后端同步语义变更。

## 32. 原生单选模式与跨宿主箭头定位需要分别核对

- 事实：当前 Server Web 与已安装 Theater 的 `enableNativeMenu` 均会在普通单选、非 TV 且未指定 `data-menu="custom"` 时使用系统菜单。2026-10-19 用户最终以 Madby“元数据语言”的普通单选截图确认目标；房间参与者、主用户和更新通道沿用这种默认单选模式。较早的多选参考不能作为强制主题浮层的依据。
- 箭头差异：当前 Web 在独立 `.emby-select-wrapper` 内用 top/bottom 与 flex 居中；旧 Theater 把 label、select、带隐藏占位的 `.selectArrowContainer` 放在同一父节点，依赖顶部偏移和 margin-top。客户端 GenericEdit 适配层已有基于控件 grid 行的修正，但其作用域不覆盖本插件页面。
- 修正：V16 四个 select 与 Madby 普通单选一样使用 `is="emby-select"`，菜单由宿主默认规则选择。插件自己的 `.wt-selectField` 仅将旧版直属 label、select、说明与原有箭头放到对应 grid 行，隐藏旧箭头占位并清除其 margin-top；新版 wrapper 内的箭头继续由宿主管理，不绘制第二个箭头，不改客户端安装文件。V15 曾错误地强制 custom，现已按用户最终参考撤回。
- 验证：菜单模式与旧版 390px 长标签几何回归修正前失败，后者偏差约 64.88px；修正后旧/新两种 DOM、390/1280px 与四个控件的中线偏差小于 1px。完整浏览器 89/89、.NET 545/545，构建打包通过。fixture 只模拟两种实际结构及定位规则，不模拟真实菜单运行；真实 Web 和客户端证据必须单独记录。
- 真实 Web 对照：神医与 Madby 的表单由 `genericui / GenericEdit` 生成。神医“搜索范围”为 multiple select，展开的是带复选框的虚拟滚动 actionSheet；Madby“元数据语言 / 图片语言 / 国家地区”为普通单选，未指定 custom，沿用系统单选菜单。不能用前者的多选展开形态定义全部原生单选的默认外观。V15 一起看显式 custom 的单选使用同一宿主 actionSheet 的单选形态；其闭合态实测高度约 34.615px、字号 14.424px、内边距与 Madby 单选相同，Web 箭头偏差约 0.0052px。这些测量只证明相应几何与组件事实，最终外观反馈见下条。
- 最终反馈：用户在 V16 部署后确认“可以了，进行下一步”。本轮最终普通单选外观与箭头方案据此记为用户确认；该反馈不扩大为原始客户端临时卡住已复现、真实双端播放已验收或公开发布授权。后续本地 1.7.0.1 包和已部署的 1.7.0.0/V16 候选保持身份区分。

## 33. 取消令牌与有界等待是两个约束

- 现象：底层返回不完成的 Task，或在返回 Task 前同步阻塞时，仅 CancelAfter 不能释放调用方。
- 结论：内置发送器使用独立有界等待，工厂在实际开始前核对已用时间与取消状态；计时不完全依赖线程池回调。
- 规则：不响应取消的传输保留对应会话通道槽位并限制总量；播放控制与 PlayItem 共用通道，DisplayMessage 独立占位，两类通道共享 issuer 的 64 个未完成传输上限。迟到成功或失败只清理资源、观察异常，不回写房间状态。Task 完成发布与 continuation 清理不是原子操作，下一次准入要依据 Task.IsCompleted；旧清理不能删除后续传输的新槽位。
- 验证：SessionBridgeCommandIssuerTests 的不协作 Task、同步阻塞、排队过期、容量和清理竞争用例，以及 SyncEngineTests.PollOnce_UncooperativeTransportTimesOutAndNextRoomStillRuns。

## 34. 确认停止要显式终止旧 Pending 序列

- 现象：主用户继续后立即停止，停止观察窗口跳过普通 Pending 确认，已执行的 Unpause 仍可能挡住停止后的 Pause。
- 结论：只有确认停止的终态转换先清除旧 Pending，再执行停止副作用；其他同步路径继续保留原有 Pending 冲突保护。
- 验证：SyncEngineTests.ConfirmedStop_AcknowledgedUnpauseDuringDebounceDoesNotSuppressPeerPause，以及 Pause/Seek、选项关闭、替换会话与单次副作用用例。

## 35. 展示读取也要避免可变字段的重复解引用

- 约束：Rooms 和 State 允许非原子展示读取，但一次状态原因计算应只捕获一次 Error，再使用局部值判空和分类。判空后重新读共享属性可能遇到同步线程清空，产生空引用异常。
- 验证：WatchTogetherServiceRoomTests.StatusReason_ConcurrentErrorChangesDoNotThrowForRoomsOrState 实际覆盖两个 API 入口并枚举房间响应；既有稳定原因映射用例保持有效。
- 相关边界：Handoff 的 PlayItemPending 独立于普通 Pending。手动 pause/resume 的同步冲突检查应包括 Handoff 对象；RoomManagerTests.Action_PauseOrResume_HandoffConflictDoesNotIssueOrMutate 验证零发送、稳定错误和运行时不变。

## 36. 安装内部状态不能由旧设置回写覆盖

- 现象：设置页读取配置后，安装器写入待重启版本，迟到的设置 POST 又清掉 pending，下一轮任务重复安装同一版本。
- 规则：设置替换和 pending 持久化共用配置锁；设置请求只更新用户设置，保留当前内部 pending。安装器在锁内取得当前对象并保存，不持有旧配置对象跨越替换边界；初始配置为空时先建立 BasePlugin 所持有的配置对象。
- 验证：PluginConfigurationTests 的保留/伪造 pending 与不可变事件测试，WatchTogetherUpdateTaskTests 的旧设置重放、双向屏障交错及初始空配置用例。对应核心回归在原基线失败、修复后通过。补充第 9 节的已完成事实约束，无需新增 ADR。

- 待重启版本也是更新下限：候选必须同时高于运行中版本和有效的已安装待重启版本，三段与四段的尾零版本视为相等，避免切换通道后用较低版本覆盖已安装 DLL。保存失败时以内存 pending override 为准；后续更高版本安装成功后，该 override 也必须前进，已有安装诊断仍保留。
- 验证：`PluginUpdateManagerTests.PendingVersion_*` 覆盖手动/自动检查与安装、尾零等价、保存失败后的重复/降级拒绝及更高版本安装后的状态刷新；新增 13 项中 11 项在旧实现失败，修复后全部通过。此证据来自安装器 mock，不等同于真实 Emby 更新验收。

## 37. Barrier 可以省阶段等待，不能省命令后的快照确认

- 事实：原正常快路在 Pause 确认后和 Seek 满足后各等待一轮才执行下一阶段。改为有界的 Pause → Seek → Restore 同轮推进后，理想下一轮确认的双端播放夹具由 5/6 轮缩短为 3/4 轮；已暂停且对齐时一轮即可进入 Watching，无需发送重复 Pause。
- 约束：仅当前快照已满足且无 Pending 时跳过命令；实际发送后结束本轮阶段推进，保持后续快照确认、身份绑定、暂停后重新锚定、固定 Seek 目标和绝对重试预算。手动 Seek 在检测当轮启动 BarrierTick，仍不能用本轮旧快照确认刚发出的 Pause。
- 验证：SyncEnginePerformanceTests 的阶段轮数、已暂停快路、未确认 Pause/Seek 和当轮手动 Seek 用例；新增三条阶段回归在逻辑修复基线失败。既有 Seek 失败测试需在 Pause 确认前注入失败，否则会错过提前后的发送轮次。
- 证据边界：轮数来自确定性夹具，周期轮询与事件唤醒、客户端上报和网络耗时会共同影响实际速度，不能直接换算成真实客户端收益。

## 38. 异步提示需要有界资源、持续过期校验和独立通道

- 规则：每引擎一个提示 worker、64 条等待消息；队满丢弃新提示，超过 10 秒寿命丢弃。寿命使用单调时间，并在出队后、等待 room gate 后和网络发送前重查；墙上时钟跳变不应改变排队寿命，当前时间仍用于会话过期选择。
- 规则：排队时的身份只能作为目标绑定，发送前重新选取当前会话并核验原房间引用、服务器、joined 成员、Session/Item、能力、快照保护、通知设置和停止状态；消息网络发送在 gate 外执行。停止清空等待队列，取消在途工作而不等待不协作传输或取消回调。
- 集成约束：仅移出轮询线程仍不够；若 DisplayMessage 与播放控制占同一会话槽，慢提示仍可能使 Pause/Seek 被误判为超时。内部通道前缀隔离占位，Emby 接收原 SessionId，总未完成传输容量继续受限。
- 验证：SyncNotificationDispatcherTests、SyncEnginePerformanceTests 的容量、异常恢复、边界过期、等待 gate、身份失效与 Stop/Dispose 用例，以及 SessionBridgeCommandIssuerTests 的双向通道隔离、容量和迟到清理用例。取舍见 [ADR-005](adr/005-sync-notification-dispatch.md)。

## 39. Waiting Pause exhaustion belongs to each target condition

- Same-item snapshots alone do not prove remote-control eligibility recovered. Clear the exhausted Waiting cycle only when the complete pair is eligible for a new Barrier.
- Waiting stale commands are already discarded individually. A whole-runtime reset on one stale Session/Item must not renew an unchanged peer's exhausted budget; retain per-target identity/capability pruning.
- Evidence: SyncEngineTests.WaitingPauseRetryLimit_SameItemRemoteFailureRemainsBounded covers immediate failure, accepted/no ACK, repeated polls, late paused status and full recovery. OnlyChangedTargetGetsNewBudget covers Session/Item/capability changes with immediate failure and unacknowledged transport success. The original same-item loop and stale Pending reset both fail their corresponding regressions before the guards.
- Explicit user resync is an intentional new cycle. These deterministic tests do not establish the network/client root cause or continuous playback after Unpause. No architecture or ADR update is needed.
- CI fixture isolation: SyncEngineTests and SessionBridgeCommandIssuerTests deliberately block ThreadPool work while asserting 100ms deadlines. Place them in a nonparallel Transport timing collection so other test classes cannot expire transport work before its entered signal. Keep the existing deadlines and assertions; this does not alter production scheduling.

## 40. Operation-bound notices and provably stale control snapshots

- Routine seek/pause/resume stages do not each need a toast. Report one completed alignment per participant; bound retry attempts by the alignment operation and discard queued notices after operation or stage changes. Already dispatched transport cannot be recalled.
- LastActivityDateUtc is not a playback sequence. Only reject strictly older activity in an eligible, complete, identity-stable Watching pair with no Pending. Never let this guard hide missing/stopped users, identity changes, eligibility loss or Pending deadlines. Equal/new timestamps keep real user controls, including the unavoidable fresh-timestamp replay ambiguity.
- SyncToastTests and SyncToastReplayTests cover normal seek, pause-drag-resume, repeated polls, delayed confirmation, operation/stage retirement, older/equal/new timestamps and identity/capability negative cases. These fixtures are not evidence of the user's real network/client cause.
