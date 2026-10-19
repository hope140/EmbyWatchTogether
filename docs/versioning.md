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

## 当前版本和后续示例

当前正式版为 [`v1.6.1.0`](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.6.1.0)，于 2026-10-18 发布并核验为 `Latest`；对应源码提交为 `1a74ad2aab3703a438f592b62424388e8dae0032`。此前公开 stable 为 `1.5.0.0`，最近一次 prerelease 为 `1.6.0.5`；历史版本和失败的 `1.5.0.4` tag 保持原状，不能复用、移动或重建。可选 GitHub Token 仅用于 beta Releases API 查询，stable 和固定资产下载保持原路径。插件默认选择 `stable`，管理员可在插件配置页选择 `beta`，更新任务按所选通道获取并自动安装；`stable` 仍只读取 `releases/latest`。

## 发布通道

当前测试线为 `1.7.0.0`：原生分页、房间媒体和头像展示构成一组兼容的用户功能，按 MINOR 递增并将右侧归零；其测试属性由 beta/prerelease 标记决定。测试版并不固定递增第二段，小范围修正仍按影响使用 PATCH 或 REVISION。正式版与 Latest 继续保持 `1.6.1.0`。

- `main` 只用于 `stable` 正式发布；`stable` workflow 创建普通 GitHub Release。
- `beta` 只用于 `beta` 测试发布；`beta` workflow 创建 GitHub prerelease。
- 手动运行 workflow 时必须选择 `channel` 并提供规范数字 tag。workflow 会拒绝未知 channel，以及 `stable` 从非 `main` 或 `beta` 从非 `beta` 触发的请求。
- channel 只改变 Release 通道标记，不改变 tag、manifest 的 `tag`/`version` 约束或四个固定签名资产名称；不得使用 `-beta` 等版本后缀。
- GitHub prerelease 不会成为 `releases/latest`。`stable` 更新器继续只读取 `releases/latest`；管理员选择 `beta` 后，更新任务通过 Releases API 在非 draft stable 与 prerelease 中选择最高规范版本，再只验证该版本的固定资产并自动安装。历史低版本资产缺失不会阻断最高版本选择；最高版本自身缺少资产仍 fail closed。beta 不能据此声称已完成真实客户端验收，任务开关和计划仍由 Emby 控制。

历史版本索引：

- 历史 stable 基线：`1.3.0.0` / `v1.3.0.0`；
- 历史 beta 修订版：`1.3.0.6` / `v1.3.0.6`；
- 历史兼容功能：`1.4.0.0` / `v1.4.0.0`；
- 历史兼容性修复：`1.4.1.0` / `v1.4.1.0`；
- 历史兼容性修复正式版：`1.4.2.0` / `v1.4.2.0`；
- 历史兼容性修复测试版：`1.4.2.2` / `v1.4.2.2`；
- 历史兼容性修复正式版：`1.4.3.0` / `v1.4.3.0`；
- 历史兼容性修复测试版：`1.4.3.1` / `v1.4.3.1`；
- 历史兼容性修复测试版：`1.4.3.2` / `v1.4.3.2`；
- 历史兼容性修复 stable：`1.4.4.0` / `v1.4.4.0`；
- 历史诊断能力 beta：`1.4.4.1` / `v1.4.4.1`；
- 历史参与者重新同步 beta：`1.4.4.2` / `v1.4.4.2`；
- 历史非管理员入口修复 beta：`1.4.4.3` / `v1.4.4.3`；
- 历史非管理员页面布局优化 beta：`1.4.4.4` / `v1.4.4.4`；
- 历史诊断展示持久化 beta：`1.4.4.6` / `v1.4.4.6`；
- 历史 S1—S4 stable：`1.4.5.0` / `v1.4.5.0`；
- 历史阶段 C beta：`1.4.5.1` / `v1.4.5.1`；
- 历史阶段 C 修订 beta：`1.4.5.2` / `v1.4.5.2`；
- 历史阶段 C stable：`1.5.0.0` / `v1.5.0.0`；
- 历史 beta：`1.5.0.1` / `v1.5.0.1`；
- 历史 beta：`1.5.0.2` / `v1.5.0.2`；
- 历史 beta：`1.5.0.3` / `v1.5.0.3`；
- 失败的 beta 发布尝试（仅保留 Git tag，无 GitHub Release）：`1.5.0.4` / `v1.5.0.4`；
- 历史 Phase D beta：`1.6.0.2` / `v1.6.0.2`；
- 历史 beta 修订版：`1.6.0.3` / `v1.6.0.3`；
- 历史 beta 修订版：`1.6.0.4` / `v1.6.0.4`；
- 历史 beta 修订版：`1.6.0.5` / `v1.6.0.5`；
- 不兼容变更示例：`2.0.0.0` / `v2.0.0.0`。

## 发布检查

维护者创建版本时按以下顺序检查：

1. 根据变更影响选择需要递增的段位，并将右侧段位归零；
2. 在项目文件中同步更新 `Version`、`FileVersion`、`AssemblyVersion`；
3. 为该版本准备并审核 `docs/releases/v<version>.md` 中文 Release Notes，确认内容覆盖用户可见变化、配置兼容性和升级注意；
4. 运行构建、测试和发布校验；
5. 在包含版本字段和 Release Notes 的提交上创建唯一的四段 `v` 前缀 tag；
6. 手动发布 workflow 时，选择与触发分支匹配的 `channel`，并将同一个 tag 作为 `tag` 输入。

相关实现和校验见：

- [`EmbyWatchTogether.csproj`](../src/EmbyWatchTogether/EmbyWatchTogether.csproj)
- [`release.yml`](../.github/workflows/release.yml)
- [`Sign-ReleaseManifest.ps1`](../scripts/release/Sign-ReleaseManifest.ps1)
- [`release-signing.tests.ps1`](../tests/release-signing.tests.ps1)
