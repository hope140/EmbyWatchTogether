# 项目版本号规则

本文是 Watch Together 项目版本号的正式约定。新版本、Git tag、发布清单和程序集元数据必须按本文执行；已有版本号保持不变，不因规则落地而重命名。

## 格式

项目新发布统一使用四段数字格式：

```text
MAJOR.MINOR.PATCH.REVISION
```

项目文件中的版本号不带 `v`，Git 发布 tag 在前面加 `v`：

```text
项目版本：1.3.0.5
Git tag：v1.3.0.5
```

发布时必须同时满足以下条件：

- `src/EmbyWatchTogether/EmbyWatchTogether.csproj` 中的 `Version`、`FileVersion` 和 `AssemblyVersion` 三项完全相同；
- 三项项目版本与 Git tag 去掉 `v` 后的值完全相同；
- DLL 程序集版本、manifest 的 `version` 和 manifest 的 `tag` 也必须与上述版本一致；
- 每一段都是非负十进制整数，除单独的 `0` 外不得有前导零；不得使用空格、`-beta`、`-rc` 或 `+metadata` 等后缀。

例如，`1.2.0.15` 与 `v1.2.0.15` 是一对匹配的版本值；`1.02.0.15`、`v1.2.0.15-beta` 和项目版本为 `1.2.0.15` 但 tag 为 `v1.2.0.16` 均无效。

当前发布 workflow 和验签代码为兼容历史版本，技术上仍接受三段数字 tag；今后的新发布不得再使用三段格式，必须使用四段格式。

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

不要为了保持版本连续而修改已经发布的版本号，也不要复用已经存在的 tag。

## 当前版本和后续示例

当前项目版本为 `1.3.0.5`，对应发布 tag 预期为 `v1.3.0.5`。`1.3.0.4`、`1.3.0.3`、`1.3.0.2` 和 `1.3.0.1` 保留为历史版本，不因本次修正而覆盖或重命名；本次 beta 尚未创建正式 Release。现有 `1.2.0.x` 历史版本主要使用第四段递增；该历史约定保留，后续发布按本文区分功能、修复和修订级别。

以当前版本为基准：

- 小范围兼容性修正：`1.3.0.5` / `v1.3.0.5`；
- 一组明确的用户可感知修复：`1.3.1.0` / `v1.3.1.0`；
- 新增兼容功能：`1.4.0.0` / `v1.4.0.0`；
- 不兼容变更：`2.0.0.0` / `v2.0.0.0`。

## 发布检查

维护者创建版本时按以下顺序检查：

1. 根据变更影响选择需要递增的段位，并将右侧段位归零；
2. 在项目文件中同步更新 `Version`、`FileVersion`、`AssemblyVersion`；
3. 为该版本准备并审核 `docs/releases/v<version>.md` 中文 Release Notes，确认内容覆盖用户可见变化、配置兼容性和升级注意；
4. 运行构建、测试和发布校验；
5. 在包含版本字段和 Release Notes 的提交上创建唯一的四段 `v` 前缀 tag；
6. 手动发布 workflow 时，将同一个 tag 作为 `tag` 输入。

相关实现和校验见：

- [`EmbyWatchTogether.csproj`](../src/EmbyWatchTogether/EmbyWatchTogether.csproj)
- [`release.yml`](../.github/workflows/release.yml)
- [`Sign-ReleaseManifest.ps1`](../scripts/release/Sign-ReleaseManifest.ps1)
- [`release-signing.tests.ps1`](../tests/release-signing.tests.ps1)
