# v1.6.1.0 发布准备记录

本文记录 S119 的准备阶段，不代表当前远端发布状态。用户已于 2026-10-18 确认本轮验收通过；正式 stable 发布由后续主线程流程完成并回读。

## 当前范围与状态

S119 当时准备 stable 候选 `1.6.1.0`，在该阶段暂不创建 tag、推送分支、运行发布 workflow、创建 GitHub Release 或部署此版本。该阶段的 NAS 记录为 S118 构建 `1.6.0.5`；UI 反馈与真实双端播放验收分开记录。

现有版本约定已预定 `1.6.1.0` 为下一正式版；它高于公开 stable `1.5.0.0` 和 beta `1.6.0.5`，且不是 revision-only 正式版。准备时远端该 tag 未占用，正式发布前必须重查。

## S119 契约（历史准备阶段）

- 目标：冻结只改变版本与发布文档的正式候选，提供本地 DLL/ZIP、版本/哈希来源、未签名 manifest 草稿和发布核对清单。
- 基础：`beta@9d84e971925c3b179725318fef99f79bc98bcf3d`；前置：S111–S118 集成及当前 UI 初步认可。`origin/main@c6568f5` 是基线祖先，候选保留该历史，后续正式 main 提升可使用 fast-forward。
- 分支与隔离：独立 `codex/stack-119-stable-release-prep` worktree，主线程已实际核验；不改动已有 main、beta 或其他 worktree。
- 文件所有权：Luna Medium worker 仅拥有 `src/EmbyWatchTogether/EmbyWatchTogether.csproj` 的三个版本字段和 `docs/releases/v1.6.1.0.md`；主线程拥有 README、technical/versioning、release 索引、development-roadmap、本准备文档和被忽略的验证产物。
- 禁止修改：其他产品代码、UI、API/同步/Session/Room/邀请/Token 业务、配置/持久化、依赖、测试、信任公钥、发布/CI 工作流、远端与 NAS。
- 实现：三项版本均为 `1.6.1.0`；中文 UTF-8 无 BOM 发布说明覆盖自 stable `1.5.0.0` 起的用户可见变化，说明 UI、换集、参与者短时恢复、保守漂移修复及更新设置兼容；不得声称偶发多跳集根因已修复、全客户端验收通过或已经正式发布。
- 验收：当前业务/UI 源码与 S118 基线完全相同；三项项目版本、DLL 程序集版本、候选 tag 字符串及 manifest 草稿一致；Release Notes 非空中文无 BOM；完整测试/构建、四组门禁、浏览器回归、ZIP 与来源/哈希核对完成。生产签名和新提交公共 CI 单独标记。
- 测试：worker 验证 XML、Release Notes 编码和 diff；主线程执行 `scripts/build.ps1 -Configuration Release`、四组 `tests/*.tests.ps1` 门禁、`tests/ui` 的 `npm ci` / `npm test`，检查候选与公开 ref 的祖先关系、版本/tag 占用、canonical manifest 草稿与包内容。
- 回滚：本地 revert 准备提交；原已部署 S118 不变。产物独立保留，不覆盖旧部署 receipt。
- 并行：主线程文档与 worker 两个文件不重叠；交接后串行审核、提交冻结和最终构建。最终构建后不再创建改变 source revision 的文档提交，证据保存在被忽略的 receipt。
- 执行代理：Luna Medium worker 完成边界明确的版本/说明；主线程负责版本决策、整合审查、验证与交付。Max 不适用。
- Knowledge context：AGENTS、PR Stack 工作流、versioning、technical 签名/更新约束、现有 beta 发布说明、S118 验证、当前源码。无新增架构决策，不需 ADR。

## 发布前核对（历史准备阶段）

本地验证与 artifact 身份以该 worktree 被忽略的 `dist/release-readiness.json` 为准。本地 DLL/ZIP 是未签名候选，manifest 草稿不带签名，不能冒充 GitHub 正式签名资产。正式发布 workflow 会根据 CI 自己的 DLL 重新生成 manifest 和签名，CI 字节哈希以下载后独立核验为准。

- UI：用户已确认当前效果可接受；真实双端基本同步、手动/自然换集、停止与恢复、短时参与者恢复及长时观看按 `client-compatibility.md` 的证据边界解释。
- 公开 CI：公开 beta 最新成功运行对应旧 `a1408e0`，不能作为本地候选提交的公共 CI 结果。
- 签名配置：实际 workflow 使用 `release` Environment；该 Environment 已发现所需生产签名 Secret 的配置元数据，当前无 Environment 人工审核/分支策略限制。未读取 Secret 值，也未执行生产签名；现有 `prod-2026-08` 公钥映射保持不变，最终匹配性仍由正式 workflow 和下载后签名核验确认。
- 正式发布：未来应将审核候选整合到 `main`，在准确目标提交创建唯一 annotated tag，并从 `main` 以 `channel=stable` 和现有生产 key id 运行签名 workflow；随后独立核验 DLL、ZIP、canonical manifest、detached signature、tag 祖先关系和 latest 指向。
- GitHub 发布不自动部署 NAS；本候选准备不替换当前已验收 UI 构建。

## 交付与停点（历史准备阶段）

候选提交冻结后运行本地完整门禁，将结果、commit、DLL/ZIP 哈希和未签名 manifest 身份写入被忽略的 `dist/release-readiness.json`。构建后的 `.release.manifest.unsigned` 草稿使用与现有签名脚本相同的字段顺序、UTF-8 无 BOM、LF 分隔且无末尾换行；不生成伪造 `.sig`。

用户复核的发布说明位于 `docs/releases/v1.6.1.0.md`。本记录只描述准备阶段不创建远端 PR/tag/Release 的停点；正式执行时重查远端 refs/tag，成功发布后核验四资产和 Latest 指向。Knowledge Review 为无新增产品/架构约束，现有签名、来源与证据分层约束继续适用。

正式发布成功后需同步 README、版本文档与排期中的公开 stable/候选状态，保留既有 tag 和构建来源；版本提升后的 NAS 部署作为单独操作核验。
