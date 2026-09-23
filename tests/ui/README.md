# Watch Together UI VNext 浏览器验证

测试服务器在 `127.0.0.1:4179` 启动，运行时读取仓库中的真实 `watchtogether.html` 和 `watchtogether.js`。fixture 提供最小 AMD loader、`BaseView`、Emby custom element 兼容层、主题变量、时钟和 `ApiClient` mock，并模拟真实宿主的 `.skinHeader`、`.headerLeft`、空的 `.headerMiddle.headerSection.sectionTabs` 与 `.headerRight`；页面顶栏会显示 `mock Emby host` 标记。它不复制第二套页面实现，也不连接真实 Emby。

使用 `host=missing` 查询参数可以验证宿主顶栏槽位缺失时的页面内回退导航，例如 `/?scenario=active&host=missing`。所有截图均来自仓库真实 HTML/JS 的 mock 渲染，不能作为真实 Emby 宿主视觉或双客户端验收证据。

```powershell
npm ci
npx playwright install chromium
npm test
```

截图和 HTML 报告写入 `test-results/ui-vnext/` 与 `test-results/report/`，这些目录已被 `.gitignore` 忽略。所有邀请码、账号和 token 都是 fixture 内的 mock 值，不能作为真实服务验收证据。
