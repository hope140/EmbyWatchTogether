# Watch Together 原生控件调整

## 当前已确认方案

用户在 2026-10-18 明确要求以本轮部署前服务器上的单页界面为基线，只将按钮、输入框、选择框、checkbox 等已有控件交给 Emby 原生样式。保留原区域顺序、房间布局和操作方式。

后续宽度与配色反馈进入 S117：采用宿主设置内容区的限宽/对齐规则，修正深色渐变主题的普通按钮背景与文字配对，原区域和交互继续保留。

基线证据：部署前 NAS 安装 DLL 与原 S110 本机产物 SHA-256 一致，为 `513dda4032d3c06c1d66091610e83e5569fcc810c2982abb05aed2adcb36d535`；S110 没有修改 Web 资源，原 UI 对应 beta `a1408e0`。`f294c67` 的 Web 资源相对该基线只增加 Phase E 的状态/诊断展示，结构仍相同，作为本次 UI 输入。

已确认的主用户晚到换集、参与者 Recovery、drift、S110 tracing 和后台权限保持本地 beta `90feffd` 的实现。

## S116 契约

- 目标：在原服务器单页布局上替换原生控件样式，纠正此前采用整个 UI VNext 布局的范围偏差。
- 基础分支：`beta@90feffd`；前置依赖：已审核的同步整合及用户最新 UI 基线确认。
- 允许修改及文件所有权：UI worker 独占 `Web/Configuration/watchtogether.html` / `watchtogether.js`、`Plugin.cs` 的 `GetPages`、`PluginPagesTests.cs`、`tests/ui/fixture/server.js` / `specs/watchtogether.spec.js`；主线程拥有架构、排期、当前 UI/验收说明及部署 receipt。
- 禁止修改：其他 C# 业务、同步/Session/Room/邀请/Token/更新器逻辑和 API、持久化、依赖/锁文件、程序集版本、发布工作流。
- 实现边界：原页面的创建房间、邀请、房间、说明、设置顺序与容器布局保持；静态 `emby-input/select/checkbox/button` 复用既有结构，只剥离控件自绘颜色/边框/圆角/hover 覆盖。动态房间/邀请按钮继续原生初始化；导出按钮补 `emby-button` 初始化。当前单页资源缓存键统一为 `WatchTogetherSettingsV5` / `.js`。真实宿主发现动态按钮的 `className` 赋值会覆盖构造器添加的原生样式类，改用 `classList.add` 保留它们，并补充宿主构造行为回归。
- 交互边界：房间动作、邀请码、确认、设置和 Token 保存/清除沿用原页面流程；Phase E 新诊断保留；不把被否决的分页、媒体占位或新的操作收纳方式带入当前界面。
- 验收：与原服务器页面的区域/ID/顺序一致；管理员/普通用户可见性及业务反馈保持；按钮/input/select/checkbox 使用宿主控件；Token 不回显、读失败清除恢复与取消零写入保留；新状态/诊断白名单导出有测试；320–1920px 与浅深主题可读。
- 测试：`node --check`；`PluginPagesTests` / 房间服务相关 .NET 测试；既有 `tests/ui` 锁定依赖的 Playwright 回归改为当前已确认单页契约（分页专用断言属于撤回布局的历史测试，功能测试保留等效覆盖）；主线程完整 build/test/publish、四组脚本、ZIP/哈希与真实宿主只读复核。
- 回滚：revert S116 commit；NAS 采用精确备份与哈希校验回滚，原始部署前备份保留。
- 允许并行：否，页面及验收文件串行交接。
- 子代理：原 Luna High UI worker；主线程负责基线恢复、跨组件审核、提交和 NAS 部署；Max 理由不适用。
- Knowledge context：AGENTS、当前架构/经验、ADR-003、原 `a1408e0` 页面与 `f294c67` 的诊断增量；当前需求优先于被撤回的 UI VNext 布局契约。

## 本轮交付与验收

本地及部署后的控件/布局证据分别记录；今晚由用户执行真实双账号播放、换集和停止验收。正式版仍等 UI 与业务验收收口后统一准备。

### 2026-10-18 验证与部署结果

- 最终产品源码：`55f101ca08d195198fbc4734011379aa1a04306c`；程序集仍为 `1.6.0.5`，本地候选未正式发布。
- .NET 545/545、Playwright 35/35、Release build/publish（0 warning/error）、四组 PowerShell 门禁、单 DLL ZIP 内容/哈希、`node --check` 和 `git diff --check` 均通过。HTML 与原 `a1408e0` 的 115 个 tag/id/native-control 顺序一致；后端相对 `90feffd` 无改动。
- 最终 DLL SHA-256：`fdc30f809ba881652a3f994788bde426cce6cb57eae66cbf3fffe57d716f4674`，1,156,608 bytes。NAS 于东八区 20:03:33 部署，上传与安装哈希一致，容器运行、插件加载、EntryPoint 完成、SyncEngine 启动及 HTTP 302 已核对。原始部署前和每次替换前备份均保留，本次精确上传临时文件已删除。
- 真实 Emby Web 完整刷新后观察到 V5 控制器，创建房间/邀请/房间/使用方法/设置五区顺序保持，配置已读取，Token 仅显示已配置状态。房间动作及诊断导出按钮均保留 `emby-button emby-button-focusscale`，字体来自宿主，边框为 `0px none`；原先动态按钮的 Arial/默认 outset 边框问题已消失。实际只读打开诊断，未执行房间控制、邀请或配置写入。
- 浏览器浅深主题、320–1920px、普通用户权限与业务失败场景属于模拟宿主证据；真实手机/普通用户、双账号播放、手动及自然换集、停止/恢复和长时观看仍由用户验收，不以启动检查或 mock 结果代替。
- Knowledge Review：记录原生声明与自绘覆盖、动态控件构造类被覆盖两项事实；架构已同步 UI 基线，没有新增同步架构决策，无需新 ADR。
- 机器路径及完整回滚位置只保存在被忽略的部署 receipt，不进入仓库；远端分支、tag 和 Release 未写入。

## S117：原生内容宽度与按钮主题适配

- 目标：按用户要求采用其他插件页同类的原生内容宽度，修正深色主题普通按钮浅底白字；保留 S116 单页结构和业务。
- 基础分支/commit：`beta@d97b82a7cb435f9ea2c7cbfeb5d7d199c0fd7501`；前置依赖：S116 已审核并部署。
- 真实宿主依据：当前 NAS `layout.css` 的 `form, .readOnlyContent { max-width: 80ch; }` 与 `.auto-center` 双侧 auto margin，路由模板使用 `readOnlyContent auto-center`；`layout_nontv.css` 的 `.page-withDockedDrawer .auto-center` 在侧栏展开时将双侧 margin 覆盖为 0，实际沿用该靠左对齐规则。按钮主题定义 `.raised` 的背景/文字配对及 `.button-submit` 的主题强调色，`emby-button.css` 未定义 `button-flat` 的背景。
- 允许修改/文件所有权：原 UI worker 独占 HTML/JS、`Plugin.cs` 的 `GetPages`、`PluginPagesTests.cs`、`tests/ui/fixture/server.js` / `specs/watchtogether.spec.js`；主线程独占 README、本契约、架构/排期/验收和经验文档及本地部署 receipt。worker 交接停止后，主线程接管最终真实宿主相关的 fixture/spec 集成。
- 禁止修改：其余 C#、同步/Session/Room/邀请/Token API 和业务、版本、依赖/锁文件、签名与发布工作流。
- 实现边界：在现有 padded scroll 容器内复用宿主 `readOnlyContent auto-center` 包裹原内容，区域/ID/顺序保持；普通按钮采用宿主 `raised` 背景/文字配对，创建/接受/保存及加入采用原生 `button-submit`；保留动态构造器类与旧确认方式。无自绘按钮颜色、红色方案、新分页、操作收纳或业务变更。页面/控制器键同步 V6。
- 验收：宽屏内容受宿主 80ch 约束并按宿主对齐（侧栏展开时靠左），手机随可用宽度收缩；所有原区域与角色规则保留，普通按钮复用浅/深/渐变及 HSL 主题配对，动态按钮保留宿主类，主操作与普通操作区分。渐变透明色的对比度目标与实际值分别记录，不把原生样式正确当作所有背景点位对比度达标。
- 测试命令：`node --check`、页面/服务 .NET 窄测；`npm ci` / `npm test`（`tests/ui`），夹具模拟 80ch 原生规则及 HSL 按钮配色，包含浅/深按钮对比度；最终 build/test/publish、四组 PowerShell 门禁与真实宿主只读复核。
- 回滚：revert S117 commit；NAS 使用精确备份恢复。旧 S116 与其他工作区保留。
- 允许并行：否，共享文件和最终构建串行。
- 子代理：沿用原 Luna High UI worker 处理宿主兼容性修正；主线程设计、审核、集成与部署；Max 理由不适用。
- Knowledge context：当前架构、经验 10/18/21/22、S116 原布局与按钮构造类边界；本轮只读取得的真实宿主 CSS/路由证据。

### S117 验证与部署结果

- 产品源码 `75b061559fe5a0193ddd062c0a0fb60043b4e5ab`，程序集 `1.6.0.5`；DLL 1,159,680 bytes，SHA-256 `b939594b28a9c48871e12d31aa83a41efe39640484fda58f2aa65051b5c3aad1`。
- 主线程独立执行 .NET 545/545、最终 UI 浏览器 39/39、Release build/publish（0 warning/error）、四组脚本门禁、单 DLL ZIP 内容/哈希和 diff 检查；源 HTML 的原 ID/控件顺序保留且标签平衡，后台业务相对 `d97b82a` 无改动。
- NAS 于 2026-10-18 21:30:42（东八区）部署；上传、安装及备份哈希核对，容器运行、插件加载、EntryPoint/SyncEngine 启动和 HTTP 302 已确认。原始及本次替换前备份保留，精确临时上传已移除，机器路径仅存被忽略的本地 receipt。
- 真实 Web V6：内容区实测 727.19px、计算 max-width 727.196px；当前展开侧栏时 margin 为 0，靠左与 `layout_nontv.css` 一致。原五个区域顺序、配置读取、Token 配置状态保留；房间动态按钮有 `raised` 与原生构造类，浅色下背景 `rgba(236,236,243,.7)`、文字 `rgba(0,0,0,.87)`。未执行房间控制、邀请或配置写入。
- 对比度边界：模拟 light/dark/HSL 普通按钮达到 4.5:1；采用当前宿主 darkgradient 透明色变量与代表背景点进行 alpha 合成，结果约 3.868:1，优于旧浅底白字但低于 4.5:1。此项不是全部背景/客户端可读性通过，保留原生主题配对，等用户实机反馈。
- Knowledge Review：经验 23 记录原生内容宽度、侧栏对齐覆盖和按钮主题配对；架构已同步，无新增同步或业务架构决策，不需新 ADR。正式发布仍等待 UI 与真实双端验收，未执行远端分支/tag/Release 写入。
