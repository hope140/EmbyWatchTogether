# Watch Together 使用指南 / User guide

## 中文

[返回安装与首播](../README.md#安装) · [English](#english)

### 房间与邀请

- 在「我的房间」查看自己参与的房间、双方状态和播放进度。可以加入、暂离、重新同步或导出脱敏诊断。
- 管理员可以用「创建与邀请」指定两个账号并选择主用户。普通用户可以通过「邀请」创建或接受邀请码。
- 邀请卡显示邀请码和到期时间，可以手动复制，也可以在有效邀请列表撤销。邀请码有效 15 分钟，只能接受一次。服务器重启后，尚未接受的邀请码会失效。
- 暂离会退出同步并保留成员关系，之后可重新加入。结束房间会解除双方的成员关系，媒体仍保留。
- 主用户换片时，插件会尝试让另一方打开同一视频并重新对齐。另一方自行换片时，主用户留在当前视频。短暂断线或明显进度差有有限的自动恢复，必要时可手动重新同步。

### 管理与更新

管理员可从插件配置页进入「后台管理」，查看全部房间、双方播放状态和主用户进度，并在确认后结束同步。「设置」可调整一方停止时暂停另一方、停止提示、同步操作提示和更新通道。三项播放和提示选项默认开启，保存后在下一轮同步生效。

在 **Dashboard → 计划任务 → Watch Together → Update Plugin** 中可以排期、停用或手动执行更新。首次安装和日常使用请选择正式版，更新完成后按提示重启 Emby Server。通道选择规则和包校验见[技术文档](technical.md#正式版更新实现约束)。

### 界面与通知语言

插件优先使用 Emby 界面语言。宿主未提供语言时，依次尝试页面区域设置、页面语言和浏览器语言。识别为简体或繁体中文时使用对应中文，其余使用英文。更改 Emby 语言后，重新打开或刷新插件页。

播放通知使用该账号上次成功打开插件页时保存的语言，多设备以服务器最后保存的值为准，首次保存前使用简体中文。管理员自定义通知按原文发送，管理员更新广播使用中文。

未填写的房间名按建房页面的语言生成。通过邀请建房时使用接受页面的语言。旧房间名和自定义名称保留原样。语言保存失败时，页面会提示并继续使用此前的语言。

## English

[Back to install and first watch](../README.en.md#install) · [中文](#中文)

### Rooms and invitations

- **My rooms** shows the room you belong to and both users’ playback status and position. From that page, you can join, temporarily leave sync, request a resync, or export redacted diagnostics.
- Administrators can select two accounts and designate a primary user under **Create and Invite**. Regular users can create or accept a code under **Invitations**.
- The invitation card shows the code and when it expires. You can copy the code or revoke it in the active invitations list. A code expires after 15 minutes and can be accepted once. Restarting the server invalidates any unaccepted codes.
- **Leave room** stops syncing temporarily and keeps your membership so you can rejoin later. **End room** removes both members while keeping the media.
- When the primary user switches videos, the plugin tries to open the same video for the other person and realign playback. If the other person switches videos, the primary user stays on the current video. Brief disconnects and large playback differences have limited automatic recovery. Request a resync if needed.

### Administration and updates

Administrators can open **Administration** from the plugin configuration page to see all rooms, both users’ playback status, and the primary user’s progress. They can confirm an action to end sync. **Settings** covers pausing the other client when playback stops, stop notifications, sync notifications, and the update channel. All three playback and notification options are on by default. Changes apply on the next sync cycle.

Use **Dashboard → Scheduled Tasks → Watch Together → Update Plugin** to schedule, disable, or run updates. Choose stable for a first install and everyday use, and restart Emby Server when prompted after an update. See the [technical documentation](technical.md#正式版更新实现约束) for channel selection and package verification.

### Interface and notification language

The plugin uses Emby’s interface language when available, then checks the page locale, page language, and browser language in that order. It shows Simplified or Traditional Chinese for the corresponding locales and English for other locales. Reopen or refresh the plugin page after changing Emby’s language.

Playback notifications use the language last saved successfully when the account opened the plugin page. The server uses the most recent save across devices. The initial default is Simplified Chinese. Custom administrator messages retain their original wording, and administrator update announcements are in Chinese.

If the room name is left blank, the plugin generates one in the language of the page used to create the room. For invitations, it uses the language of the page that accepts the code. Existing and custom names stay as they are. If saving a language fails, the page shows a message and keeps the previous language.
