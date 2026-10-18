# Watch Together 原生控件调整

## 当前已确认方案

用户在 2026-10-18 明确要求以本轮部署前服务器上的单页界面为基线，只将按钮、输入框、选择框、checkbox 等已有控件交给 Emby 原生样式。保留原区域顺序、房间布局和操作方式。

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
