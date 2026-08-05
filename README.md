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

脚本会执行构建、全部单元测试，并把单个插件 DLL 打包到 `dist/EmbyWatchTogether/`
目录及 `dist/EmbyWatchTogether.zip`。插件只依赖 Emby 服务端自带的程序集
（MediaBrowser.* / Emby.*）与 `IJsonSerializer`，无需附带额外运行库。

### 安装

1. 停止 Emby Server。
2. 把 `dist/EmbyWatchTogether.zip` 里的单个 `Emby.Plugins.WatchTogether.dll`
   复制到 Emby 的 `plugins/` 目录**根层**（与服务端既有插件平铺一致；
   Windows 通常为 `%ProgramData%\Emby-Server\programdata\plugins\`，
   Docker 常见为 `<config 卷>/plugins/`）。已验证子目录放置不会被本服务端扫描。
3. 重启 Emby Server。日志出现
   `Starting entry point Emby.Plugins.WatchTogether.WatchTogetherEntryPoint`
   即加载成功。
4. 管理员登录后在主菜单点击「Watch Together」进入房间管理页：创建房间、选择
   两名参与者与主用户；两人用支持控制台遥控的客户端打开同一视频后自动开始同步。

### 验证状态

- 构建与 93 个单元测试通过；已在目标服务器（`117.50.223.21:2334`，
  Emby 4.9.0.60）部署并验证：插件入口点正常启动、`/WatchTogether/*` 路由
  注册（匿名返回 401）、插件页 `/web/configurationpage?name=WatchTogether`
  正常返回。
- 待完成：管理员登录后的 API 验收（建房/控制/消息）与双客户端实机同步测试，
  见 `docs/watch-together-emby-plugin-plan.md` 验收标准。

## 禁止事项

- 不得把 toLocal 相关文件或依赖重新引入本分支。
- `reference/` 为只读蓝本，不得修改；需要新版本时从 `watch_together` 分支重新复制。
