# Emby Watch Together 服务端插件

本分支（`codex/emby-plugin-watchtogether`）是 Emby 服务端插件的专用开发分支，已与
embyToLocalPlayer（toLocal）完全解耦：分支历史中不含任何 toLocal 代码，运行时不依赖
etlp、mpv 代理、嵌入式 Python 或油猴脚本。

## 目标

在同一个 Emby 服务器下实现双人同步观看：管理员建房间、拉入用户、两人打开同一视频
自动判定开始、同步暂停/播放/进度。所有参与方使用支持 Emby 控制台远程控制的客户端
（如小秘版 Windows 客户端），零安装。

规划文档见 [`docs/watch-together-emby-plugin-plan.md`](docs/watch-together-emby-plugin-plan.md)；
协作流程见 [`docs/pr-stack-workflow.md`](docs/pr-stack-workflow.md)。

## 目录结构

- `src/`：C# Emby 插件工程（逐步实现中）
- `reference/python-watch-together/`：Python 参考实现（只读移植蓝本，不可运行）
- `docs/`：规划与协作流程文档

## 构建与安装

### 环境

- Windows + .NET SDK 10（或 8+；netstandard2.0 目标）
- NuGet 自动还原 `MediaBrowser.Server.Core 4.9.0.52-beta`（对齐目标服务端
  Emby 4.9.0.60，见规划文档中的版本偏差说明）

### 构建

```powershell
.\scripts\build.ps1
```

脚本会执行构建、全部单元测试，并把插件 DLL 与运行依赖打包到 `dist/EmbyWatchTogether/`
目录及 `dist/EmbyWatchTogether.zip`。

### 安装

1. 停止 Emby Server。
2. 把 `dist/EmbyWatchTogether/` 整个文件夹复制到 Emby 的 `plugins/` 目录
   （Windows 通常为 `%ProgramData%\Emby-Server\programdata\plugins\`）。
3. 重启 Emby Server，在「插件」页确认 Watch Together 已加载。
4. 管理员登录后在主菜单点击「Watch Together」进入房间管理页：创建房间、选择
   两名参与者与主用户；两人用支持控制台遥控的客户端打开同一视频后自动开始同步。

### 验证状态

单元测试与构建在 CI/本机可完整执行；真实 Emby 服务端的实机验收（双客户端同步、
DisplayMessage/Seek 行为、UI 入口呈现）需在装有 Emby 4.9 的服务器上按
`docs/watch-together-emby-plugin-plan.md` 的验收标准执行，本机未安装 Emby 服务端。

## 禁止事项

- 不得把 toLocal 相关文件或依赖重新引入本分支。
- `reference/` 为只读蓝本，不得修改；需要新版本时从 `watch_together` 分支重新复制。
