# 项目版本号规则

本文是 Watch Together 项目版本号的正式约定。新版本、Git tag、发布清单和程序集元数据必须按本文执行；已有版本号保持不变，不因规则落地而重命名。

## 格式

项目新发布统一使用四段数字格式：

```text
MAJOR.MINOR.PATCH.REVISION
```

项目文件中的版本号不带 `v`，Git 发布 tag 在前面加 `v`：

```text
项目版本：1.6.0.5
Git tag：v1.6.0.5
```

发布时必须同时满足以下条件：

- `src/EmbyWatchTogether/EmbyWatchTogether.csproj` 中的 `Version`、`FileVersion` 和 `AssemblyVersion` 三项完全相同；
- 三项项目版本与 Git tag 去掉 `v` 后的值完全相同；
- DLL 程序集版本、manifest 的 `version` 和 manifest 的 `tag` 也必须与上述版本一致；
- 每一段都是非负十进制整数，除单独的 `0` 外不得有前导零；不得使用空格、`-beta`、`-rc` 或 `+metadata` 等后缀。

例如，`1.2.0.15` 与 `v1.2.0.15` 是一对匹配的版本值；`1.02.0.15`、`v1.2.0.15-beta` 和项目版本为 `1.2.0.15` 但 tag 为 `v1.2.0.16` 均无效。

历史版本可以保留三段 tag，但新发布 tag 必须严格使用四段规范数字格式。正式版至少改变前 3 段中的一段；第四段 `REVISION` 仅允许递增为 beta/prerelease，不得单独用于 stable 正式版。

## 四段的含义

| 段位 | 含义 | 递增条件 |
| --- | --- | --- |
| `MAJOR` | 主版本 | 发生不兼容的架构、同步协议、持久化格式、配置语义、公共 API 或运行环境契约变化。 |
| `MINOR` | 功能版本 | 在保持兼容的前提下增加一组面向用户的功能或较大的行为能力。 |
| `PATCH` | 修复版本 | 对现有功能进行一组明确、面向用户的兼容性修复，且不引入新的功能线。 |
| `REVISION` | 发布修订号 | 同一修复版本内的小范围、低风险、可独立部署的修复、边界保护、日志/提示调整、打包或更新流程修正。 |

递增高位时，右侧各段归零：

- `MAJOR` 递增：`2.0.0.0`；
- `MINOR` 递增：`1.3.0.0`；
- `PATCH` 递增：`1.2.1.0`；
- `REVISION` 递增：`1.2.0.15`。

不要为了保持版本连续而修改已经发布的版本号，也不要复用已经存在的 tag。历史版本整理只更新文档或索引，不移动、重命名或重建已有 tag。

## 发布通道

历史版本和发布记录见[发布索引](releases/README.md)及[维护记录](maintenance/README.md)。测试版沿用当前正式版的前三段，依次递增第四段。前三段的更新随正式版发布。测试版在 GitHub 标记为 prerelease。

- `main` 只用于 `stable` 正式发布；`stable` workflow 创建普通 GitHub Release。
- `beta` 只用于 `beta` 测试发布；`beta` workflow 创建 GitHub prerelease。
- 手动运行 workflow 时必须选择 `channel` 并提供规范数字 tag。workflow 会拒绝未知 channel，以及 `stable` 从非 `main` 或 `beta` 从非 `beta` 触发的请求。
- channel 只改变 Release 通道标记，不改变 tag、manifest 的 `tag`/`version` 约束或四个固定签名资产名称；不得使用 `-beta` 等版本后缀。
- GitHub prerelease 不会成为 `releases/latest`。`stable` 更新器继续只读取 `releases/latest`；管理员选择 `beta` 后，更新任务通过 Releases API 在非 draft stable 与 prerelease 中选择最高规范版本，再只验证该版本的固定资产并自动安装。历史低版本资产缺失不会阻断最高版本选择；最高版本自身缺少资产仍 fail closed。beta 不能据此声称已完成真实客户端验收，任务开关和计划仍由 Emby 控制。

## 内部预览

内部预览沿用要验证的通道已经公开的最近数字版本。只在本地构建时设置 `InformationalVersion`，例如 `1.7.3.4-preview.20261118T130000Z.abcdef0`。同时记录准确源码、未提交差异及 DLL 的 SHA-256，以便区分相同数字版本的不同构建。预览通过私有方式交付，不创建公开 tag、Release 或自动更新资产。仓库中的三个数字版本字段只在准备公开发版时递增。

项目的三项数字版本已和预览基线一致时，可在仓库根目录构建预览。例如：

```powershell
$baseVersion = '1.7.3.4'
$previewId = '{0}.{1}' -f (Get-Date -AsUTC -Format 'yyyyMMddTHHmmssZ'), (git rev-parse --short HEAD)
dotnet publish src/EmbyWatchTogether/EmbyWatchTogether.csproj -c Release -o "../ewt-preview-$previewId" "-p:InformationalVersion=$baseVersion-preview.$previewId"
```

构建后核对 DLL 的程序集版本、文件版本和带预览标识的产品版本。如果数字版本与所选基线不一致，先在独立构建中显式设置 `Version`、`FileVersion` 与 `AssemblyVersion`。完成验证后记录 DLL 摘要。

更新器比较已加载 DLL 的四段数字版本，只有更高版本会自动安装。同一数字版本的预览和公开包之间切换，使用备份或已验证的公开 DLL 手动替换，并按安装说明重启。

## 发布检查

维护者创建版本时按以下顺序检查：

1. 测试版使用当前正式版的前三段，在这个系列**已经公开的最高版本**基础上把第四段加一。首次测试发布时从当前正式版加一。只在内部验证的构建不计入。先核对已公开 Release 和 tag；如果应使用的 tag 已存在但未发布，先核对其指向及发布状态。保留已公开版本及历史缺号。正式版按变更影响选择递增的段位，并将右侧段位归零。
2. 在发布源提交中同步更新 `Version`、`FileVersion`、`AssemblyVersion`。
3. 同一提交新增 `docs/releases/v<version>.md`，覆盖用户可见变化、配置兼容性和升级注意。同步更新 `README.md`、`README.en.md` 与[发布索引](releases/README.md)的对应通道、下载和说明入口，将上一版本移入按数字降序排列的历史。需要时更新安装、配置和开发文档。
4. 运行构建、测试和发布校验。
5. 按授权选择发布入口：手动 workflow 使用与通道匹配的分支及唯一 tag；[审核式发布请求](release-requests.md)固定源码和 CI，由受信任工作流创建或核验 tag。
6. 发布完成后从公开渠道回读实际 tag、Release 正文和四个附件，核对签名、版本及中英文下载链接。

相关实现和校验见：

- [`EmbyWatchTogether.csproj`](../src/EmbyWatchTogether/EmbyWatchTogether.csproj)
- [`release.yml`](../.github/workflows/release.yml)
- [`Sign-ReleaseManifest.ps1`](../scripts/release/Sign-ReleaseManifest.ps1)
- [`release-signing.tests.ps1`](../tests/release-signing.tests.ps1)
