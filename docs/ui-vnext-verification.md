# Watch Together UI VNext 验证记录

本文件在各 Stack 完成后追加经真实代码、测试或渲染验证的结果。模拟宿主证据与真实 Emby 验收必须分开记录。

## 基线

| 项目 | 结果 |
|---|---|
| 分支 / commit | `beta` / `6268a90b4346deb4ba8b5585e06369e465f0ddff` |
| 工作区 | clean |
| .NET SDK | `10.0.302` |
| Node.js / npm | `v24.18.1` / `11.17.0` |
| 单元测试 | 461/461 PASS |
| Release build | PASS，0 warning / 0 error |
| `git diff --check` | PASS |

## 验收矩阵

以下项目仅在对应命令或真实环境执行后更新。

| 类别 | 需要的证据 | 当前状态 |
|---|---|---|
| 主题 | 宿主浅色、深色、完整色变量、仅 HSL 分量 | MOCK BROWSER PASS：light / dark / HSL-only |
| 响应式 | 320、360、390、430、768、1024、1440、1920px；无页面横向溢出 | MOCK BROWSER PASS |
| 身份 | 管理员非成员、管理员成员、普通已加入、普通暂离 | .NET CONTRACT PASS；mock 覆盖普通成员与管理员非成员 |
| 房间状态 | Waiting、Barrier、Watching、Handoff、Unavailable、未知原因 | STATIC CONTRACT PASS；mock 浏览器覆盖 Watching，其他状态待真实客户端 |
| 生命周期 | 单一轮询、迟到响应隔离、pause/resume、账号切换、旧数据标记 | MOCK PASS：5 秒更新与 pause 迟到响应；账号切换待真实客户端 |
| 邀请 | 完整 24 位码、大小写、`-`/`_`、复制失败、撤销、过期、Accept 超时读回 | MOCK PARTIAL PASS：创建/原码/业务失败/网络读回；剪贴板与过期实机待验收 |
| 操作 | 点击前、accepted、busy、部分失败、失败、取消 | .NET/STATIC CONTRACT PASS；mock 覆盖取消零请求与焦点循环 |
| 设置 | dirty、离开确认、失败保留草稿、成功回读、隐藏字段保留 | MOCK PASS：失败保留与放弃恢复；真实服务器回读待验收 |
| 诊断 | 按需读取、三轮轮询后保持展开、迟到隔离、导出脱敏 | MOCK BROWSER PASS |
| 构建包 | 最终 commit 构建、单 DLL ZIP、DLL/ZIP 哈希、provenance | LOCAL PACKAGE GATE PASS；精确身份见交付目录 provenance |
| 真实 Emby | 桌面/手机、双账号、真实主题、真实播放与缓存 | NOT AUTHORIZED / NOT RUN |

## P0 / P1 状态

- P0：本地自动化候选已通过；真实 Emby/双客户端验收待授权。
- P1：skipped；未验证前使用“媒体信息待权限验证”占位。

### S4 媒体展示能力门禁审计（2026-09-22）

结论：P1 skipped。生产代码保持“媒体信息待权限验证”占位，当前证据不足以安全显示真实片名、封面或头像。

代码与测试证据：

- `WatchTogetherService.GetRoomState` 只返回 `SyncItemId`、Session 的 Item/位置/时长/播放状态和能力标志；没有片名、封面或头像字段。
- `SessionSnapshot` 只从 `NowPlayingItem` 提取 Id 与 `RunTimeTicks`，现有状态 DTO 无法支撑媒体卡片。
- 参与者摘要和管理员 `/Users` 只返回 Id/Name；后者也不是普通参与者的媒体或头像授权边界。
- 前端只调用本插件路由，没有经过验证的 Emby Items、图片或头像 API 读取与跨账号缓存隔离。

待真实双账号 Emby 验收宿主媒体/图片权限、账号切换清除和无媒体回退后再评估。P0 占位属于权限门禁的预期行为，不是媒体展示缺陷。

## S5 浏览器验证边界

`tests/ui` 在 `127.0.0.1` 加载仓库真实 HTML/JS，只模拟最小 AMD loader、BaseView、Emby custom elements、主题变量和 `ApiClient`。截图中的账号、房间、邀请码和诊断值均为固定 mock 数据，不连接真实 Emby，不是部署或双客户端验收证据。

### S5 已执行结果（2026-09-23）

- `@playwright/test` 固定为 `1.63.0`；`npm ci` 与 `npm audit --audit-level=high` 通过，0 vulnerabilities。
- Playwright Chromium 153 mock 浏览器测试 19/19 PASS。
- 覆盖普通成员当前房间、管理员非成员、插件内导航、邀请创建与接受失败、Accept 网络失败只读回、确认取消零请求与焦点循环、5 秒轮询状态保持、pause 后迟到响应、设置 dirty/失败/放弃、诊断脱敏、浅色/深色/HSL-only，以及 320–1920px 无横向溢出和 44px 控件目标。
- Mock 截图位于忽略目录 `tests/ui/test-results/ui-vnext/`，包含桌面浅/深、390px 当前房间/邀请/设置/诊断和 320px 长文本。
- 浏览器门禁发现并修复：隐藏确认遮罩仍拦截点击、房间卡错误读取诊断节点导致首次渲染失败、成员状态在 320/390px 横向溢出、当前房间未置顶。
- `node --check` 覆盖生产控制器、fixture 与 spec；.NET 462/462 PASS；Release build 0 warning / 0 error；四组 PowerShell build/release/CI 门禁 PASS；`git diff --check` PASS。

未执行：真实 Emby 登录、实际宿主 CSS/缓存、真实双账号邀请与播放同步、移动端软键盘、真实剪贴板失败、部署、重启、签名与发布。

### S7 Emby 顶栏与房间布局浏览器门禁（2026-09-23）

`tests/ui` fixture 继续读取生产 `watchtogether.html` / `watchtogether.js`，并加入最小 mock Emby 宿主 DOM：`.skinHeader` 下的 `.headerLeft`、空的 `.headerMiddle.headerSection.sectionTabs` 和 `.headerRight`。mock 顶栏使用与已核验宿主布局一致的三列桌面布局及小屏第二行分页布局，截图带有 `mock Emby host` 标记；`host=missing` 覆盖页面槽位回退。

- Playwright Chromium 测试 27/27 PASS，包含顶栏胶囊导航挂载、原页面槽位为空、admin mine/manage/settings 与 `aria-current`、普通用户隐藏管理入口、浅色/深色/HSL-only 当前项 4.5:1 对比度、10 次 pause/resume 单实例、离页清理宿主 class、宿主缺失回退、桌面媒体左/成员纵向行和按钮右、手机媒体→成员→按钮顺序、管理员成员个人动作与管理动作收纳、已有房间收起帮助、无房间保留邀请和帮助，以及 active / waiting / State 失败三类媒体状态与成员行一致性。
- 320、360、390、430、768、1024、1440、1920px 均无页面横向溢出、头部遮挡或可见控件小于 44px；浅色、深色和仅 HSL 变量仍通过。
- 浏览器实证发现并修复三项生产 UI 缺陷：房间卡 `width:100%` 与 padding 叠加导致 768/1024px 横向溢出；分页导航移入 Emby header 后脱离 `.wt-page` 变量作用域，导致胶囊链接触控高度降至约 28px；active capsule 只使用 HSL fallback，深色宿主完整文字变量下出现深底深字。修复为房间卡 `box-sizing:border-box`，分页组件自身声明 44px 控件高度，active 背景优先使用宿主完整文字色；成员区改为右栏两条纵向分隔行，避免桌面双列小卡挤压。
- S7 截图位于忽略目录 `tests/ui/test-results/ui-vnext/`，包含 desktop light/dark active room、desktop light admin-member（三项顶栏分页、我的房间当前项）、390px mobile active room 和 320px long text；均为真实生产 HTML/JS 的 mock 渲染。

### S7 最终视觉收口（2026-09-23）

- 当前房间媒体区使用居中的中性胶片图标，并按“尚未检测到播放 / 媒体信息待权限验证”上下排版；该区仍是只读占位，State 无媒体时不显示可点播放入口。
- 当前房间列表移除“当前房间”三级分组标题，保留页面“我的房间”、房间名和状态；刷新按钮降低强调。无房间场景继续显示邀请入口和使用说明。
- “暂离房间”改为中性描边，结束/删除房间继续使用危险色；参与者主操作改为“请求重新对齐”，确认、accepted、busy 和等待播放器确认反馈语义保持一致。
- 最终浏览器测试为 27/27 PASS，新增 active / waiting / State 失败三类 State 会话语义断言；截图已重新生成，媒体状态与成员行保持一致。

真实 Emby 收口时发现旧页面键会导致嵌入 HTML 与 AMD controller 继续命中客户端缓存，即使 DLL 已更新且强制刷新仍显示旧 UI。S8 先将页面入口与 controller 键联动更新为 `WatchTogetherUiVNext` / `WatchTogetherUiVNext.js`，并保留原嵌入资源路径。S9 再轮换为 `WatchTogetherUiMineV2`、`WatchTogetherUiManageV2`、`WatchTogetherUiSettingsV2` 与 `WatchTogetherUiNativeTabsV2.js`，三页共用 HTML/JS 资源并由 Emby 原生 `mainTabsManager` 路由。

S9 的 mock Playwright fixture 验证 `tabs-viewmenubar`、`emby-tabs-slider`、`emby-tab-button` 与 route-backed 切页；生产页面不再包含 `wtPluginNav`，controller 不再操作 `.skinHeader` class。设置页 dirty 状态通过捕获宿主原生 tab 点击并复用现有确认对话框拦截，取消时保留当前路由与草稿，确认后恢复配置再继续路由。

实机回归发现 `mainTabsManager` 对同一页面实例的首次 tabs 初始化具有粘性：若 `onResume` 在权限请求完成前先以普通用户状态初始化，管理员后续刷新不会补出管理与设置页。修正为等待 `WatchTogether/Users` 权限结果后首次初始化，成功管理员生成三项，403 或其他失败安全生成一项；mock 同样忽略同一 page 的重复初始化并由管理员分页测试覆盖。

S11 集成 S9 的权限后初始化修正与 S10 原生主体组件。由于真实 Emby 缓存嵌入 HTML 和 AMD controller，集成测试包将三项页面键与共享 controller 键同步轮换到 `V3`，避免客户端复用此前已加载的 `V2` 页面脚本。真实宿主验收仍需重新执行。

仍待真实 Emby 收口的视觉差异：mock 顶栏无法复现真实 Emby 侧栏、账号菜单、宿主字体和缓存后的 header 尺寸；媒体名称、封面和头像继续按权限门禁使用占位；真实播放状态、双账号设备信息和移动端软键盘布局尚未验收。

未执行：S8 键变更后的真实 Emby Web 页面复核、真实双账号邀请/播放同步、部署、重启、签名和发布。该 Stack 只建立本地代码与测试门禁。

### 本地包门禁

标准 `scripts/build.ps1 -Configuration Release` 已在独立输出目录完成 build、462 项测试、publish 和单 DLL ZIP；ZIP 布局检查要求恰好一个根目录 `Emby.Plugins.WatchTogether.dll`，ZIP entry 与旁边 DLL 的 SHA-256 必须一致。最终交付目录以 commit 标识命名，并包含 `SHA256.txt` 与 `PROVENANCE.txt`；精确 commit、字节数和哈希以该目录内文件为准。

## 本地测试包约束

- 包类型：未签名本地候选，不是 Release。
- 程序集版本保持 `1.6.0.1`，必须依靠 commit 与 SHA-256 区分公开 `v1.6.0.1`。
- 输出名称：`EmbyWatchTogether-UI-VNext-localtest-<short-sha>-UNSIGNED.zip`。
- 附件：`SHA256.txt`、`PROVENANCE.txt`。
- 禁止：manifest、signature、tag、Release、上传、部署、Emby 重启。

### S10 Emby 原生主体组件收口（2026-09-23）

- 主体内容宽度收敛到约 727px；表单继续使用宿主 `emby-input` / `emby-select`，设置三项改为 `emby-toggle-label`、`emby-toggle`、`toggleSwitch` 结构，保存按钮保留 `raised button-submit block` 语义。
- 房间与邀请码列表使用 `paperList visualCardBox vertical-list` 和 `listItem listItem-border` 语义；删除创建、保存、动作和诊断按钮的自绘背景、边框、圆角与 hover 覆写，保留媒体左右布局、移动端单列和必要状态布局。
- Playwright Chromium 测试 30/30 PASS；覆盖原生组件 DOM 证据、浅色/深色/HSL-only、320–1920px 无横向溢出和 44px 触达；`node --check` 与 `git diff --check` PASS。
- 未执行：真实 Emby 宿主 CSS 计算样式、真实部署和双账号播放同步验收。

## Knowledge Review

新增稳定约束：插件自有 fixed overlay 必须显式声明 `[hidden] { display: none !important; }`，不能只依赖宿主/UA 默认样式；RoomId keyed 卡片必须保留实际诊断节点对象而不是把 DOM 节点误当作 panel 记录。两项均由真实 Chromium 复现并已加入浏览器门禁。无需更新 `architecture.md` 或新增 ADR。
