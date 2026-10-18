# Watch Together UI VNext 实施契约

本文把 UI VNext 设计材料收敛为当前仓库可执行的实现边界。设计材料是方案输入，真实代码、测试和本文件记录的基线优先。

## 历史基线

- UI VNext 早期基线：`6268a90b4346deb4ba8b5585e06369e465f0ddff`，插件版本 `1.6.0.1`，461 项测试；该阶段的本地未签名包约束只适用于历史阶段。

## S111/S113 整合基线

- S111 审核通过提交：`98ace2b26bab2fc7cfe5a6675e1381728b34d8c9`，基于 beta `v1.6.0.5`，主线程已完成三方合并准备。
- S113 只整合原生风格 UI 与 beta Token 设置，保留业务/API/Token 存储和同步实现；不接入暂停中的 Phase E 字段。
- 本轮交付边界：本地 UI 实现、验证和主线程审核；不部署 NAS、不 push、不创建 PR、Release、tag 或签名包。

## 当前实现事实

- UI 是嵌入 DLL 的单个 HTML 与 AMD JavaScript 页面，不引入生产 SPA、Node 服务或额外部署目录。
- 页面入口使用三个 route-backed 缓存键 `WatchTogetherUiMineV4`、`WatchTogetherUiManageV4`、`WatchTogetherUiSettingsV4`，共用嵌入 HTML；仅“我的房间”进入用户菜单。共享 controller 使用 `WatchTogetherUiNativeTabsV4.js`，HTML 与 controller 键必须联动。
- 插件分页由 Emby `mainTabsManager.setTabs` 与 `Dashboard.getConfigurationPageUrl` 提供，使用宿主原生 `emby-tabs` 结构；业务页面不创建自绘导航，也不修改 `.skinHeader` class。管理员路由在权限确认后显示，非管理员只能保留“我的房间”。
- `/Rooms` 已返回 `CurrentUserIsParticipant`、`CurrentUserJoined`、`IsAdmin` 和 `CanEnd`，前端按参与者身份区分个人视图与管理员管理视图。
- 页面读取 `/Rooms/{Id}/State`，只读展示逐成员播放状态、暂停状态和媒体占位；读取失败保留过期标记。
- `/State` 与 `/State.Sessions` 已包含 `CurrentUserIsParticipant`、`IsAdmin`、`RunTimeTicks` 等只读字段，不改变会话选择或同步状态机。
- 邀请码保持完整、区分大小写并保留 `-` / `_`；创建邀请不等于创建房间。
- 任何参与者是否可结束房间只读取服务端 `CanEnd`，不从主用户、管理员、Joined 或自助房间标记推断。

## 产品与视觉方向

- 保留 Emby 宿主侧栏、顶栏、导航、字体、账号菜单、播放器和全局主题。
- 插件内容区沿用 Emby 宿主原生 `verticalSection`、`emby-input`、`emby-select`、`emby-toggle`、`emby-button` 与 `paperList/listItem` 组件，仅保留必要的窄栏和媒体左右布局；不使用外部字体、CDN、渐变、玻璃拟态或装饰性多色图标。
- 默认页优先显示当前用户所属房间；管理员房间管理、设置和说明进入插件内子视图。
- 桌面和手机业务语义一致；手机不增加宿主级底部导航，触控目标至少 44×44 CSS px。
- 媒体字段未经权限和宿主能力验证时显示完整占位，不伪造片名、封面、设备、时长或同步误差。

## P0 契约

### 只读响应扩展

- `/Rooms`：新增 `CurrentUserIsParticipant`。
- `/State`：新增 `CurrentUserIsParticipant` 与 `IsAdmin`。
- `/State.Sessions`：新增 `RunTimeTicks`。

以上字段都是向后兼容的只读展示字段。禁止修改 `SyncEngine`、`SessionSelector`、`SessionSnapshot`、`RoomManager`、`RoomStore` 或邀请生命周期。

### 页面状态与生命周期

- 房间列表保持约 5 秒读取；邀请只在需要时刷新；诊断只在用户展开时读取。
- 同一资源最多一个读取请求，并以页面 generation 和资源 sequence 丢弃迟到响应。
- `onPause` 停止轮询、倒计时和反馈计时器并使在途响应失效。
- 账号变化或销毁时清空房间、用户、诊断、邀请码明文、草稿、权限和 busy 状态。
- 读取失败保留最后成功数据并标记可能过期；不得把失败渲染成“没有房间”。
- 以 RoomId 稳定更新 DOM，轮询不得丢失焦点、输入值、菜单和诊断展开状态。

### 身份与权限

- “我的房间”必须由 `CurrentUserIsParticipant` 判定，不能取管理员列表中的第一项。
- 管理员非成员只显示管理动作，不显示“已暂离”“加入同步”或参与者重新对齐。
- 管理员同时是成员时，个人动作与管理员动作分别计算。
- `Watching` 只表示同步已建立；播放中、已暂停和未检测到播放来自逐成员 Session。
- `Online=false` 不得解释为账号或设备离线。

### 邀请、操作、设置与诊断

- 邀请流程覆盖创建、完整码复制、倒计时、撤销、失效、接受、超时后权威读回和业务错误。
- 接口 accepted 只表示请求已受理；播放器完成必须等待后续 State。
- 暂离、结束房间、暂停播放和重新对齐是不同动作；危险操作必须可取消并恢复焦点。
- 保留暂离副作用的成功、部分失败和未尝试反馈；busy/冷却使用中性反馈。
- 设置保存前读取完整配置，只合并四个受支持字段，失败保留草稿，成功以服务器回读确认。
- GitHub Token 设置使用 Emby 原生输入框和按钮，不回显明文；状态读取、保存和清除受页面 generation、账号身份及 pause 生命周期隔离，离页或账号变化时清空草稿。
- 诊断继续使用现有白名单、事件上限和客户端清洗；邀请码、用户名、真实 ID、Token、路径和异常文本不得进入导出。

## P1 门禁

真实媒体片名、封面或头像只在当前用户权限、宿主 SDK 方法和缓存隔离均有证据时实现。无法完成该验证时，P1 明确跳过并保留“媒体信息待权限验证”占位；不得为截图写死媒体数据。

## Stack 与回滚

实施顺序为 S0 契约、S1 视觉基础、S2 生命周期/身份/State、S3 交互、S4 条件媒体展示、S5 浏览器验证和本地打包。共享 HTML/JS 的 Stack 串行执行，每个 Stack 使用独立 branch/worktree 和本地 commit；单 Stack 以 revert 该 commit 回滚。

## 历史本地测试包身份

早期 UI VNext 阶段未改变项目版本，其本地包名称为 `EmbyWatchTogether-UI-VNext-localtest-<short-sha>-UNSIGNED.zip`，并附 `SHA256.txt` 与 `PROVENANCE.txt`。历史 Provenance 记录完整 commit、构建时间、程序集版本及 DLL/ZIP 哈希，并明确 `UNSIGNED / LOCAL TEST / NOT A RELEASE`。

这些历史包的程序集版本为 `1.6.0.1`，通过 commit 与 SHA-256 区分。2026-10-18 的本地整合保留 `1.6.0.5`，构建输出只用于本地验证，不能冒充已发布的同版本资产。统一正式版按当前开发排期验收后另行准备；ZIP 根目录继续只包含 `Emby.Plugins.WatchTogether.dll`。
