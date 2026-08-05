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

待 S7 完成前仅占位。插件 DLL 将输出到 Emby 的 `plugins/` 目录。

## 禁止事项

- 不得把 toLocal 相关文件或依赖重新引入本分支。
- `reference/` 为只读蓝本，不得修改；需要新版本时从 `watch_together` 分支重新复制。
