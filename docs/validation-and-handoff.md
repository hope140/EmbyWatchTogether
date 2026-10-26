# 验证与交接

本页统一 EWT 的验证范围、证据记录和交接格式。职责、授权与完整 diff 门禁见 [`AGENTS.md`](../AGENTS.md)，隔离、派发和停止流程见 [PR Stack 工作流](pr-stack-workflow.md)。按变更影响选择检查，不能用单测、mock 截图或构建成功替代实际运行验收。

## 项目验证入口

以当前仓库配置为准：插件目标为 [`netstandard2.0`](../src/EmbyWatchTogether/EmbyWatchTogether.csproj)，依赖 `MediaBrowser.Server.Core 4.9.0.52-beta`；[测试项目](../tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj)目标为 `net10.0`。[CI](../.github/workflows/ci.yml)使用 Windows、.NET 10 SDK 和 Node.js 24。依赖包版本不等于所有客户端的兼容性证明，也不从其他 Emby 客户端项目套用 .NET、API、版本或验收规则。

| 变更范围 | 适用验证与入口 |
| --- | --- |
| 纯治理文档 | 检查规则一致性、Markdown 相对链接与锚点、UTF-8 编码、围栏及表格格式、`git diff --check` 和完整 diff；不要求产品全量回归 |
| .NET 插件、API、同步逻辑 | 仓库根目录执行 `dotnet restore src/EmbyWatchTogether.sln --nologo`、`dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal`、`dotnet build src/EmbyWatchTogether.sln -c Release --nologo`；按影响补对应运行验收 |
| 嵌入式 HTML/JS | 在 `tests/ui` 执行 `npm ci`、`npx playwright install chromium`、`npm test`；夹具与报告边界见[浏览器验证说明](../tests/ui/README.md)，真实宿主另验 |
| 构建、打包或发布基础设施 | 按影响运行 `pwsh -NoProfile -File scripts/build.ps1 -Configuration Release`、四组 `tests/*.tests.ps1` 门禁以及 `node --test tests/release-request.tests.cjs`；准确命令清单以 [CI](../.github/workflows/ci.yml)为准 |
| Emby 加载、客户端、播放或更新行为 | 按[技术文档](technical.md)、[客户端兼容性记录](client-compatibility.md)和相关 ADR 确定现场场景；记录服务器/客户端版本、插件源码与资产身份以及可复核观察 |

构建脚本会生成 DLL/ZIP，不代表已签名、部署或发布；相关操作仍需独立授权。四组 PowerShell 门禁为 `build-path-guard.tests.ps1`、`release-signing.tests.ps1`、`release-workflow.tests.ps1`、`ci-workflow.tests.ps1`。验证入口变化时先核对真实配置，再更新本页，不复制历史用例数量作为当前保证。

## 证据层次与身份

| 证据 | 能证明的范围 |
| --- | --- |
| 静态检查、单元测试、构建及 mock 浏览器测试 | 对应源码和夹具中的断言；mock Emby host 不能证明真实菜单、宿主行为或双端播放 |
| 隔离 Emby / 真实宿主检查 | 所记录服务器、身份、插件及客户端条件下的 API、页面或运行行为 |
| 服务器部署与插件加载 | 指定资产已安装、加载及启动；HTTP 可达不能证明同步逻辑已验收 |
| 真实双客户端播放与远控 | 所记录两端、媒体和场景下的同步、暂停/继续、Seek、换集、停止或恢复行为 |
| 签名资产与公开发布核验 | 对应 tag、源码、版本、哈希和签名的资产身份；不能代替播放验收 |

每项证据标注源码 commit、工作区是否有未提交差异、执行命令、环境、结果和日志/报告位置。未提交验证要附 diff 范围或摘要，不能只用 HEAD 代表全部源码；构建后源码变化会使旧证据失效，按影响重新验证。二进制交付还需资产 SHA-256 与程序集版本，不能只凭同名 DLL 或版本号认定同一资产。

CI 记录 workflow、run/job 链接、实际受测 SHA、触发事件和结论。分清 PR head、PR 测试合并提交、合并后的 main 和发布 source SHA；只认与声称受测对象一致的结果。别的 SHA 的绿灯不能替代当前候选。新增提交后重新核对证据；发布请求还须满足[发布请求规则](release-requests.md)对准确 source SHA 及请求 head CI 的要求。本地通过不写成 CI 通过，本次无远端运行就明确说明。

## 失败与未完成项

结果按检查记录 `PASS`、`FAIL`、`BLOCKED`、`NOT RUN`；证据不足的行为结论保留 `UNKNOWN`，不算通过。失败先依据日志区分产品缺陷、测试夹具/测试假设、环境/依赖或权限/授权；尚不能归因时写“待定位”，不能为了通过直接归为环境问题。

报告准确命令/动作、目标、退出码或拒绝信息、已完成部分和下一步。权限拒绝、审批待处理、执行结果不确定分别说明；结果不确定先查实际状态。不得静默换验证路径、浏览器、依赖或目标版本，不得跳过断言、自动重试掩盖失败或降低门禁。替代检查如获授权，明确其差异和证据局限，原失败仍保留；范围、权限或验收条件需要变化时返回主线程确认。

## 交接模板

子代理先交付可审核结果与阻塞，主线程核对真实仓库状态后形成最终报告；无对应内容可明确写“无”或“不适用”，不编造已运行结果。

```text
结果与状态: <完成 | 部分完成/受阻；可观察结果>
任务/边界: <本次授权范围；验收标准>
执行设置: <实际 model / effort；Standard，Fast 关闭；未知项明确标注>
源码身份: <worktree、branch、固定 base SHA、HEAD SHA；staged/unstaged/untracked 状态>
提交与远端: <本地 commit 授权及 SHA；push/PR/merge/发布是否执行>
修改文件: <清单与原因>
验证: <每项命令、环境、退出码/结果、受测源码状态、日志/报告>
CI: <workflow/run/job、事件、准确受测 SHA 和结果；或未运行及原因>
现场/资产: <适用的真实宿主/双端/部署/签名证据与身份；或未验证>
未执行项与残余风险: <原因、缺少哪一层证据、后续负责方>
阻塞: <动作、目标、原因、已完成部分；已拒绝/待处理/结果不确定>
Knowledge Findings: <有事实依据的新约束、去重/架构/ADR 建议；或无>
```

私有环境路径、账号、地址、凭据和私人对话不进入公开文档；交接给主线程的本地证据位置与需要提交的公开事实分开处理。
