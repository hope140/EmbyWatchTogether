# 当前架构

本文只描述当前代码已实现的结构；行为细节、API、构建和人工验收入口见 [`technical.md`](technical.md)。[`watch-together-emby-plugin-plan.md`](watch-together-emby-plugin-plan.md) 保留为历史实现说明。架构变更应先更新本文，再评估是否需要 [`adr/README.md`](adr/README.md) 中的 ADR。

当前项目版本目标为 `1.6.1.0`。用户已于 2026-10-18 确认本轮验收通过；正式发布状态由发布流程和 GitHub readback 单独确认。

## 运行时边界

插件目标为 `netstandard2.0`，由 Emby 服务器加载。`Plugin` 暴露插件元数据和嵌入式配置页；`WatchTogetherEntryPoint` 负责启动、配置热更新和停止时释放运行时对象。仓库不依赖外部同步服务。

## 组件与数据流

```text
Emby SessionManager
        │
        ▼
SessionBridge ──> SessionBridgeSnapshotProvider ──> SessionSelector
        │                                                   │
        └──── SessionBridgeCommandIssuer <──────── SyncEngine

Plugin ──> WatchTogetherEntryPoint ──> RoomManager ──> RoomStore (rooms.json)
                                  ├──> RoomInvitationManager (memory)
                                  └──> WatchTogetherService + embedded Web UI
```

- `SessionBridge` 将 Emby 会话和事件适配为快照、命令和立即轮询唤醒；`SendPlayItemAsync` 通过 Emby `SendPlayCommand` 请求当前会话打开指定 Item。内置命令发送器只向调用方返回稳定错误码，完整异常仅写入服务器私有日志。
- `SessionSelector` 为参与者选择有效会话并绑定 session identity，初始绑定优先选择原始远控标志满足 `RoomEligibility` 的会话；只有已进入 `Watching` 的绑定身份才允许使用短时远控恢复，避免旧会话确认新设备命令。
- `RoomManager` 管理房间元数据和每房间 `RoomRuntime`；房间命令、消息目标和离开后的播放副作用在每房间 gate 内重新校验当前房间、成员关系、服务器和会话身份，消息网络发送在退出 gate 后通过 5 秒有界 issuer 执行；`RoomStore` 只持久化房间元数据。房间保留 `CreatorUserId` 和 `IsSelfService`，旧 JSON 缺失字段时回退到旧管理员创建语义。
- `RoomInvitationManager` 保存运行时邀请码的校验值、创建者、名称和有效期，不保存明文邀请码或独立持久化文件。接受操作在其串行边界内调用 `RoomManager` 原子创建完整双人房间，创建成功后清除该创建者的全部待接受邀请；重启会丢弃未接受邀请。
- `SyncEngine` 按轮询驱动每房间状态机，使用独立 gate 串行处理；状态包括 `Waiting`、`Handoff`、`Barrier`、`Watching`、`Recovering`、`Unavailable`。`Handoff` 只负责让参与者打开主用户当前 Item，确认后再转入由 `Barrier` 负责的时间轴对齐；`Recovering` 只等待非 Primary 参与者原 User、原 SessionId 和原 ItemId 的短暂恢复，Primary 缺失继续走停止 debounce 和晚到换集候选。
- `WatchTogetherService` 提供 REST 管理接口并在服务端检查身份、管理员权限和成员关系；运行时尚未就绪时明确返回可重试的服务不可用状态。房间响应只附带该房间两名参与者的受限显示摘要，普通参与者不能借此读取全站用户目录；管理页按钮不是安全边界。邀请码接口只返回当前用户的邀请元数据和稳定状态，接受成功后创建者或管理员可按房间权限结束自助房间。
- `GET /WatchTogether/Rooms/{Id}/Diagnostics` 在当前房间 gate 内重新校验房间、成员和服务器身份，返回只读、有界、脱敏的同步诊断 DTO。诊断事件环、当前选中快照、Pending、Handoff、Barrier、Recovery 和 drift telemetry 仅驻留 `RoomRuntime` 内存，房间删除或运行时重建时丢弃，不写入 `rooms.json`；导出使用 `userA`/`userB` 别名、短 hash 和稳定错误分类，不包含用户名、GUID、Token、路径或异常文本。

## 嵌入式 UI

UI 由原服务器单页的嵌入式 HTML 和 AMD JavaScript 实现，保留创建房间、邀请、房间、说明、设置的原有顺序。内容区复用宿主 `readOnlyContent auto-center` 的限宽与对齐规则（当前宿主为 `80ch`；展开侧栏时宿主覆盖为靠左），窄屏随可用空间收缩。`Plugin.GetPages()` 注册一个页面入口及其控制器，使用 `WatchTogetherSettingsV7` / `.js` 缓存键；原有 `emby-input`、`emby-select`、`emby-checkbox`、`emby-button` 由宿主提供外观，动态按钮使用相同原生初始化并保留构造器附加的样式类。按钮显式设置 flex 内容居中；普通操作使用原生 `raised raised-outline`，轮廓色来自已有宿主文字派生变量 `--wt-muted`，主操作保持 `raised button-submit` 主题填充；服务端继续决定所有读取和操作权限。

页面沿用原房间列表、邀请、设置与按需诊断读取流程；Phase E 的恢复/漂移诊断保留，`/State` 的只读扩展仍向后兼容。Token 使用现有独立私有存储和 API，UI 不回显已有 Token，离页清空输入。房间操作、确认、设置保存和权限可见性沿用原页面流程；详细控件边界和验证见 `native-controls-plan.md`。

## 状态与持久化

起播 `Barrier` 按 Pause → Seek（仅非锚点用户）→ Restore 执行，远程命令等待会话快照确认；进入 Restore 前，锚点和另一端都必须在固定 Seek 目标的容差内。Seek 未确认时保留原目标和原播放意图，并在同一 Barrier 的绝对预算内重试；只有检测到锚点有当前远程命令无法解释的明显新位置操作时，才显式重建 Barrier。正常 `Watching` 期间传播明确暂停/继续和明显手动 Seek，持续明显漂移满足门槛时触发低频 Barrier；同一 Session/Item 的原始远控标志短暂丢失且仍有有效能力证据、没有 Pending 命令时，使用绑定身份和受影响用户集合的 8 秒内存恢复窗口，期间不发送命令，恢复后继续观察，超时或条件变化仍进入严格等待。

主用户在 `Watching` 中从 Item A 切换到 Item B 时，`SyncEngine` 先记录主用户 Item 变化并进入独立的 `Handoff` runtime。该 runtime 绑定目标 Item、主用户 Session identity、参与者 Session identity 和 generation。参与者尚未位于 B 时，通过 `IPlayItemIssuer` 发起一次有界的 `PlayItem(B)`，随后只接受当前参与者用户、当前 Session 和 `SessionSnapshot.ItemId == B` 的确认；目标变化会使旧 generation 失效。参与者已经位于 B 时跳过 PlayItem，直接进入 `Barrier`，由 Barrier 重新暂停、定位和恢复播放意图。PlayItem 请求本身的成功不等于播放器完成打开，确认超时或有限重试失败会清理 Handoff 并回到安全的 `Waiting`。

`PlaybackStopped` 只唤醒轮询。主用户停止 A 后，现有 2 秒停止 debounce 同时作为有限的媒体切换观察窗口；窗口内选中 B 时识别为 Handoff，不执行普通停止副作用。窗口结束仍无新 Item 时继续原有 Stop 行为，但从首次缺失开始保留最多 10 秒的仅内存、身份绑定候选，停止确认后若主用户以可信会话打开 B 且参与者仍以原可信会话停留在 A，仍可恢复到同一 Handoff；候选过期、参与者换 Item/会话、能力不可信或房间成员改变时清除。参与者自行切换 Item 不触发主用户跟随；只有显式 Participant Resync 请求才会让非 Primary 参与者通过同一 Handoff/Barrier 流程回到主用户当前 Item。运行时快照、Pending 命令、Handoff、恢复窗口和 Barrier 阶段不写入 `rooms.json`；房间文件采用候选文件替换并保留备份，损坏时报告错误而不静默覆盖。

Phase E 在现有 `Watching` 逻辑旁增加独立的 `Recovering` runtime。仅非 Primary 参与者从选中快照中短暂消失时最多等待 10 秒；缺失会话按原 User、原 SessionId 和原 ItemId 恢复时重新执行一次现有 Barrier。Primary 缺失、停止、Session 或 Item 变化会先退出 Recovery，再由既有停止 debounce 或晚到换集 Handoff 处理；参与者替换 SessionId 或其他不受控的 Item 变化会回到 `Waiting`，不自动换绑设备。双方 actively playing 且无 Pending/Handoff/Barrier/Recovering 时记录 `Participant.PositionTicks - Primary.PositionTicks` 的 signed drift；绝对 drift 至少 3 秒并持续 5 秒才触发一次 Barrier，自动纠偏之间冷却 120 秒，不执行连续追帧式 Seek。

## 发布信任边界

更新组件由 `GitHubReleaseClient`、`PluginUpdateManager`、`ReleaseSignatureVerifier` 和 `ReleaseTrustStore` 协作：默认 `stable` 通道从固定的 `releases/latest` 资产读取，管理员选择 `beta` 后先从公开 Releases API 的非 draft stable 与 prerelease 中选择最高规范版本，再构造本仓库规范数字 tag 的固定资产地址；API 返回的下载地址不作为安装来源。两条通道最终都校验 manifest 的版本、大小、SHA-256、tag 与 detached RSA 签名，未知 key、当前插件版本不可读或校验失败时 fail closed；GitHub 资产下载允许其官方 CDN 重定向，但不会因此放宽内容验签。每次检查会使旧的已验证 release 缓存失效。检查、发现版本、安装成功或失败及待重启等结果统一通过 Emby Web 管理端的 `GeneralCommand` / `DisplayMessage` 短提示反馈；提示发送失败只记录日志，不改变已经完成的检查或安装事实。发布 workflow 负责构建和资产发布，不负责服务器部署。

管理员可在嵌入式管理页保存可选的 GitHub fine-grained token。Token 仅用于 beta Releases API 请求，stable 查询和两条通道的固定资产下载均不携带 Token；未配置 Token 时 beta 继续使用匿名 API。Token 保存在独立的私有存储中，不进入通用插件配置或页面回显。

## 证据与维护

上述结论来自 `src/EmbyWatchTogether` 的入口、房间、会话、同步和发布信任实现，以及对应 `tests/EmbyWatchTogether.Tests` 测试。新增跨组件约束时先补测试或文档证据，再更新本文；稳定决策另建 ADR。
