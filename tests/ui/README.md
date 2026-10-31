# Watch Together 原生分页浏览器验证

测试服务器在 `127.0.0.1:4179` 启动，运行时读取仓库中的真实 `watchtogether.html` 和 `watchtogether.js`。fixture 提供最小 AMD loader、`BaseView`、Emby custom element 兼容层、主题变量、时钟和 `ApiClient` mock，并模拟真实宿主的 `.skinHeader`、`.headerLeft`、空的 `.headerMiddle.headerSection.sectionTabs` 与 `.headerRight`；页面顶栏会显示 `mock Emby host` 标记。它不复制第二套页面实现，也不连接真实 Emby。

使用 `host=missing` 查询参数可以验证宿主分页容器缺失时页面仍能安全渲染、且不会创建第二套页面导航，例如 `/?scenario=active&host=missing`。所有截图均来自仓库真实 HTML/JS 的 mock 渲染，不能作为真实 Emby 宿主视觉或双客户端验收证据。

源码/CI SHA、失败归因及证据交接统一按[验证与交接](../../docs/validation-and-handoff.md)记录；本页详述浏览器夹具和报告操作。

```powershell
npm ci
npx playwright install chromium
npm test
```

## GitHub CI

仓库的 `Pull request and branch CI` 在 PR 和 `main` / `beta` 推送时运行独立的 `Browser UI on Windows` 检查，与 .NET 检查并行。使用 Node.js 24、现有锁文件和 Chromium，命令在 `tests/ui` 下执行；首次运行会下载浏览器，不需要 Emby 服务器或登录凭据。

CI 使用一个 worker，禁止 `test.only`，测试总时限为 10 分钟，任务上限为 15 分钟。失败直接使检查失败，不配置自动重试。现有截图和 trace 仅在失败时保留，HTML 报告每次生成。

在 GitHub Actions 对应运行的 Artifacts 中下载 `ui-browser-results`，其中包含 `playwright-report` 和 `test-results`，保留 7 天；测试失败也会尝试上传。将报告解压后，在本目录运行 `npx playwright show-report <解压后的 playwright-report 路径>`，可查看失败步骤、截图和 trace。若依赖安装或浏览器下载阶段失败，应查看对应步骤日志，可能尚未产生测试报告。

本地复现 CI 运行约束可在 PowerShell 中执行，结束后恢复当前进程原有的 `CI` 值：

```powershell
$previousCi = $env:CI
try {
    $env:CI = 'true'
    npm test
} finally {
    $env:CI = $previousCi
}
```

这项检查只覆盖本地 fixture 中的真实插件 HTML/JS。真实 Emby 宿主和双客户端播放仍分别验证；仓库必需检查和手动发布门禁需另外配置，本次接入不会自动修改这些设置。

截图写入 `test-results/native-controls/` 和 `test-results/native-pages/`，默认 HTML 报告在 `playwright-report/`，这些目录已被 `.gitignore` 忽略。所有邀请码、账号和 token 都是 fixture 内的 mock 值，不能作为真实服务验收证据。

S122 使用 `watchtogether.spec.js` 保留原生控件、主题、宽度、邀请、设置、Token、诊断回归，适配房间/创建与邀请/设置三页。`native-media.spec.js` 覆盖当前用户媒体查询、状态门槛、迟到响应、账号/服务器变化、文本安全和 10 秒新鲜度。页签通过模拟的真实 `mainTabsManager` owner 语义切换；测试不会连接真实 Emby。

S122 验证与部署边界见 [`native-pages-media-plan.md`](../../docs/native-pages-media-plan.md)。

V10 新增 `cinema-ui.spec.js` 检查同源背景图、图片失败与迟到事件、诊断导出隔离及三页布局，截图在 `test-results/cinema-ui/`。fixture 图片为本地合成 SVG，仅用于功能与布局测试；没有从外部图片站或真实媒体库取图。

V11 新增 `avatars.spec.js`，覆盖房间限定的头像请求、等待状态、403/404 首字回退与重复失败抑制、SDK 缺失或非法地址、受限摘要缺失、迟到事件、账号/服务器切换、房间移除以及诊断隔离。头像也使用本地合成 SVG。

V12 新增 `page-actions.spec.js`，覆盖创建邀请码、接受邀请、管理员创建房间期间离页再返回，验证按钮恢复、可以重试且旧响应不能解锁新请求；旧诊断面板的迟到展开事件不能重新读取或展开新页面诊断。当前总计 82 个浏览器用例。

S124 V14 候选将默认 fixture 对齐当前 Server Web，不提供旧的 `Dashboard` 全局；新增初始化、分页和离页回归，以及保留旧宿主全局情况下的下拉选项稳定性回归。导航解析 anchor pathname/query，复现宿主对相对链接的处理，避免把 `#!/` 链接直接赋给地址栏掩盖 emby-linkbutton 路由错误。刷新或编辑名称保留未变化的 option，修改参与者仍更新主用户选项与有效选择。当前总计 84 个浏览器用例；这些检查不代表已复现或验收真实客户端的临时卡住问题。

当前 V17 的 `select-controls.spec.js` 包含五项检查：四个控件沿用 Madby 的宿主默认单选模式，以及 `select_host=legacy|modern` 下 390/1280px 的箭头中线定位。fixture 仅按已核对的两种宿主 DOM/CSS 建立几何夹具；长标签换行后仍要求箭头偏差小于 1px，不能用此证明真实菜单打开或客户端运行验收。当前合计 90 个浏览器用例。
