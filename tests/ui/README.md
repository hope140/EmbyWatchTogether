# Watch Together 原生分页浏览器验证

测试服务器在 `127.0.0.1:4179` 启动，运行时读取仓库中的真实 `watchtogether.html` 和 `watchtogether.js`。fixture 提供最小 AMD loader、`BaseView`、Emby custom element 兼容层、主题变量、时钟和 `ApiClient` mock，并模拟真实宿主的 `.skinHeader`、`.headerLeft`、空的 `.headerMiddle.headerSection.sectionTabs` 与 `.headerRight`；页面顶栏会显示 `mock Emby host` 标记。它不复制第二套页面实现，也不连接真实 Emby。

使用 `host=missing` 查询参数可以验证宿主分页容器缺失时页面仍能安全渲染、且不会创建第二套页面导航，例如 `/?scenario=active&host=missing`。所有截图均来自仓库真实 HTML/JS 的 mock 渲染，不能作为真实 Emby 宿主视觉或双客户端验收证据。

```powershell
npm ci
npx playwright install chromium
npm test
```

截图写入 `test-results/native-controls/` 和 `test-results/native-pages/`，默认 HTML 报告在 `playwright-report/`，这些目录已被 `.gitignore` 忽略。所有邀请码、账号和 token 都是 fixture 内的 mock 值，不能作为真实服务验收证据。

S122 使用 `watchtogether.spec.js` 保留原生控件、主题、宽度、邀请、设置、Token、诊断回归，适配房间/创建与邀请/设置三页。`native-media.spec.js` 覆盖当前用户媒体查询、状态门槛、迟到响应、账号/服务器变化、文本安全和 10 秒新鲜度。页签通过模拟的真实 `mainTabsManager` owner 语义切换；测试不会连接真实 Emby。

S122 验证与部署边界见 [`native-pages-media-plan.md`](../../docs/native-pages-media-plan.md)。

V10 新增 `cinema-ui.spec.js` 检查同源背景图、图片失败与迟到事件、诊断导出隔离及三页布局，截图在 `test-results/cinema-ui/`。fixture 图片为本地合成 SVG，仅用于功能与布局测试；没有从外部图片站或真实媒体库取图。

V11 新增 `avatars.spec.js`，覆盖房间限定的头像请求、等待状态、403/404 首字回退与重复失败抑制、SDK 缺失或非法地址、受限摘要缺失、迟到事件、账号/服务器切换、房间移除以及诊断隔离。头像也使用本地合成 SVG。

V12 新增 `page-actions.spec.js`，覆盖创建邀请码、接受邀请、管理员创建房间期间离页再返回，验证按钮恢复、可以重试且旧响应不能解锁新请求；旧诊断面板的迟到展开事件不能重新读取或展开新页面诊断。当前总计 82 个浏览器用例。
