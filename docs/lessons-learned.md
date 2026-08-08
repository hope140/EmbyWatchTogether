# 维护经验

本文件只收录已验证、可复用且不属于单次任务日志的经验。写入前搜索去重；发现失效时直接更新当前事实并注明依据。

## 1. 不要把自然播放位移当成手动 Seek

- 现象：轮询快照中的位置持续变化，若直接比较两次位置会产生误报。
- 原因：播放速率和轮询间隔会造成预期位移。
- 结论：`SyncEngine` 先按上一快照、经过时间和播放速率估算 expected，再以阈值判断明显跳变。
- 规则：同步逻辑不得通过周期性 Seek 消除小幅速度差；修改判定时必须覆盖暂停、速率变化和远程 Seek 回传。
- 验证：`SyncEngineTests` 覆盖自然速率差不误判、手动前进/后退和远程 Seek 去重。

## 2. 命令确认必须绑定会话身份

- 现象：设备重连后仍播放同一 Item，旧 Pending 命令可能被新会话快照“确认”。
- 原因：Item 和位置相同不足以证明是同一播放器会话。
- 结论：Pending、Suppressed 和 Barrier 状态记录 session identity；身份或 Item 变化时丢弃旧命令并回到等待。
- 规则：新增命令生命周期逻辑不得跨 session identity 重试或确认。
- 验证：`SessionSelectorTests`、`SyncEngineTests` 覆盖会话切换、旧命令丢弃和重新 Barrier。

## 3. 签名更新失败必须 fail closed

- 现象：版本号或 MD5 正确不代表发布资产可信。
- 原因：未签名或未知 key 的资产可能被替换。
- 结论：更新前必须验证 canonical manifest、大小、SHA-256、程序集身份、tag 和 RSA detached signature；未知 key 或任一失败都拒绝更新。
- 规则：不得把 installer 的 MD5 二次校验当作信任根，也不得把私钥或 secret 写入仓库文档。
- 验证：`ReleaseTrustStoreTests`、`release-signing.tests.ps1` 与 `release-workflow.tests.ps1`。
