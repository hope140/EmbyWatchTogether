# Watch Together UI VNext 浏览器验证

测试服务器在 `127.0.0.1:4179` 启动，运行时读取仓库中的真实 `watchtogether.html` 和 `watchtogether.js`。fixture 只提供最小 AMD loader、`BaseView`、Emby custom element 兼容层、主题变量、时钟和 `ApiClient` mock；它不复制第二套页面实现，也不连接真实 Emby。

```powershell
npm ci
npx playwright install chromium
npm test
```

截图和 HTML 报告写入 `test-results/ui-vnext/` 与 `test-results/report/`，这些目录已被 `.gitignore` 忽略。所有邀请码、账号和 token 都是 fixture 内的 mock 值，不能作为真实服务验收证据。
