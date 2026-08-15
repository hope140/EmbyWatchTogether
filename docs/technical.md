# 技术文档（开发者与维护者）

本文面向 Watch Together 的开发者、维护者和发布人员，记录构建、版本、签名发布、项目结构、运行时行为、持久化和 REST API 等实现细节。Emby 管理员和插件使用者请先阅读[用户文档 README.md](../README.md)。

## 运行时行为与约束

- 起播同步使用 `Barrier`：先暂停双方，以主用户位置为锚点对齐另一端，再恢复起播前的暂停/播放状态。进入恢复阶段前，双方都必须在固定 Seek 目标的容差内；Seek 未确认时保留原目标和原播放意图，并在同一 Barrier 的预算内有限重试。
- `Watching` 阶段只传播明确的暂停/继续和明显的手动 Seek，不做周期性追帧。主用户同时操作时作为冲突裁决者；命令确认、抑制窗口以及 session identity、Item、设备会话绑定用于避免回环和旧会话误控制。
- 两端切换到不同 Item 时回到等待状态，不跨 Item Seek；两人同时播放时按安全规则暂停活跃会话，单人播放受到保护。
- `PlaybackStopped` 事件只用于立即唤醒轮询，不直接确认停止。`Watching` 开始后，停止、离线或缺失状态持续达到 2 秒才确认停止；临时同用户替换的不同 `SessionId`（包括不可远控的快照）不能清除观察，只有原 `Previous SessionId` + `ItemId` 且在线、未停止并支持远程控制才算恢复。合法的 seek-to-zero 不单独视为停止。
- Session snapshot provider 连续失败达到 2 秒后进入 `Waiting` 保护，清理不可信同步状态且不向旧 session 发命令；恢复需连续成功 2 秒后使用 fresh snapshot 重新同步，弱网抖动不会频繁触发 Barrier。管理页遇到 `snapshot_unavailable` 时仅提示快照不可用，不声称播放器已暂停。
- 多个共同 Item 候选使用对称 maximin 全局评分选出唯一共同 Item；完全同分时安全等待，不擅自选择。
- 同一用户的多会话仅沿用唯一历史 `SessionId` + `ItemId` 关联；无历史或关联失效时保持 `Waiting`，不猜测新的会话。
- 管理员手动 `pause`/`resume` 在 Barrier 或任一参与者存在 Pending 时整次拒绝且不覆盖同步；成功操作不会把陈旧的运行时错误误报为本次失败。
- 退出自动暂停返回稳定的 `Attempted`、`Succeeded`、`Failed` 汇总；群发消息按目标隔离异常并返回 `Sent`、`Failed`、`Skipped`。
- 命令具备取消、超时和有限重试；起播失败会进入冷却并自动重试。每个房间独立串行处理并隔离异常，不影响其他房间的轮询。
- `SessionInfo` 是服务器轮询快照，不是播放器内部时钟。明显位置跳变的阈值会根据已观测的命令确认延迟提高（普通情况下约 4 秒起），不保证每一帧一致，也不主动消除长期小幅漂移。

## 配置与运行时参数

`PollIntervalSeconds` 默认 `0.5` 秒，用于控制会话轮询频率；`PollIntervalSeconds`、`PauseOtherOnPlaybackStop` 和 `NotifyOtherOnPlaybackStop` 保存后通过配置事件热更新，下一轮轮询生效，配置变更还会唤醒等待中的循环。`Enabled`、`MaxRuntimeDifferenceSeconds`、`SeekToleranceSeconds`、`BarrierSeekTimeoutSeconds` 和 `StaleSessionTimeoutSeconds` 仍保留在配置模型中，但不作为当前实时同步策略；轮询频率也不会开启周期性漂移 Seek。

## 版本号规则

项目新发布统一使用四段版本 `MAJOR.MINOR.PATCH.REVISION`。四段分别表示主版本、功能版本、修复版本和发布修订号：

- `MAJOR`：不兼容的架构、同步协议、持久化格式、配置语义、公共 API 或运行环境契约变化。
- `MINOR`：在保持兼容的前提下增加一组面向用户的功能或较大的行为能力。
- `PATCH`：对现有功能进行一组明确、面向用户的兼容性修复，且不引入新的功能线。
- `REVISION`：同一修复版本内的小范围、低风险、可独立部署的修复、边界保护、日志/提示调整、打包或更新流程修正。

递增高位时，右侧各段归零，例如 `MAJOR` 递增为 `2.0.0.0`，`MINOR` 递增为 `1.3.0.0`，`PATCH` 递增为 `1.2.1.0`，`REVISION` 递增为 `1.2.0.15`。项目文件中的 `Version`、`FileVersion`、`AssemblyVersion` 必须完全一致且不带 `v`；Git tag 使用 `v` 前缀并与三项版本一致，例如 `1.2.0.15` 对应 `v1.2.0.15`。正式版至少改变 MAJOR、MINOR、PATCH 中的一段，第四段仅递增为 beta/prerelease。示例：`1.4.0.0` -> `1.4.0.1`（beta）-> `1.4.1.0`（stable）。当前项目版本为 `1.4.0.0`，对应发布 tag `v1.4.0.0`，已合入 `main` 并进入正式发布线；后续 beta 从 `beta` 分支以 GitHub prerelease 发布，管理员可在插件配置页选择 beta 让更新任务自动获取测试版。历史版本整理不移动、重命名或重建已有 tag。

完整的递增条件、归零规则、历史版本兼容和发布检查见[正式版本号规则](versioning.md)。

## 构建、测试和打包

要求：PowerShell、可用的 .NET SDK，以及能够还原 `MediaBrowser.Server.Core` `4.9.0.52-beta` 的 NuGet 源。首次构建可先执行 `dotnet restore`，然后运行：

```powershell
dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release --nologo -v minimal
dotnet build src/EmbyWatchTogether.sln -c Release --nologo
```

发布脚本会按相同配置依次构建、测试、发布并压缩单 DLL：

```powershell
.\scripts\build.ps1 -Configuration Release
```

成功后产物为：

```text
dist/EmbyWatchTogether/Emby.Plugins.WatchTogether.dll
dist/EmbyWatchTogether.zip
```

ZIP 内 DLL 位于根目录，解压后可直接按[用户文档中的安装步骤](../README.md#安装已构建插件)复制。脚本使用 `.publish/` 作为临时发布目录；这些目录和二进制输出均已被 `.gitignore` 忽略。

## 签名发布流程（维护者）

- `scripts/release/New-ReleaseSigningKey.ps1`：在仓库外生成 RSA PKCS#8 私钥和 `RSAKeyValue` 公钥；私钥不得写入仓库。
- `scripts/release/Sign-ReleaseManifest.ps1`：检查 DLL 名称、程序集名和版本，流式计算大小与 SHA-256，并生成 canonical manifest 与 detached signature。
- `tests/release-signing.tests.ps1`：验证密钥生成、清单 canonical 规则、签名和 DLL 校验流程。
- `tests/release-workflow.tests.ps1`：验证 workflow 只允许手动触发、channel 与分支门禁、输入和固定资产、版本校验、签名步骤及 `--verify-tag`。
- `.github/workflows/release.yml`：只接受 `workflow_dispatch` 的 `tag`、`channel`、`key_id` 输入；新 tag 必须是四段规范数字；`stable` 仅允许从 `main` 触发并通过 GitHub Releases API 检查最高正式版本，要求前三段至少一段变化，`beta` 仅允许从 `beta` 触发；checkout 对应 tag，校验 `Version`、`FileVersion`、`AssemblyVersion`，构建并测试签名后发布四个固定资产。`beta` 创建 prerelease，`stable` 创建普通 Release，不部署服务器。匹配 Secret 缺失或错误、未知 key、未知 channel、分支错配、版本门禁或签名失败时会安全失败。

## 正式版更新实现约束

正式版检查从 GitHub 下载以下三个固定名称的资产：

- `Emby.Plugins.WatchTogether.dll`
- `EmbyWatchTogether.release.manifest`
- `EmbyWatchTogether.release.manifest.sig`

stable 检查入口使用这三个资产的 `releases/latest/download/<asset>` 地址，不调用 GitHub REST API，因此只获取正式 stable Release；管理员选择 beta 后，更新器通过 GitHub Releases API 选择最高版本的非 draft prerelease，再使用对应规范数字 tag 的固定资产地址。两种通道都不能把 API 返回的下载地址直接作为安装来源，且都必须通过同一套签名校验；不能将静态测试描述为真实客户端验收。发布清单必须是严格 UTF-8、LF 换行的 canonical 字段序列（`schema`、`keyId`、`tag`、`version`、`assetName`、`size`、`sha256`）；签名使用 RSA PKCS#1 v1.5 + SHA-256。插件会校验 `keyId` 是否受信任，再以流式 SHA-256、文件大小、程序集名和程序集版本验证 DLL。只有清单验证通过后，安装器的 `sourceUrl` 才使用清单 `tag` 对应的精确地址：`https://github.com/hope140/EmbyWatchTogether/releases/download/<tag>/Emby.Plugins.WatchTogether.dll`；GitHub 资产下载允许官方 CDN 重定向，MD5 仅作为 Emby installer 的二次校验，不是发布信任根。

安装由 Emby 的插件安装器负责，插件不会自行覆盖 DLL，也不会调用重启或关机。安装成功后插件会通知 Emby“等待重启”，仪表盘会出现重启提示；重启前同一版本不会重复安装。正式版 Release 必须包含固定的四个资产：DLL、`EmbyWatchTogether.zip`、发布清单和 detached signature，并且 tag 与三项程序集版本一致。

当前 `ReleaseTrustStore` 已完成生产 bootstrap，包含已审核的公开 `keyId` `prod-2026-08`，并通过不可变的 Ordinal 映射提供验签信任根。匹配 Secret 缺失或错误、未知 key 或签名失败时仍然 fail closed。首次信任引导版本 `1.2.0.9` 必须由运营人工部署；完成后版本方可使用签名自动更新。禁止在文档或仓库写入或提交真实生产私钥、GitHub secret 值、token、本机服务器信息或私人路径；示例不得包含真实生产私钥或 secret 值。

## 项目结构

```text
src/EmbyWatchTogether/        插件入口、房间存储、会话适配、同步状态机和嵌入式管理页
tests/EmbyWatchTogether.Tests/ 单元测试和同步边界回归测试
scripts/build.ps1              构建、测试、发布 DLL 并生成 ZIP
scripts/release/               生成签名密钥和发布清单
tests/release-*.tests.ps1      签名与发布 workflow 静态/集成校验
.github/workflows/release.yml  手动签名发布 workflow，不部署服务器
docs/                          当前实现说明、排错和协作流程
```

## 运行时持久化与生命周期

插件运行时在 Emby 插件数据目录写入 `rooms.json`。房间元数据先写入候选文件，再用 `File.Replace` 替换现有文件并保留 `.bak`；损坏或未知房间不会被静默补建，`RoomManager` 只为已知房间创建 runtime。Pending、Suppressed、上一轮快照和 Barrier 阶段等运行时状态只保存在内存，重启后会重新进入 `Waiting` 并建立新的 Barrier。Pending 不会跨 session、Item 或设备重连重试；重复 `Leave` 不改变成员状态，也不会反复触发暂停。

## REST API

所有接口都需要 Emby 身份认证。管理员可以管理房间和用户列表；参与者只能查看或操作自己参与的房间。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/WatchTogether/Users` | 管理员读取用户列表 |
| `GET` / `POST` | `/WatchTogether/Rooms` | 列出或创建房间 |
| `DELETE` | `/WatchTogether/Rooms/{id}` | 删除房间 |
| `GET` | `/WatchTogether/Rooms/{id}/State` | 查看状态、资格和会话快照 |
| `POST` | `/WatchTogether/Rooms/{id}/Join` | 参与者加入房间 |
| `POST` | `/WatchTogether/Rooms/{id}/Leave` | 参与者退出房间 |
| `POST` | `/WatchTogether/Rooms/{id}/Action` | 管理员执行 `pause`、`resume` 或 `resync` |
| `POST` | `/WatchTogether/Rooms/{id}/Message` | 管理员向在线参与者发送提示 |
| `GET` | `/WatchTogether/Info` | 管理员读取当前版本与 GitHub 仓库地址 |

服务端会再次校验管理员和参与者权限，不能仅依赖管理页隐藏按钮作为安全边界。

## 相关文档

- [返回用户文档 README.md](../README.md)
- [当前架构](architecture.md)
- [完整状态机、测试范围和人工验收步骤](watch-together-emby-plugin-plan.md)
