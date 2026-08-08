# Emby Watch Together 插件

Watch Together 是一个运行在 Emby Server 内的双人同步观看插件，当前项目版本为 `1.2.0.9`。它读取同一台服务器上的会话快照，并通过 Emby 远程控制命令协调起播、暂停/继续、用户手动拖动进度、切换视频和停止播放。

插件只负责房间内的协调，不会修改媒体库、转码设置或播放器客户端。正常播放期间不做周期性 Seek：网络延迟、SessionInfo 更新延迟和小幅播放速度差不会被反复纠正。

## 功能

- 管理员创建一个包含两名用户的房间，并指定主用户；每名用户同时只能属于一个房间。
- 两位参与者打开相同 Item 后，插件执行起播 Barrier：暂停双方、以主用户位置为锚点对齐另一端，再恢复起播前的暂停/播放状态。
- `Watching` 阶段传播明确的暂停/继续和手动 Seek。主用户同时操作时优先作为冲突裁决者，命令确认和抑制窗口可避免回环与重复控制；会话选择和同步命令都绑定当前 session identity、Item 和设备会话。
- 切换到不同 Item 时回到等待状态，不跨 Item Seek；两人都在播放时会按安全规则暂停活跃会话，单人播放受到保护。
- 停止处理优先消费 Emby 的 `PlaybackStopped` 事件，并按 user/session/item identity 匹配当前房间；事件缺失时才使用会话快照判断。会话暂时消失要经过 2 秒 debounce，合法的 seek-to-zero 不会单独被当成停止；默认暂停另一方并发送提示消息，两个行为可以分别关闭。
- Emby 管理页提供房间创建、加入/退出、暂停、继续、重新同步、删除和状态查看。
- 命令带取消和超时，确认超时后只做有限重试；起播失败进入冷却并自动重试，不会无限刷命令。每个房间的同步串行执行，单个房间异常会被隔离并记录，不会终止其他房间的轮询。

## 兼容性与边界

- 目标运行环境是 Emby Server 4.9 API（项目引用 `MediaBrowser.Server.Core` `4.9.0.52-beta`），程序集目标框架为 `netstandard2.0`。
- 一个房间恰好两名参与者，且两人必须登录同一台 Emby Server；不支持跨服务器或多人同步。
- 两个会话必须打开相同 Item、媒体时长相差不超过 3 秒、播放速率接近 1，并同时支持 Pause、Unpause、Seek 远程命令。客户端没有这些能力时房间会停留在 `Waiting`。
- SessionInfo 是服务器轮询快照，不是播放器内部时钟。插件只传播明显的单次位置跳变；阈值会根据已观测的命令确认延迟提高（普通情况下约 4 秒起），不保证每一帧都相同，也不主动消除长期的小幅漂移。
- 不依赖外部运行时、脚本或额外服务；消息展示、远程控制和确认延迟仍取决于实际 Emby 客户端。
- `Enabled` 和四个保留阈值字段在当前版本仍保留在 Emby 配置对象中，但不作为实时同步策略；请以本 README 描述的行为为准。

## 安装已构建插件

1. 在 Emby Server 管理后台确认服务器已停止写入旧版本（升级前建议备份旧 DLL）。
2. 从发布产物解压 `EmbyWatchTogether.zip`，得到根目录下的 `Emby.Plugins.WatchTogether.dll`。
3. 将 DLL 直接复制到 Emby Server 数据目录的 `plugins` 目录，不要再套一层 `EmbyWatchTogether` 子目录。若不确定数据目录位置，可在 Emby 管理后台的服务器路径页面查看。
4. 启动或重启 Emby Server。进入 **Dashboard → Plugins → Watch Together**，确认设置页能够打开。

插件是单 DLL 交付，不需要复制源码、NuGet 包或其他旁车进程。升级时停止 Emby、替换 DLL 后再启动；回滚时恢复备份的旧 DLL。

## 插件正式版更新

插件更新由 Emby 计划任务处理，插件配置页不再提供检查、安装或更新设置。服务器启动后，**Dashboard → 计划任务** 中会出现名为“Watch Together 更新检查”的任务，默认每 24 小时运行一次；管理员可以在那里调整检测时间、禁用任务或手动执行。插件配置页只显示当前版本和 GitHub 链接。

正式版检查从 GitHub 下载以下三个固定名称的资产：

- `Emby.Plugins.WatchTogether.dll`
- `EmbyWatchTogether.release.manifest`
- `EmbyWatchTogether.release.manifest.sig`

检查入口使用这三个资产的 `releases/latest/download/<asset>` 地址，不调用 GitHub REST API。发布清单必须是严格 UTF-8、LF 换行的 canonical 字段序列（`schema`、`keyId`、`tag`、`version`、`assetName`、`size`、`sha256`）；签名使用 RSA PKCS#1 v1.5 + SHA-256。插件会校验 `keyId` 是否受信任，再以流式 SHA-256、文件大小、程序集名和程序集版本验证 DLL。只有清单验证通过后，安装器的 `sourceUrl` 才使用清单 `tag` 对应的精确地址：`https://github.com/hope140/EmbyWatchTogether/releases/download/<tag>/Emby.Plugins.WatchTogether.dll`；MD5 仅作为 Emby installer 的二次校验，不是发布信任根。

安装由 Emby 的插件安装器负责，插件不会自行覆盖 DLL，也不会调用重启或关机。安装成功后插件会通知 Emby“等待重启”，仪表盘会出现重启提示；重启前同一版本不会重复安装。正式版 Release 必须包含固定的四个资产：DLL、`EmbyWatchTogether.zip`、发布清单和 detached signature，并且 tag 与三项程序集版本一致。

当前 `ReleaseTrustStore` 已完成生产 bootstrap，包含已审核的公开 `keyId` `prod-2026-08`，并通过不可变的 Ordinal 映射提供验签信任根。匹配 Secret 缺失或错误、未知 key 或签名失败时仍然 fail closed。`1.2.0.9` 必须先由运营人工部署并完成信任引导；完成后版本方可使用签名自动更新。禁止在文档或仓库写入或提交真实生产私钥、GitHub secret 值、token、本机服务器信息或私人路径；示例不得包含真实生产私钥或 secret 值。

## 使用方法

### 创建房间

1. 使用管理员账号打开 **Watch Together** 页面。
2. 填写房间名称，选择两名不同的参与者，并从两人中指定主用户。
3. 创建后让两名用户登录同一 Emby Server、打开同一个视频；必要时在房间卡片上点击“加入房间”。
4. 房间状态依次可能显示为：

   - `Waiting`（等待参与者或等待双方打开同一 Item）；
   - `Barrier`（正在暂停、Seek 和恢复）；
   - `Watching`（已完成起播同步）；
   - `Unavailable`（房间归属的服务器 ID 与当前实例不一致）。

进入 `Watching` 后，暂停/继续和明显的手动拖动会传播到另一端。管理员可以对房间执行暂停、继续或重新同步；重新同步会清理运行时状态并重新执行 Barrier。

### 停止行为设置

设置页的“播放停止行为”区域提供两个独立开关，默认均开启：

| 配置项 | 默认值 | 作用 |
| --- | ---: | --- |
| `PauseOtherOnPlaybackStop` | `true` | 一方被 `PlaybackStopped` 事件或快照停止逻辑确认后，暂停仍在播放的另一方 |
| `NotifyOtherOnPlaybackStop` | `true` | 同一停止事件发生时向另一方发送文字提示 |

配置由 Emby 保存，只有管理员可以修改。`PollIntervalSeconds` 默认 `0.5` 秒，用于控制会话轮询频率；`PollIntervalSeconds`、`PauseOtherOnPlaybackStop` 和 `NotifyOtherOnPlaybackStop` 保存后通过配置事件热更新，下一轮轮询生效，配置变更还会唤醒等待中的循环。`Enabled`、`MaxRuntimeDifferenceSeconds`、`SeekToleranceSeconds`、`BarrierSeekTimeoutSeconds` 和 `StaleSessionTimeoutSeconds` 仍是模型中的保留字段，不作为实时策略；轮询频率也不会开启周期性漂移 Seek。

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

ZIP 内 DLL 位于根目录，解压后可直接按“安装已构建插件”中的步骤复制。脚本使用 `.publish/` 作为临时发布目录；这些目录和二进制输出均已被 `.gitignore` 忽略。

### 签名发布流程（维护者）

- `scripts/release/New-ReleaseSigningKey.ps1`：在仓库外生成 RSA PKCS#8 私钥和 `RSAKeyValue` 公钥；私钥不得写入仓库。
- `scripts/release/Sign-ReleaseManifest.ps1`：检查 DLL 名称、程序集名和版本，流式计算大小与 SHA-256，并生成 canonical manifest 与 detached signature。
- `tests/release-signing.tests.ps1`：验证密钥生成、清单 canonical 规则、签名和 DLL 校验流程。
- `tests/release-workflow.tests.ps1`：验证 workflow 只允许手动触发、输入和固定资产、版本校验、签名步骤及 `--verify-tag`。
- `.github/workflows/release.yml`：只接受 `workflow_dispatch` 的 `tag`、`key_id` 输入；checkout 对应 tag，校验 `Version`、`FileVersion`、`AssemblyVersion`，构建并测试签名后发布四个固定资产，不部署服务器。匹配 Secret 缺失或错误、未知 key 或签名失败时会安全失败。

## 项目结构

```text
src/EmbyWatchTogether/       插件入口、房间存储、会话适配、同步状态机和嵌入式管理页
tests/EmbyWatchTogether.Tests/ 单元测试和同步边界回归测试
scripts/build.ps1             构建、测试、发布 DLL 并生成 ZIP
scripts/release/              生成签名密钥和发布清单
tests/release-*.tests.ps1     签名与发布 workflow 静态/集成校验
.github/workflows/release.yml 手动签名发布 workflow，不部署服务器
docs/                          当前实现说明、排错和协作流程
```

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

## 排错清单

- 状态长时间为 `Waiting`：确认两人已加入、在线会话打开相同 Item，且客户端报告了 Pause、Unpause、Seek 能力。
- 状态出现不同视频提示：两端 ItemId 不一致；插件不会跨视频追赶，重新打开相同视频即可触发新的 Barrier。
- Barrier 失败：查看房间卡片的错误信息，确认 Emby 能返回更新后的 SessionInfo；命令会有限重试，冷却结束后自动重试。
- 频繁跳转：本插件只对明显单次跳变发送 Seek。若正常播放仍反复跳转，应先检查其他客户端、插件或遥控器是否在发送 Seek。
- 停止后另一端未暂停或未收到消息：检查设置页两个开关，以及目标客户端是否支持 Pause 或 DisplayMessage。

更完整的状态机、确认窗口、测试范围和人工验收步骤见 [`docs/watch-together-emby-plugin-plan.md`](docs/watch-together-emby-plugin-plan.md)。
