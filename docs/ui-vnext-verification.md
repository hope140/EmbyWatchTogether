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

以下项目在对应 Stack 完成前均为 `NOT RUN`。

| 类别 | 需要的证据 | 当前状态 |
|---|---|---|
| 主题 | 宿主浅色、深色、完整色变量、仅 HSL 分量 | NOT RUN |
| 响应式 | 320、360、390、430、768、1024、1440px；无页面横向溢出 | NOT RUN |
| 身份 | 管理员非成员、管理员成员、普通已加入、普通暂离 | NOT RUN |
| 房间状态 | Waiting、Barrier、Watching、Handoff、Unavailable、未知原因 | NOT RUN |
| 生命周期 | 单一轮询、迟到响应隔离、pause/resume、账号切换、旧数据标记 | NOT RUN |
| 邀请 | 完整 24 位码、大小写、`-`/`_`、复制失败、撤销、过期、Accept 超时读回 | NOT RUN |
| 操作 | 点击前、accepted、busy、部分失败、失败、取消 | NOT RUN |
| 设置 | dirty、离开确认、失败保留草稿、成功回读、隐藏字段保留 | NOT RUN |
| 诊断 | 按需读取、三轮轮询后保持展开、迟到隔离、导出脱敏 | NOT RUN |
| 构建包 | 最终 commit 构建、单 DLL ZIP、DLL/ZIP 哈希、provenance | NOT RUN |
| 真实 Emby | 桌面/手机、双账号、真实主题、真实播放与缓存 | NOT AUTHORIZED / NOT RUN |

## P0 / P1 状态

- P0：进行中。
- P1：待宿主媒体读取能力和当前用户权限验证；未验证前使用占位。

### S4 媒体展示能力门禁审计（2026-09-22）

结论：P1 skipped。生产代码保持“媒体信息待权限验证”占位，当前证据不足以安全显示真实片名、封面或头像。

代码与测试证据：

- `src/EmbyWatchTogether/WatchTogetherService.cs:321-373` 的 `GET /WatchTogether/Rooms/{Id}/State` 只返回 `SyncItemId`、每个 `Sessions` 的 `ItemId`、`PositionTicks`、`RunTimeTicks`、播放状态和能力标志；没有片名、媒体详情、封面 URL 或头像 URL。
- `src/EmbyWatchTogether/SessionSnapshot.cs:70-113` 从 `SessionInfo.NowPlayingItem` 只提取 `Id` 与 `RunTimeTicks`，没有保留 `BaseItemDto` 的标题或图片字段。因此现有状态 DTO 无法支撑媒体卡片。
- `src/EmbyWatchTogether/WatchTogetherService.cs:951-978` 的参与者摘要只通过 `IUserManager.GetUserById` 读取并返回用户 `Id`、`Name`；未返回头像标识或图片地址。
- `src/EmbyWatchTogether/WatchTogetherService.cs:662-678` 的 `/WatchTogether/Users` 是管理员专用（`RequireAdmin()`），返回范围为全站用户 Id/Name；它不是普通参与者可用的头像或媒体读取授权边界。
- `src/EmbyWatchTogether/Web/Configuration/watchtogether.js:28-47` 仅封装 `ApiClient.getUrl`、`getJSON`、`fetch` 调用插件路由；代码中没有已验证的 Emby `Items`、图片或用户头像 API 调用。`loadRoomStates`（约 `2085` 行）也只读取本插件 `/Rooms/{Id}/State`。
- `tests/EmbyWatchTogether.Tests/WatchTogetherServiceRoomTests.cs:198-248` 验证管理员非成员、普通成员/暂离成员和房间状态读取隔离；测试仅断言 `Sessions` 的运行时字段，未证明跨用户媒体详情或图片访问权限。测试构造的 `NowPlayingItem`（约 `1594` 行）也只提供 Id/时长。

待真实 Emby 验收后才能重新评估放行：使用双账号（管理员非成员、普通已加入/暂离）分别验证宿主 `Items`/图片/头像 API 的可调用性、返回字段的用户权限过滤、跨账号缓存隔离、账号切换后的旧数据清除，以及无媒体/过期状态的回退。未完成这些检查前不得写入 mock 片名、封面或头像，也不得把 `ItemId` 当作可展示标题。

P0 占位属于当前权限门禁的预期行为，不是媒体展示缺陷；本 Stack 未修改 HTML/JS/tests。

## 本地测试包约束

- 包类型：未签名本地候选，不是 Release。
- 程序集版本：保持 `1.6.0.1`，因此必须依靠 commit 与 SHA-256 区分公开 `v1.6.0.1`。
- 输出名称：`EmbyWatchTogether-UI-VNext-localtest-<short-sha>-UNSIGNED.zip`。
- 附件：`SHA256.txt`、`PROVENANCE.txt`。
- 禁止：manifest、signature、tag、Release、上传、部署、Emby 重启。

## Knowledge Review

S0 新确认：`/Rooms` 已有 `IsAdmin`；计划内最小只读扩展应为 `/Rooms.CurrentUserIsParticipant`、`/State.CurrentUserIsParticipant`、`/State.IsAdmin` 和 `/State.Sessions.RunTimeTicks`。管理员非成员当前会被前端误渲染为暂离成员，这是 P0 必修缺陷。暂不需要更新 `architecture.md` 或新增 ADR。
