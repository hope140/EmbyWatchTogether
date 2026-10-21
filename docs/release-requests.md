# 经审核的云端发布请求

发布入口由 `.github/workflows/release-request.yml` 控制，复用原有
`release.yml` 的构建、签名、验签与发布步骤。无需本地电脑、PAT 或新增长期
secret。原 `workflow_dispatch` 的 tag、channel、key_id 用法保留。

## 两个独立 PR

先审核并合并发布基础设施，确认 main CI 成功。每次发布另建同仓库分支
`release-request/<tag>`，向 main 提交专用 PR；该 PR 只能新增一个文件：
`.github/release-requests/<tag>.json`。普通代码 PR、普通 main push、修改旧请求
和关闭未合并的 PR 都不能授权发布。合并是执行该请求的审核边界；不额外假定
仓库已配置独立 reviewer 或 Environment 审批规则。

请求采用两空格缩进 JSON，末尾一个 LF，字段必须完整且不能增加字段，例如：

```json
{
  "schema": 1,
  "tag": "v1.7.1.0",
  "channel": "stable",
  "keyId": "prod-2026-08",
  "sourceSha": "完整的40位小写源码提交SHA",
  "ciRunId": 39575988859,
  "title": "Watch Together v1.7.1.0 正式版",
  "notesPath": "docs/releases/v1.7.1.0.md",
  "notesSha256": "该源码提交中说明文件原始字节的64位小写SHA256"
}
```

示例中的 SHA 占位符必须替换。标题完全取自请求；正文完全取自固定源码 SHA
中的 notesPath，并由 notesSha256 锁定。Actions 不生成或改写正文。
channel 决定 stable 或 prerelease；不接受额外任意命令、脚本或路径。

sourceSha 必须是对应通道分支（stable 为 main，beta 为 beta）的祖先，三处
项目版本必须与四段数字 tag 一致。ciRunId 必须是同仓库该准确源码 SHA、对应
分支 push 的 `.github/workflows/ci.yml` 成功运行；请求 PR 自己的准确 head
也必须通过该 CI。源码必须包含中文发布说明和已审核签名公钥映射。

## 信任与失败边界

`pull_request_target: closed` 只在 main 的专用请求 PR 合并后进入校验。
控制器 checkout `github.workflow_sha`，读取合并提交中的请求；不会 checkout
或执行 PR head。源码单独 checkout 到 `source/`。只读校验 job 没有签名 secret；
写 job 进入原 `release` Environment 后重新核实全部请求与输入，再创建 tag。
私钥仅传入原签名步骤；签名脚本取自受信任控制器 checkout。

所有手动及请求发布共用 `watch-together-signed-release` job 串行锁。tag 只允许
首次创建；已存在时必须指向相同源码 SHA，绝不移动。已有 Release（包括 draft）
直接拒绝，不覆盖资产。创建 tag 后失败，可以在查明远端状态、确认没有 Release
后重跑同一请求 run；若存在部分 Release，先停止调查，不自动删除或覆盖。
请求路径固定在历史中，旧请求修改不能再次触发。重跑同一已成功请求会拒绝。

tag 创建和发布在同一可信 run 内完成，不依赖 `GITHUB_TOKEN` 的 tag push
触发后续 workflow。Actions token 仅在 job 生命周期使用；校验 job 只读，发布
job 为 Contents write、Actions read、Pull requests read，无账户级扩权。
Environment 若要求审批就等待；本方案不修改仓库权限、保护规则或审批设置。

撤销通道可通过正常审核 PR 移除或禁用请求 workflow，或撤销官方 connector 的
仓库访问。基础设施回滚采用 revert 其 PR；已发布 tag 和资产不随代码回滚删除。

## 验证

`node --test tests/release-request.tests.cjs` 覆盖事件、PR 范围、schema、SHA、
CI、路径、说明 hash、旧 tag、重复请求及 workflow 连接约束。原 PowerShell
发布静态检查、签名测试和产品 CI 继续执行。实际发布仍须检查 tag、Release
标题/正文/channel、DLL 版本、manifest hash 和可信公钥签名，不能把 mock 测试
当作已经发布或真实客户端验收。
