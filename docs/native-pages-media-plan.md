# S122 原生分页与房间媒体展示

本轮界面及发布前生命周期修正最初随 [v1.7.0.0 测试版](releases/v1.7.0.0.md)发布，当时页面键为 V12，见[历史发布记录](maintenance/v1.7.0.0-beta-publication.md)。已发布的 v1.7.0.1 使用 V16，当前开发代码因诊断标签修正更新为 V17；下文 V8–V11 的版本、未提交和未发布措辞保留各次本地部署当时的事实。

S124 后续本地部署页面键为 V16，用户于 2026-10-19 确认最终普通单选外观及箭头方案；其修正已随 v1.7.0.1 签名发布，见[发布与核验记录](maintenance/v1.7.0.1-beta-publication.md)。真实双端功能与原始客户端临时卡住问题保持独立验收边界。

2026-10-19 用户确认“房间 / 创建与邀请 / 设置”分页、两位参与者信息及纯文本剧名与季集，未同步时在相同位置显示原因及下一步；随后授权本地实现和服务器部署。此前单页约束是历史基线，本轮授权覆盖该界面决策。

## 执行契约

- 基础：`beta@66bedc4dafbae6ebff80de4fad7ca3b6f01e3ffb`，无前置 Stack。
- 隔离：`codex/stack-122-native-pages-media` 独立 worktree；保留主目录既有治理修改。
- 目标：复用 Emby 原生标签、控件、主题、80ch 内容宽度；仅房间入口进入用户菜单。普通用户显示房间和邀请，管理员额外拥有创建与设置。
- 允许文件及所有权：worker 负责 `Web/Configuration/watchtogether.html`、`watchtogether.js`、`Plugin.cs` 的页面注册和 `PluginPagesTests.cs`；主线程在交接后负责浏览器 fixture/spec、文档、集成和部署。定点返修按原 worker 与相同文件所有权进行。
- 禁止修改：其他 C#、SyncEngine、RoomManager、SessionSelector、SessionSnapshot、持久化、鉴权、邀请生命周期、版本、依赖、签名和发布工作流。
- Tier 2：异步页面生命周期、账号/服务器身份及媒体权限显示；主线程设计，运行时可用 Sol High worker 实现限定 UI。后端架构不变。
- 并行：UI 写入串行交接；只读分析可以并行。测试和定点返修不同时运行。主线程浏览器 fixture/spec 与 worker 四文件定点补丁所有权不重叠。
- 授权：初始实现只允许本地修改和服务器部署；用户后续明确要求“发一个测试版本”，当前允许为该界面测试版提交、推送 beta、创建唯一 tag 和签名 prerelease。
- 验收：三页可达且权限正确；主区域完整展开；双方状态与片名准确降级；旧账号、服务器、房间及 Item 的响应不得回写；原有房间、邀请、设置、Token、诊断操作保留；主题、窄屏、原生控件与焦点检查通过。
- 命令：`node --check src/EmbyWatchTogether/Web/Configuration/watchtogether.js`；`dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release`；`npm test`（`tests/ui`）；`pwsh -NoProfile -File scripts/build.ps1 -Configuration Release`；四组既有 PowerShell 门禁及 `git diff --check`。
- 回滚：本地移除 S122 范围补丁；服务器仅恢复部署前精确 DLL 备份并重新启动。原件哈希、候选哈希及备份位置放入忽略的本地 receipt。

## 显示与权限契约

1. `/Rooms/{Id}/State` 现有响应包含 State、StatusReason、Eligible、SyncItemId、成员和 selected Sessions。本接口是非原子读取且重新选择快照，不能当作同步引擎原子身份凭据。界面展示最近一次可靠读取结果，保持约 5 秒刷新。
2. 仅 `Watching/watching` 或 `Barrier/aligning`、Eligible 为 true、两名不同成员均 joined、两份在线 Session 正好覆盖成员且同 Item 并匹配非空 SyncItemId 时读取媒体；RoomId 必须匹配请求。Waiting 可能保留旧 SyncItemId，不能单独使用该字段。
3. 媒体使用当前用户的 `ApiClient.getItem(userId, itemId)`。已只读检查真实宿主实现：有 userId 时调用 `Users/{userId}/Items/{itemId}`，空 userId 会走全局 Items，因此本页面禁止空身份调用。仅显示匹配响应 Id 的 Name、SeriesName、Type、ParentIndexNumber、IndexNumber；不新增服务端元数据权限接口。
4. 元数据失败显示“媒体信息暂不可用”；状态失败、房间移除、Waiting/Handoff/Recovering、账号或服务器变化、pause 清除当前片名并使在途目标失效。State 读取与元数据读取分别 single-flight，慢媒体不得阻塞后续 State。
5. State 的显示新鲜度窗口为 10 秒，超过窗口或请求本身耗时超过窗口后不继续展示旧片名；下一次可靠响应恢复显示。它是 UI 数据新鲜度约束，不改变后端恢复时限。
6. 片名使用 textContent，不含播放箭头或进度控件；电影显示 Name，剧集优先 SeriesName 并显示有效季集，季号 0 可显示特别篇。V10 背景仅取当前授权媒体 DTO 的背景标签，经宿主 SDK 和同源检查生成地址；媒体名、图片标识与 URL 不进入诊断导出、日志或持久浏览器缓存。
7. 当前开发页面缓存键为 `WatchTogetherRoomsV17`、`WatchTogetherInviteV17`、`WatchTogetherSettingsV17`，共用 `WatchTogetherNativeV17.js`；S122 首次发布时为 V12。原生 mainTabsManager 对同 owner 不重建标签列表，必须在权限判定后注册；只清理仍由本页持有的标签元素。
8. 头像只接受当前房间两名不同成员及受限 Participants 摘要共同包含的用户 ID；宿主 getUserImageUrl 返回地址复用同源检查，不额外获取用户资料。头像可在等待状态显示，失败回退首字；代次、身份、view、成员和 URL 校验阻止旧回调回显。

## 验证与部署记录

本地候选已于东八区 2026-10-19 11:21:14 部署。程序集沿用基础版本 `1.6.1.0`，未创建 commit、push、PR、tag 或 Release。此候选与已发布的同版本签名资产用文件哈希区分。

- 主线程验证：完整 .NET 测试 545/545；浏览器 63/63；Release build/publish 零警告、零错误；构建路径、发布签名、发布工作流和 CI 工作流四组脚本检查通过；语法及 diff 检查通过。
- DLL：1,186,816 bytes，SHA-256 `1bb7ebaab93bce4916050c6631215a631b752d3ba20273710c974b4e9a0cfaaf`；ZIP 核对为单 DLL。基线 commit、未提交修改及各文件哈希保存在本地忽略的 provenance。
- 部署：上传完成，上传/安装哈希核对一致；替换前精确备份及其原件哈希已核对。容器重启，插件 `1.6.1.0` 加载、EntryPoint 完成、SyncEngine 启动，HTTP 恢复 302。只清理本次精确临时上传文件。
- 真实 Emby Web：完整刷新后使用 V8 controller，三个宿主原生标签均可切换；各页只显示对应区域。内容宽度约 727px，原生表单标签、动态按钮、配置读取和 Token 已配置状态正常，Token 输入为空。1280px DOM 宽度无横向溢出；临时 viewport 已恢复。
- 当前真实房间为 Waiting，双方下方显示等待播放原因；未启动用户播放器或执行房间/邀请/配置写入。真实同步中的片名、自然换集、双端播放与真实手机仍待用户使用确认。模拟媒体和 320–1920px 浏览器证据不代替这些验收。
- 回滚所需备份路径及部署 receipt 仅保留在本地忽略的 `artifacts/s122`，不写入公共文档。实机房间页截图也保存在该目录。

## Knowledge Review

新增约束是原生标签 owner 行为、State 与媒体独立请求以及非原子 State 的展示边界。已检查现有经验 10、18、19、21–24 去重；当前架构只更新 UI 事实，无新增后端 ADR。

## 房间页视觉调整（2026-10-19）

用户在部署后要求继续调整第一个页面。该增量由主线程在同一 S122 worktree 完成，仅修改房间页的布局、文字层级、说明和页面缓存键；读写 API、同步与媒体显示门槛未改。

- 房间及帮助区外层框移除；参与者用双列文字和细分隔线，姓名采用主文字色与 600 字重；媒体标签与加粗片名分行，等待原因使用同一浅色区域。
- 操作仍保留两组原生按钮，第二组不重复画分隔线；成员说明缩短并移到操作之后，窄屏按钮可用两列。帮助改为完整展开的简洁“观看提示”。创建/邀请及设置布局保持。
- 页面及控制器缓存键同步递增为 V9。原始 V8 DLL 和修改前 UI 源码保存在忽略的本地 artifact，便于增量回退。
- 重新执行浏览器 63/63、完整 .NET 545/545、Release build/publish（零警告/错误）、语法和 diff 检查；实际查看桌面模拟剧集状态及 390px 长文本截图。
- DLL 为 1,190,400 bytes，SHA-256 `fd4f8497e12d6729d8191f1aa88173a2447b746099c68b0c48c06b20d7b3b032`。东八区 11:32:17 替换部署，安装与上传哈希一致，V8 DLL 精确备份保留。插件加载、EntryPoint 和 SyncEngine 启动、HTTP 302 已核对。
- 真实 Web 已确认 V9、约 727px 内容宽度、房间外框和参与者边框为 0、姓名字重 600；当前房间显示等待播放，设置页读取正常。未操作真实播放；截图与 receipt 在 `artifacts/s122-refine`。未创建提交或发布。
- Knowledge Review：本次新增发现为纯视觉层级问题，已由截图和 CSS 对照确认；不增加后端约束或 ADR。

## 影院式三页（2026-10-19）

用户确认新的同步房间、等待播放、创建与邀请、设置四张设计图后，授权实现并查看实际效果。本轮继续使用同一 S122 分支与 worktree，在已保存的 V9 未提交源码上增量实现；没有创建后继提交或扩展后台业务。

- 布局上限扩展为 1180px。房间左侧媒体画面与参与者、右侧原生控制；等待时使用内联 SVG 占位。创建与邀请、设置按确认设计双列排列，窄屏单列。全部原房间、邀请、配置、Token、诊断和导出功能保持，诊断仅在用户打开时显示。
- 背景使用当前账号 `getItem` 成功返回的 `BackdropImageTags[0]`，或 `ParentBackdropItemId` 与 `ParentBackdropImageTags[0]`。真实宿主 `getImageUrl` 和锁定 SDK DTO 字段已只读核实。仅接受受限 ID/tag、非空字符串、HTTP(S)、绑定服务器同 origin 且不含嵌入式用户名/密码的地址；不读取任意外链图片。
- 图片 load/error 核对页面代次、view、当前媒体 target、URL 和当前 State；在同目标同 URL 下避免轮询重新加载。失败保留有效片名；Waiting、读取失败、过期、账号/服务器变化和 pause 与片名同步清除 src。图片信息不加入诊断导出。
- 图片容器采用明确的可收缩宽度及最小高度，长文本自然撑高。首轮浏览器发现 `aspect-ratio + min-height` 将 390px 页面撑宽，修正后不使用横向裁剪掩盖问题。
- 本轮限定四个产品/页面测试文件由原 Sol High worker 完成；主线程负责真实 diff 审核、fixture、浏览器检查、文档、构建和部署。媒体/图片接口契约固定后并行准备独立 fixture，产品返修和浏览器运行串行。
- 最终浏览器 **71/71**，完整 .NET **545/545**，Release build/publish 零警告/错误，语法与 diff 检查通过。新增用例覆盖自身与父背景、缺图/404、非法地址零请求、旧图片事件、权限撤回、诊断导出、三页浅深主题和窄屏；原有邀请、设置、Token 与媒体边界回归保持。
- DLL 为 **1,200,128 bytes**，SHA-256 `52fdfa987882a5b0408330ac69767e872993d2188b9c28638f05c070e4e7359c`，程序集仍为 `1.6.1.0`。东八区 **12:07:01** 部署，V9 精确备份保留，上传与安装哈希一致，容器、插件加载、EntryPoint、SyncEngine 和 HTTP 302 已核对。
- 真实 Web 已检查 V10、1180px、左右房间布局及全部六个管理员动作；创建与邀请的原生标签和双列、设置复选框的逐行排布及配置读取正常，Token 输入为空。只读打开诊断，内容和导出入口可用。未执行房间、邀请、配置或播放写操作。
- 当前真实房间处于等待播放，因此真实剧集背景、换集和双端播放仍待用户使用确认；模拟图像不作为真实媒体权限或播放验收。部署 receipt、修改前文件及实机截图保存在忽略的 `artifacts/s122-cinema`。未 commit/push/PR/Release。
- Knowledge Review：图片回调的权限/目标绑定、SDK URL 返回类型校验、媒体比例最小尺寸问题已由代码与测试交叉确认，更新 UI 架构及经验记录；无需后端 ADR。

## 房间边框与 Emby 头像（2026-10-19）

用户提供真实同步剧集截图，确认视频页面效果，并要求补轻边框及 Emby 用户头像。该截图支持 V10 背景、片名与季集的显示确认，不扩展为自然换集或双端播放完整验收。

- 在同一 S122 worktree 增量添加房间整体浅色细边框，保持媒体、参与者及控制区布局；头像按房间限定身份通过宿主图片接口加载，缺图和 403/404 保留首字。无新增用户资料接口、后端协议或持久化变更。
- 原 worker 负责限定产品四文件，主线程审核完整 diff 并负责 fixture、七项头像回归、文档和部署。页面缓存键同步更新为 V11。
- 浏览器 **78/78**、完整 .NET **545/545** 通过；Release build/publish 零警告/错误，JS 语法和 diff 检查通过。覆盖旧图片事件、账号/服务器变化、页面离开、房间删除、失败抑制和诊断隔离。
- DLL **1,204,736 bytes**，SHA-256 `cfa34ffa0118c8c50b7356e5da14a5e44dcd12a1aa06d42479ca27008533e6f1`，版本仍为 `1.6.1.0`。东八区 **12:34:47** 部署，V10 精确备份保留；上传、安装及原件备份哈希核对一致，容器、插件加载、EntryPoint、SyncEngine 和 HTTP 302 已核对。
- 真实 Emby V11 已显示房间细边框和主用户头像；另一参与者图片不可用时显示首字。卡片宽度 1180px，六个房间动作保持。当前为等待播放，没有操作真实播放或修改成员、邀请、配置。实际截图及 receipt 保存在忽略的 `artifacts/s122-avatars`，未 commit/push/PR/Release。
- Knowledge Review：头像身份与媒体资格门槛应分开；同页面代次的失败缓存避免状态重绘反复请求不存在的照片。已补充经验，未改变后端架构，不新增 ADR。

## 测试发布前复核

用户要求发布测试版本后，原实现代理只读审核最终 diff；主线程用浏览器复现了创建邀请码、接受邀请、管理员创建房间三种 pending POST 期间离页再返回时，静态按钮保留 disabled/aria-busy/处理中标签的回归。三个新增测试在修正前均失败。

主线程在 clearPageData 中恢复上述按钮及旧创建反馈，随后管理员按钮仍由权限和表单有效性决定是否启用。旧代次请求继续被拒绝，不能解锁新请求；没有自动重发 POST。页面与控制器键递增到 V12，避免继续加载旧控制器。三个定点回归修正后通过；发布构建、完整回归及资产核验在版本发布记录中汇总。

同轮复核发现旧诊断 details 节点的迟到 toggle 仍可写入新页面的展开状态并触发只读查询。新增浏览器回归在修正前检测到意外 Diagnostics 请求；回调补充 active identity、generation 和当前 panel 引用检查，保留用户在当前页面主动查看诊断的行为。
