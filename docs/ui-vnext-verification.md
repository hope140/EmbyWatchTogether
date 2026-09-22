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

## 本地测试包约束

- 包类型：未签名本地候选，不是 Release。
- 程序集版本：保持 `1.6.0.1`，因此必须依靠 commit 与 SHA-256 区分公开 `v1.6.0.1`。
- 输出名称：`EmbyWatchTogether-UI-VNext-localtest-<short-sha>-UNSIGNED.zip`。
- 附件：`SHA256.txt`、`PROVENANCE.txt`。
- 禁止：manifest、signature、tag、Release、上传、部署、Emby 重启。

## Knowledge Review

S0 新确认：`/Rooms` 已有 `IsAdmin`；计划内最小只读扩展应为 `/Rooms.CurrentUserIsParticipant`、`/State.CurrentUserIsParticipant`、`/State.IsAdmin` 和 `/State.Sessions.RunTimeTicks`。管理员非成员当前会被前端误渲染为暂离成员，这是 P0 必修缺陷。暂不需要更新 `architecture.md` 或新增 ADR。
