# 当前架构

本文只描述当前代码已实现的结构；行为细节、API、构建和人工验收入口见 [`technical.md`](technical.md)。[`watch-together-emby-plugin-plan.md`](watch-together-emby-plugin-plan.md) 保留为历史实现说明。架构变更应先更新本文，再评估是否需要 [`adr/README.md`](adr/README.md) 中的 ADR。

当前正式版为 [v1.7.1.0](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.7.1.0)，当前公开测试版为 [v1.7.2.3](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.7.2.3)。测试版的真实双端播放验收尚待执行；各版本的事实和边界见[发布日志索引](releases/README.md)。

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

内置 SessionBridgeCommandIssuer 通过 BoundedSessionDispatch 执行播放、PlayItem 和消息传输。独立的 5 秒有界等待涵盖同步调用入口和不响应取消的 Task；同一 Session 的播放控制与 PlayItem 共用 `playback:` 槽位，DisplayMessage 使用独立的 `message:` 槽位，两类请求仍共享每个 issuer 最多 64 个未完成传输的总量限制。发送器在超时返回后不会以迟到结果回写 RoomRuntime；旧传输实际完成后释放槽位，准入同时依据 Task 的完成状态清理已完成项，避免清理回调稍晚而误拒绝下一条正常命令。已进入底层的物理发送不能强制撤回，取消令牌仍传入下层，同一 Session 同一通道在未完成期间拒绝叠加请求。

同步引擎的提示通过 `SyncNotificationDispatcher` 发送，每个引擎最多一个发送 worker、64 条等待消息；队满丢弃新提示，排队超过 10 秒的提示按单调时钟过期。发送前重新选择当前会话，并在房间 gate 内核对原房间引用、当前服务器、成员加入状态、Session/Item 身份、快照保护和通知设置，网络发送在退出 gate 后执行。停止时清空等待队列并取消在途提示；底层忽略取消或取消回调阻塞时，停止调用不等待其完成。提示发送不修改同步状态，取舍见 [ADR-005](adr/005-sync-notification-dispatch.md)。

Plugin 的普通设置保存与安装后的 PendingUpdateVersion 持久化共用配置锁。设置提交保留当前内部 pending，更新管理器通过原子 writer 获取当前配置并记录安装事实；配置尚未建立时将新对象挂入 BasePlugin 后保存。设置变更事件在锁外发布已规范化的不可变选项。

## 嵌入式 UI

本工作区的 S122 界面将共享嵌入式 HTML/AMD 控制器组织为“房间 / 创建与邀请 / 设置”三页；普通用户只有房间与邀请。当前页面键为 `WatchTogetherRoomsV16`、`WatchTogetherInviteV16`、`WatchTogetherSettingsV16`，共享 `WatchTogetherNativeV16.js`，只有房间入口显示在用户菜单。标签由宿主 `mainTabsManager` 管理，权限判定后注册，只清理当前页仍拥有的标签元素。它不代表已发布的 v1.6.1.0 资产已包含这些改动。

内容区沿用宿主 `readOnlyContent auto-center` 的侧栏对齐，在插件作用域按已确认的影院式布局扩展到最大 1180px；窄屏随可用空间收缩并堆叠为单列。房间采用媒体/参与者主区和原生控制侧栏，创建与邀请、设置使用双列分组。`emby-input`、`emby-select`、`emby-checkbox`、`emby-button` 由宿主提供外观，动态按钮保留构造器附加的样式类。按钮显式设置 flex 内容居中；普通操作使用 `raised raised-outline`，轮廓来自 `--wt-muted`，主操作保持 `raised button-submit` 主题填充。

S124 已随 v1.7.0.1 发布，使用 V16 页面/控制器缓存键，去除 Dashboard 全局依赖并使用宿主相对路由链接；四个单选控件沿用 Madby 普通单选的宿主默认菜单模式。旧 Theater 的直属箭头按控件 grid 行居中，新版 Web 保留其包装层定位；变更证据和真实验收边界见经验记录第 31、32 节。

房间页按约 5 秒刷新既有 `/Rooms` 和 `/State`，并在严格状态、成员、同 Item 与资格检查后，通过当前登录用户的 Emby `getItem` 接口读取片名。状态新鲜度为 10 秒，错误或过期时不显示旧片名；状态与媒体请求独立，迟到响应绑定页面代次、账号、服务器、房间与媒体目标。媒体区在可靠同步状态下显示片名及当前授权 DTO 提供的背景；等待、失效和无图时使用中性占位。背景通过宿主 `getImageUrl` 生成，仅接受绑定服务器同源 HTTP(S) 地址且不得嵌入用户名/密码；图片加载回调绑定页面、房间、目标和 URL，失效时清除 src。片名、图片标识和 URL 不进入诊断导出。`/State` 是非原子读取，其结果只用于展示，不能替代同步核心身份确认。后端逻辑及协议未修改。

房间外层使用浅色细边框。参与者头像仅从当前房间两位成员及其受限摘要中取 ID，通过宿主 `getUserImageUrl` 生成同服务器地址，不新增用户资料查询。头像与同步状态独立；缺图或失败保留首字，页面代次内抑制重复失败请求。账号、服务器、房间或页面生命周期变化使旧图片回调失效，清除图片 src；头像信息不进入诊断、日志或持久缓存。

邀请、房间操作、确认、配置保存和按需诊断沿用既有业务。Token 不回显，离页清空敏感输入；所有旧代次回调与操作入口都核对页面身份。清理页面时也恢复三个静态创建/接受按钮的 busy 和文字，管理员创建按钮随后按权限与当前表单重新判定可用性；旧请求不能解锁新代次操作。详细候选边界和验证见 [S122 契约](native-pages-media-plan.md)；此前原生控件验证保留在 `native-controls-plan.md`。

## 状态与持久化

起播 `Barrier` 按 Pause → Seek（仅非锚点用户）→ Restore 执行，远程命令等待会话快照确认；进入 Restore 前，锚点和另一端都必须在固定 Seek 目标的容差内。Seek 未确认时保留原目标和原播放意图，并在同一 Barrier 的绝对预算内重试；只有检测到锚点有当前远程命令无法解释的明显新位置操作时，才显式重建 Barrier。正常 `Watching` 期间传播明确暂停/继续和明显手动 Seek，持续明显漂移满足门槛时触发低频 Barrier；同一 Session/Item 的原始远控标志短暂丢失且仍有有效能力证据、没有 Pending 命令时，使用绑定身份和受影响用户集合的 8 秒内存恢复窗口，期间不发送命令，恢复后继续观察，超时或条件变化仍进入严格等待。

Barrier 在当前快照已满足条件且没有待确认命令时，同轮继续推进下一阶段，最多经过 Pause → Seek → Restore；已满足的 Pause/Restore 不重复发送。发出需要改变状态的命令后立即结束本轮阶段推进，继续等待后续轮询的快照确认。Watching 检测到明确手动 Seek 时，当轮启动 Barrier 的暂停阶段。以上变化缩短调度等待，不能强制客户端更快执行或上报。

主用户在 `Watching` 中从 Item A 切换到 Item B 时，`SyncEngine` 先记录主用户 Item 变化并进入独立的 `Handoff` runtime。该 runtime 绑定目标 Item、主用户 Session identity、参与者 Session identity 和 generation。参与者尚未位于 B 时，通过 `IPlayItemIssuer` 发起一次有界的 `PlayItem(B)`，随后只接受当前参与者用户、当前 Session 和 `SessionSnapshot.ItemId == B` 的确认；目标变化会使旧 generation 失效。参与者已经位于 B 时跳过 PlayItem，直接进入 `Barrier`，由 Barrier 重新暂停、定位和恢复播放意图。PlayItem 请求本身的成功不等于播放器完成打开，确认超时或有限重试失败会清理 Handoff 并回到安全的 `Waiting`。

`PlaybackStopped` 只唤醒轮询。主用户停止 A 后，现有 2 秒停止 debounce 同时作为有限的媒体切换观察窗口；窗口内选中 B 时识别为 Handoff，不执行普通停止副作用。窗口结束仍无新 Item 时继续原有 Stop 行为，但从首次缺失开始保留最多 10 秒的仅内存、身份绑定候选，停止确认后若主用户以可信会话打开 B 且参与者仍以原可信会话停留在 A，仍可恢复到同一 Handoff；候选过期、参与者换 Item/会话、能力不可信或房间成员改变时清除。参与者自行切换 Item 不触发主用户跟随；只有显式 Participant Resync 请求才会让非 Primary 参与者通过同一 Handoff/Barrier 流程回到主用户当前 Item。运行时快照、Pending 命令、Handoff、恢复窗口和 Barrier 阶段不写入 `rooms.json`；房间文件采用候选文件替换并保留备份，损坏时报告错误而不静默覆盖。

Phase E 在现有 `Watching` 逻辑旁增加独立的 `Recovering` runtime。仅非 Primary 参与者从选中快照中短暂消失时最多等待 10 秒；缺失会话按原 User、原 SessionId 和原 ItemId 恢复时重新执行一次现有 Barrier。Primary 缺失、停止、Session 或 Item 变化会先退出 Recovery，再由既有停止 debounce 或晚到换集 Handoff 处理；参与者替换 SessionId 或其他不受控的 Item 变化会回到 `Waiting`，不自动换绑设备。双方 actively playing 且无 Pending/Handoff/Barrier/Recovering 时记录 `Participant.PositionTicks - Primary.PositionTicks` 的 signed drift；绝对 drift 至少 3 秒并持续 5 秒才触发一次 Barrier，自动纠偏之间冷却 120 秒，不执行连续追帧式 Seek。

## 发布信任边界

更新组件由 `GitHubReleaseClient`、`PluginUpdateManager`、`ReleaseSignatureVerifier` 和 `ReleaseTrustStore` 协作：默认 `stable` 通道从固定的 `releases/latest` 资产读取，管理员选择 `beta` 后先从公开 Releases API 的非 draft stable 与 prerelease 中选择最高规范版本，再构造本仓库规范数字 tag 的固定资产地址；API 返回的下载地址不作为安装来源。两条通道最终都校验 manifest 的版本、大小、SHA-256、tag 与 detached RSA 签名，未知 key、当前插件版本不可读或校验失败时 fail closed；GitHub 资产下载允许其官方 CDN 重定向，但不会因此放宽内容验签。每次检查会使旧的已验证 release 缓存失效。检查、发现版本、安装成功或失败及待重启等结果统一通过 Emby Web 管理端的 `GeneralCommand` / `DisplayMessage` 短提示反馈；提示发送失败只记录日志，不改变已经完成的检查或安装事实。发布 workflow 负责构建和资产发布，不负责服务器部署。

管理员可在嵌入式管理页保存可选的 GitHub fine-grained token。Token 仅用于 beta Releases API 请求，stable 查询和两条通道的固定资产下载均不携带 Token；未配置 Token 时 beta 继续使用匿名 API。Token 保存在独立的私有存储中，不进入通用插件配置或页面回显。

发布控制面支持独立的[审核请求 PR](release-requests.md)，保留原手动 dispatch。
只有同仓库专用 PR 向 main 新增唯一请求文件并合并后，受信任控制器才读取固定
源码 SHA、成功 CI、四段版本、通道、标题及中文说明 hash。控制器与源码分开
checkout；普通 main 提交不发布。请求和手动入口共用串行签名发布 job，沿用
`release` Environment 与运行期 GitHub token，不新增持久凭据，tag/Release
不覆盖。取舍见 [ADR-004](adr/004-reviewed-release-requests.md)。

## 证据与维护

上述结论来自 `src/EmbyWatchTogether` 的入口、房间、会话、同步和发布信任实现，以及对应 `tests/EmbyWatchTogether.Tests` 测试。新增跨组件约束时先补测试或文档证据，再更新本文；稳定决策另建 ADR。
