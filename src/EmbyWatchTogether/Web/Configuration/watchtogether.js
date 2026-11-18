define(['baseView', 'dom', 'loading', 'globalize', 'emby-input', 'emby-select', 'emby-button', 'mainTabsManager'], function (BaseView, dom, loading, globalize, embyInput, embySelect, embyButton, mainTabsManager) {
    'use strict';

    // Chinese source strings are the canonical keys. Add translations to the
    // locale table; missing entries deliberately fall back to the source text.
    var translations = { en: {
        "房间已结束或已由其他管理员结束": "The room has ended or was ended by another administrator",
        "参与者：": "Participants: ",
        "当前没有房间。": "There are no rooms.",
        "后台管理": "Administration",
        "等待参与者": "Waiting for participants",
        "正在对齐": "Aligning",
        "同步中": "Synchronizing",
        "正在同步视频": "Syncing video",
        "正在等待临时掉线的参与者恢复": "Waiting for temporarily disconnected participants to resume",
        "暂不可用": "Not available yet",
        "等待两位参与者打开同一视频": "Wait for two participants to open the same video",
        "正在对齐两位参与者的播放位置": "Aligning the playback positions of two participants",
        "两位参与者已连接，播放会自动同步": "Both participants are connected; playback will sync automatically",
        "正在让另一位参与者打开主用户当前视频": "Asking the other participant to open the primary user’s current video",
        "当前房间暂时无法使用，请稍后重试": "The room is temporarily unavailable. Please try again later.",
        "暂停播放": "Pause playback",
        "继续播放": "Resume playback",
        "重新同步": "Resync",
        "账号或服务器已变化，请重新打开一起看。": "The account or server has changed. Please reopen Watch Together.",
        "无法打开确认窗口，请重新打开一起看后重试。": "Unable to open the confirmation dialog. Reopen Watch Together and try again.",
        "房间或权限已变化，请重新确认操作。": "The room or permissions have changed, please reconfirm the operation.",
        "权限不足，请使用管理员账号重试。": "Insufficient permissions, please try again with an administrator account.",
        "服务器暂时不可用，请稍后重试。": "The server is temporarily unavailable, please try again later.",
        "网络连接失败，请稍后重试。": "Network connection failed, please try again later.",
        "操作未完成，请稍后重试。": "The operation is not completed, please try again later.",
        "临时掉线恢复中": "Temporarily offline and recovering",
        "服务器暂时不可用": "Server is temporarily unavailable",
        "播放会话暂时不可读": "The playback session is temporarily unreadable",
        "播放会话数据已过期": "Play session data has expired",
        "两位参与者打开了不同视频": "Two participants opened different videos",
        "播放已停止，等待重新打开视频": "Playback has stopped, waiting to reopen the video",
        "停止后进度差较大，等待确认重新同步": "The progress difference is large after stopping, waiting for confirmation to resynchronize.",
        "检测到手动操作冲突": "Manual operation conflict detected",
        "对齐重试次数已用尽": "Alignment retries exhausted",
        "等待暂停重试次数已用尽": "Waiting for pause retries exhausted",
        "正在让参与者打开主用户当前视频": "Asking participants to open the primary user's current video",
        "媒体切换同步未完成": "Media switching sync not completed",
        "参与者重新同步未完成": "Participant resync not completed",
        "打开视频确认超时": "Open video confirmation timeout",
        "远程控制超时": "Remote control timeout",
        "目标会话已离线": "Target session is offline",
        "媒体切换参数无效": "Media switching parameter is invalid",
        "播放控制未完成": "Playback control not completed",
        "正在对齐播放位置": "Aligning playback position",
        "两位参与者已连接": "Two participants are connected",
        "当前客户端不支持远程控制": "The current client does not support remote control",
        "播放速度不是 1 倍": "Playback speed is not 1x",
        "等待双方打开同一视频并开始播放": "Wait for both participants to open the same video and start playing it",
        "新鲜": "Fresh",
        "过期": "Expired",
        "不可用": "Not available",
        "未知": "Unknown",
        "成功": "Success",
        "失败": "Failed",
        "已安排重试": "Retry scheduled",
        "等待确认": "Waiting for confirmation",
        "已停止": "Stopped",
        "已进入": "Already entered",
        "已恢复": "Restored",
        "已变化": "changed",
        "已清除": "cleared",
        "活动时间较旧": "The event is older",
        "已过期": "Expired",
        "主用户回到原视频": "Primary user returns to original video",
        "已记录": "Recorded",
        "暂停阶段": "pause phase",
        "定位阶段": "Positioning stage",
        "恢复播放阶段": "Resume playback stage",
        "暂停": "Pause",
        "继续": "Resume",
        "播放/暂停": "Play/Pause",
        "定位": "Seek",
        "停止": "Stop",
        "打开视频": "Open video",
        "提示": "Notification",
        "开始对齐": "Start aligning",
        "对齐阶段变化": "Alignment stage changes",
        "进入同步中": "Entering synchronization",
        "发送控制": "send control",
        "控制已确认": "Control confirmed",
        "控制失败": "control failure",
        "安排重试": "Schedule a retry",
        "确认停止": "confirm stop",
        "停止后等待确认恢复": "Wait for confirmation to resume after stopping",
        "观察主用户停止后的换片": "Observe the media change after the primary user stops",
        "换片等待远控条件恢复": "Media change waiting for remote control to recover",
        "换片观察窗口已过期": "Media change observation window has expired",
        "换片观察已失效": "Media change observation has expired",
        "进入保护状态": "Enter protection state",
        "保护状态恢复": "Protection status restoration",
        "忽略过期快照": "Ignore expired snapshots",
        "同步条件变化": "Synchronization condition changes",
        "手动操作": "manual operation",
        "重新同步开始对齐": "Resync start alignment",
        "开始媒体切换": "Start media switching",
        "请求打开视频": "Request to open video",
        "目标视频已确认": "Target video confirmed",
        "请求主用户暂停等待": "Request the primary user to pause and wait",
        "主用户已暂停等待": "The primary user has been paused and waiting",
        "主用户操作取消换片等待": "Primary user operation cancels media change waiting",
        "媒体切换开始对齐": "Media switch starts aligning",
        "媒体切换完成": "Media switching completed",
        "媒体切换失败": "Media switching failed",
        "媒体切换目标变化": "Media switching target changes",
        "主用户视频变化": "Primary user video changes",
        "开始临时掉线恢复": "Start temporary offline recovery",
        "临时掉线恢复成功": "Temporary offline recovery successful",
        "临时掉线恢复超时": "Temporary offline recovery timeout",
        "恢复会话发生变化": "Recovery session changes",
        "恢复视频发生变化": "Restore video changes",
        "进入漂移阈值": "Enter drift threshold",
        "漂移阈值已清除": "Drift threshold cleared",
        "开始漂移自动纠偏": "Start automatic drift correction",
        "漂移自动纠偏完成": "Automatic drift correction completed",
        "漂移自动纠偏失败": "Automatic drift correction failed",
        "观察事件": "Observe events",
        "远程控制失败": "Remote control failed",
        "时间未知": "Time unknown",
        "导出诊断 JSON": "Export diagnostic JSON",
        "读取时间：": "Retrieved at: ",
        "状态": "Status",
        "状态原因": "Status reason",
        "当前状态需要检查": "Current status needs to be checked",
        "快照健康": "snapshot health",
        "事件数量": "Number of events",
        "参与者会话": "participant session",
        "暂无可用会话。": "No sessions available yet.",
        "在线": "Online",
        "离线": "Offline",
        "，位置 ": ", position ",
        "播放状态": "Playing status",
        "已暂停": "Paused",
        "播放中": "Playing",
        "状态未知": "Status unknown",
        "能力": "ability",
        "上报远控 ": "Reported remote control: ",
        "支持": "support",
        "不支持": "Not supported",
        "；有效远控 ": "; effective remote control: ",
        "确认延迟": "Confirmation delay",
        " 秒": " seconds",
        "播放速率": "Play rate",
        " 倍": "×",
        "可用控制": "Available controls",
        "无": "None",
        "等待中的控制": "Control in waiting",
        "当前没有等待确认的控制。": "There are currently no controls waiting for confirmation.",
        "未知控制": "unknown control",
        "，等待 ": ", waiting ",
        "，重试 ": ", retries ",
        " 次": " times",
        "媒体切换": "Media switching",
        "类型": "type",
        "参与者重新同步": "Participants resync",
        "主用户切换媒体": "Primary user changed media",
        "目标视频标识": "target video identifier",
        "来源视频标识": "Source video identification",
        "参与者": "participants",
        "控制状态": "control state",
        "等待打开视频确认": "Waiting for opening video confirmation",
        "准备发送": "ready to send",
        "重试次数": "Number of retries",
        "操作代际": "Operation generation",
        "持续时间": "duration",
        "最近错误": "recent errors",
        "对齐与恢复": "Align and restore",
        "对齐阶段": "Alignment phase",
        "未进行对齐": "Not aligned",
        "目标位置": "Target location",
        "命令进度": "Command progress",
        "暂停 ": "Pause: ",
        "已发送": "Sent",
        "未发送": "Not sent",
        "定位 ": "Seek: ",
        "恢复 ": "Resume: ",
        "恢复窗口": "recovery window",
        "活动中": "Active",
        "未活动": "Inactive",
        "恢复窗口时长": "Recovery window duration",
        "受影响参与者": "Affected participants",
        "临时掉线恢复": "Temporary offline recovery",
        "恢复中": "Recovering",
        "已等待": "Already waiting",
        "剩余窗口": "remaining window",
        "缺失参与者": "missing participant",
        "等待该会话恢复": "Wait for the session to resume",
        "会话仍在线": "Session is still online",
        "预期 Session 标识": "Expected Session ID",
        "预期视频标识": "Expected video identification",
        "上次位置": "last location",
        "上次播放状态": "Last played status",
        "播放漂移": "Playback drift",
        "当前漂移": "current drift",
        "最大绝对漂移": "maximum absolute drift",
        "纠偏保持计时": "Time above correction threshold",
        "自动纠偏次数": "Number of automatic corrections",
        "最近自动纠偏": "Recent automatic correction",
        "最近纠偏漂移": "Recent correction drift",
        "冷却剩余": "Cooldown remaining",
        "最近结果": "recent results",
        "最近动作": "Recent actions",
        "动作目标": "action target",
        "动作控制": "Action command",
        "动作结果": "action result",
        "事件记录": "event record",
        "暂无事件记录。": "There is no event record yet.",
        "，漂移 ": ", drift ",
        "没有权限查看此房间的诊断。": "No permission to view diagnostics for this room.",
        "诊断读取失败，请稍后重试。": "Diagnostic read failed, please try again later.",
        "诊断详情": "Diagnosis details",
        "正在读取诊断…": "Reading diagnostics…",
        "读取中…": "Reading…",
        "当前浏览器不支持导出诊断 JSON。": "This browser does not support exporting diagnostic JSON.",
        "有效期暂时无法显示": "The validity period cannot be displayed at the moment",
        "有效期至：": "Valid until:",
        "已复制": "Copied",
        "复制邀请码": "Copy invitation code",
        "当前没有有效的邀请码。": "There are currently no valid invitation codes.",
        "我创建的邀请码": "Invitation code I created",
        "接受邀请后生成房间名": "Generate room name after accepting invitation",
        "撤销邀请码": "Revoke invitation code",
        "邀请码列表暂时无法加载，请稍后重试。": "The invitation code list cannot be loaded temporarily, please try again later.",
        "创建中…": "Creating…",
        "正在创建邀请码…": "Creating invitation code…",
        "你已属于一个房间，请先结束当前房间后再创建邀请码。": "You already belong to a room. Please end the current room before creating an invitation code.",
        "当前邀请码数量已达上限，请稍后再试。": "The current number of invitation codes has reached the upper limit, please try again later.",
        "邀请码已创建，请立即复制并发送给对方。": "The invitation code has been created, please copy it immediately and send it to the other participant.",
        "邀请码创建失败，请稍后重试。": "The invitation code creation failed, please try again later.",
        "当前没有可复制的邀请码。": "There are currently no invitation codes to copy.",
        "邀请码已复制。": "The invitation code has been copied.",
        "复制失败，请选中邀请码手动复制。": "Copy failed. Select the invitation code and copy it manually.",
        "撤销中…": "Withdrawing…",
        "邀请码已撤销。": "The invitation code has been revoked.",
        "邀请码不存在或已失效。": "The invitation code no longer exists or is invalid.",
        "邀请码撤销失败：": "Invitation code revocation failed:",
        "房间已创建，请双方打开同一视频。": "The room has been created. Please open the same video for both participants.",
        "邀请码无效或已过期，请向对方索取新邀请码。": "The invitation code is invalid or expired. Please ask the other participant for a new invitation code.",
        "不能接受自己创建的邀请码。": "Invitation codes created by yourself cannot be accepted.",
        "尝试次数过多，请稍后再试。": "Too many attempts, please try again later.",
        "暂时无法创建房间，请稍后重试。": "Unable to create room at the moment, please try again later.",
        "邀请码未能接受，请稍后重试。": "The invitation code could not be accepted, please try again later.",
        "请输入邀请码。": "Please enter the invitation code.",
        "接受中…": "Accepting…",
        "正在接受邀请…": "Accepting invitation…",
        "邀请接受失败：": "Invitation acceptance failed:",
        "保存中…": "Saving…",
        "保存 Token": "Save Token",
        "清除中…": "Clearing…",
        "清除 Token": "Clear Token",
        "正在读取 Token 状态…": "Reading token status…",
        "Token 已配置。": "Token has been configured.",
        "尚未配置 Token。": "Token has not been configured yet.",
        "只有管理员可以查看和修改 Token。": "Only administrators can view and modify Token.",
        "Token 状态读取失败，可尝试清除后重新配置。": "Token status reading failed, try clearing and reconfiguring.",
        "请输入 GitHub Token。": "Please enter your GitHub Token.",
        "正在保存 Token…": "Saving Token…",
        "Token 未配置。": "Token is not configured.",
        "Token 已保存": "Token saved",
        "保存被拒绝：只有管理员可以修改 Token。": "Save refused: Only administrators can modify the Token.",
        "Token 保存失败，请稍后重试。": "Token saving failed, please try again later.",
        "清除 Token？beta 查询将改用匿名 API。": "Clear Token? Beta queries will use the anonymous API instead.",
        "清除": "Clear",
        "正在清除 Token…": "Clearing Token…",
        "Token 仍已配置。": "Token is still configured.",
        "Token 已清除": "Token has been cleared",
        "清除被拒绝：只有管理员可以修改 Token。": "Clear denied: Only administrators can modify the token.",
        "Token 清除失败，请稍后重试。": "Token clearing failed, please try again later.",
        "一起看页面": "Watch Together",
        "创建房间后，两位参与者打开同一视频，播放、暂停和进度会自动同步。": "After creating a room, two participants open the same video and play, pause, and progress are automatically synchronized.",
        "创建与邀请": "Create & invite",
        "选择两位参与者，或通过邀请码邀请对方。": "Select two participants or invite each other via invitation code.",
        "直接创建房间": "Create a room directly",
        "房间名称": "Room name",
        "留空自动生成房间名": "Leave blank to automatically generate room name",
        "参与者 1": "Participant 1",
        "参与者 2": "Participant 2",
        "主用户": "Primary user",
        "主用户的播放位置作为同步基准。": "The primary user's playback position is used as the synchronization reference.",
        "创建房间": "Create room",
        "正在加载用户列表…": "Loading user list...",
        "发出邀请": "Send invitation",
        "创建一次性邀请码，发送给另一位已登录 Emby 的用户。邀请码有效期为 15 分钟。": "Create a one-time invitation code to send to another user who is logged in to Emby. The invitation code is valid for 15 minutes.",
        "创建邀请码": "Create invitation code",
        "本次邀请码：": "This invitation code:",
        "接受邀请": "Accept invitation",
        "邀请码": "Invitation code",
        "输入对方发来的邀请码": "Enter the invitation code sent by the other participant",
        "房间": "Rooms",
        "刷新": "Refresh",
        "通知语言未能保存，播放通知将继续使用之前的语言。": "Could not save the notification language. Playback notifications will continue using the previous language.",
        "观看提示": "Viewing Tips",
        "创建房间并选择两名参与者。": "Create a room and select two participants.",
        "两人分别登录 Emby，打开同一视频。": "Both participants sign in to Emby and open the same video.",
        "看到“同步中”后即可一起观看。": "Once the status is “Synchronizing”, you can watch together.",
        "一方暂时不在线时，房间会等待；恢复后会继续尝试同步。": "When one party is temporarily offline, the room will wait; it will continue to try to synchronize after recovery.",
        "设置": "Settings",
        "播放与更新": "Play and update",
        "停止或退出视频时暂停另一方": "Pause the other participant when stopping or exiting a video",
        "停止或退出视频时发送文字提示": "Send text reminder when stopping or exiting video",
        "同步操作时发送文字提示": "Send text prompts during synchronization operations",
        "显示房间操作与同步结果提示；停止提示单独设置。": "Display room operation and synchronization result prompts; stop prompts can be set individually.",
        "更新通道": "Update channel",
        "正式版 / stable": "Stable",
        "测试版 / beta": "Beta",
        "自动检查并安装所选通道的新版本；测试版通道也接收正式版。": "Automatically checks for and installs new versions of selected channels; beta channels also receive official versions.",
        "保存设置": "Save settings",
        "正在读取配置…": "Reading settings…",
        "可选": "Optional",
        "测试版更新检查遇到 GitHub 请求限制时可填写。": "Optional: use a token if beta update checks hit GitHub rate limits.",
        "输入新的 GitHub Token": "Enter new GitHub Token",
        "粘贴需要保存的 Token": "Paste the Token that needs to be saved",
        "如何获取 Token": "How to get Token",
        "在": "At",
        "创建 fine-grained token，设置有效期，仅授予读取公开仓库的权限。保存后不回显。": "create a fine-grained token with an expiration date and permission only to read public repositories. The token is not shown after saving.",
        "关于插件": "About plug-ins",
        "当前版本：": "Current version:",
        "打开 GitHub 项目": "Open GitHub project",
        "GitHub 项目": "GitHub projects",
        "在 Emby 计划任务的“Watch Together → Update Plugin”中调整检查时间或手动运行。安装后重启 Emby 生效。": "In Emby Scheduled Tasks, adjust the schedule or run Watch Together → Update Plugin manually. Restart Emby after installation.",
        "邀请": "Invite",
        "我的房间": "My rooms",
        "加入房间后，与另一位参与者打开同一视频。": "After joining, both participants should open the same video.",
        "播放、暂停和进度会自动同步。": "Play, pause and progress are automatically synchronized.",
        "需要重新对齐时，点击“重新同步”。": "When realignment is required, click Resync.",
        "只有管理员可以查看和修改此设置。": "Only administrators can view and modify this setting.",
        "配置读取失败：": "Configuration read failed:",
        "配置已保存": "Configuration saved",
        "保存前读取配置失败，尚未提交修改：": "Failed to read the configuration before saving, and the modification has not been submitted:",
        "配置已提交，但暂时无法确认，请重新读取检查。": "The configuration has been submitted, but it cannot be confirmed temporarily. Please read and check again.",
        "配置提交被拒绝，请确认管理员权限后重新读取检查。": "Configuration submission was rejected, please confirm administrator rights and read the check again.",
        "配置提交请求失败，结果尚未确认，请重新读取检查。": "The configuration submission request failed and the result has not been confirmed. Please read the check again.",
        "未知用户": "unknown user",
        "未命名房间": "unnamed room",
        "用户“": "User “",
        "”已在房间“": "” is already in room “",
        "”中，请先结束原房间，或者选择其他用户。暂离仍会保留成员关系。": "”. End that room or select another user. Leaving temporarily retains membership.",
        "请选择参与者。": "Please select participants.",
        "两名参与者必须不同。": "The two participants must be different.",
        "请选择参与者中的一人为主用户。": "Please select one of the participants as the primary user.",
        "用户或房间数据尚不可用，暂时不能创建房间。": "User or room data is not available yet, rooms cannot be created at this time.",
        "当前用户角色信息不完整，请重新打开页面。": "The current user role information is incomplete, please reopen the page.",
        "当前用户权限暂时无法确认，请重新打开页面。": "The current user permissions cannot be confirmed temporarily, please reopen the page.",
        "当前用户权限读取失败：": "Failed to read current user permissions:",
        "只有管理员可以创建房间。": "Only administrators can create rooms.",
        "用户列表暂时无法加载，请稍后重试。": "The user list cannot be loaded temporarily, please try again later.",
        "用户列表加载失败：": "User list loading failed:",
        "房间所属服务器暂不可用，请确认当前服务器连接。": "The server to which the room belongs is temporarily unavailable. Please confirm the current server connection.",
        "暂时无法读取播放会话，自动同步已进入保护状态；恢复后会重新对齐。": "The playback session cannot be read at the moment, and automatic synchronization has entered a protected state; it will be realigned after recovery.",
        "两位参与者打开了不同视频，请打开同一视频。": "Two participants have opened different videos, please open the same video.",
        "播放已停止，请双方重新打开同一视频。": "Playback has stopped. Both participants should reopen the same video.",
        "双方已打开同一视频，但停止后的进度差较大。如需以主用户当前进度恢复，请使用房间内的重新同步操作。": "Both parties have opened the same video, but the progress after stopping is quite different. To resume at the primary user's current progress, use the in-room resync operation.",
        "播放控制未完成，请检查两位参与者的客户端。": "Playback control is not complete, please check both participants' clients.",
        "仍有参与者未加入，请先让双方加入房间。": "There are still participants who have not joined, please let both participants join the room first.",
        "正在对齐播放位置，请稍候。": "Aligning playback position, please wait.",
        "两位参与者已连接，播放会自动同步。": "Both participants are connected; playback will sync automatically.",
        "当前客户端不支持远程控制，请更换或更新客户端。": "The current client does not support remote control, please replace or update the client.",
        "两位参与者的媒体信息不一致，请打开同一视频。": "The media information of the two participants is inconsistent. Please open the same video.",
        "正在让另一位参与者打开主用户当前视频，请稍候。": "Asking another participant to open the primary user's current video, please wait.",
        "换片等待或对齐未完成，已停止自动恢复。请确认双方播放状态后使用重新同步。": "Waiting for the media change or alignment did not complete; automatic recovery has stopped. Check both playback states, then resync.",
        "播放速度不是 1 倍，请恢复正常速度后重试。": "The playback speed is not 1x, please restore the normal speed and try again.",
        "等待双方打开同一视频并开始播放。": "Wait for both participants to open the same video and start playing.",
        "正在等待临时掉线的参与者恢复。": "Waiting for temporarily disconnected participants to resume.",
        "当前房间状态需要检查，请稍后重试。": "The room status needs to be checked. Please try again later.",
        "结束同步": "End synchronization",
        "主用户进度": "Primary progress",
        "主用户已暂停 · 对方播放中": "Primary paused · other playing",
        "主用户播放中 · 对方已暂停": "Primary playing · other paused",
        "双方设备会继续各自播放。": "Playback on each device will continue.",
        "结束房间": "End room",
        "删除房间": "Delete room",
        "暂离房间": "Leave room",
        "加入房间": "Join room",
        "查看诊断": "View diagnostics",
        "解除双方成员关系，不删除媒体": "Terminate the membership relationship between both participants without deleting the media",
        "仅移除同播关系，不删除媒体": "Remove the room relationship without deleting media",
        "可重新加入，并尝试暂停对方": "Can rejoin and try to pause the other participant",
        "请双方打开同一视频": "Both participants should open the same video",
        "读取当前房间的脱敏同步诊断": "Read redacted synchronization diagnostics for this room",
        "请求重新对齐双方进度": "Request to realign the progress of both participants",
        "特别篇": "Special",
        "第 {0} 季": "Season {0}",
        "第 {0} 集": "Episode {0}",
        "第 ": "",
        " 季": " season",
        " 集": " episode",
        "状态暂不可用": "Status is temporarily unavailable",
        "正在读取状态": "Reading status",
        "播放状态已过期，正在等待重新读取。": "The playback status has expired and is waiting to be reread.",
        "状态读取失败，将自动重试。": "Could not load the room status. Retrying automatically.",
        "正在读取当前播放状态…": "Reading current playback status...",
        "视频不一致": "Videos do not match",
        "等待连接恢复": "Wait for connection to be restored",
        "等待播放": "waiting to play",
        "正在同步\n": "Synchronizing\n",
        "正在一起观看\n": "Watching together\n",
        "等待双方打开视频": "Wait for both participants to open the video",
        "媒体信息暂不可用": "Media information is currently unavailable",
        "还没有房间": "No room yet",
        "暂无参与的房间": "No participating rooms yet",
        "到“创建与邀请”创建房间或邀请码，再让两位参与者打开同一视频。": "Go to \"Create and Invite\" to create a room or invitation code, and then let two participants open the same video.",
        "到“邀请”接受邀请码，或请管理员创建房间。": "Go to \"Invitations\" to accept the invitation code, or ask the administrator to create a room.",
        "已加入": "Joined",
        "已暂离": "Left",
        "房间控制": "room control",
        "播放控制": "Playback controls",
        "以主用户视频和进度为准，可能切换视频。": "Based on the primary user's video and progress, the video may be switched.",
        "成员与诊断": "Members and Diagnostics",
        "房间成员": "room members",
        "暂离可重新加入；结束解除双方关系。": "You can rejoin after leaving. Ending the room removes both participants.",
        "房间列表刷新失败，当前列表可能不是最新。将自动重试。": "The room list could not refresh and may be out of date. Retrying automatically.",
        "房间列表加载失败，将自动重试。": "The room list failed to load. Retrying automatically.",
        "暂时暂停双方并重新对齐，继续吗？": "Temporarily pause both sides and realign, continue?",
        "操作": "Action",
        "处理中…": " in progress…",
        "已发起，但部分客户端未完成，请检查客户端。": " started, but some clients did not complete it. Check the clients.",
        "重新同步已开始，可能暂停播放，请稍候。": "Resynchronization has started, playback may be paused, please wait.",
        "已向 ": " sent to ",
        " 个当前会话发送。": " current sessions.",
        "没有可控制的当前会话。": ": no current sessions can be controlled.",
        "已开始，等待完成。": " started; waiting for completion.",
        "失败：": " failed: ",
        "正在请求重新同步…": "Requesting resync...",
        "请求已受理，将尝试暂停双方并对齐。": "Request accepted. Both participants will be paused and aligned if possible.",
        "已请求重新同步，请稍后再试。": "Resync requested, please try again later.",
        "同步中，请等待完成。": "Syncing, please wait for completion.",
        "当前播放会话暂时不可读，请稍后再试。": "The current playback session is temporarily unreadable, please try again later.",
        "当前房间暂时不可用，请稍后再试。": "The current room is temporarily unavailable, please try again later.",
        "重新同步请求未完成，请稍后再试。": "The resync request was not completed, please try again later.",
        "重新同步请求失败：": "Resync request failed:",
        "结束“": "End “",
        "删除“": "Delete “",
        "”？双方将退出房间。": "”? Both participants will leave the room.",
        "结束": "End",
        "删除": "Delete",
        "结束中…": "Ending…",
        "删除中…": "Deleting…",
        "房间不存在或已删除": "The room does not exist or has been deleted",
        "房间已结束": "Room ended",
        "房间已删除": "Room deleted",
        "结束失败：": "End failed:",
        "删除失败：": "Delete failed:",
        "暂离“": "Leave “",
        "”？可重新加入，并尝试暂停对方。": "”? You can rejoin; the other participant will be paused if possible.",
        "暂离": "Leave",
        "加入中…": "Joining…",
        "暂离中…": "Temporarily away...",
        "已加入，请双方打开同一视频。": "You joined the room. Both participants should open the same video.",
        "加入": "Join",
        "已暂离，对方暂停失败，请检查客户端。": "You left the room, but the other participant could not be paused. Check their client.",
        "已暂离，对方已暂停，可重新加入。": "You left the room and the other participant was paused. You can rejoin.",
        "已暂离，自动同步已停止；对方暂停未确认。": "You left the room and automatic synchronization stopped; the other participant’s pause could not be confirmed.",
        "至少需要选择两名参与者。": "At least two participants need to be selected.",
        "请选择两名不同的参与者。": "Please select two different participants.",
        "正在创建房间…": "Creating room…",
        "房间已创建": "Room created",
        "创建失败：": "Creation failed:",
        "—": "—",
        "进行中": "In progress"
}, "zh-Hant": {
        "房间已结束或已由其他管理员结束": "房間已結束或已由其他管理員結束",
        "参与者：": "參與者：",
        "当前没有房间。": "目前沒有房間。",
        "后台管理": "後台管理",
        "等待参与者": "等待參與者",
        "正在对齐": "正在對齊",
        "同步中": "同步中",
        "正在同步视频": "正在同步影片",
        "正在等待临时掉线的参与者恢复": "正在等待臨時掉線的參與者恢復",
        "暂不可用": "暫不可用",
        "等待两位参与者打开同一视频": "等待兩位參與者開啟同一影片",
        "正在对齐两位参与者的播放位置": "正在對齊兩位參與者的播放位置",
        "两位参与者已连接，播放会自动同步": "兩位參與者已連線，播放會自動同步",
        "正在让另一位参与者打开主用户当前视频": "正在讓另一位參與者開啟主使用者當前影片",
        "当前房间暂时无法使用，请稍后重试": "目前的房間暫時無法使用，請稍後重試",
        "暂停播放": "暫停播放",
        "继续播放": "繼續播放",
        "重新同步": "重新同步",
        "账号或服务器已变化，请重新打开一起看。": "帳號或伺服器已變化，請重新開啟一起看。",
        "无法打开确认窗口，请重新打开一起看后重试。": "無法開啟確認視窗，請重新開啟一起看後重試。",
        "房间或权限已变化，请重新确认操作。": "房間或權限已變化，請重新確認操作。",
        "权限不足，请使用管理员账号重试。": "權限不足，請使用管理員帳號重試。",
        "服务器暂时不可用，请稍后重试。": "伺服器暫時不可用，請稍後重試。",
        "网络连接失败，请稍后重试。": "網路連線失敗，請稍後重試。",
        "操作未完成，请稍后重试。": "操作未完成，請稍後重試。",
        "临时掉线恢复中": "臨時掉線恢復中",
        "服务器暂时不可用": "伺服器暫時不可用",
        "播放会话暂时不可读": "播放會話暫時不可讀",
        "播放会话数据已过期": "播放會話資料已過期",
        "两位参与者打开了不同视频": "兩位參與者開啟了不同影片",
        "播放已停止，等待重新打开视频": "播放已停止，等待重新開啟影片",
        "停止后进度差较大，等待确认重新同步": "停止後進度差較大，等待確認重新同步",
        "检测到手动操作冲突": "檢測到手動操作衝突",
        "对齐重试次数已用尽": "對齊重試次數已用盡",
        "等待暂停重试次数已用尽": "等待暫停重試次數已用盡",
        "正在让参与者打开主用户当前视频": "正在讓參與者開啟主使用者當前影片",
        "媒体切换同步未完成": "媒體切換同步未完成",
        "参与者重新同步未完成": "參與者重新同步未完成",
        "打开视频确认超时": "開啟影片確認超時",
        "远程控制超时": "遠端控制超時",
        "目标会话已离线": "目標會話已離線",
        "媒体切换参数无效": "媒體切換參數無效",
        "播放控制未完成": "播放控制未完成",
        "正在对齐播放位置": "正在對齊播放位置",
        "两位参与者已连接": "兩位參與者已連線",
        "当前客户端不支持远程控制": "當前客戶端不支援遠端控制",
        "播放速度不是 1 倍": "播放速度不是 1 倍",
        "等待双方打开同一视频并开始播放": "等待雙方開啟同一影片並開始播放",
        "新鲜": "新鮮",
        "过期": "過期",
        "不可用": "不可用",
        "未知": "未知",
        "成功": "成功",
        "失败": "失敗",
        "已安排重试": "已安排重試",
        "等待确认": "等待確認",
        "已停止": "已停止",
        "已进入": "已進入",
        "已恢复": "已恢復",
        "已变化": "已變化",
        "已清除": "已清除",
        "活动时间较旧": "活動時間較舊",
        "已过期": "已過期",
        "主用户回到原视频": "主使用者回到原影片",
        "已记录": "已記錄",
        "暂停阶段": "暫停階段",
        "定位阶段": "定位階段",
        "恢复播放阶段": "恢復播放階段",
        "暂停": "暫停",
        "继续": "繼續",
        "播放/暂停": "播放/暫停",
        "定位": "定位",
        "停止": "停止",
        "打开视频": "開啟影片",
        "提示": "提示",
        "开始对齐": "開始對齊",
        "对齐阶段变化": "對齊階段變化",
        "进入同步中": "進入同步中",
        "发送控制": "傳送控制",
        "控制已确认": "控制已確認",
        "控制失败": "控制失敗",
        "安排重试": "安排重試",
        "确认停止": "確認停止",
        "停止后等待确认恢复": "停止後等待確認恢復",
        "观察主用户停止后的换片": "觀察主使用者停止後的換片",
        "换片等待远控条件恢复": "換片等待遠控條件恢復",
        "换片观察窗口已过期": "換片觀察視窗已過期",
        "换片观察已失效": "換片觀察已失效",
        "进入保护状态": "進入保護狀態",
        "保护状态恢复": "保護狀態恢復",
        "忽略过期快照": "忽略過期快照",
        "同步条件变化": "同步條件變化",
        "手动操作": "手動操作",
        "重新同步开始对齐": "重新同步開始對齊",
        "开始媒体切换": "開始媒體切換",
        "请求打开视频": "請求開啟影片",
        "目标视频已确认": "目標影片已確認",
        "请求主用户暂停等待": "請求主使用者暫停等待",
        "主用户已暂停等待": "主使用者已暫停等待",
        "主用户操作取消换片等待": "主使用者操作取消換片等待",
        "媒体切换开始对齐": "媒體切換開始對齊",
        "媒体切换完成": "媒體切換完成",
        "媒体切换失败": "媒體切換失敗",
        "媒体切换目标变化": "媒體切換目標變化",
        "主用户视频变化": "主使用者影片變化",
        "开始临时掉线恢复": "開始臨時掉線恢復",
        "临时掉线恢复成功": "臨時掉線恢復成功",
        "临时掉线恢复超时": "臨時掉線恢復超時",
        "恢复会话发生变化": "恢復會話發生變化",
        "恢复视频发生变化": "恢復影片發生變化",
        "进入漂移阈值": "進入漂移閾值",
        "漂移阈值已清除": "漂移閾值已清除",
        "开始漂移自动纠偏": "開始漂移自動糾偏",
        "漂移自动纠偏完成": "漂移自動糾偏完成",
        "漂移自动纠偏失败": "漂移自動糾偏失敗",
        "观察事件": "觀察事件",
        "远程控制失败": "遠端控制失敗",
        "时间未知": "時間未知",
        "导出诊断 JSON": "匯出診斷 JSON",
        "读取时间：": "讀取時間：",
        "状态": "狀態",
        "状态原因": "狀態原因",
        "当前状态需要检查": "當前狀態需要檢查",
        "快照健康": "快照健康",
        "事件数量": "事件數量",
        "参与者会话": "參與者會話",
        "暂无可用会话。": "暫無可用會話。",
        "在线": "線上",
        "离线": "離線",
        "，位置 ": "，位置 ",
        "播放状态": "播放狀態",
        "已暂停": "已暫停",
        "播放中": "播放中",
        "状态未知": "狀態未知",
        "能力": "能力",
        "上报远控 ": "上報遠控 ",
        "支持": "支援",
        "不支持": "不支援",
        "；有效远控 ": "；有效遠控 ",
        "确认延迟": "確認延遲",
        " 秒": " 秒",
        "播放速率": "播放速率",
        " 倍": " 倍",
        "可用控制": "可用控制",
        "无": "無",
        "等待中的控制": "等待中的控制",
        "当前没有等待确认的控制。": "當前沒有等待確認的控制。",
        "未知控制": "未知控制",
        "，等待 ": "，等待 ",
        "，重试 ": "，重試 ",
        " 次": " 次",
        "媒体切换": "媒體切換",
        "类型": "類型",
        "参与者重新同步": "參與者重新同步",
        "主用户切换媒体": "主使用者切換媒體",
        "目标视频标识": "目標影片標識",
        "来源视频标识": "來源影片標識",
        "参与者": "參與者",
        "控制状态": "控制狀態",
        "等待打开视频确认": "等待開啟影片確認",
        "准备发送": "準備傳送",
        "重试次数": "重試次數",
        "操作代际": "操作代際",
        "持续时间": "持續時間",
        "最近错误": "最近錯誤",
        "对齐与恢复": "對齊與恢復",
        "对齐阶段": "對齊階段",
        "未进行对齐": "未進行對齊",
        "目标位置": "目標位置",
        "命令进度": "命令進度",
        "暂停 ": "暫停 ",
        "已发送": "已傳送",
        "未发送": "未傳送",
        "定位 ": "定位 ",
        "恢复 ": "恢復 ",
        "恢复窗口": "恢復視窗",
        "活动中": "活動中",
        "未活动": "未活動",
        "恢复窗口时长": "恢復視窗時長",
        "受影响参与者": "受影響參與者",
        "临时掉线恢复": "臨時掉線恢復",
        "恢复中": "恢復中",
        "已等待": "已等待",
        "剩余窗口": "剩餘視窗",
        "缺失参与者": "缺失參與者",
        "等待该会话恢复": "等待該會話恢復",
        "会话仍在线": "會話仍線上",
        "预期 Session 标识": "預期 Session 標識",
        "预期视频标识": "預期影片標識",
        "上次位置": "上次位置",
        "上次播放状态": "上次播放狀態",
        "播放漂移": "播放漂移",
        "当前漂移": "當前漂移",
        "最大绝对漂移": "最大絕對漂移",
        "纠偏保持计时": "糾偏保持計時",
        "自动纠偏次数": "自動糾偏次數",
        "最近自动纠偏": "最近自動糾偏",
        "最近纠偏漂移": "最近糾偏漂移",
        "冷却剩余": "冷卻剩餘",
        "最近结果": "最近結果",
        "最近动作": "最近動作",
        "动作目标": "動作目標",
        "动作控制": "動作控制",
        "动作结果": "動作結果",
        "事件记录": "事件記錄",
        "暂无事件记录。": "暫無事件記錄。",
        "，漂移 ": "，漂移 ",
        "没有权限查看此房间的诊断。": "沒有權限檢視此房間的診斷。",
        "诊断读取失败，请稍后重试。": "診斷讀取失敗，請稍後重試。",
        "诊断详情": "診斷詳情",
        "正在读取诊断…": "正在讀取診斷…",
        "读取中…": "讀取中…",
        "当前浏览器不支持导出诊断 JSON。": "當前瀏覽器不支援匯出診斷 JSON。",
        "有效期暂时无法显示": "有效期暫時無法顯示",
        "有效期至：": "有效期至：",
        "已复制": "已複製",
        "复制邀请码": "複製邀請碼",
        "当前没有有效的邀请码。": "當前沒有有效的邀請碼。",
        "我创建的邀请码": "我建立的邀請碼",
        "接受邀请后生成房间名": "接受邀請後生成房間名",
        "撤销邀请码": "撤銷邀請碼",
        "邀请码列表暂时无法加载，请稍后重试。": "邀請碼列表暫時無法載入，請稍後重試。",
        "创建中…": "建立中…",
        "正在创建邀请码…": "正在建立邀請碼…",
        "你已属于一个房间，请先结束当前房间后再创建邀请码。": "你已屬於一個房間，請先結束當前房間後再建立邀請碼。",
        "当前邀请码数量已达上限，请稍后再试。": "當前邀請碼數量已達上限，請稍後再試。",
        "邀请码已创建，请立即复制并发送给对方。": "邀請碼已建立，請立即複製並傳送給對方。",
        "邀请码创建失败，请稍后重试。": "邀請碼建立失敗，請稍後重試。",
        "当前没有可复制的邀请码。": "當前沒有可複製的邀請碼。",
        "邀请码已复制。": "邀請碼已複製。",
        "复制失败，请选中邀请码手动复制。": "複製失敗，請選取邀請碼手動複製。",
        "撤销中…": "撤銷中…",
        "邀请码已撤销。": "邀請碼已撤銷。",
        "邀请码不存在或已失效。": "邀請碼不存在或已失效。",
        "邀请码撤销失败：": "邀請碼撤銷失敗：",
        "房间已创建，请双方打开同一视频。": "房間已建立，請雙方開啟同一影片。",
        "邀请码无效或已过期，请向对方索取新邀请码。": "邀請碼無效或已過期，請向對方索取新邀請碼。",
        "不能接受自己创建的邀请码。": "不能接受自己建立的邀請碼。",
        "尝试次数过多，请稍后再试。": "嘗試次數過多，請稍後再試。",
        "暂时无法创建房间，请稍后重试。": "暫時無法建立房間，請稍後重試。",
        "邀请码未能接受，请稍后重试。": "邀請碼未能接受，請稍後重試。",
        "请输入邀请码。": "請輸入邀請碼。",
        "接受中…": "接受中…",
        "正在接受邀请…": "正在接受邀請…",
        "邀请接受失败：": "邀請接受失敗：",
        "保存中…": "儲存中…",
        "保存 Token": "儲存 Token",
        "清除中…": "清除中…",
        "清除 Token": "清除 Token",
        "正在读取 Token 状态…": "正在讀取 Token 狀態…",
        "Token 已配置。": "Token 已配置。",
        "尚未配置 Token。": "尚未配置 Token。",
        "只有管理员可以查看和修改 Token。": "只有管理員可以檢視和修改 Token。",
        "Token 状态读取失败，可尝试清除后重新配置。": "Token 狀態讀取失敗，可嘗試清除後重新配置。",
        "请输入 GitHub Token。": "請輸入 GitHub Token。",
        "正在保存 Token…": "正在儲存 Token…",
        "Token 未配置。": "Token 未配置。",
        "Token 已保存": "Token 已儲存",
        "保存被拒绝：只有管理员可以修改 Token。": "儲存被拒絕：只有管理員可以修改 Token。",
        "Token 保存失败，请稍后重试。": "Token 儲存失敗，請稍後重試。",
        "清除 Token？beta 查询将改用匿名 API。": "清除 Token？beta 查詢將改用匿名 API。",
        "清除": "清除",
        "正在清除 Token…": "正在清除 Token…",
        "Token 仍已配置。": "Token 仍已配置。",
        "Token 已清除": "Token 已清除",
        "清除被拒绝：只有管理员可以修改 Token。": "清除被拒絕：只有管理員可以修改 Token。",
        "Token 清除失败，请稍后重试。": "Token 清除失敗，請稍後重試。",
        "一起看页面": "一起看頁面",
        "创建房间后，两位参与者打开同一视频，播放、暂停和进度会自动同步。": "建立房間後，兩位參與者開啟同一影片，播放、暫停和進度會自動同步。",
        "创建与邀请": "建立與邀請",
        "选择两位参与者，或通过邀请码邀请对方。": "選擇兩位參與者，或透過邀請碼邀請對方。",
        "直接创建房间": "直接建立房間",
        "房间名称": "房間名稱",
        "留空自动生成房间名": "留空自動生成房間名",
        "参与者 1": "參與者 1",
        "参与者 2": "參與者 2",
        "主用户": "主使用者",
        "主用户的播放位置作为同步基准。": "主使用者的播放位置作為同步基準。",
        "创建房间": "建立房間",
        "正在加载用户列表…": "正在載入使用者列表…",
        "发出邀请": "發出邀請",
        "创建一次性邀请码，发送给另一位已登录 Emby 的用户。邀请码有效期为 15 分钟。": "建立一次性邀請碼，傳送給另一位已登入 Emby 的使用者。邀請碼有效期為 15 分鐘。",
        "创建邀请码": "建立邀請碼",
        "本次邀请码：": "本次邀請碼：",
        "接受邀请": "接受邀請",
        "邀请码": "邀請碼",
        "输入对方发来的邀请码": "輸入對方發來的邀請碼",
        "房间": "房間",
        "刷新": "重新整理",
        "通知语言未能保存，播放通知将继续使用之前的语言。": "通知語言未能儲存，播放通知將繼續使用之前的語言。",
        "观看提示": "觀看提示",
        "创建房间并选择两名参与者。": "建立房間並選擇兩名參與者。",
        "两人分别登录 Emby，打开同一视频。": "兩人分別登入 Emby，開啟同一影片。",
        "看到“同步中”后即可一起观看。": "看到“同步中”後即可一起觀看。",
        "一方暂时不在线时，房间会等待；恢复后会继续尝试同步。": "一方暫時離線時，房間會等待；恢復後會繼續嘗試同步。",
        "设置": "設定",
        "播放与更新": "播放與更新",
        "停止或退出视频时暂停另一方": "停止或退出影片時暫停另一方",
        "停止或退出视频时发送文字提示": "停止或退出影片時傳送文字提示",
        "同步操作时发送文字提示": "同步操作時傳送文字提示",
        "显示房间操作与同步结果提示；停止提示单独设置。": "顯示房間操作與同步結果提示；停止提示單獨設定。",
        "更新通道": "更新通道",
        "正式版 / stable": "正式版 / stable",
        "测试版 / beta": "測試版 / beta",
        "自动检查并安装所选通道的新版本；测试版通道也接收正式版。": "自動檢查並安裝所選通道的新版本；測試版通道也接收正式版。",
        "保存设置": "儲存設定",
        "正在读取配置…": "正在讀取配置…",
        "可选": "可選",
        "测试版更新检查遇到 GitHub 请求限制时可填写。": "測試版更新檢查遇到 GitHub 請求限制時可填寫。",
        "输入新的 GitHub Token": "輸入新的 GitHub Token",
        "粘贴需要保存的 Token": "貼上需要儲存的 Token",
        "如何获取 Token": "如何獲取 Token",
        "在": "在",
        "创建 fine-grained token，设置有效期，仅授予读取公开仓库的权限。保存后不回显。": "建立 fine-grained token，設定有效期，僅授予讀取公開倉庫的權限。儲存後不回顯。",
        "关于插件": "關於外掛",
        "当前版本：": "當前版本：",
        "打开 GitHub 项目": "開啟 GitHub 專案",
        "GitHub 项目": "GitHub 專案",
        "在 Emby 计划任务的“Watch Together → Update Plugin”中调整检查时间或手动运行。安装后重启 Emby 生效。": "在 Emby 計劃任務的“Watch Together → Update Plugin”中調整檢查時間或手動執行。安裝後重啟 Emby 生效。",
        "邀请": "邀請",
        "我的房间": "我的房間",
        "加入房间后，与另一位参与者打开同一视频。": "加入房間後，與另一位參與者開啟同一影片。",
        "播放、暂停和进度会自动同步。": "播放、暫停和進度會自動同步。",
        "需要重新对齐时，点击“重新同步”。": "需要重新對齊時，點擊“重新同步”。",
        "只有管理员可以查看和修改此设置。": "只有管理員可以檢視和修改此設定。",
        "配置读取失败：": "配置讀取失敗：",
        "配置已保存": "配置已儲存",
        "保存前读取配置失败，尚未提交修改：": "儲存前讀取配置失敗，尚未提交修改：",
        "配置已提交，但暂时无法确认，请重新读取检查。": "配置已提交，但暫時無法確認，請重新讀取檢查。",
        "配置提交被拒绝，请确认管理员权限后重新读取检查。": "配置提交被拒絕，請確認管理員權限後重新讀取檢查。",
        "配置提交请求失败，结果尚未确认，请重新读取检查。": "配置提交請求失敗，結果尚未確認，請重新讀取檢查。",
        "未知用户": "未知使用者",
        "未命名房间": "未命名房間",
        "用户“": "使用者“",
        "”已在房间“": "”已在房間“",
        "”中，请先结束原房间，或者选择其他用户。暂离仍会保留成员关系。": "”中，請先結束原房間，或者選擇其他使用者。暫離仍會保留成員關係。",
        "请选择参与者。": "請選擇參與者。",
        "两名参与者必须不同。": "兩名參與者必須不同。",
        "请选择参与者中的一人为主用户。": "請選擇參與者中的一人為主使用者。",
        "用户或房间数据尚不可用，暂时不能创建房间。": "使用者或房間資料尚不可用，暫時不能建立房間。",
        "当前用户角色信息不完整，请重新打开页面。": "當前使用者角色資訊不完整，請重新開啟頁面。",
        "当前用户权限暂时无法确认，请重新打开页面。": "當前使用者權限暫時無法確認，請重新開啟頁面。",
        "当前用户权限读取失败：": "當前使用者權限讀取失敗：",
        "只有管理员可以创建房间。": "只有管理員可以建立房間。",
        "用户列表暂时无法加载，请稍后重试。": "使用者列表暫時無法載入，請稍後重試。",
        "用户列表加载失败：": "使用者列表載入失敗：",
        "房间所属服务器暂不可用，请确认当前服务器连接。": "房間所屬伺服器暫不可用，請確認當前伺服器連線。",
        "暂时无法读取播放会话，自动同步已进入保护状态；恢复后会重新对齐。": "暫時無法讀取播放會話，自動同步已進入保護狀態；恢復後會重新對齊。",
        "两位参与者打开了不同视频，请打开同一视频。": "兩位參與者開啟了不同影片，請開啟同一影片。",
        "播放已停止，请双方重新打开同一视频。": "播放已停止，請雙方重新開啟同一影片。",
        "双方已打开同一视频，但停止后的进度差较大。如需以主用户当前进度恢复，请使用房间内的重新同步操作。": "雙方已開啟同一影片，但停止後的進度差較大。如需以主使用者當前進度恢復，請使用房間內的重新同步操作。",
        "播放控制未完成，请检查两位参与者的客户端。": "播放控制未完成，請檢查兩位參與者的客戶端。",
        "仍有参与者未加入，请先让双方加入房间。": "仍有參與者未加入，請先讓雙方加入房間。",
        "正在对齐播放位置，请稍候。": "正在對齊播放位置，請稍候。",
        "两位参与者已连接，播放会自动同步。": "兩位參與者已連線，播放會自動同步。",
        "当前客户端不支持远程控制，请更换或更新客户端。": "當前客戶端不支援遠端控制，請更換或更新客戶端。",
        "两位参与者的媒体信息不一致，请打开同一视频。": "兩位參與者的媒體資訊不一致，請開啟同一影片。",
        "正在让另一位参与者打开主用户当前视频，请稍候。": "正在讓另一位參與者開啟主使用者當前影片，請稍候。",
        "换片等待或对齐未完成，已停止自动恢复。请确认双方播放状态后使用重新同步。": "換片等待或對齊未完成，已停止自動恢復。請確認雙方播放狀態後使用重新同步。",
        "播放速度不是 1 倍，请恢复正常速度后重试。": "播放速度不是 1 倍，請恢復正常速度後重試。",
        "等待双方打开同一视频并开始播放。": "等待雙方開啟同一影片並開始播放。",
        "正在等待临时掉线的参与者恢复。": "正在等待臨時掉線的參與者恢復。",
        "当前房间状态需要检查，请稍后重试。": "目前的房間狀態需要檢查，請稍後重試。",
        "结束同步": "結束同步",
        "主用户进度": "主使用者進度",
        "主用户已暂停 · 对方播放中": "主使用者已暫停 · 對方播放中",
        "主用户播放中 · 对方已暂停": "主使用者播放中 · 對方已暫停",
        "双方设备会继续各自播放。": "雙方裝置會繼續各自播放。",
        "结束房间": "結束房間",
        "删除房间": "刪除房間",
        "暂离房间": "暫離房間",
        "加入房间": "加入房間",
        "查看诊断": "檢視診斷",
        "解除双方成员关系，不删除媒体": "解除雙方成員關係，不刪除媒體",
        "仅移除同播关系，不删除媒体": "僅移除同播關係，不刪除媒體",
        "可重新加入，并尝试暂停对方": "可重新加入，並嘗試暫停對方",
        "请双方打开同一视频": "請雙方開啟同一影片",
        "读取当前房间的脱敏同步诊断": "讀取當前房間的脫敏同步診斷",
        "请求重新对齐双方进度": "請求重新對齊雙方進度",
        "特别篇": "特別篇",
        "第 {0} 季": "第 {0} 季",
        "第 {0} 集": "第 {0} 集",
        "第 ": "第 ",
        " 季": " 季",
        " 集": " 集",
        "状态暂不可用": "狀態暫不可用",
        "正在读取状态": "正在讀取狀態",
        "播放状态已过期，正在等待重新读取。": "播放狀態已過期，正在等待重新讀取。",
        "状态读取失败，将自动重试。": "狀態讀取失敗，將自動重試。",
        "正在读取当前播放状态…": "正在讀取當前播放狀態…",
        "视频不一致": "影片不一致",
        "等待连接恢复": "等待連線恢復",
        "等待播放": "等待播放",
        "正在同步\n": "正在同步\n",
        "正在一起观看\n": "正在一起觀看\n",
        "等待双方打开视频": "等待雙方開啟影片",
        "媒体信息暂不可用": "媒體資訊暫不可用",
        "还没有房间": "還沒有房間",
        "暂无参与的房间": "暫無參與的房間",
        "到“创建与邀请”创建房间或邀请码，再让两位参与者打开同一视频。": "到“建立與邀請”建立房間或邀請碼，再讓兩位參與者開啟同一影片。",
        "到“邀请”接受邀请码，或请管理员创建房间。": "到“邀請”接受邀請碼，或請管理員建立房間。",
        "已加入": "已加入",
        "已暂离": "已暫離",
        "房间控制": "房間控制",
        "播放控制": "播放控制",
        "以主用户视频和进度为准，可能切换视频。": "以主使用者影片和進度為準，可能切換影片。",
        "成员与诊断": "成員與診斷",
        "房间成员": "房間成員",
        "暂离可重新加入；结束解除双方关系。": "暫離可重新加入；結束解除雙方關係。",
        "房间列表刷新失败，当前列表可能不是最新。将自动重试。": "房間列表重新整理失敗，目前的列表可能不是最新。將自動重試。",
        "房间列表加载失败，将自动重试。": "房間列表載入失敗，將自動重試。",
        "暂时暂停双方并重新对齐，继续吗？": "暫時暫停雙方並重新對齊，繼續嗎？",
        "操作": "操作",
        "处理中…": "處理中…",
        "已发起，但部分客户端未完成，请检查客户端。": "已發起，但部分客戶端未完成，請檢查客戶端。",
        "重新同步已开始，可能暂停播放，请稍候。": "重新同步已開始，可能暫停播放，請稍候。",
        "已向 ": "已向 ",
        " 个当前会话发送。": " 個當前會話傳送。",
        "没有可控制的当前会话。": "沒有可控制的當前會話。",
        "已开始，等待完成。": "已開始，等待完成。",
        "失败：": "失敗：",
        "正在请求重新同步…": "正在請求重新同步…",
        "请求已受理，将尝试暂停双方并对齐。": "請求已受理，將嘗試暫停雙方並對齊。",
        "已请求重新同步，请稍后再试。": "已請求重新同步，請稍後再試。",
        "同步中，请等待完成。": "同步中，請等待完成。",
        "当前播放会话暂时不可读，请稍后再试。": "當前播放會話暫時不可讀，請稍後再試。",
        "当前房间暂时不可用，请稍后再试。": "當前房間暫時不可用，請稍後再試。",
        "重新同步请求未完成，请稍后再试。": "重新同步請求未完成，請稍後再試。",
        "重新同步请求失败：": "重新同步請求失敗：",
        "结束“": "結束“",
        "删除“": "刪除“",
        "”？双方将退出房间。": "”？雙方將退出房間。",
        "结束": "結束",
        "删除": "刪除",
        "结束中…": "結束中…",
        "删除中…": "刪除中…",
        "房间不存在或已删除": "房間不存在或已刪除",
        "房间已结束": "房間已結束",
        "房间已删除": "房間已刪除",
        "结束失败：": "結束失敗：",
        "删除失败：": "刪除失敗：",
        "暂离“": "暫離“",
        "”？可重新加入，并尝试暂停对方。": "”？可重新加入，並嘗試暫停對方。",
        "暂离": "暫離",
        "加入中…": "加入中…",
        "暂离中…": "暫離中…",
        "已加入，请双方打开同一视频。": "已加入，請雙方開啟同一影片。",
        "加入": "加入",
        "已暂离，对方暂停失败，请检查客户端。": "已暫離，對方暫停失敗，請檢查客戶端。",
        "已暂离，对方已暂停，可重新加入。": "已暫離，對方已暫停，可重新加入。",
        "已暂离，自动同步已停止；对方暂停未确认。": "已暫離，自動同步已停止；對方暫停未確認。",
        "至少需要选择两名参与者。": "至少需要選擇兩名參與者。",
        "请选择两名不同的参与者。": "請選擇兩名不同的參與者。",
        "正在创建房间…": "正在建立房間…",
        "房间已创建": "房間已建立",
        "创建失败：": "建立失敗：",
        "—": "—",
        "进行中": "進行中"
} };
    function currentLocale() {
        var locale;
        try {
            var hostGlobalize = globalize && (typeof globalize.getCurrentLocale === 'function' ? globalize : globalize.default);
            if (hostGlobalize && typeof hostGlobalize.getCurrentLocale === 'function') locale = hostGlobalize.getCurrentLocale();
        } catch (error) { /* Older hosts may not expose a locale. */ }
        if (typeof locale !== 'string' || !locale.trim()) locale = document.documentElement.getAttribute('data-culture') || (document.body && document.body.getAttribute('data-culture'));
        if (typeof locale !== 'string' || !locale.trim()) locale = document.documentElement.lang;
        if (typeof locale !== 'string' || !locale.trim()) locale = navigator.language || '';
        var tag = locale.trim().replace(/_/g, '-').toLowerCase();
        if (!/^zh(?:-|$)/.test(tag)) return 'en';
        if (/(?:^|-)hans(?:-|$)/.test(tag)) return 'zh';
        if (/(?:^|-)hant(?:-|$)/.test(tag) || /(?:^|-)(tw|hk|mo)(?:-|$)/.test(tag)) return 'zh-Hant';
        return 'zh';
    }
    var locale = currentLocale();
    function t(key) { return (translations[locale] && translations[locale][key]) || key; }

    // Translate only original static content. Dynamic content uses t() at the
    // point it is created; user-supplied room and media names are untouched.
    function translateStatic(page) {
        if (!page._wtStaticTranslations) {
            var entries = [];
            var walker = document.createTreeWalker(page, NodeFilter.SHOW_TEXT);
            var node;
            while ((node = walker.nextNode())) {
                if (node.parentElement && node.parentElement.closest('style, script')) continue;
                var original = node.nodeValue;
                var key = original.trim();
                if (key && translations.en[key]) {
                    entries.push({ node: node, parent: node.parentElement, original: original, key: key });
                    if (node.parentElement && node.parentElement.tagName === 'BUTTON') node.parentElement._wtStaticLabelKey = key;
                }
            }
            page.querySelectorAll('*').forEach(function (element) {
                ['label', 'placeholder', 'title', 'aria-label'].forEach(function (name) {
                    var value = element.getAttribute(name);
                    if (value && translations.en[value]) entries.push({ node: element, attribute: name, key: value });
                });
            });
            page._wtStaticTranslations = entries;
        }
        page._wtStaticTranslations.forEach(function (entry) {
            if (entry.attribute) entry.node.setAttribute(entry.attribute, t(entry.key));
            else if (entry.node.parentNode === entry.parent) entry.node.nodeValue = entry.original.replace(entry.key, t(entry.key));
            else if (entry.parent && entry.parent.tagName === 'BUTTON') entry.parent.textContent = t(entry.key);
        });
    }

    var pluginId = '0f8d1c2e-3b4a-4c5d-8e6f-7a8b9c0d1e2f';
    var stateLabels = {
        get Waiting() { return t('等待参与者'); },
        get Barrier() { return t('正在对齐'); },
        get Watching() { return t('同步中'); },
        get Handoff() { return t('正在同步视频'); },
        get Recovering() { return t('正在等待临时掉线的参与者恢复'); },
        get Unavailable() { return t('暂不可用'); }
    };
    var stateDescriptions = {
        get Waiting() { return t('等待两位参与者打开同一视频'); },
        get Barrier() { return t('正在对齐两位参与者的播放位置'); },
        get Watching() { return t('两位参与者已连接，播放会自动同步'); },
        get Handoff() { return t('正在让另一位参与者打开主用户当前视频'); },
        get Recovering() { return t('正在等待临时掉线的参与者恢复'); },
        get Unavailable() { return t('当前房间暂时无法使用，请稍后重试'); }
    };
    var actionLabels = {
        get pause() { return t('暂停播放'); },
        get resume() { return t('继续播放'); },
        get resync() { return t('重新同步'); },
        get participantResync() { return t('重新同步'); }
    };

    var maxDiagnosticEvents = 50;

    function apiUrl(path) {
        return ApiClient.getUrl(path);
    }

    function pageIdentity() {
        try {
            return {
                user: typeof ApiClient.getCurrentUserId === 'function' ? String(ApiClient.getCurrentUserId() || '') : '',
                server: String(ApiClient.serverAddress() || ''),
                serverId: typeof ApiClient.serverId === 'function' ? String(ApiClient.serverId() || '') : '',
                client: ApiClient
            };
        } catch (error) {
            return null;
        }
    }

    function clearPageData(page) {
        clearTimeout(page._wtAdminExpiry);
        if (page._wtCardObserver) page._wtCardObserver.disconnect();
        page._wtCardObserver = null;
        page._wtCardMetadata = new Map();
        page._wtConfirmation = null;
        page._wtCreateTouched = {};
        page._wtCreateSubmitted = false;
        var notices = page.querySelector('#wtSuccessFallback');
        if (notices) clearChildren(notices);
        releaseNativeTabs(page);
        clearInterval(page._wtTimer);
        window.removeEventListener('focus', page._wtRefreshVisible);
        page.ownerDocument.removeEventListener('visibilitychange', page._wtRefreshVisible);
        clearAllRoomFeedback(page);
        clearTimeout(page._wtStatusTimer);
        Object.keys(page._wtStateExpiryTimers || {}).forEach(function (roomId) {
            clearRoomStateExpiry(page, roomId);
        });
        page._wtStateExpiryTimers = {};
        page._wtStatusTimer = null;
        page._wtUsers = [];
        page._wtRooms = null;
        page._wtRoomsLoaded = false;
        page._wtRoomsPollFailures = 0;
        page._wtRoomsRetryAt = 0;
        page._wtLastRooms = null;
        setRoomsStatus(page, '');
        page._wtInvitations = [];
        page._wtInvitationListRequest = null;
        var listStatus = page.querySelector('#wtInvitationListStatus');
        if (listStatus) {
            listStatus.textContent = '';
            listStatus.hidden = true;
        }
        var invitationList = page.querySelector('#wtInvitations');
        if (invitationList) invitationList.hidden = true;
        setInvitationStatus(page, '', false);
        setAcceptInvitationError(page, '');
        page._wtInvitationCode = null;
        page._wtInvitationCopied = false;
        page._wtPendingCreatedFeedback = null;
        setCreateStatus(page, '', false);
        // Retired request continuations cannot reset controls in a new lifecycle.
        ['#wtCreate', '#wtCreateInvitation', '#wtAcceptInvitation'].forEach(function (selector) {
            setButtonBusy(page.querySelector(selector), false);
        });
        Object.keys(page._wtRoomViews || {}).forEach(function (roomId) {
            clearRoomBackdrop(page._wtRoomViews[roomId]);
            clearRoomAvatars(page._wtRoomViews[roomId]);
        });
        page._wtAvatarFailures = {};
        page._wtRoomViews = {};
        page._wtRoomStates = {};
        page._wtRoomSequences = {};
        page._wtMedia = {};
        page._wtMediaFlights = {};
        page._wtMetadataFlights = {};
        page._wtMediaTargets = {};
        page._wtResources = {};
        page._wtDiagnostics = {};
        page._wtDiagnosticPanels = {};
        page._wtDiagnosticRequests = {};
        page._wtDiagnosticBusy = {};
        page._wtDiagnosticOpen = {};
        page._wtRoomBusy = {};
        page._wtRoomsLoading = false;
        page._wtRoomsSignature = null;
        page._wtIsAdmin = undefined;
        page._wtTabsPermissionReady = false;
        page._wtConfigReady = false;
        page._wtPluginConfiguration = null;
        page._wtGitHubTokenReady = false;
        page._wtGitHubTokenCanClear = false;
        page._wtNativeTabsKey = null;
        clearGitHubTokenInput(page);
        ['#wtRoomName', '#wtInvitationName', '#wtInvitationAcceptCode'].forEach(function (selector) {
            var input = page.querySelector(selector);
            if (input) input.value = '';
        });
        ['#wtUserA', '#wtUserB', '#wtPrimary', '#wtRooms', '#wtInvitations'].forEach(function (selector) {
            clearChildren(page.querySelector(selector));
        });
        renderInvitationCode(page);
        setAdminVisibility(page, false);
        setConfigBusy(page, true);
        setGitHubTokenBusy(page, true, 'read');
    }

    function pageActionAllowed(page) {
        if (!page._wtActive) return false;
        var current = pageIdentity();
        var owner = page._wtIdentity;
        if (current && owner && current.user && current.user.toLowerCase() === owner.user.toLowerCase() &&
            current.server === owner.server && current.serverId === owner.serverId && current.client === owner.client) return true;
        page._wtActive = false;
        page._wtGeneration += 1;
        clearPageData(page);
        loading.hide();
        setStatus(page, t('账号或服务器已变化，请重新打开一起看。'), true);
        return false;
    }

    function confirmationRoomState(page, roomId) {
        var room = (page._wtRooms || []).filter(function (item) { return item.RoomId === roomId; })[0];
        return room ? JSON.stringify([room.RoomId, room.Name, room.PrimaryUserId,
            (room.ParticipantUserIds || []).map(idKey).sort(),
            (room.JoinedParticipantUserIds || []).map(idKey).sort(),
            room.CurrentUserJoined, room.CanEnd, room.IsAdmin]) : null;
    }

    // Emby hosts may disable native script dialogs. Use the same asynchronous
    // confirm module as the dashboard; resolve means OK, reject means cancel.
    function confirmAction(page, options, roomId, onConfirmed) {
        if (!pageActionAllowed(page) || page._wtConfirmation) return;
        var generation = page._wtGeneration;
        var route = page._wtVisibleRoute;
        var admin = page._wtIsAdmin;
        var roomState = roomId ? confirmationRoomState(page, roomId) : null;
        if (roomId && !roomState) return;
        var pending = {};
        page._wtConfirmation = pending;
        if (roomId) {
            setRoomBusy(page, roomId, true);
            renderRooms(page, page._wtRooms || []);
        } else {
            setGitHubTokenBusy(page, true);
        }

        function owned() {
            return pageActionAllowed(page) && page._wtGeneration === generation && page._wtConfirmation === pending;
        }
        function current() {
            return owned() && page._wtVisibleRoute === route;
        }
        function release() {
            if (!owned()) return false;
            page._wtConfirmation = null;
            if (roomId) {
                setRoomBusy(page, roomId, false);
                renderRooms(page, page._wtRooms || []);
            } else {
                setGitHubTokenBusy(page, false);
            }
            return true;
        }
        function feedback(text) {
            if (roomId && confirmationRoomState(page, roomId)) {
                roomFeedback(page, roomId, text, true, true);
                renderRooms(page, page._wtRooms || []);
            } else if (roomId) {
                setStatus(page, text, true);
            } else {
                setGitHubTokenStatus(page, text, true);
            }
        }
        function failed() {
            if (release()) feedback(t('无法打开确认窗口，请重新打开一起看后重试。'));
        }
        return Promise.resolve().then(function () {
            return require(['confirm']);
        }).then(function (modules) {
            if (!current()) { release(); return; }
            var confirm = modules && modules[0];
            confirm = confirm && (confirm.default || confirm);
            if (typeof confirm !== 'function') throw new Error('Confirmation module unavailable');
            var result = confirm(Object.assign({ primary: 'cancel' }, options));
            if (!result || typeof result.then !== 'function') throw new Error('Invalid confirmation result');
            return result.then(function () {
                if (!current()) { release(); return; }
                var unchanged = admin === page._wtIsAdmin &&
                    (!roomId || roomState === confirmationRoomState(page, roomId));
                if (!release()) return;
                if (!unchanged) {
                    feedback(t('房间或权限已变化，请重新确认操作。'));
                    return;
                }
                onConfirmed();
            }, function () {
                release();
            });
        }, failed).catch(failed);
    }

    // Guard every continuation, including catch/finally and chained mutations.
    // A retired generation never completes into the next page lifecycle.
    function scopedPromise(page, promise, generation) {
        generation = generation === undefined ? page._wtGeneration : generation;
        function guard(callback) {
            return function (value) {
                if (!pageActionAllowed(page) || page._wtGeneration !== generation) return new Promise(function () {});
                return callback(value);
            };
        }
        return {
            then: function (fulfilled, rejected) {
                return scopedPromise(page, Promise.resolve(promise).then(
                    guard(typeof fulfilled === 'function' ? fulfilled : function (value) { return value; }),
                    guard(typeof rejected === 'function' ? rejected : function (error) { throw error; })), generation);
            },
            catch: function (rejected) { return this.then(null, rejected); },
            finally: function (callback) {
                return this.then(function (value) { callback(); return value; }, function (error) { callback(); throw error; });
            }
        };
    }

    function pageRequest(page, key, request) {
        if (!pageActionAllowed(page)) return scopedPromise(page, new Promise(function () {}));
        var resources = page._wtResources;
        if (resources[key]) return resources[key];
        var generation = page._wtGeneration;
        var promise = Promise.resolve().then(function () {
            if (!pageActionAllowed(page) || page._wtGeneration !== generation) return new Promise(function () {});
            return request();
        });
        resources[key] = scopedPromise(page, promise, generation);
        function release() { delete resources[key]; }
        promise.then(release, release);
        return resources[key];
    }

    function apiGet(page, path) {
        return pageRequest(page, 'GET:' + path, function () { return ApiClient.getJSON(apiUrl(path)); });
    }

    function apiSend(page, path, method, body) {
        return pageRequest(page, method + ':' + path, function () { return ApiClient.fetch({
            url: apiUrl(path),
            type: method,
            data: body === undefined ? null : JSON.stringify(body),
            contentType: 'application/json',
            dataType: 'json'
        }); });
    }

    function errorMessage(error) {
        var status = error && (error.status || error.statusCode);
        if (status === 401 || status === 403) {
            return t('权限不足，请使用管理员账号重试。');
        }
        if (status >= 500) {
            return t('服务器暂时不可用，请稍后重试。');
        }
        if (error && error.name === 'TypeError') {
            return t('网络连接失败，请稍后重试。');
        }
        return t('操作未完成，请稍后重试。');
    }

    var diagnosticStateLabels = {
        get Waiting() { return t('等待参与者'); },
        get Barrier() { return t('正在对齐'); },
        get Watching() { return t('同步中'); },
        get Handoff() { return t('正在同步视频'); },
        get Recovering() { return t('临时掉线恢复中'); },
        get Unavailable() { return t('暂不可用'); }
    };
    var diagnosticReasonLabels = {
        get server_unavailable() { return t('服务器暂时不可用'); },
        get snapshot_unavailable() { return t('播放会话暂时不可读'); },
        get snapshot_stale() { return t('播放会话数据已过期'); },
        get different_video() { return t('两位参与者打开了不同视频'); },
        get playback_stopped() { return t('播放已停止，等待重新打开视频'); },
        get stop_resume_confirmation_required() { return t('停止后进度差较大，等待确认重新同步'); },
        get action_conflict() { return t('检测到手动操作冲突'); },
        get barrier_retry_exhausted() { return t('对齐重试次数已用尽'); },
        get waiting_pause_retry_limit() { return t('等待暂停重试次数已用尽'); },
        get media_handoff() { return t('正在让参与者打开主用户当前视频'); },
        get handoff_failed() { return t('媒体切换同步未完成'); },
        get participant_resync_failed() { return t('参与者重新同步未完成'); },
        get play_item_timeout() { return t('打开视频确认超时'); },
        get command_timeout() { return t('远程控制超时'); },
        get session_offline() { return t('目标会话已离线'); },
        get invalid_argument() { return t('媒体切换参数无效'); },
        get command_failed() { return t('播放控制未完成'); },
        get aligning() { return t('正在对齐播放位置'); },
        get watching() { return t('两位参与者已连接'); },
        get remote_control_unavailable() { return t('当前客户端不支持远程控制'); },
        get unsupported_playback_rate() { return t('播放速度不是 1 倍'); },
        get waiting_for_playback() { return t('等待双方打开同一视频并开始播放'); },
        get transient_recovery() { return t('正在等待临时掉线的参与者恢复'); }
    };
    var diagnosticHealthLabels = {
        get fresh() { return t('新鲜'); },
        get stale() { return t('过期'); },
        get unavailable() { return t('不可用'); },
        get unknown() { return t('未知'); }
    };
    var diagnosticResultLabels = {
        get success() { return t('成功'); },
        get failed() { return t('失败'); },
        get retry_scheduled() { return t('已安排重试'); },
        get pending() { return t('等待确认'); },
        get stopped() { return t('已停止'); },
        get entered() { return t('已进入'); },
        get recovered() { return t('已恢复'); },
        get changed() { return t('已变化'); },
        get cleared() { return t('已清除'); },
        get older_activity() { return t('活动时间较旧'); },
        get expired() { return t('已过期'); },
        get source_resumed() { return t('主用户回到原视频'); },
        get observed() { return t('已记录'); }
    };
    var diagnosticBarrierStageLabels = {
        get Pause() { return t('暂停阶段'); },
        get Seek() { return t('定位阶段'); },
        get Restore() { return t('恢复播放阶段'); }
    };
    var diagnosticCommandLabels = {
        get Pause() { return t('暂停'); },
        get Unpause() { return t('继续'); },
        get PlayPause() { return t('播放/暂停'); },
        get Seek() { return t('定位'); },
        get Stop() { return t('停止'); },
        get PlayItem() { return t('打开视频'); },
        get DisplayMessage() { return t('提示'); }
    };
    var diagnosticEventLabels = {
        get barrier_started() { return t('开始对齐'); },
        get barrier_stage_changed() { return t('对齐阶段变化'); },
        get entered_watching() { return t('进入同步中'); },
        get command_issued() { return t('发送控制'); },
        get command_acknowledged() { return t('控制已确认'); },
        get command_failed() { return t('控制失败'); },
        get retry_scheduled() { return t('安排重试'); },
        get stop_confirmed() { return t('确认停止'); },
        get stop_resume_confirmation_required() { return t('停止后等待确认恢复'); },
        get primary_transition_candidate() { return t('观察主用户停止后的换片'); },
        get primary_transition_waiting() { return t('换片等待远控条件恢复'); },
        get primary_transition_expired() { return t('换片观察窗口已过期'); },
        get primary_transition_invalidated() { return t('换片观察已失效'); },
        get snapshot_protection_entered() { return t('进入保护状态'); },
        get snapshot_protection_recovered() { return t('保护状态恢复'); },
        get snapshot_ignored() { return t('忽略过期快照'); },
        get eligibility_changed() { return t('同步条件变化'); },
        get manual_action() { return t('手动操作'); },
        get resync() { return t('重新同步'); },
        get resync_barrier_started() { return t('重新同步开始对齐'); },
        get handoff_started() { return t('开始媒体切换'); },
        get handoff_play_requested() { return t('请求打开视频'); },
        get handoff_target_confirmed() { return t('目标视频已确认'); },
        get handoff_primary_pause_requested() { return t('请求主用户暂停等待'); },
        get handoff_primary_paused() { return t('主用户已暂停等待'); },
        get handoff_primary_hold_interrupted() { return t('主用户操作取消换片等待'); },
        get handoff_barrier_started() { return t('媒体切换开始对齐'); },
        get handoff_completed() { return t('媒体切换完成'); },
        get handoff_failed() { return t('媒体切换失败'); },
        get handoff_superseded() { return t('媒体切换目标变化'); },
        get primary_item_changed() { return t('主用户视频变化'); },
        get recovery_started() { return t('开始临时掉线恢复'); },
        get recovery_confirmed() { return t('临时掉线恢复成功'); },
        get recovery_timed_out() { return t('临时掉线恢复超时'); },
        get recovery_session_changed() { return t('恢复会话发生变化'); },
        get recovery_item_changed() { return t('恢复视频发生变化'); },
        get drift_threshold_entered() { return t('进入漂移阈值'); },
        get drift_threshold_cleared() { return t('漂移阈值已清除'); },
        get drift_auto_repair_started() { return t('开始漂移自动纠偏'); },
        get drift_auto_repair_completed() { return t('漂移自动纠偏完成'); },
        get drift_auto_repair_failed() { return t('漂移自动纠偏失败'); },
        get observed() { return t('观察事件'); }
    };

    var diagnosticHandoffErrorLabels = {
        get command_timeout() { return t('远程控制超时'); },
        get command_failed() { return t('远程控制失败'); },
        get remote_control_unsupported() { return t('当前客户端不支持远程控制'); },
        get session_offline() { return t('目标会话已离线'); },
        get invalid_argument() { return t('媒体切换参数无效'); },
        get play_item_timeout() { return t('打开视频确认超时'); },
        get handoff_failed() { return t('媒体切换失败'); }
    };

    function diagnosticFiniteNumber(value) {
        return typeof value === 'number' && isFinite(value) ? value : null;
    }

    function diagnosticAlias(value) {
        return value === 'userA' || value === 'userB' ? value : 'unknown';
    }

    function diagnosticCommand(value) {
        var names = Object.keys(diagnosticCommandLabels);
        for (var i = 0; i < names.length; i++) {
            if (String(value || '').toLowerCase() === names[i].toLowerCase()) {
                return names[i];
            }
        }
        return null;
    }

    function diagnosticResult(value) {
        return diagnosticResultLabels[value] ? value : null;
    }

    function diagnosticBarrierStage(value) {
        return diagnosticBarrierStageLabels[value] ? value : null;
    }

    function diagnosticSecondsFromTicks(value) {
        var ticks = diagnosticFiniteNumber(value);
        return ticks === null ? null : Math.max(0, ticks / 10000000);
    }

    function sanitizeDiagnosticSession(session) {
        session = session || {};
        return {
            alias: diagnosticAlias(session.Alias),
            positionSeconds: diagnosticFiniteNumber(session.PositionSeconds),
            online: session.Online === true,
            paused: session.Paused === true,
            playbackRate: diagnosticFiniteNumber(session.PlaybackRate),
            runtimeSeconds: diagnosticFiniteNumber(session.RuntimeSeconds),
            lastActivityAgeSeconds: diagnosticFiniteNumber(session.LastActivityAgeSeconds),
            ackLatencySeconds: diagnosticFiniteNumber(session.AckLatencySeconds),
            reportedRemoteControl: session.ReportedSupportsRemoteControl === true,
            effectiveRemoteControl: session.EffectiveSupportsRemoteControl === true,
            canPause: session.CanPause === true,
            canUnpause: session.CanUnpause === true,
            canSeek: session.CanSeek === true,
            canDisplayMessage: session.CanDisplayMessage === true,
            supportedCommands: (Array.isArray(session.SupportedCommandNames) ? session.SupportedCommandNames : [])
                .map(diagnosticCommand).filter(function (command, index, commands) {
                    return command && commands.indexOf(command) === index;
                })
        };
    }

    function sanitizeDiagnosticEvent(event) {
        event = event || {};
        return {
            type: diagnosticEventLabels[event.Type] ? event.Type : 'observed',
            command: diagnosticCommand(event.Command),
            alias: diagnosticAlias(event.Alias),
            result: diagnosticResult(event.Result),
            positionSeconds: event.PositionTicks === null || event.PositionTicks === undefined
                ? null : diagnosticSecondsFromTicks(event.PositionTicks),
            latencySeconds: diagnosticFiniteNumber(event.LatencySeconds),
            driftSeconds: diagnosticFiniteNumber(event.DriftSeconds),
            atUtc: typeof event.AtUtc === 'string' ? event.AtUtc : null
        };
    }

    function sanitizeDiagnostic(raw) {
        if (!raw || typeof raw !== 'object') {
            return null;
        }
        var participants = Array.isArray(raw.Participants) ? raw.Participants.slice(0, 2).map(function (participant) {
            participant = participant || {};
            return {
                alias: diagnosticAlias(participant.Alias),
                primary: participant.IsPrimary === true,
                joined: participant.Joined === true
            };
        }) : [];
        var sessions = Array.isArray(raw.Sessions) ? raw.Sessions.slice(0, 2).map(sanitizeDiagnosticSession) : [];
        var pending = Array.isArray(raw.Pending) ? raw.Pending.slice(0, 2).map(function (item) {
            item = item || {};
            return {
                alias: diagnosticAlias(item.Alias),
                command: diagnosticCommand(item.Command),
                positionSeconds: item.PositionTicks === null || item.PositionTicks === undefined
                    ? null : diagnosticSecondsFromTicks(item.PositionTicks),
                ageSeconds: diagnosticFiniteNumber(item.AgeSeconds),
                retries: diagnosticFiniteNumber(item.Retries)
            };
        }) : [];
        var barrier = raw.Barrier && typeof raw.Barrier === 'object' ? {
            stage: diagnosticBarrierStage(raw.Barrier.Stage),
            anchorAlias: diagnosticAlias(raw.Barrier.AnchorAlias),
            targetPositionSeconds: diagnosticFiniteNumber(raw.Barrier.TargetPositionSeconds),
            ageSeconds: diagnosticFiniteNumber(raw.Barrier.AgeSeconds),
            pauseSent: raw.Barrier.PauseSent === true,
            seekSent: raw.Barrier.SeekSent === true,
            restoreSent: raw.Barrier.RestoreSent === true,
            seekRetryPending: raw.Barrier.SeekRetryPending === true
        } : null;
        var handoff = raw.Handoff && typeof raw.Handoff === 'object' ? {
            targetItemHash: typeof raw.Handoff.TargetItemHash === 'string' ? raw.Handoff.TargetItemHash : null,
            sourceItemHash: typeof raw.Handoff.SourceItemHash === 'string' ? raw.Handoff.SourceItemHash : null,
            primaryAlias: diagnosticAlias(raw.Handoff.PrimaryAlias),
            participantAlias: diagnosticAlias(raw.Handoff.ParticipantAlias),
            playItemPending: raw.Handoff.PlayItemPending === true,
            retryCount: diagnosticFiniteNumber(raw.Handoff.RetryCount),
            generation: diagnosticFiniteNumber(raw.Handoff.Generation),
            ageSeconds: diagnosticFiniteNumber(raw.Handoff.AgeSeconds),
            lastError: diagnosticHandoffErrorLabels[raw.Handoff.LastError] ? raw.Handoff.LastError : null,
            participantResync: raw.Handoff.IsParticipantResync === true
        } : null;
        var recovery = raw.RecoveryWindow && typeof raw.RecoveryWindow === 'object' ? {
            active: raw.RecoveryWindow.Active === true,
            ageSeconds: diagnosticFiniteNumber(raw.RecoveryWindow.AgeSeconds),
            affectedAliases: (Array.isArray(raw.RecoveryWindow.AffectedAliases) ? raw.RecoveryWindow.AffectedAliases : [])
                .slice(0, 2).map(diagnosticAlias)
        } : { active: false, ageSeconds: null, affectedAliases: [] };
        var transientRecovery = raw.TransientRecovery && typeof raw.TransientRecovery === 'object' ? {
            active: raw.TransientRecovery.Active === true,
            ageSeconds: diagnosticFiniteNumber(raw.TransientRecovery.AgeSeconds),
            remainingSeconds: diagnosticFiniteNumber(raw.TransientRecovery.RemainingSeconds),
            missingAliases: (Array.isArray(raw.TransientRecovery.MissingAliases) ? raw.TransientRecovery.MissingAliases : [])
                .slice(0, 2).map(diagnosticAlias),
            participants: (Array.isArray(raw.TransientRecovery.Participants) ? raw.TransientRecovery.Participants : [])
                .slice(0, 2).map(function (participant) {
                    participant = participant || {};
                    return {
                        alias: diagnosticAlias(participant.Alias),
                        missing: participant.Missing === true,
                        expectedSessionHash: typeof participant.ExpectedSessionHash === 'string' ? participant.ExpectedSessionHash : null,
                        expectedItemHash: typeof participant.ExpectedItemHash === 'string' ? participant.ExpectedItemHash : null,
                        lastKnownPositionSeconds: diagnosticFiniteNumber(participant.LastKnownPositionSeconds),
                        lastKnownPaused: participant.LastKnownPaused === true
                    };
                })
        } : { active: false, ageSeconds: null, remainingSeconds: null, missingAliases: [], participants: [] };
        var drift = raw.Drift && typeof raw.Drift === 'object' ? {
            currentSeconds: diagnosticFiniteNumber(raw.Drift.CurrentDriftSeconds),
            maxAbsoluteSeconds: diagnosticFiniteNumber(raw.Drift.MaxObservedAbsoluteDriftSeconds),
            holdAgeSeconds: diagnosticFiniteNumber(raw.Drift.HoldAgeSeconds),
            lastRepairAtUtc: typeof raw.Drift.LastAutoRepairAtUtc === 'string' ? raw.Drift.LastAutoRepairAtUtc : null,
            repairCount: diagnosticFiniteNumber(raw.Drift.AutoRepairCount),
            lastRepairDriftSeconds: diagnosticFiniteNumber(raw.Drift.LastAutoRepairDriftSeconds),
            cooldownRemainingSeconds: diagnosticFiniteNumber(raw.Drift.CooldownRemainingSeconds)
        } : {
            currentSeconds: null, maxAbsoluteSeconds: null, holdAgeSeconds: null,
            lastRepairAtUtc: null, repairCount: null, lastRepairDriftSeconds: null,
            cooldownRemainingSeconds: null
        };
        return {
            schemaVersion: typeof raw.SchemaVersion === 'string' ? raw.SchemaVersion : null,
            pluginVersion: typeof raw.PluginVersion === 'string' ? raw.PluginVersion : null,
            roomState: diagnosticStateLabels[raw.RoomState] ? raw.RoomState : 'Unavailable',
            statusReason: diagnosticReasonLabels[raw.StatusReason] ? raw.StatusReason : null,
            snapshotHealth: diagnosticHealthLabels[raw.SnapshotHealth] ? raw.SnapshotHealth : 'unknown',
            participants: participants,
            sessions: sessions,
            pending: pending,
            handoff: handoff,
            barrier: barrier,
            recovery: recovery,
            transientRecovery: transientRecovery,
            drift: drift,
            lastAction: raw.LastAction && typeof raw.LastAction === 'object' ? sanitizeDiagnosticEvent(raw.LastAction) : null,
            lastError: diagnosticReasonLabels[raw.LastError] ? raw.LastError : null,
            events: (Array.isArray(raw.Events) ? raw.Events : [])
                .slice(-maxDiagnosticEvents)
                .reverse()
                .map(sanitizeDiagnosticEvent),
            generatedAtUtc: typeof raw.GeneratedAtUtc === 'string' ? raw.GeneratedAtUtc : null
        };
    }

    function diagnosticLabel(map, value, fallback) {
        return map[value] || fallback || t('未知');
    }

    function diagnosticFormatNumber(value, suffix) {
        var number = diagnosticFiniteNumber(value);
        return number === null ? '—' : number.toFixed(1) + (suffix || '');
    }

    function diagnosticFormatPosition(value) {
        var seconds = diagnosticFiniteNumber(value);
        if (seconds === null) return '—';
        var total = Math.max(0, Math.floor(seconds));
        var minutes = Math.floor(total / 60);
        var remainingNumber = total % 60;
        var remaining = remainingNumber < 10 ? '0' + remainingNumber : String(remainingNumber);
        return minutes + ':' + remaining;
    }

    function diagnosticFormatTime(value) {
        if (!value) return t('时间未知');
        var date = new Date(value);
        return isNaN(date.getTime()) ? t('时间未知') : date.toLocaleString();
    }

    function diagnosticField(parent, label, value) {
        var field = document.createElement('div');
        var labelEl = document.createElement('span');
        labelEl.className = 'wt-diagnosticLabel';
        labelEl.textContent = label + '：';
        var valueEl = document.createElement('span');
        valueEl.className = 'wt-diagnosticValue';
        valueEl.textContent = value;
        field.appendChild(labelEl);
        field.appendChild(valueEl);
        parent.appendChild(field);
    }

    function diagnosticGroup(parent, title) {
        var group = document.createElement('section');
        group.className = 'wt-diagnosticGroup';
        var heading = document.createElement('h4');
        heading.textContent = title;
        group.appendChild(heading);
        parent.appendChild(group);
        return group;
    }

    function renderDiagnosticDetails(page, roomId, diagnostic, body) {
        clearChildren(body);
        var toolbar = document.createElement('div');
        toolbar.className = 'wt-diagnosticToolbar';
        var exportButton = document.createElement('button', { is: 'emby-button' });
        exportButton.type = 'button';
        exportButton.setAttribute('is', 'emby-button');
        exportButton.classList.add('raised', 'raised-outline');
        exportButton.dataset.action = 'exportDiagnostics';
        exportButton.textContent = t('导出诊断 JSON');
        exportButton.addEventListener('click', function () {
            exportDiagnostic(page, roomId);
        });
        toolbar.appendChild(exportButton);
        var timestamp = document.createElement('span');
        timestamp.className = 'fieldDescription';
        timestamp.textContent = t('读取时间：') + diagnosticFormatTime(diagnostic.generatedAtUtc);
        toolbar.appendChild(timestamp);
        body.appendChild(toolbar);

        var summary = document.createElement('div');
        summary.className = 'wt-diagnosticSummary';
        diagnosticField(summary, t('状态'), diagnosticLabel(diagnosticStateLabels, diagnostic.roomState, t('暂不可用')));
        diagnosticField(summary, t('状态原因'), diagnosticLabel(diagnosticReasonLabels, diagnostic.statusReason, t('当前状态需要检查')));
        diagnosticField(summary, t('快照健康'), diagnosticLabel(diagnosticHealthLabels, diagnostic.snapshotHealth, t('未知')));
        diagnosticField(summary, t('事件数量'), String(diagnostic.events.length));
        body.appendChild(summary);

        var sessions = diagnosticGroup(body, t('参与者会话'));
        if (diagnostic.sessions.length === 0) {
            var noSessions = document.createElement('p');
            noSessions.className = 'fieldDescription';
            noSessions.textContent = t('暂无可用会话。');
            sessions.appendChild(noSessions);
        } else {
            diagnostic.sessions.forEach(function (session) {
                var sessionSummary = document.createElement('div');
                sessionSummary.className = 'wt-diagnosticSummary';
                diagnosticField(sessionSummary, session.alias, (session.online ? t('在线') : t('离线')) + t('，位置 ') + diagnosticFormatPosition(session.positionSeconds));
                diagnosticField(sessionSummary, t('播放状态'), session.online ? (session.paused ? t('已暂停') : t('播放中')) : t('状态未知'));
                diagnosticField(sessionSummary, t('能力'), t('上报远控 ') + (session.reportedRemoteControl ? t('支持') : t('不支持')) + t('；有效远控 ') + (session.effectiveRemoteControl ? t('支持') : t('不支持')));
                diagnosticField(sessionSummary, t('确认延迟'), diagnosticFormatNumber(session.ackLatencySeconds, t(' 秒')));
                diagnosticField(sessionSummary, t('播放速率'), diagnosticFormatNumber(session.playbackRate, t(' 倍')));
                diagnosticField(sessionSummary, t('可用控制'), session.supportedCommands.length > 0 ? session.supportedCommands.map(function (command) {
                    return diagnosticCommandLabels[command];
                }).join('、') : t('无'));
                sessions.appendChild(sessionSummary);
            });
        }

        var pending = diagnosticGroup(body, t('等待中的控制'));
        if (diagnostic.pending.length === 0) {
            var noPending = document.createElement('p');
            noPending.className = 'fieldDescription';
            noPending.textContent = t('当前没有等待确认的控制。');
            pending.appendChild(noPending);
        } else {
            var pendingList = document.createElement('ul');
            pendingList.className = 'wt-diagnosticList';
            diagnostic.pending.forEach(function (item) {
                var pendingItem = document.createElement('li');
                pendingItem.textContent = item.alias + '：' + (diagnosticCommandLabels[item.command] || t('未知控制')) +
                    t('，位置 ') + diagnosticFormatPosition(item.positionSeconds) +
                    t('，等待 ') + diagnosticFormatNumber(item.ageSeconds, t(' 秒')) +
                    t('，重试 ') + (item.retries === null ? '—' : String(Math.max(0, Math.floor(item.retries))) + t(' 次'));
                pendingList.appendChild(pendingItem);
            });
            pending.appendChild(pendingList);
        }

        if (diagnostic.handoff) {
            var handoffGroup = diagnosticGroup(body, t('媒体切换'));
            var handoffSummary = document.createElement('div');
            handoffSummary.className = 'wt-diagnosticSummary';
            diagnosticField(handoffSummary, t('类型'), diagnostic.handoff.participantResync ? t('参与者重新同步') : t('主用户切换媒体'));
            diagnosticField(handoffSummary, t('目标视频标识'), diagnostic.handoff.targetItemHash || '—');
            diagnosticField(handoffSummary, t('来源视频标识'), diagnostic.handoff.sourceItemHash || '—');
            diagnosticField(handoffSummary, t('参与者'), diagnostic.handoff.participantAlias);
            diagnosticField(handoffSummary, t('控制状态'), diagnostic.handoff.playItemPending ? t('等待打开视频确认') : t('准备发送'));
            diagnosticField(handoffSummary, t('重试次数'), diagnostic.handoff.retryCount === null ? '—' : String(Math.max(0, Math.floor(diagnostic.handoff.retryCount))));
            diagnosticField(handoffSummary, t('操作代际'), diagnostic.handoff.generation === null ? '—' : String(Math.max(0, Math.floor(diagnostic.handoff.generation))));
            diagnosticField(handoffSummary, t('持续时间'), diagnosticFormatNumber(diagnostic.handoff.ageSeconds, t(' 秒')));
            if (diagnostic.handoff.lastError) {
                diagnosticField(handoffSummary, t('最近错误'), diagnosticLabel(diagnosticHandoffErrorLabels, diagnostic.handoff.lastError, t('媒体切换失败')));
            }
            handoffGroup.appendChild(handoffSummary);
        }

        var barrier = diagnosticGroup(body, t('对齐与恢复'));
        var barrierSummary = document.createElement('div');
        barrierSummary.className = 'wt-diagnosticSummary';
        diagnosticField(barrierSummary, t('对齐阶段'), diagnostic.barrier ? diagnosticLabel(diagnosticBarrierStageLabels, diagnostic.barrier.stage, t('未知')) : t('未进行对齐'));
        diagnosticField(barrierSummary, t('目标位置'), diagnostic.barrier ? diagnosticFormatPosition(diagnostic.barrier.targetPositionSeconds) : '—');
        diagnosticField(barrierSummary, t('命令进度'), diagnostic.barrier ?
            [t('暂停 ') + (diagnostic.barrier.pauseSent ? t('已发送') : t('未发送')),
                t('定位 ') + (diagnostic.barrier.seekSent ? t('已发送') : t('未发送')),
                t('恢复 ') + (diagnostic.barrier.restoreSent ? t('已发送') : t('未发送'))].join('、') : '—');
        diagnosticField(barrierSummary, t('恢复窗口'), diagnostic.recovery.active ? t('活动中') : t('未活动'));
        diagnosticField(barrierSummary, t('恢复窗口时长'), diagnosticFormatNumber(diagnostic.recovery.ageSeconds, t(' 秒')));
        diagnosticField(barrierSummary, t('受影响参与者'), diagnostic.recovery.affectedAliases.length > 0 ? diagnostic.recovery.affectedAliases.join('、') : '—');
        barrier.appendChild(barrierSummary);

        var transientRecoveryGroup = diagnosticGroup(body, t('临时掉线恢复'));
        var transientRecoverySummary = document.createElement('div');
        transientRecoverySummary.className = 'wt-diagnosticSummary';
        diagnosticField(transientRecoverySummary, t('状态'), diagnostic.transientRecovery.active ? t('恢复中') : t('未活动'));
        diagnosticField(transientRecoverySummary, t('已等待'), diagnosticFormatNumber(diagnostic.transientRecovery.ageSeconds, t(' 秒')));
        diagnosticField(transientRecoverySummary, t('剩余窗口'), diagnosticFormatNumber(diagnostic.transientRecovery.remainingSeconds, t(' 秒')));
        diagnosticField(transientRecoverySummary, t('缺失参与者'), diagnostic.transientRecovery.missingAliases.length > 0 ? diagnostic.transientRecovery.missingAliases.join('、') : '—');
        transientRecoveryGroup.appendChild(transientRecoverySummary);
        diagnostic.transientRecovery.participants.forEach(function (participant) {
            var participantSummary = document.createElement('div');
            participantSummary.className = 'wt-diagnosticSummary';
            diagnosticField(participantSummary, participant.alias, participant.missing ? t('等待该会话恢复') : t('会话仍在线'));
            diagnosticField(participantSummary, t('预期 Session 标识'), participant.expectedSessionHash || '—');
            diagnosticField(participantSummary, t('预期视频标识'), participant.expectedItemHash || '—');
            diagnosticField(participantSummary, t('上次位置'), diagnosticFormatPosition(participant.lastKnownPositionSeconds));
            diagnosticField(participantSummary, t('上次播放状态'), participant.lastKnownPaused ? t('已暂停') : t('播放中'));
            transientRecoveryGroup.appendChild(participantSummary);
        });

        var driftGroup = diagnosticGroup(body, t('播放漂移'));
        var driftSummary = document.createElement('div');
        driftSummary.className = 'wt-diagnosticSummary';
        diagnosticField(driftSummary, t('当前漂移'), diagnosticFormatNumber(diagnostic.drift.currentSeconds, t(' 秒')));
        diagnosticField(driftSummary, t('最大绝对漂移'), diagnosticFormatNumber(diagnostic.drift.maxAbsoluteSeconds, t(' 秒')));
        diagnosticField(driftSummary, t('纠偏保持计时'), diagnosticFormatNumber(diagnostic.drift.holdAgeSeconds, t(' 秒')));
        diagnosticField(driftSummary, t('自动纠偏次数'), diagnostic.drift.repairCount === null ? '—' : String(Math.max(0, Math.floor(diagnostic.drift.repairCount))));
        diagnosticField(driftSummary, t('最近自动纠偏'), diagnostic.drift.lastRepairAtUtc ? diagnosticFormatTime(diagnostic.drift.lastRepairAtUtc) : '—');
        diagnosticField(driftSummary, t('最近纠偏漂移'), diagnosticFormatNumber(diagnostic.drift.lastRepairDriftSeconds, t(' 秒')));
        diagnosticField(driftSummary, t('冷却剩余'), diagnosticFormatNumber(diagnostic.drift.cooldownRemainingSeconds, t(' 秒')));
        driftGroup.appendChild(driftSummary);

        var last = diagnosticGroup(body, t('最近结果'));
        var lastSummary = document.createElement('div');
        lastSummary.className = 'wt-diagnosticSummary';
        diagnosticField(lastSummary, t('最近动作'), diagnostic.lastAction ? diagnosticLabel(diagnosticEventLabels, diagnostic.lastAction.type, t('观察事件')) : '—');
        diagnosticField(lastSummary, t('动作目标'), diagnostic.lastAction && diagnostic.lastAction.alias !== 'unknown' ? diagnostic.lastAction.alias : '—');
        diagnosticField(lastSummary, t('动作控制'), diagnostic.lastAction && diagnostic.lastAction.command ? (diagnosticCommandLabels[diagnostic.lastAction.command] || t('未知控制')) : '—');
        diagnosticField(lastSummary, t('动作结果'), diagnostic.lastAction && diagnostic.lastAction.result ? diagnosticLabel(diagnosticResultLabels, diagnostic.lastAction.result, t('已记录')) : '—');
        diagnosticField(lastSummary, t('最近错误'), diagnostic.lastError ? diagnosticLabel(diagnosticReasonLabels, diagnostic.lastError, t('播放控制未完成')) : t('无'));
        last.appendChild(lastSummary);

        var events = diagnosticGroup(body, t('事件记录'));
        var eventList = document.createElement('ul');
        eventList.className = 'wt-diagnosticList';
        if (diagnostic.events.length === 0) {
            var noEvents = document.createElement('li');
            noEvents.textContent = t('暂无事件记录。');
            eventList.appendChild(noEvents);
        } else {
            diagnostic.events.forEach(function (event) {
                var eventItem = document.createElement('li');
                var detail = diagnosticLabel(diagnosticEventLabels, event.type, t('观察事件'));
                if (event.alias !== 'unknown') detail += '（' + event.alias + '）';
                if (event.command) detail += '：' + (diagnosticCommandLabels[event.command] || t('未知控制'));
                if (event.result) detail += '，' + diagnosticLabel(diagnosticResultLabels, event.result, t('已记录'));
                if (event.driftSeconds !== null) detail += t('，漂移 ') + diagnosticFormatNumber(event.driftSeconds, t(' 秒'));
                eventItem.textContent = diagnosticFormatTime(event.atUtc) + ' · ' + detail;
                eventList.appendChild(eventItem);
            });
        }
        events.appendChild(eventList);
    }

    function diagnosticErrorMessage(error) {
        var status = error && (error.status || error.statusCode);
        if (status === 401 || status === 403) return t('没有权限查看此房间的诊断。');
        if (status >= 500) return t('服务器暂时不可用，请稍后重试。');
        if (error && error.name === 'TypeError') return t('网络连接失败，请稍后重试。');
        return t('诊断读取失败，请稍后重试。');
    }

    function createDiagnosticPanel(page, room) {
        var generation = page._wtGeneration;
        page._wtDiagnosticPanels = page._wtDiagnosticPanels || {};
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
        var details = document.createElement('details');
        details.className = 'wt-diagnostics';
        var summary = document.createElement('summary');
        summary.textContent = t('诊断详情');
        details.appendChild(summary);
        var body = document.createElement('div');
        body.className = 'wt-diagnosticBody';
        details.appendChild(body);
        details.addEventListener('toggle', function () {
            var current = page._wtDiagnosticPanels && page._wtDiagnosticPanels[room.RoomId];
            if (!pageActionAllowed(page) || page._wtGeneration !== generation || !current || current.details !== details) return;
            page._wtDiagnosticOpen[room.RoomId] = details.open;
            details.hidden = !details.open;
            if (details.open && !page._wtDiagnostics[room.RoomId]) loadRoomDiagnostics(page, room.RoomId);
        });
        page._wtDiagnosticPanels[room.RoomId] = { details: details, body: body };
        var diagnostic = page._wtDiagnostics && page._wtDiagnostics[room.RoomId];
        if (diagnostic) {
            renderDiagnosticDetails(page, room.RoomId, diagnostic, body);
        }
        details.open = page._wtDiagnosticOpen[room.RoomId] === true;
        details.hidden = !details.open;
        return details;
    }

    function loadRoomDiagnostics(page, roomId, button) {
        if (!pageActionAllowed(page)) return;
        var panel = page._wtDiagnosticPanels && page._wtDiagnosticPanels[roomId];
        if (!panel || (page._wtDiagnosticBusy && page._wtDiagnosticBusy[roomId])) return;
        page._wtDiagnosticBusy = page._wtDiagnosticBusy || {};
        page._wtDiagnosticRequests = page._wtDiagnosticRequests || {};
        var requestToken = String(Date.now()) + ':' + String(Math.random());
        page._wtDiagnosticRequests[roomId] = requestToken;
        page._wtDiagnosticBusy[roomId] = true;
        panel.details.hidden = false;
        panel.details.open = true;
        clearChildren(panel.body);
        var loadingText = document.createElement('p');
        loadingText.className = 'fieldDescription';
        loadingText.textContent = t('正在读取诊断…');
        panel.body.appendChild(loadingText);
        setButtonBusy(button, true, t('读取中…'));
        return apiGet(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Diagnostics').then(function (raw) {
            var diagnostic = sanitizeDiagnostic(raw);
            if (!diagnostic) {
                throw { name: 'InvalidDiagnostic' };
            }
            if (page._wtDiagnosticRequests[roomId] !== requestToken ||
                !page._wtDiagnosticPanels[roomId]) {
                return;
            }
            page._wtDiagnostics = page._wtDiagnostics || {};
            page._wtDiagnostics[roomId] = diagnostic;
            page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
            page._wtDiagnosticOpen[roomId] = true;
            var currentPanel = page._wtDiagnosticPanels[roomId];
            renderDiagnosticDetails(page, roomId, diagnostic, currentPanel.body);
            currentPanel.details.hidden = false;
            currentPanel.details.open = true;
        }).catch(function (error) {
            if (page._wtDiagnosticRequests[roomId] !== requestToken ||
                !page._wtDiagnosticPanels[roomId]) {
                return;
            }
            var currentPanel = page._wtDiagnosticPanels[roomId];
            clearChildren(currentPanel.body);
            var errorText = document.createElement('p');
            errorText.className = 'wt-diagnosticError';
            errorText.setAttribute('role', 'alert');
            errorText.textContent = diagnosticErrorMessage(error);
            currentPanel.body.appendChild(errorText);
            currentPanel.details.hidden = false;
            currentPanel.details.open = true;
        }).finally(function () {
            if (page._wtDiagnosticRequests[roomId] === requestToken) {
                delete page._wtDiagnosticBusy[roomId];
                setButtonBusy(button, false);
            }
        });
    }

    function exportDiagnostic(page, roomId) {
        if (!pageActionAllowed(page)) return;
        var diagnostic = page._wtDiagnostics && page._wtDiagnostics[roomId];
        if (!diagnostic || typeof Blob === 'undefined' || !window.URL || !window.URL.createObjectURL) {
            setTransientStatus(page, t('当前浏览器不支持导出诊断 JSON。'), true);
            return;
        }
        var blob = new Blob([JSON.stringify(diagnostic, null, 2)], { type: 'application/json' });
        var url = window.URL.createObjectURL(blob);
        var link = document.createElement('a');
        link.href = url;
        link.download = 'watch-together-diagnostics-' + new Date().toISOString().replace(/[^0-9]/g, '').slice(0, 14) + '.json';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.setTimeout(function () {
            window.URL.revokeObjectURL(url);
        }, 1000);
    }

    function setStatus(page, text, isError) {
        if (page._wtStatusTimer) {
            clearTimeout(page._wtStatusTimer);
            page._wtStatusTimer = null;
        }
        var el = page.querySelector('#wtStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setFormHint(page, text, isError) {
        var el = page.querySelector('#wtFormHint');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setCreateStatus(page, text, isError) {
        var el = page.querySelector('#wtCreateStatus');
        if (el) {
            el.textContent = text || '';
            el.hidden = !text;
            el.classList.toggle('error', !!isError);
            el.setAttribute('role', isError ? 'alert' : 'status');
        }
    }

    function setInvitationStatus(page, text, isError) {
        var el = page.querySelector('#wtInvitationStatus');
        if (el) {
            el.textContent = text || '';
            el.setAttribute('role', isError ? 'alert' : 'status');
            el.classList.toggle('error', !!isError);
        }
    }

    function invitationDate(value) {
        var date = value ? new Date(value) : null;
        if (!date || isNaN(date.getTime())) {
            return t('有效期暂时无法显示');
        }
        try {
            return t('有效期至：') + date.toLocaleString();
        } catch (error) {
            return t('有效期至：') + date.toISOString();
        }
    }

    function renderInvitationCode(page) {
        var panel = page.querySelector('#wtInvitationCodePanel');
        var code = page.querySelector('#wtInvitationCode');
        var expires = page.querySelector('#wtInvitationExpires');
        var copy = page.querySelector('#wtCopyInvitationCode');
        var value = page._wtInvitationCode;
        if (!panel || !code || !expires) {
            return;
        }
        panel.hidden = !value;
        if (!value) {
            code.textContent = '';
            expires.textContent = '';
            return;
        }
        code.textContent = value.code;
        expires.textContent = invitationDate(value.expiresAtUtc);
        if (copy) {
            copy.disabled = false;
            copy.textContent = page._wtInvitationCopied ? t('已复制') : t('复制邀请码');
        }
    }

    function renderInvitations(page, invitations) {
        var container = page.querySelector('#wtInvitations');
        if (!container) {
            return;
        }
        clearChildren(container);
        var list = Array.isArray(invitations) ? invitations : [];
        container.hidden = list.length === 0;
        if (container.hidden) return;
        var title = document.createElement('h3');
        title.className = 'wt-subHeading';
        title.textContent = t('我创建的邀请码');
        container.appendChild(title);
        list.forEach(function (invitation) {
            var item = document.createElement('div');
            item.className = 'wt-roomMeta';
            var name = document.createElement('div');
            name.textContent = invitation.Name || t('接受邀请后生成房间名');
            var expiry = document.createElement('div');
            expiry.textContent = invitationDate(invitation.ExpiresAtUtc);
            var revoke = document.createElement('button', { is: 'emby-button' });
            revoke.type = 'button';
            revoke.setAttribute('is', 'emby-button');
            revoke.classList.add('raised', 'raised-outline', 'wt-action', 'wt-action--danger');
            revoke.textContent = t('撤销邀请码');
            revoke.addEventListener('click', function () {
                revokeInvitation(page, invitation, revoke);
            });
            item.appendChild(name);
            item.appendChild(expiry);
            item.appendChild(revoke);
            container.appendChild(item);
        });
    }

    function loadInvitations(page) {
        var request = {};
        page._wtInvitationListRequest = request;
        function update(list, failed) {
            if (page._wtInvitationListRequest !== request) return [];
            page._wtInvitations = list;
            renderInvitations(page, list);
            var status = page.querySelector('#wtInvitationListStatus');
            if (status) {
                status.hidden = !failed;
                status.textContent = failed ? t('邀请码列表暂时无法加载，请稍后重试。') : '';
            }
            return list;
        }
        return apiGet(page, 'WatchTogether/Invitations').then(function (list) {
            return Array.isArray(list) ? update(list, false) : update([], true);
        }).catch(function () {
            return update([], true);
        });
    }

    function createInvitation(page) {
        if (!pageActionAllowed(page)) return;
        var input = page.querySelector('#wtInvitationName');
        var button = page.querySelector('#wtCreateInvitation');
        var rawName = input ? input.value : '';
        var name = rawName.trim() ? rawName : '';
        setButtonBusy(button, true, t('创建中…'));
        setInvitationStatus(page, t('正在创建邀请码…'), false);
        apiSend(page, 'WatchTogether/Invitations', 'POST', { Name: name || null }).then(function (result) {
            result = result || {};
            if (result.Status === 'creator_already_in_room') {
                setInvitationStatus(page, t('你已属于一个房间，请先结束当前房间后再创建邀请码。'), true);
                return null;
            }
            if (result.Status === 'invitation_unavailable') {
                setInvitationStatus(page, t('当前邀请码数量已达上限，请稍后再试。'), true);
                return null;
            }
            if (result.Created !== true || !result.Code || result.Status !== 'created') {
                throw new Error('invitation_unavailable');
            }
            page._wtInvitationCode = {
                id: result.InvitationId,
                code: String(result.Code),
                expiresAtUtc: result.ExpiresAtUtc
            };
            page._wtInvitationCopied = false;
            if (input) {
                input.value = '';
            }
            renderInvitationCode(page);
            setInvitationStatus(page, '', false);
            notifyToast(page, t('邀请码已创建，请立即复制并发送给对方。'));
            return loadInvitations(page);
        }).catch(function (error) {
            setInvitationStatus(page, error && error.message === 'invitation_unavailable'
                ? t('当前邀请码数量已达上限，请稍后再试。')
                : t('邀请码创建失败，请稍后重试。'), true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function copyInvitationCode(page, button) {
        if (!pageActionAllowed(page)) return;
        var invitation = page._wtInvitationCode;
        if (!invitation || !invitation.code) {
            setInvitationStatus(page, t('当前没有可复制的邀请码。'), true);
            return;
        }
        var copied = Promise.resolve().then(function () {
            if (!window.navigator || !window.navigator.clipboard || !window.navigator.clipboard.writeText) {
                throw new Error('clipboard_unavailable');
            }
            return window.navigator.clipboard.writeText(invitation.code);
        });
        scopedPromise(page, copied).then(function () {
            if (page._wtInvitationCode !== invitation) return;
            page._wtInvitationCopied = true;
            renderInvitationCode(page);
            setInvitationStatus(page, '', false);
            notifyToast(page, t('邀请码已复制。'));
        }).catch(function () {
            if (page._wtInvitationCode !== invitation) return;
            page._wtInvitationCopied = false;
            if (button) {
                button.textContent = t('复制邀请码');
            }
            setInvitationStatus(page, t('复制失败，请选中邀请码手动复制。'), true);
        });
    }

    function revokeInvitation(page, invitation, button) {
        if (!pageActionAllowed(page)) return;
        if (!invitation || !invitation.InvitationId) {
            return;
        }
        setButtonBusy(button, true, t('撤销中…'));
        setInvitationStatus(page, '', false);
        apiSend(page, 'WatchTogether/Invitations/' + encodeURIComponent(invitation.InvitationId), 'DELETE').then(function (result) {
            if (!result || typeof result.Deleted !== 'boolean') throw new Error('invalid_invitation_result');
            if (page._wtInvitationCode && page._wtInvitationCode.id === invitation.InvitationId) {
                page._wtInvitationCode = null;
                page._wtInvitationCopied = false;
                renderInvitationCode(page);
            }
            if (result && result.Deleted) notifyToast(page, t('邀请码已撤销。'));
            else setInvitationStatus(page, t('邀请码不存在或已失效。'), true);
            return loadInvitations(page);
        }).catch(function (error) {
            setInvitationStatus(page, t('邀请码撤销失败：') + errorMessage(error), true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function invitationAcceptMessage(status) {
        return {
            accepted: t('房间已创建，请双方打开同一视频。'),
            invalid_or_expired: t('邀请码无效或已过期，请向对方索取新邀请码。'),
            creator_cannot_accept: t('不能接受自己创建的邀请码。'),
            rate_limited: t('尝试次数过多，请稍后再试。'),
            room_unavailable: t('暂时无法创建房间，请稍后重试。')
        }[status] || t('邀请码未能接受，请稍后重试。');
    }

    function setAcceptInvitationError(page, text) {
        var message = page.querySelector('#wtInvitationAcceptError');
        if (!message) return;
        message.hidden = !text;
        message.textContent = text;
    }

    function acceptInvitation(page) {
        if (!pageActionAllowed(page)) return;
        var input = page.querySelector('#wtInvitationAcceptCode');
        var button = page.querySelector('#wtAcceptInvitation');
        var code = input ? input.value.trim() : '';
        setAcceptInvitationError(page, '');
        if (!code) {
            notifyToast(page, t('请输入邀请码。'), function (text) {
                if (input && !input.value.trim()) setAcceptInvitationError(page, text);
            });
            if (input) {
                input.focus();
            }
            return;
        }
        setButtonBusy(button, true, t('接受中…'));
        setInvitationStatus(page, t('正在接受邀请…'), false);
        apiSend(page, 'WatchTogether/Invitations/' + encodeURIComponent(code) + '/Accept', 'POST', { Language: locale }).then(function (result) {
            result = result || {};
            if (result.Status === 'accepted') {
                setInvitationStatus(page, '', false);
                notifyToast(page, invitationAcceptMessage(result.Status));
                if (input) {
                    input.value = '';
                }
                return Promise.all([loadRooms(page), loadInvitations(page)]);
            }
            setInvitationStatus(page, invitationAcceptMessage(result.Status), true);
            return null;
        }).catch(function (error) {
            setInvitationStatus(page, t('邀请接受失败：') + errorMessage(error), true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function setConfigStatus(page, text, isError) {
        var el = page.querySelector('#wtConfigStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setGitHubTokenStatus(page, text, isError) {
        var el = page.querySelector('#wtGitHubTokenStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setGitHubTokenBusy(page, isBusy, operation) {
        var input = page.querySelector('#wtGitHubToken');
        var saveButton = page.querySelector('#wtSaveGitHubToken');
        var clearButton = page.querySelector('#wtClearGitHubToken');
        operation = operation || '';
        if (input) {
            input.disabled = isBusy;
        }
        if (saveButton) {
            saveButton.disabled = isBusy || !page._wtGitHubTokenReady;
            saveButton.setAttribute('aria-busy', isBusy && operation === 'save' ? 'true' : 'false');
            saveButton.textContent = isBusy && operation === 'save' ? t('保存中…') : t('保存 Token');
        }
        if (clearButton) {
            clearButton.disabled = isBusy || !page._wtGitHubTokenCanClear;
            clearButton.setAttribute('aria-busy', isBusy && operation === 'clear' ? 'true' : 'false');
            clearButton.textContent = isBusy && operation === 'clear' ? t('清除中…') : t('清除 Token');
        }
    }

    function clearGitHubTokenInput(page) {
        var input = page.querySelector('#wtGitHubToken');
        if (input) {
            input.value = '';
        }
    }

    function loadGitHubTokenStatus(page) {
        page._wtGitHubTokenReady = false;
        page._wtGitHubTokenCanClear = false;
        clearGitHubTokenInput(page);
        setGitHubTokenStatus(page, t('正在读取 Token 状态…'));
        setGitHubTokenBusy(page, true, 'read');
        return apiGet(page, 'WatchTogether/GitHubToken').then(function (result) {
            page._wtGitHubTokenReady = true;
            page._wtGitHubTokenCanClear = true;
            setGitHubTokenStatus(page, result && result.Configured ? t('Token 已配置。') : t('尚未配置 Token。'));
        }).catch(function (error) {
            page._wtGitHubTokenReady = false;
            page._wtGitHubTokenCanClear = !isPermissionError(error);
            setGitHubTokenStatus(page, isPermissionError(error)
                ? t('只有管理员可以查看和修改 Token。')
                : t('Token 状态读取失败，可尝试清除后重新配置。'), true);
        }).then(function () {
            setGitHubTokenBusy(page, false);
        });
    }

    function saveGitHubToken(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        var input = page.querySelector('#wtGitHubToken');
        var value = input ? String(input.value || '').trim() : '';
        if (!value) {
            setGitHubTokenStatus(page, t('请输入 GitHub Token。'), true);
            if (input) {
                input.focus();
            }
            return Promise.resolve();
        }

        setGitHubTokenBusy(page, true, 'save');
        setGitHubTokenStatus(page, t('正在保存 Token…'));
        return apiSend(page, 'WatchTogether/GitHubToken', 'POST', { Token: value }).then(function (result) {
            clearGitHubTokenInput(page);
            setGitHubTokenStatus(page, result && result.Configured ? t('Token 已配置。') : t('Token 未配置。'));
            if (result && result.Configured) notifyToast(page, t('Token 已保存'));
        }).catch(function (error) {
            setGitHubTokenStatus(page,
                isPermissionError(error) ? t('保存被拒绝：只有管理员可以修改 Token。') : t('Token 保存失败，请稍后重试。'), true);
        }).then(function () {
            clearGitHubTokenInput(page);
            setGitHubTokenBusy(page, false);
        });
    }

    function clearGitHubToken(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        return confirmAction(page, { title: t('清除 Token'),
            text: t('清除 Token？beta 查询将改用匿名 API。'),
            confirmText: t('清除') }, null, function () { clearGitHubTokenConfirmed(page); });
    }

    function clearGitHubTokenConfirmed(page) {
        if (!pageActionAllowed(page) || page._wtIsAdmin !== true) return;
        setGitHubTokenBusy(page, true, 'clear');
        setGitHubTokenStatus(page, t('正在清除 Token…'));
        return apiSend(page, 'WatchTogether/GitHubToken', 'DELETE').then(function (result) {
            clearGitHubTokenInput(page);
            if (result && result.Configured === false) {
                page._wtGitHubTokenReady = true;
                page._wtGitHubTokenCanClear = true;
            }
            setGitHubTokenStatus(page, result && result.Configured ? t('Token 仍已配置。') : t('尚未配置 Token。'));
            if (result && result.Configured === false) notifyToast(page, t('Token 已清除'));
        }).catch(function (error) {
            setGitHubTokenStatus(page,
                isPermissionError(error) ? t('清除被拒绝：只有管理员可以修改 Token。') : t('Token 清除失败，请稍后重试。'), true);
        }).then(function () {
            clearGitHubTokenInput(page);
            setGitHubTokenBusy(page, false);
        });
    }

    function isPermissionError(error) {
        return !!(error && (error.status === 401 || error.status === 403 ||
            error.statusCode === 401 || error.statusCode === 403));
    }

    var routePageNames = {
        rooms: 'WatchTogetherRoomsV35',
        invite: 'WatchTogetherInviteV35',
        settings: 'WatchTogetherSettingsV35',
        admin: 'WatchTogetherAdminV35'
    };

    function currentRoute(page) {
        var href = String((page.ownerDocument.defaultView || window).location.href || '');
        if (href.indexOf(routePageNames.admin) >= 0) return 'admin';
        if (href.indexOf(routePageNames.settings) >= 0) return 'settings';
        if (href.indexOf(routePageNames.invite) >= 0) return 'invite';
        return 'rooms';
    }

    function configurationPageUrl(page, name) {
        // Match pluginManager's relative href. emby-linkbutton routes the anchor
        // pathname; a hash href incorrectly includes index.html in that route.
        var identity = page._wtIdentity || pageIdentity();
        var href = 'configurationpage?name=' + encodeURIComponent(name);
        if (identity && identity.user) href += '&userId=' + encodeURIComponent(identity.user);
        if (identity && identity.serverId) href += '&serverId=' + encodeURIComponent(identity.serverId);
        return href;
    }

    function nativeTabs(page) {
        var tabs = [
            { href: configurationPageUrl(page, routePageNames.rooms), name: t('房间') },
            { href: configurationPageUrl(page, routePageNames.invite), name: page._wtIsAdmin === true ? t('创建与邀请') : t('邀请') }
        ];
        if (page._wtIsAdmin === true) {
            tabs.push({ href: configurationPageUrl(page, routePageNames.admin), name: t('后台管理') });
            tabs.push({ href: configurationPageUrl(page, routePageNames.settings), name: t('设置') });
        }
        return tabs;
    }

    function ownsNativeTabs(page) {
        return !!(page._wtNativeTabsElement && mainTabsManager &&
            typeof mainTabsManager.getTabsElement === 'function' &&
            mainTabsManager.getTabsElement() === page._wtNativeTabsElement);
    }

    function releaseNativeTabs(page) {
        try {
            // The host replaces the actual tabs element when another view owns it.
            // Never clear a newer owner's tabs from this view's pause or reset.
            if (ownsNativeTabs(page)) mainTabsManager.setTabs(null);
        } catch (error) {
            // A missing host header still has the route-link fallback.
        }
        page._wtNativeTabsElement = null;
        page._wtNativeTabsKey = null;
    }

    function applyRoute(page) {
        var route = page._wtRoute || 'rooms';
        if ((route === 'settings' || route === 'admin') && page._wtIsAdmin !== true) route = 'rooms';
        page._wtVisibleRoute = route;
        page.classList.toggle('wt-page--rooms', route === 'rooms');
        page.classList.toggle('wt-page--admin', route === 'admin');
        page.querySelector('#wtRoomsHeading').textContent = route === 'admin' ? t('后台管理') : t('我的房间');
        var inviteLayout = page.querySelector('#wtInviteLayout');
        inviteLayout.hidden = route !== 'invite';
        inviteLayout.classList.toggle('wt-pageColumns--single', page._wtIsAdmin !== true);
        page.querySelector('#wtInviteHeading').textContent = page._wtIsAdmin === true ? t('创建与邀请') : t('邀请');
        page.querySelector('#wtRoomsSection').hidden = route !== 'rooms' && route !== 'admin';
        page.querySelector('#wtHelpSection').hidden = route !== 'rooms';
        page.querySelector('#wtInvitationSection').hidden = route !== 'invite';
        page.querySelector('#wtAdminSection').hidden = route !== 'invite' || page._wtIsAdmin !== true;
        page.querySelector('#wtSettingsSection').hidden = route !== 'settings' || page._wtIsAdmin !== true;
        var fallback = page.querySelector('#wtNavigation');
        if (!fallback) return;
        clearChildren(fallback);
        var tabs = nativeTabs(page);
        tabs.forEach(function (tab) {
            var link = document.createElement('a');
            link.href = tab.href;
            link.textContent = tab.name;
            if (tab.href.indexOf(routePageNames[route]) >= 0) link.setAttribute('aria-current', 'page');
            fallback.appendChild(link);
        });
        fallback.hidden = false;
        var key = route + ':' + String(page._wtIsAdmin);
        fallback.hidden = page._wtNativeTabsKey === key && ownsNativeTabs(page);
        if (!page._wtActive || !page._wtTabsPermissionReady || fallback.hidden) return;
        if (mainTabsManager && typeof mainTabsManager.setTabs === 'function') {
            try {
                // setTabs with the same owner only selects a tab. Rebuild our
                // own element when the resolved permission changes the list.
                releaseNativeTabs(page);
                var selected = route === 'invite' ? 1 : route === 'admin' ? 2 : route === 'settings' ? 3 : 0;
                var result = mainTabsManager.setTabs(page, selected, function () { return nativeTabs(page); });
                if (result && result.replaced === false && typeof mainTabsManager.selectedTabIndex === 'function') {
                    mainTabsManager.selectedTabIndex(selected);
                }
                page._wtNativeTabsKey = key;
                page._wtNativeTabsElement = typeof mainTabsManager.getTabsElement === 'function'
                    ? mainTabsManager.getTabsElement() : null;
                // Without a host header the route links keep all permitted pages reachable.
                fallback.hidden = ownsNativeTabs(page);
            } catch (error) {
                fallback.hidden = false;
            }
        }
    }

    function setAdminVisibility(page, isAdmin) {
        var adminSection = page.querySelector('#wtAdminSection');
        var settingsSection = page.querySelector('#wtSettingsSection');
        var roomsHeading = page.querySelector('#wtRoomsHeading');
        var helpSteps = [
            page.querySelector('#wtHelpStep1'),
            page.querySelector('#wtHelpStep2'),
            page.querySelector('#wtHelpStep3')
        ];
        if (adminSection) {
            adminSection.style.display = isAdmin ? '' : 'none';
        }
        if (settingsSection) {
            settingsSection.style.display = isAdmin ? '' : 'none';
        }
        if (roomsHeading) {
            roomsHeading.textContent = page._wtVisibleRoute === 'admin' ? t('后台管理') : t('我的房间');
        }
        var helpText = isAdmin ? [
            t('创建房间并选择两名参与者。'),
            t('两人分别登录 Emby，打开同一视频。'),
            t('看到“同步中”后即可一起观看。')
        ] : [
            t('加入房间后，与另一位参与者打开同一视频。'),
            t('播放、暂停和进度会自动同步。'),
            t('需要重新对齐时，点击“重新同步”。')
        ];
        applyRoute(page);
        helpSteps.forEach(function (step, index) {
            if (step) {
                step.textContent = helpText[index];
            }
        });
    }

    function clearChildren(element) {
        while (element && element.firstChild) {
            element.removeChild(element.firstChild);
        }
    }

    function setConfigBusy(page, isBusy) {
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        var saveButton = page.querySelector('#wtSaveConfig');
        if (pauseCheckbox) {
            pauseCheckbox.disabled = isBusy;
        }
        if (notifyCheckbox) {
            notifyCheckbox.disabled = isBusy;
        }
        if (syncNotifyCheckbox) {
            syncNotifyCheckbox.disabled = isBusy;
        }
        if (updateChannel) {
            updateChannel.disabled = isBusy;
        }
        if (saveButton) {
            saveButton.disabled = isBusy || !page._wtConfigReady;
            saveButton.setAttribute('aria-busy', isBusy ? 'true' : 'false');
            saveButton.textContent = isBusy ? t('保存中…') : t('保存设置');
        }
    }

    function applyPluginConfiguration(page, config) {
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        page._wtPluginConfiguration = config || {};
        page._wtConfigReady = true;
        if (pauseCheckbox) {
            pauseCheckbox.checked = page._wtPluginConfiguration.PauseOtherOnPlaybackStop !== false;
            pauseCheckbox.disabled = false;
        }
        if (notifyCheckbox) {
            notifyCheckbox.checked = page._wtPluginConfiguration.NotifyOtherOnPlaybackStop !== false;
            notifyCheckbox.disabled = false;
        }
        if (syncNotifyCheckbox) {
            syncNotifyCheckbox.checked = page._wtPluginConfiguration.NotifyOnSyncActions !== false;
            syncNotifyCheckbox.disabled = false;
        }
        if (updateChannel) {
            updateChannel.value = page._wtPluginConfiguration.UpdateChannel === 'beta' ? 'beta' : 'stable';
            updateChannel.disabled = false;
        }
        var saveButton = page.querySelector('#wtSaveConfig');
        if (saveButton) {
            saveButton.disabled = false;
        }
    }

    function loadPluginConfiguration(page) {
        setConfigStatus(page, t('正在读取配置…'));
        setConfigBusy(page, true);
        return pageRequest(page, 'config:read', function () { return ApiClient.getPluginConfiguration(pluginId); }).then(function (config) {
            applyPluginConfiguration(page, config);
            setConfigStatus(page, '');
            return loadGitHubTokenStatus(page).then(function () {
                return config;
            });
        }).catch(function (error) {
            page._wtConfigReady = false;
            var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
            var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
            var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
            var updateChannel = page.querySelector('#wtUpdateChannel');
            var saveButton = page.querySelector('#wtSaveConfig');
            if (pauseCheckbox) {
                pauseCheckbox.disabled = true;
            }
            if (notifyCheckbox) {
                notifyCheckbox.disabled = true;
            }
            if (syncNotifyCheckbox) {
                syncNotifyCheckbox.disabled = true;
            }
            if (updateChannel) {
                updateChannel.disabled = true;
            }
            if (saveButton) {
                saveButton.disabled = true;
            }
            if (isPermissionError(error)) {
                setAdminVisibility(page, false);
                setConfigStatus(page, t('只有管理员可以查看和修改此设置。'), true);
            } else {
                setConfigStatus(page, t('配置读取失败：') + errorMessage(error), true);
            }
            throw error;
        }).finally(function () {
            if (page._wtConfigReady) {
                setConfigBusy(page, false);
            }
        });
    }

    function savePluginConfiguration(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true || page.querySelector('#wtSaveConfig').getAttribute('aria-busy') === 'true') return;
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        if (!pauseCheckbox || !notifyCheckbox || !syncNotifyCheckbox || !updateChannel || !page._wtConfigReady) {
            return Promise.resolve();
        }

        var saveStage = 'read';
        setConfigBusy(page, true);
        setConfigStatus(page, '');
        return pageRequest(page, 'config:read', function () { return ApiClient.getPluginConfiguration(pluginId); }).then(function (config) {
            config = config || {};
            config.PauseOtherOnPlaybackStop = pauseCheckbox.checked;
            config.NotifyOtherOnPlaybackStop = notifyCheckbox.checked;
            config.NotifyOnSyncActions = syncNotifyCheckbox.checked;
            config.UpdateChannel = updateChannel.value === 'beta' ? 'beta' : 'stable';
            saveStage = 'submit';
            return pageRequest(page, 'config:save', function () { return ApiClient.updatePluginConfiguration(pluginId, config); });
        }).then(function () {
            saveStage = 'readback';
            return pageRequest(page, 'config:read', function () { return ApiClient.getPluginConfiguration(pluginId); });
        }).then(function (config) {
            applyPluginConfiguration(page, config);
            setConfigStatus(page, '');
            notifyToast(page, t('配置已保存'));
        }).catch(function (error) {
            var message;
            if (saveStage === 'read') {
                message = t('保存前读取配置失败，尚未提交修改：') + errorMessage(error);
            } else if (saveStage === 'readback') {
                message = t('配置已提交，但暂时无法确认，请重新读取检查。');
            } else {
                message = isPermissionError(error)
                    ? t('配置提交被拒绝，请确认管理员权限后重新读取检查。')
                    : t('配置提交请求失败，结果尚未确认，请重新读取检查。');
            }
            setConfigStatus(page, message, true);
        }).then(function () {
            setConfigBusy(page, false);
        });
    }

    function loadPluginInfo(page) {
        return apiGet(page, 'WatchTogether/Info').then(function (info) {
            info = info || {};
            var version = page.querySelector('#wtPluginVersion');
            if (version) {
                version.textContent = info.CurrentVersion || '—';
            }
            var link = page.querySelector('#wtRepositoryLink');
            if (link && info.RepositoryUrl) {
                link.href = info.RepositoryUrl;
            }
        }).catch(function () {
            var version = page.querySelector('#wtPluginVersion');
            if (version) {
                version.textContent = '—';
            }
        });
    }

    function findUser(page, id) {
        return page._wtUsers.filter(function (user) {
            return user.Id === id;
        })[0] || null;
    }

    function userName(page, id) {
        var user = findUser(page, id);
        return user ? user.Name : (id || t('未知用户'));
    }

    function roomName(room) {
        return room && (room.Name || room.RoomId) || t('未命名房间');
    }

    function roomParticipant(room, id) {
        var participants = room && Array.isArray(room.Participants) ? room.Participants : [];
        var normalizedId = String(id || '').toLowerCase();
        return participants.filter(function (participant) {
            return participant && String(participant.Id || '').toLowerCase() === normalizedId;
        })[0] || null;
    }

    function roomUserName(page, room, id) {
        var participant = roomParticipant(room, id);
        if (participant && participant.Name) {
            return participant.Name;
        }
        var user = findUser(page, id);
        return user && user.Name ? user.Name : t('未知用户');
    }

    function findRoomForUser(page, userId) {
        var rooms = Array.isArray(page._wtRooms) ? page._wtRooms : [];
        var normalizedId = String(userId || '').toLowerCase();
        return rooms.filter(function (room) {
            return (room.ParticipantUserIds || []).some(function (id) {
                return String(id).toLowerCase() === normalizedId;
            });
        })[0] || null;
    }

    function findRoomConflict(page, userIds) {
        for (var i = 0; i < userIds.length; i++) {
            var room = findRoomForUser(page, userIds[i]);
            if (room) {
                return {
                    userId: userIds[i],
                    room: room
                };
            }
        }
        return null;
    }

    function conflictMessage(page, conflict) {
        return t('用户“') + userName(page, conflict.userId) + t('”已在房间“') + roomName(conflict.room) +
            t('”中，请先结束原房间，或者选择其他用户。暂离仍会保留成员关系。');
    }

    function fillSelect(select, options, selected) {
        if (!select) {
            return;
        }

        var unchanged = select.options.length === options.length && options.every(function (user, index) {
            var option = select.options[index];
            return option.value === user.Id && option.textContent === user.Name;
        });
        // Polls and name edits must not replace options under an open native menu.
        if (!unchanged) {
            clearChildren(select);
            options.forEach(function (user) {
                var opt = document.createElement('option');
                opt.value = user.Id;
                opt.textContent = user.Name;
                select.appendChild(opt);
            });
        }

        var selectedExists = options.some(function (user) {
            return user.Id === selected;
        });
        if (selectedExists && select.value !== selected) {
            select.value = selected;
        } else if (!selectedExists && options.length > 0 && select.selectedIndex !== 0) {
            select.selectedIndex = 0;
        }
    }

    function syncPrimarySelect(page) {
        var participantA = page.querySelector('#wtUserA');
        var participantB = page.querySelector('#wtUserB');
        var primary = page.querySelector('#wtPrimary');
        var selectedPrimary = primary ? primary.value : '';
        var ids = [];

        [participantA && participantA.value, participantB && participantB.value].forEach(function (id) {
            if (id && ids.indexOf(id) === -1) {
                ids.push(id);
            }
        });

        fillSelect(primary, ids.map(function (id) { return findUser(page, id); }).filter(function (user) {
            return !!user;
        }), selectedPrimary);
    }

    function syncForm(page) {
        syncPrimarySelect(page);
        var ids = ['wtRoomName', 'wtUserA', 'wtUserB', 'wtPrimary'];
        var fields = ids.map(function (id) { return page.querySelector('#' + id); });
        var a = fields[1].value, b = fields[2].value;
        var errors = {};
        if (!a) errors.wtUserA = t('请选择参与者。');
        if (!b) errors.wtUserB = t('请选择参与者。');
        if (a && a === b) errors.wtUserB = t('两名参与者必须不同。');
        var conflict = findRoomConflict(page, [a, b]);
        if (conflict) errors[conflict.userId === a ? 'wtUserA' : 'wtUserB'] = conflictMessage(page, conflict);
        if (!fields[3].value || (fields[3].value !== a && fields[3].value !== b)) errors.wtPrimary = t('请选择参与者中的一人为主用户。');
        var touched = page._wtCreateTouched || {};
        ids.forEach(function (id, i) {
            var message = (page._wtCreateSubmitted || touched[id]) ? errors[id] || '' : '';
            var hint = page.querySelector('#' + id + 'Error');
            hint.textContent = message;
            hint.hidden = !message;
            fields[i].setAttribute('aria-invalid', message ? 'true' : 'false');
        });
        var unavailable = page._wtIsAdmin !== true || !page._wtUsers || page._wtUsers.length < 2 || !Array.isArray(page._wtRooms);
        var button = page.querySelector('#wtCreate');
        button.disabled = unavailable || button.getAttribute('aria-busy') === 'true';
        setFormHint(page, unavailable ? t('用户或房间数据尚不可用，暂时不能创建房间。') : '', unavailable);
        return { errors: errors, unavailable: unavailable, fields: fields, ids: ids };
    }

    function loadUsers(page) {
        var roleLoaded = false;
        return pageRequest(page, 'current-user:role', function () {
            return ApiClient.getCurrentUser({ enableCache: false });
        }).then(function (user) {
            if (!user || String(user.Id || '') !== page._wtIdentity.user ||
                !user.Policy || typeof user.Policy.IsAdministrator !== 'boolean') {
                throw new Error(t('当前用户角色信息不完整，请重新打开页面。'));
            }
            roleLoaded = true;
            if (user.Policy.IsAdministrator) return apiGet(page, 'WatchTogether/Users');
            page._wtIsAdmin = false;
            page._wtTabsPermissionReady = true;
            page._wtRooms = null;
            page._wtUsers = [];
            fillSelect(page.querySelector('#wtUserA'), [], null);
            fillSelect(page.querySelector('#wtUserB'), [], null);
            syncForm(page);
            setAdminVisibility(page, false);
            return null;
        }).then(function (list) {
            if (list === null) return;
            page._wtIsAdmin = true;
            page._wtTabsPermissionReady = true;
            setAdminVisibility(page, true);
            page._wtUsers = Array.isArray(list) ? list : [];
            page._wtRooms = null;
            fillSelect(page.querySelector('#wtUserA'), page._wtUsers, page._wtUsers.length > 0 ? page._wtUsers[0].Id : null);
            fillSelect(page.querySelector('#wtUserB'), page._wtUsers, page._wtUsers.length > 1 ? page._wtUsers[1].Id : null);
            syncForm(page);
        }).catch(function (err) {
            page._wtIsAdmin = roleLoaded && isPermissionError(err) ? false : undefined;
            page._wtTabsPermissionReady = true;
            page._wtRooms = null;
            page._wtUsers = [];
            fillSelect(page.querySelector('#wtUserA'), [], null);
            fillSelect(page.querySelector('#wtUserB'), [], null);
            syncForm(page);
            setAdminVisibility(page, false);
            if (!roleLoaded) {
                setFormHint(page, t('当前用户权限暂时无法确认，请重新打开页面。'), true);
                setStatus(page, t('当前用户权限读取失败：') + errorMessage(err), true);
            } else if (isPermissionError(err)) {
                setFormHint(page, t('只有管理员可以创建房间。'), true);
                setStatus(page, '');
            } else {
                setFormHint(page, t('用户列表暂时无法加载，请稍后重试。'), true);
                setStatus(page, t('用户列表加载失败：') + errorMessage(err), true);
            }
        });
    }

    function getStateInfo(state) {
        var knownState = stateLabels[state] ? state : 'Unavailable';
        return {
            label: stateLabels[knownState],
            description: stateDescriptions[knownState],
            className: knownState.toLowerCase()
        };
    }

    var statusReasonMessages = {
        get server_unavailable() { return t('房间所属服务器暂不可用，请确认当前服务器连接。'); },
        get snapshot_unavailable() { return t('暂时无法读取播放会话，自动同步已进入保护状态；恢复后会重新对齐。'); },
        get different_video() { return t('两位参与者打开了不同视频，请打开同一视频。'); },
        get playback_stopped() { return t('播放已停止，请双方重新打开同一视频。'); },
        get stop_resume_confirmation_required() { return t('双方已打开同一视频，但停止后的进度差较大。如需以主用户当前进度恢复，请使用房间内的重新同步操作。'); },
        get command_failed() { return t('播放控制未完成，请检查两位参与者的客户端。'); },
        get member_left() { return t('仍有参与者未加入，请先让双方加入房间。'); },
        get aligning() { return t('正在对齐播放位置，请稍候。'); },
        get watching() { return t('两位参与者已连接，播放会自动同步。'); },
        get remote_control_unavailable() { return t('当前客户端不支持远程控制，请更换或更新客户端。'); },
        get media_mismatch() { return t('两位参与者的媒体信息不一致，请打开同一视频。'); },
        get media_handoff() { return t('正在让另一位参与者打开主用户当前视频，请稍候。'); },
        get handoff_failed() { return t('换片等待或对齐未完成，已停止自动恢复。请确认双方播放状态后使用重新同步。'); },
        get unsupported_playback_rate() { return t('播放速度不是 1 倍，请恢复正常速度后重试。'); },
        get waiting_for_playback() { return t('等待双方打开同一视频并开始播放。'); },
        get transient_recovery() { return t('正在等待临时掉线的参与者恢复。'); }
    };

    function statusReasonMessage(reason) {
        return statusReasonMessages[reason] || t('当前房间状态需要检查，请稍后重试。');
    }

    function roomFeedback(page, roomId, text, isError, persistent) {
        page._wtRoomFeedback = page._wtRoomFeedback || {};
        page._wtRoomFeedbackTimers = page._wtRoomFeedbackTimers || {};
        if (page._wtRoomFeedbackTimers[roomId]) {
            clearTimeout(page._wtRoomFeedbackTimers[roomId]);
            delete page._wtRoomFeedbackTimers[roomId];
        }
        var token = String(Date.now()) + ':' + String(Math.random());
        page._wtRoomFeedback[roomId] = { text: text, isError: !!isError, token: token };
        if (!isError && !persistent) {
            page._wtRoomFeedbackTimers[roomId] = setTimeout(function () {
                if (!pageActionAllowed(page)) return;
                if (page._wtRoomFeedback[roomId] && page._wtRoomFeedback[roomId].token === token) {
                    delete page._wtRoomFeedback[roomId];
                    renderRooms(page, page._wtRooms || []);
                }
                delete page._wtRoomFeedbackTimers[roomId];
            }, 8000);
        }
    }

    function clearRoomFeedback(page, roomId) {
        if (page._wtRoomFeedbackTimers && page._wtRoomFeedbackTimers[roomId]) {
            clearTimeout(page._wtRoomFeedbackTimers[roomId]);
            delete page._wtRoomFeedbackTimers[roomId];
        }
        if (page._wtRoomFeedback) {
            delete page._wtRoomFeedback[roomId];
        }
    }

    function clearAllRoomFeedback(page) {
        if (page._wtRoomFeedbackTimers) {
            Object.keys(page._wtRoomFeedbackTimers).forEach(function (roomId) {
                clearTimeout(page._wtRoomFeedbackTimers[roomId]);
            });
        }
        page._wtRoomFeedbackTimers = {};
        page._wtRoomFeedback = {};
    }

    // Notifications never participate in the mutation promise chain. Host failures
    // cannot turn a completed operation into a failed request or retry it.
    function notifyToast(page, text, onUnavailable) {
        var generation = page._wtGeneration;
        var route = page._wtVisibleRoute;
        function current() {
            return pageActionAllowed(page) && page._wtGeneration === generation && page._wtVisibleRoute === route;
        }
        function fallback() {
            if (!current()) return;
            if (onUnavailable) { onUnavailable(text); return; }
            var region = page.querySelector('#wtSuccessFallback');
            if (!region) return;
            var message = document.createElement('p');
            message.className = 'fieldDescription';
            message.textContent = text;
            region.appendChild(message);
        }
        Promise.resolve().then(function () {
            if (!current()) return;
            return require(['toast']).then(function (modules) {
                if (!current()) return;
                var toast = modules && modules[0];
                toast = toast && (toast.default || toast);
                if (typeof toast !== 'function') throw new Error('Toast unavailable');
                // Host toast renders HTML. Only fixed, trusted messages are passed.
                return toast({ text: text });
            });
        }).catch(fallback);
    }

    function setTransientStatus(page, text, isError) {
        setStatus(page, text, isError);
        page._wtStatusTimer = setTimeout(function () {
            if (!pageActionAllowed(page)) return;
            page._wtStatusTimer = null;
            var el = page.querySelector('#wtStatus');
            if (el) {
                el.textContent = '';
                el.classList.remove('error');
            }
        }, 8000);
    }

    function setRoomBusy(page, roomId, busy) {
        page._wtRoomBusy = page._wtRoomBusy || {};
        if (busy) {
            page._wtRoomBusy[roomId] = true;
        } else {
            delete page._wtRoomBusy[roomId];
        }
    }

    function createActionButton(page, room, action) {
        var button = document.createElement('button', { is: 'emby-button' });
        button.type = 'button';
        button.setAttribute('is', 'emby-button');
        var toneClass = action === 'join'
            ? 'wt-action--primary'
            : action === 'leave' || action === 'delete'
                ? 'wt-action--danger'
                : action === 'resync' || action === 'participantResync'
                    ? 'wt-action--accent'
                    : '';
        button.classList.add('raised', 'wt-action');
        if (action === 'join') button.classList.add('button-submit');
        else button.classList.add('raised-outline');
        if (toneClass) button.classList.add(toneClass);
        button.dataset.act = action;
        button.dataset.action = action;
        button.textContent = action === 'delete' ? (room.CanEnd ? t('结束房间') : t('删除房间')) : action === 'leave' ? t('暂离房间') : action === 'join' ? t('加入房间') : action === 'diagnostics' ? t('查看诊断') : actionLabels[action];
        button.title = action === 'delete'
            ? (room.CanEnd ? t('解除双方成员关系，不删除媒体') : t('仅移除同播关系，不删除媒体'))
            : action === 'leave'
                ? t('可重新加入，并尝试暂停对方')
                : action === 'join'
                    ? t('请双方打开同一视频')
                    : action === 'diagnostics'
                        ? t('读取当前房间的脱敏同步诊断')
                        : action === 'participantResync'
                            ? t('请求重新对齐双方进度')
                            : actionLabels[action] + '：' + (getStateInfo(room.State).description || '');
        button.addEventListener('click', function () {
            if (action === 'delete') {
                deleteRoom(page, room, button);
            } else if (action === 'join' || action === 'leave') {
                membership(page, room, action, button);
            } else if (action === 'diagnostics') {
                loadRoomDiagnostics(page, room.RoomId, button);
            } else if (action === 'participantResync') {
                participantResync(page, room.RoomId, button);
            } else {
                control(page, room.RoomId, action, button);
            }
        });
        if (action === 'diagnostics' && page._wtDiagnosticBusy && page._wtDiagnosticBusy[room.RoomId]) {
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            button.textContent = t('读取中…');
        }
        if (page._wtRoomBusy && page._wtRoomBusy[room.RoomId]) {
            button.disabled = true;
        }
        return button;
    }

    function rememberDiagnosticPanelState(page) {
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
        var panels = page._wtDiagnosticPanels || {};
        Object.keys(panels).forEach(function (roomId) {
            if (panels[roomId] && panels[roomId].details) {
                page._wtDiagnosticOpen[roomId] = panels[roomId].details.open;
            }
        });
    }

    function idKey(value) { return value === null || value === undefined ? '' : String(value).toLowerCase(); }

    function mediaItemId(state) {
        if (!state || state.Eligible !== true ||
            !((state.State === 'Watching' && state.StatusReason === 'watching') ||
                (state.State === 'Barrier' && state.StatusReason === 'aligning'))) return null;
        var members = Array.isArray(state.ParticipantUserIds) ? state.ParticipantUserIds.map(idKey) : null;
        var joined = Array.isArray(state.JoinedParticipantUserIds) ? state.JoinedParticipantUserIds.map(idKey) : null;
        var sessions = state.Sessions;
        if (!Array.isArray(members) || members.length !== 2 || !members[0] || !members[1] || members[0] === members[1] ||
            members.indexOf(idKey(state.PrimaryUserId)) < 0 || !Array.isArray(joined) || joined.length !== 2 ||
            joined[0] === joined[1] || !members.every(function (id) { return joined.indexOf(id) >= 0; }) ||
            !Array.isArray(sessions) || sessions.length !== 2 || !state.SyncItemId) return null;
        if (!members.every(function (id) {
            return sessions.filter(function (session) {
                return session && idKey(session.UserId) === id && session.Online === true &&
                    session.ItemId && idKey(session.ItemId) === idKey(state.SyncItemId);
            }).length === 1;
        })) return null;
        return String(state.SyncItemId);
    }

    function mediaTitle(item) {
        function index(value) { return typeof value === 'number' && isFinite(value) && value >= 0 && Math.floor(value) === value; }
        function name(value) { return typeof value === 'string' ? value.trim() : ''; }
        var title = name(item.Name);
        if (item.Type !== 'Episode') return title;
        title = name(item.SeriesName) || title;
        var parts = [];
        if (title) parts.push(title);
        if (index(item.ParentIndexNumber)) parts.push(item.ParentIndexNumber === 0 ? t('特别篇') : t('第 {0} 季').replace('{0}', item.ParentIndexNumber));
        if (index(item.IndexNumber)) parts.push(t('第 {0} 集').replace('{0}', item.IndexNumber));
        return title ? parts.join(' · ') : '';
    }

    function roomPresentation(page, room) {
        var entry = page._wtRoomStates[room.RoomId];
        var state = entry && entry.state;
        if (!state) return { label: entry && entry.failed ? t('状态暂不可用') : t('正在读取状态'), description: entry && entry.expired ? t('播放状态已过期，正在等待重新读取。') : entry && entry.failed ? t('状态读取失败，将自动重试。') : t('正在读取当前播放状态…'), className: 'unavailable' };
        var info = getStateInfo(state.State);
        info.description = statusReasonMessage(state.StatusReason);
        if (state.StatusReason === 'different_video' || state.StatusReason === 'media_mismatch') info.label = t('视频不一致');
        else if (state.StatusReason === 'transient_recovery' || state.State === 'Recovering') info.label = t('等待连接恢复');
        else if (state.StatusReason === 'member_left') info.label = t('等待参与者');
        else if (['snapshot_unavailable', 'snapshot_stale', 'server_unavailable'].indexOf(state.StatusReason) >= 0) info.label = t('状态暂不可用');
        else if (state.State === 'Waiting') info.label = t('等待播放');
        else if (state.State === 'Unavailable') info.label = t('状态暂不可用');
        return info;
    }

    function safeImagePart(value) { return typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value); }

    function sameServerImageUrl(page, raw) {
        if (typeof raw !== 'string' || !raw.trim()) return null;
        try {
            var server = new URL(page._wtIdentity.server);
            var url = new URL(raw, server.href);
            return (url.protocol === 'http:' || url.protocol === 'https:') && !url.username && !url.password &&
                url.origin === server.origin ? url.href : null;
        } catch (error) {
            return null;
        }
    }

    function roomBackdropUrl(page, item, maxWidth) {
        if (typeof ApiClient.getImageUrl !== 'function') return null;
        var imageId = item.Id;
        var tag = Array.isArray(item.BackdropImageTags) ? item.BackdropImageTags[0] : null;
        if (!safeImagePart(imageId) || !safeImagePart(tag)) {
            imageId = item.ParentBackdropItemId;
            tag = Array.isArray(item.ParentBackdropImageTags) ? item.ParentBackdropImageTags[0] : null;
        }
        if (!safeImagePart(imageId) || !safeImagePart(tag)) return null;
        try {
            var raw = ApiClient.getImageUrl(imageId, { type: 'Backdrop', index: 0, tag: tag, maxWidth: maxWidth || 1280 });
            return sameServerImageUrl(page, raw);
        } catch (error) {
            return null;
        }
    }

    function roomAvatarUserId(room, userId) {
        if (!safeImagePart(userId) || !room || !Array.isArray(room.ParticipantUserIds) ||
            room.ParticipantUserIds.length !== 2 || idKey(room.ParticipantUserIds[0]) === idKey(room.ParticipantUserIds[1]) ||
            !room.ParticipantUserIds.some(function (id) { return idKey(id) === idKey(userId); })) return null;
        var participant = roomParticipant(room, userId);
        return participant && safeImagePart(participant.Id) ? participant.Id : null;
    }

    function roomAvatarUrl(page, room, userId) {
        if (typeof ApiClient.getUserImageUrl !== 'function') return null;
        userId = roomAvatarUserId(room, userId);
        if (!userId) return null;
        try {
            return sameServerImageUrl(page, ApiClient.getUserImageUrl(userId, { type: 'Primary', maxWidth: 96 }));
        } catch (error) {
            return null;
        }
    }

    function clearRoomAvatars(view) {
        (view && view.avatars || []).forEach(function (record) {
            record.image.onload = null;
            record.image.onerror = null;
            record.image.removeAttribute('src');
            record.image.hidden = true;
            record.fallback.hidden = false;
            record.url = null;
        });
    }

    function startRoomAvatars(page, room, view) {
        var generation = page._wtGeneration;
        function currentMember(record) {
            if (!pageActionAllowed(page) || page._wtGeneration !== generation ||
                page._wtRoomViews[room.RoomId] !== view || view.avatars.indexOf(record) < 0 ||
                record.image.parentNode !== record.element || !page.contains(record.element)) return false;
            var currentRoom = (page._wtRooms || []).filter(function (value) { return value.RoomId === room.RoomId; })[0];
            return !!(roomAvatarUserId(room, record.userId) && roomAvatarUserId(currentRoom, record.userId));
        }
        view.avatars.forEach(function (record) {
            if (!currentMember(record)) return;
            var url = roomAvatarUrl(page, room, record.userId);
            if (!url || record.url === url) return;
            var failureKey = JSON.stringify([room.RoomId, idKey(record.userId), url]);
            if (page._wtAvatarFailures[failureKey]) return;
            record.url = url;
            function current() {
                return currentMember(record) && record.url === url && record.image.getAttribute('src') === url;
            }
            record.image.onload = function () {
                if (!current()) return;
                record.image.hidden = false;
                record.fallback.hidden = true;
            };
            record.image.onerror = function () {
                if (!current()) return;
                // Failures are suppressed only for this page generation.
                page._wtAvatarFailures[failureKey] = true;
                record.image.hidden = true;
                record.image.removeAttribute('src');
                record.fallback.hidden = false;
            };
            record.image.src = url;
        });
    }

    function clearRoomBackdrop(view) {
        if (!view || !view.backdrop) return;
        view.backdrop.onload = null;
        view.backdrop.onerror = null;
        view.backdrop.removeAttribute('src');
        view.backdrop.hidden = true;
        view.presentation.classList.remove('wt-roomPresentation--image');
        view.backdropUrl = null;
        view.backdropTarget = null;
    }

    function updateRoomBackdrop(page, room, view, media) {
        var url = media && media.title && media.backdropUrl;
        var target = page._wtMediaTargets[room.RoomId];
        if (!url || !target) {
            clearRoomBackdrop(view);
            return;
        }
        // Rechecking metadata must not reload an unchanged image every poll.
        if (view.backdropUrl === url && view.backdropTarget === target) return;
        clearRoomBackdrop(view);
        view.backdropUrl = url;
        view.backdropTarget = target;
        var generation = page._wtGeneration;
        function current() {
            var currentMedia = page._wtMedia[room.RoomId];
            var currentState = page._wtRoomStates[room.RoomId];
            return pageActionAllowed(page) && page._wtGeneration === generation &&
                page._wtRoomViews[room.RoomId] === view && page._wtMediaTargets[room.RoomId] === target &&
                currentState && idKey(mediaItemId(currentState.state)) === idKey(media.itemId) &&
                currentMedia && currentMedia.backdropUrl === url && view.backdrop.getAttribute('src') === url;
        }
        view.backdrop.onload = function () {
            if (!current()) return;
            view.backdrop.hidden = false;
            view.presentation.classList.add('wt-roomPresentation--image');
        };
        view.backdrop.onerror = function () {
            if (!current()) return;
            view.backdrop.hidden = true;
            view.backdrop.removeAttribute('src');
            view.presentation.classList.remove('wt-roomPresentation--image');
            // Keep this target/url marked attempted until either changes.
        };
        view.backdrop.src = url;
    }

    function waitingDecoration() {
        var ns = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('viewBox', '0 0 240 160');
        svg.setAttribute('class', 'wt-waitingDecoration');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        var paths = [
            'M40 16h160v82H40z M52 103h136 M120 99v12',
            'M51 112q0-12 12-12h24q12 0 12 12v31H51z M46 123v20h58v-20 M58 144v8 M93 144v8',
            'M141 112q0-12 12-12h24q12 0 12 12v31h-48z M136 123v20h58v-20 M148 144v8 M183 144v8',
            'M16 55h9 M20 36l7 6 M20 74l7-6 M215 55h9 M213 42l7-6 M213 68l7 6'
        ];
        paths.forEach(function (d) {
            var path = document.createElementNS(ns, 'path');
            path.setAttribute('d', d);
            path.setAttribute('fill', 'none');
            path.setAttribute('stroke', 'currentColor');
            path.setAttribute('stroke-width', '2');
            path.setAttribute('stroke-linecap', 'round');
            path.setAttribute('stroke-linejoin', 'round');
            svg.appendChild(path);
        });
        return svg;
    }

    function updateRoomPresentation(page, room) {
        var view = page._wtRoomViews[room.RoomId];
        if (!view) return;
        var info = roomPresentation(page, room);
        view.state.textContent = info.label;
        view.state.title = info.description;
        view.state.className = 'wt-roomState wt-roomState-' + info.className;
        view.description.textContent = info.description;
        var media = page._wtMedia[room.RoomId];
        var state = page._wtRoomStates[room.RoomId];
        var prefix = state && state.state && state.state.State === 'Barrier' ? t('正在同步\n') : t('正在一起观看\n');
        var hasTitle = !!(media && media.title);
        view.presentation.classList.toggle('wt-roomPresentation--media', hasTitle);
        view.waiting.hidden = hasTitle;
        view.waitingTitle.textContent = info.label === t('等待播放') ? t('等待双方打开视频') : info.label;
        view.waitingTitle.hidden = hasTitle;
        view.mediaLabel.textContent = hasTitle ? prefix : media && media.unavailable ? t('媒体信息暂不可用') : '';
        view.mediaTitle.textContent = hasTitle ? media.heading || media.title : '';
        view.mediaDetail.textContent = hasTitle ? media.detail || '' : '';
        view.mediaDetail.hidden = !view.mediaDetail.textContent;
        view.description.hidden = hasTitle;
        view.media.hidden = !hasTitle && !(media && media.unavailable);
        if (view.pauseButton) {
            var primaryPause = !!(state && state.state && state.state.State === 'Watching' && state.state.StatusReason === 'watching');
            view.pauseButton.classList.toggle('button-submit', primaryPause);
            view.pauseButton.classList.toggle('raised-outline', !primaryPause);
        }
        updateRoomBackdrop(page, room, view, media);
    }

    function clearRoomStateExpiry(page, roomId) {
        if (!page._wtStateExpiryTimers) return;
        clearTimeout(page._wtStateExpiryTimers[roomId]);
        delete page._wtStateExpiryTimers[roomId];
    }

    function renewRoomStateExpiry(page, room) {
        var roomId = room.RoomId;
        var generation = page._wtGeneration;
        clearRoomStateExpiry(page, roomId);
        var timer = setTimeout(function () {
            if (!pageActionAllowed(page) || page._wtGeneration !== generation || page._wtStateExpiryTimers[roomId] !== timer) return;
            delete page._wtStateExpiryTimers[roomId];
            if (!(page._wtRooms || []).some(function (currentRoom) { return currentRoom.RoomId === roomId; })) return;
            page._wtRoomStates[roomId] = { failed: true, expired: true };
            delete page._wtMediaTargets[roomId];
            delete page._wtMedia[roomId];
            updateRoomPresentation(page, room);
        }, 10000);
        page._wtStateExpiryTimers[roomId] = timer;
    }

    function loadRoomMedia(page, room) {
        if (!pageActionAllowed(page) || page.ownerDocument.hidden || page._wtVisibleRoute !== 'rooms' ||
            page._wtMediaFlights[room.RoomId]) return;
        var roomId = room.RoomId;
        var sequence = (page._wtRoomSequences[roomId] || 0) + 1;
        page._wtRoomSequences[roomId] = sequence;
        var flights = page._wtMediaFlights;
        flights[roomId] = true;
        var requestedAt = Date.now();
        function current() {
            return pageActionAllowed(page) && page._wtRoomSequences[roomId] === sequence &&
                (page._wtRooms || []).some(function (value) { return value.RoomId === roomId; });
        }
        return apiGet(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/State').finally(function () {
            delete flights[roomId];
        }).then(function (state) {
            if (!current()) return;
            if (Date.now() - requestedAt >= 10000) throw new Error('stale_state');
            if (!state || state.RoomId !== roomId) throw new Error('invalid_state');
            page._wtRoomStates[roomId] = { state: state };
            renewRoomStateExpiry(page, room);
            var itemId = mediaItemId(state);
            var target = page._wtMediaTargets[roomId];
            if (!itemId || !target || idKey(target.itemId) !== idKey(itemId)) {
                delete page._wtMedia[roomId];
                target = itemId ? { itemId: itemId, sequence: sequence } : null;
                page._wtMediaTargets[roomId] = target;
            }
            // This read is fresher than Rooms, but the endpoint is not an atomic snapshot.
            renderRooms(page, page._wtRooms || []);
            if (!itemId || page.ownerDocument.hidden || page._wtMetadataFlights[roomId] ||
                target.nextMetadataReadAt > Date.now()) return;
            // Preserve the previous metadata request cadence for an unchanged item.
            target.nextMetadataReadAt = Date.now() + 5000;
            var identity = page._wtIdentity;
            if (!identity.user || typeof ApiClient.getItem !== 'function') {
                page._wtMedia[roomId] = { unavailable: true };
                updateRoomPresentation(page, room);
                return;
            }
            var metadataFlights = page._wtMetadataFlights;
            metadataFlights[roomId] = true;
            function currentMedia() {
                return pageActionAllowed(page) && page._wtMediaTargets[roomId] === target &&
                    page._wtRoomStates[roomId] && idKey(mediaItemId(page._wtRoomStates[roomId].state)) === idKey(itemId) &&
                    (page._wtRooms || []).some(function (value) { return value.RoomId === roomId; });
            }
            return pageRequest(page, 'item:' + roomId + ':' + sequence, function () {
                return ApiClient.getItem(identity.user, itemId);
            }).then(function (item) {
                if (!currentMedia()) return;
                var title = item && idKey(item.Id) === idKey(itemId) ? mediaTitle(item) : '';
                var heading = title && item.Type === 'Episode' && typeof item.SeriesName === 'string' && item.SeriesName.trim()
                    ? item.SeriesName.trim() : title && typeof item.Name === 'string' ? item.Name.trim() : '';
                page._wtMedia[roomId] = title ? {
                    title: title,
                    heading: heading || title,
                    detail: heading ? title.slice(heading.length).replace(/^ · /, '') : '',
                    itemId: itemId,
                    backdropUrl: roomBackdropUrl(page, item)
                } : { unavailable: true };
                updateRoomPresentation(page, room);
            }).catch(function () {
                if (!currentMedia()) return;
                page._wtMedia[roomId] = { unavailable: true };
                updateRoomPresentation(page, room);
            }).finally(function () { delete metadataFlights[roomId]; });
        }).catch(function () {
            if (!current()) return;
            clearRoomStateExpiry(page, roomId);
            page._wtRoomStates[roomId] = { failed: true };
            delete page._wtMediaTargets[roomId];
            delete page._wtMedia[roomId];
            updateRoomPresentation(page, room);
        });
    }

    function scheduleCardExpiry(page) {
        clearTimeout(page._wtAdminExpiry);
        if (!pageActionAllowed(page) || page._wtVisibleRoute !== 'admin') return;
        var now = Date.now();
        var remaining = (page._wtRooms || []).map(function (r) { return r._wtExpires - now; })
            .filter(function (ms) { return ms > 0; });
        if (remaining.length) page._wtAdminExpiry = setTimeout(function () {
            if (!pageActionAllowed(page) || page._wtVisibleRoute !== 'admin') return;
            renderRooms(page, page._wtRooms || []);
            scheduleCardExpiry(page);
        }, Math.min.apply(null, remaining) + 1);
    }

    function createAdminCard(page, room) {
        var row = document.createElement('article');
        row.className = 'wt-adminRoom';
        row.dataset.roomId = room.RoomId;
        function append(parent, tag, className) {
            var element = document.createElement(tag);
            if (className) element.className = className;
            parent.appendChild(element);
            return element;
        }
        var scene = append(row, 'div', 'wt-cardScene');
        var image = append(scene, 'img', 'wt-cardBackdrop');
        image.alt = '';
        image.hidden = true;
        var top = append(scene, 'div', 'wt-cardTop');
        append(top, 'span', 'wt-cardRoom');
        append(top, 'span', 'wt-cardStatus');
        var waiting = waitingDecoration();
        waiting.classList.add('wt-cardWaiting');
        scene.appendChild(waiting);
        append(scene, 'h3', 'wt-cardFilm');
        append(scene, 'div', 'wt-cardPeople');
        var foot = append(row, 'div', 'wt-cardFoot');
        var meter = append(foot, 'div', 'wt-cardProgress');
        var owner = append(meter, 'span', 'wt-cardOwner');
        var primaryName = roomUserName(page, room, room.PrimaryUserId);
        append(owner, 'span', 'wt-cardRole').textContent = t('主用户');
        append(owner, 'span').textContent = primaryName;
        append(meter, 'strong').hidden = true;
        var bar = append(meter, 'progress');
        bar.hidden = true;
        bar.max = 100;
        bar.setAttribute('aria-label', t('主用户进度') + ' · ' + primaryName);
        append(foot, 'div', 'wt-cardActions');
        append(foot, 'div', 'wt-roomFeedback').hidden = true;
        row.querySelector('.wt-cardRoom').textContent = room.Name || t('未命名房间');
        var members = row.querySelector('.wt-cardPeople');
        (room.ParticipantUserIds || []).forEach(function (id) {
            var member = document.createElement('span');
            member.textContent = roomUserName(page, room, id);
            members.appendChild(member);
        });
        var button = createActionButton(page, room, 'delete');
        button.textContent = t('结束同步');
        row.querySelector('.wt-cardActions').appendChild(button);
        var feedback = page._wtRoomFeedback[room.RoomId];
        var message = row.querySelector('.wt-roomFeedback');
        message.hidden = !feedback;
        if (feedback) {
            message.classList.toggle('error', !!feedback.isError);
            message.setAttribute('role', feedback.isError ? 'alert' : 'status');
            message.textContent = feedback.text;
        }
        return row;
    }

    function cardMetadata(page, itemId) {
        var cache = page._wtCardMetadata;
        var entry = cache.get(itemId);
        if (entry && entry.expires > Date.now()) return entry.read;
        if (cache.size >= 96) cache.delete(cache.keys().next().value);
        entry = { expires: Date.now() + 300000 };
        entry.read = Promise.resolve().then(function () {
            if (cache !== page._wtCardMetadata || !pageActionAllowed(page)) return null;
            return ApiClient.getItem(page._wtIdentity.user, itemId);
        }).then(function (item) {
            if (cache !== page._wtCardMetadata || !pageActionAllowed(page) ||
                !item || idKey(item.Id) !== idKey(itemId)) return null;
            return { title: mediaTitle(item), image: roomBackdropUrl(page, item, 640) };
        }).catch(function () {
            entry.expires = Date.now() + 30000;
            return null;
        });
        cache.set(itemId, entry);
        return entry.read;
    }

    function loadCardArtwork(page, row) {
        var target = row._wtTarget;
        if (!target || !pageActionAllowed(page) || page.ownerDocument.hidden) return;
        var read = cardMetadata(page, target.id);
        if (target.read === read) return;
        target.read = read;
        function current() {
            return pageActionAllowed(page) && page._wtVisibleRoute === 'admin' && page._wtIsAdmin === true &&
                row.isConnected && row._wtTarget === target && row._wtExpiry > Date.now() && target.read === read;
        }
        read.then(function (meta) {
            if (!current()) return;
            var film = row.querySelector('.wt-cardFilm');
            row.classList.toggle('wt-cardNoTitle', !(meta && meta.title));
            film.textContent = meta && meta.title || row.querySelector('.wt-cardRoom').textContent;
            var img = row.querySelector('img');
            if (!meta || !meta.image) {
                img.hidden = true;
                img.removeAttribute('src');
                row.classList.remove('wt-cardWithImage');
                return;
            }
            img.onload = function () {
                if (!current()) return;
                img.hidden = false;
                row.classList.add('wt-cardWithImage');
            };
            img.onerror = function () {
                if (!current()) return;
                row.classList.remove('wt-cardWithImage');
                img.hidden = true;
            };
            img.src = meta.image;
        });
    }

    function paintAdminCard(page, row, room) {
        if (!row || !row.classList.contains('wt-adminRoom')) return;
        var fresh = page._wtRooms && room._wtExpires > Date.now();
        var value = fresh && room.Playback;
        var item = value && safeImagePart(value.ItemId) && value.ItemId;
        var state = room.State;
        var status = room.StatusReason === 'different_video' ? t('视频不一致') :
            state === 'Waiting' ? t('等待播放') : getStateInfo(state).label;
        if (!page._wtRooms || ['server_unavailable', 'snapshot_unavailable'].indexOf(room.StatusReason) >= 0 ||
            (!item && state === 'Watching' && room.StatusReason !== 'different_video')) status = t('状态暂不可用');
        if (item && state === 'Watching') {
            status = value.PrimaryPaused === value.OtherPaused ? (value.PrimaryPaused ? t('已暂停') : t('播放中')) :
                value.PrimaryPaused ? t('主用户已暂停 · 对方播放中') : t('主用户播放中 · 对方已暂停');
        }
        row.querySelector('.wt-cardStatus').textContent = status;
        var progress = row.querySelector('.wt-cardProgress');
        var percent = item && value.Percent;
        var hasProgress = !!item && Number.isInteger(percent) && percent >= 0 && percent <= 100;
        var percentLabel = progress.querySelector('strong');
        var bar = progress.querySelector('progress');
        row.classList.toggle('wt-cardHasProgress', hasProgress);
        percentLabel.hidden = bar.hidden = !hasProgress;
        if (hasProgress) {
            percentLabel.textContent = percent + '%';
            bar.value = percent;
        }
        row._wtExpiry = room._wtExpires;
        var target = row._wtTarget;
        if (!target || target.id !== item) {
            var img = row.querySelector('img');
            img.onload = img.onerror = null;
            img.removeAttribute('src');
            img.hidden = true;
            row.classList.remove('wt-cardWithImage');
            row.classList.add('wt-cardNoTitle');
            row.querySelector('.wt-cardFilm').textContent = room.Name || t('未命名房间');
            row._wtTarget = item ? { id: item } : null;
        }
        row.classList.toggle('wt-cardIdle', !item);
        if (!item) return;
        if (!window.IntersectionObserver) { loadCardArtwork(page, row); return; }
        if (!page._wtCardObserver) page._wtCardObserver = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                entry.target._wtInView = entry.isIntersecting;
                if (entry.isIntersecting && entry.target.isConnected) loadCardArtwork(page, entry.target);
            });
        });
        if (row._wtInView) loadCardArtwork(page, row);
        else page._wtCardObserver.observe(row);
    }

    function renderRooms(page, rooms) {
        // Display-only fallback: _wtRooms stays null after a failed read so
        // stale membership data cannot validate a new form or confirmation.
        if (page._wtRooms === null && page._wtLastRooms) rooms = page._wtLastRooms;
        var container = page.querySelector('#wtRooms');
        if (!container) {
            return;
        }

        var adminList = page._wtVisibleRoute === 'admin' && page._wtIsAdmin === true;
        container.classList.toggle('wt-adminGrid', adminList);
        rooms = (rooms || []).filter(function (room) {
            return page._wtTabsPermissionReady && (adminList || (room.ParticipantUserIds || []).some(function (id) {
                return idKey(id) === idKey(page._wtIdentity.user);
            }));
        });
        var renderedRooms = (rooms || []).map(function (room) {
            var entry = page._wtRoomStates[room.RoomId];
            return entry && entry.state ? Object.assign({}, room, entry.state) : room;
        });
        var signature = JSON.stringify([renderedRooms.map(function (room) {
            return [room.RoomId, room.Name, room.State, room.StatusReason, room.ParticipantUserIds, room.PrimaryUserId,
                room.JoinedParticipantUserIds, room.Participants, room.IsAdmin, room.CanEnd, room.CurrentUserJoined];
        }), page._wtRoomBusy, page._wtRoomFeedback, page._wtIsAdmin, page._wtVisibleRoute]);
        if (page._wtRoomsSignature === signature) {
            container.setAttribute('aria-busy', 'false');
            if (adminList) {
                rooms.forEach(function (room, index) { paintAdminCard(page, container.children[index], room); });
            } else (rooms || []).forEach(function (room) { updateRoomPresentation(page, room); });
            return;
        }
        page._wtRoomsSignature = signature;
        var focused = page.ownerDocument.activeElement;
        var focusedCard = focused && focused.closest ? focused.closest('[data-room-id]') : null;
        var focusRoom = focusedCard && focusedCard.dataset.roomId;
        var focusAction = focused && focused.dataset && focused.dataset.action;
        var focusSummary = focused && focused.tagName === 'SUMMARY';
        if (page._wtCardObserver) page._wtCardObserver.disconnect();
        rememberDiagnosticPanelState(page);
        Object.keys(page._wtRoomViews || {}).forEach(function (roomId) {
            clearRoomBackdrop(page._wtRoomViews[roomId]);
            clearRoomAvatars(page._wtRoomViews[roomId]);
        });
        clearChildren(container);
        page._wtDiagnosticPanels = {};
        page._wtDiagnosticBusy = page._wtDiagnosticBusy || {};
        page._wtDiagnosticRequests = page._wtDiagnosticRequests || {};
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
        page._wtRoomViews = {};
        container.setAttribute('aria-busy', 'false');
        if (page._wtIsAdmin !== undefined) {
            setAdminVisibility(page, page._wtIsAdmin);
        }
        if (!rooms || rooms.length === 0) {
            var empty = document.createElement('div');
            empty.className = 'wt-emptyState';
            var emptyTitle = document.createElement('strong');
            emptyTitle.textContent = adminList ? t('当前没有房间。') : t('暂无参与的房间');
            var emptyText = document.createElement('p');
            emptyText.className = 'fieldDescription';
            emptyText.textContent = page._wtIsAdmin === true
                ? t('到“创建与邀请”创建房间或邀请码，再让两位参与者打开同一视频。')
                : t('到“邀请”接受邀请码，或请管理员创建房间。');
            empty.appendChild(emptyTitle);
            if (!adminList) empty.appendChild(emptyText);
            container.appendChild(empty);
            return;
        }

        renderedRooms.forEach(function (room) {
            if (adminList) {
                var row = createAdminCard(page, room);
                container.appendChild(row);
                paintAdminCard(page, row, room);
                if (focusRoom === room.RoomId) {
                    var focusTarget = focusAction ? row.querySelector('[data-action="' + focusAction + '"]') : focusSummary ? row.querySelector('summary') : null;
                    if (focusTarget) focusTarget.focus({ preventScroll: true });
                }
                return;
            }
            var card = document.createElement('article');
            card.className = 'wt-roomCard';
            card.dataset.roomId = room.RoomId;
            var header = document.createElement('div');
            header.className = 'wt-roomHeader';
            var name = document.createElement('h3');
            name.className = 'wt-roomName';
            name.textContent = room.Name || t('未命名房间');
            var state = document.createElement('span');
            header.appendChild(name);
            header.appendChild(state);
            card.appendChild(header);

            var layout = document.createElement('div');
            layout.className = 'wt-roomLayout';
            var main = document.createElement('div');
            main.className = 'wt-roomMain';
            var presentation = document.createElement('div');
            presentation.className = 'wt-roomPresentation';
            var backdrop = document.createElement('img');
            backdrop.className = 'wt-roomBackdrop';
            backdrop.alt = '';
            backdrop.setAttribute('aria-hidden', 'true');
            backdrop.decoding = 'async';
            backdrop.hidden = true;
            presentation.appendChild(backdrop);
            var waiting = document.createElement('div');
            waiting.className = 'wt-waitingArtwork';
            waiting.setAttribute('aria-hidden', 'true');
            waiting.appendChild(waitingDecoration());
            presentation.appendChild(waiting);
            var waitingTitle = document.createElement('h4');
            waitingTitle.className = 'wt-waitingTitle';
            presentation.appendChild(waitingTitle);
            var stateDescription = document.createElement('p');
            stateDescription.className = 'fieldDescription wt-roomStateDescription';
            presentation.appendChild(stateDescription);
            var media = document.createElement('p');
            media.className = 'wt-roomMedia';
            var mediaLabel = document.createElement('span');
            mediaLabel.className = 'wt-roomMediaLabel';
            var mediaTitle = document.createElement('strong');
            var mediaDetail = document.createElement('span');
            mediaDetail.className = 'wt-roomMediaDetail';
            media.appendChild(mediaLabel);
            media.appendChild(mediaTitle);
            media.appendChild(mediaDetail);
            presentation.appendChild(media);
            main.appendChild(presentation);

            var meta = document.createElement('div');
            meta.className = 'wt-participants';
            var participantIds = room.ParticipantUserIds || [];
            var avatars = [];
            participantIds.forEach(function (id) {
                var participant = document.createElement('div');
                participant.className = 'wt-participant';
                var displayName = roomUserName(page, room, id);
                var avatar = document.createElement('span');
                avatar.className = 'wt-participantAvatar';
                avatar.setAttribute('aria-hidden', 'true');
                var avatarFallback = document.createElement('span');
                avatarFallback.className = 'wt-participantAvatarFallback';
                avatarFallback.textContent = Array.from(String(displayName).trim())[0] || '?';
                var avatarImage = document.createElement('img');
                avatarImage.className = 'wt-participantAvatarImage';
                avatarImage.alt = '';
                avatarImage.setAttribute('aria-hidden', 'true');
                avatarImage.decoding = 'async';
                avatarImage.hidden = true;
                avatar.appendChild(avatarFallback);
                avatar.appendChild(avatarImage);
                avatars.push({ userId: id, element: avatar, image: avatarImage, fallback: avatarFallback, url: null });
                participant.appendChild(avatar);
                var text = document.createElement('div');
                text.className = 'wt-participantText';
                var participantName = document.createElement('div');
                participantName.className = 'wt-participantName';
                participantName.textContent = displayName;
                if (idKey(id) === idKey(room.PrimaryUserId)) {
                    var primary = document.createElement('small');
                    primary.className = 'wt-primaryLabel';
                    primary.textContent = t('主用户');
                    participantName.appendChild(primary);
                }
                var joined = document.createElement('div');
                joined.className = 'wt-participantState';
                var isJoined = (room.JoinedParticipantUserIds || []).map(idKey).indexOf(idKey(id)) >= 0;
                joined.textContent = isJoined ? t('已加入') : t('已暂离');
                joined.classList.toggle('wt-participantState--joined', isJoined);
                text.appendChild(participantName);
                text.appendChild(joined);
                participant.appendChild(text);
                meta.appendChild(participant);
            });
            main.appendChild(meta);
            layout.appendChild(main);

            var controls = document.createElement('aside');
            controls.className = 'wt-roomControls';
            var controlsTitle = document.createElement('h3');
            controlsTitle.textContent = t('房间控制');
            controls.appendChild(controlsTitle);
            function groupHeading(group, title) {
                var heading = document.createElement('h4');
                heading.className = 'wt-controlHeading';
                heading.textContent = title;
                group.appendChild(heading);
            }
            var playback = document.createElement('div');
            playback.className = 'wt-roomActions wt-roomActions--playback';
            playback.setAttribute('role', 'group');
            playback.setAttribute('aria-label', t('播放控制'));
            groupHeading(playback, t('播放控制'));
            if (room.CurrentUserJoined && !room.IsAdmin) {
                playback.appendChild(createActionButton(page, room, 'participantResync'));
                var resyncHint = document.createElement('p');
                resyncHint.className = 'fieldDescription wt-resyncHint';
                resyncHint.textContent = t('以主用户视频和进度为准，可能切换视频。');
                playback.appendChild(resyncHint);
            }
            var pauseButton = null;
            if (room.IsAdmin) {
                ['pause', 'resume', 'resync'].forEach(function (action) {
                    var button = createActionButton(page, room, action);
                    if (action === 'pause') pauseButton = button;
                    playback.appendChild(button);
                });
            }
            if (playback.querySelector('button')) controls.appendChild(playback);
            var actions = document.createElement('div');
            actions.className = 'wt-roomActions wt-roomActions--members';
            actions.setAttribute('role', 'group');
            actions.setAttribute('aria-label', t('成员与诊断'));
            groupHeading(actions, t('房间成员'));
            var isMember = participantIds.map(idKey).indexOf(idKey(page._wtIdentity.user)) >= 0;
            if (isMember) actions.appendChild(createActionButton(page, room, room.CurrentUserJoined ? 'leave' : 'join'));
            if (room.IsAdmin) {
                actions.appendChild(createActionButton(page, room, 'delete'));
            } else if (room.CanEnd) {
                actions.appendChild(createActionButton(page, room, 'delete'));
            }
            if (actions.querySelector('button')) controls.appendChild(actions);
            var membershipLine = document.createElement('p');
            membershipLine.className = 'fieldDescription wt-roomMembership';
            membershipLine.hidden = !isMember;
            membershipLine.textContent = t('暂离可重新加入；结束解除双方关系。');
            controls.appendChild(membershipLine);
            var diagnosticsAction = document.createElement('div');
            diagnosticsAction.className = 'wt-diagnosticAction';
            diagnosticsAction.appendChild(createActionButton(page, room, 'diagnostics'));
            controls.appendChild(diagnosticsAction);
            layout.appendChild(controls);
            card.appendChild(layout);

            var feedback = page._wtRoomFeedback && page._wtRoomFeedback[room.RoomId];
            var feedbackEl = document.createElement('div');
            feedbackEl.className = 'wt-roomFeedback' + (feedback && feedback.isError ? ' error' : '');
            feedbackEl.setAttribute('role', feedback && feedback.isError ? 'alert' : 'status');
            feedbackEl.setAttribute('aria-live', 'polite');
            feedbackEl.textContent = feedback ? feedback.text : '';
            feedbackEl.hidden = !feedback;
            card.appendChild(feedbackEl);
            card.appendChild(createDiagnosticPanel(page, room));
            page._wtRoomViews[room.RoomId] = {
                state: state, description: stateDescription, media: media, mediaLabel: mediaLabel,
                mediaTitle: mediaTitle, mediaDetail: mediaDetail, presentation: presentation, backdrop: backdrop,
                waiting: waiting, waitingTitle: waitingTitle, pauseButton: pauseButton, avatars: avatars
            };
            updateRoomPresentation(page, room);
            container.appendChild(card);
            startRoomAvatars(page, room, page._wtRoomViews[room.RoomId]);
            if (focusRoom === room.RoomId) {
                var target = focusAction ? card.querySelector('[data-action="' + focusAction + '"]') : focusSummary ? card.querySelector('summary') : null;
                if (target) target.focus({ preventScroll: true });
            }
        });
    }

    function setRoomsStatus(page, text) {
        var el = page.querySelector('#wtRoomsStatus');
        if (!el) return;
        // Repeated polling failures must not repeatedly announce unchanged text.
        if (el.textContent !== text) el.textContent = text;
        el.hidden = !text;
    }

    function loadRooms(page) {
        if (!pageActionAllowed(page)) return Promise.resolve();
        if (page._wtIsAdmin === undefined) {
            var region = page.querySelector('#wtRooms');
            if (region) region.setAttribute('aria-busy', 'false');
            return Promise.resolve();
        }
        if (page._wtRoomsLoading) {
            return Promise.resolve();
        }

        var container = page.querySelector('#wtRooms');
        page._wtRoomsLoading = true;
        if (container) {
            container.setAttribute('aria-busy', 'true');
        }

        var scope = page._wtIsAdmin === true && (page._wtVisibleRoute === 'admin' || page._wtVisibleRoute === 'invite') ? 'all' : 'mine';
        var cards = scope === 'all' && page._wtVisibleRoute === 'admin';
        var requestedAt = Date.now();
        return apiGet(page, 'WatchTogether/Rooms?Scope=' + scope + (cards ? '&IncludePlayback=true' : '')).then(function (rooms) {
            var list = Array.isArray(rooms) ? rooms : [];
            if (cards) list.forEach(function (room) {
                var value = room.Playback;
                room._wtExpires = value && Number.isFinite(value.ValidForMs)
                    ? requestedAt + Math.min(10000, value.ValidForMs) : 0;
            });
            page._wtRooms = list;
            page._wtRoomsLoaded = true;
            page._wtRoomsPollFailures = 0;
            page._wtRoomsRetryAt = 0;
            page._wtLastRooms = list;
            setRoomsStatus(page, '');
            Object.keys(page._wtRoomStates).forEach(function (roomId) {
                if (!list.some(function (room) { return room.RoomId === roomId; })) {
                    page._wtRoomSequences[roomId] = (page._wtRoomSequences[roomId] || 0) + 1;
                    clearRoomStateExpiry(page, roomId);
                    delete page._wtRoomStates[roomId];
                    delete page._wtMediaTargets[roomId];
                    delete page._wtMedia[roomId];
                }
            });
            page._wtDiagnostics = page._wtDiagnostics || {};
            Object.keys(page._wtDiagnostics).forEach(function (roomId) {
                if (!list.some(function (room) { return room && room.RoomId === roomId; })) {
                    delete page._wtDiagnostics[roomId];
                }
            });
            Object.keys(page._wtDiagnosticOpen || {}).forEach(function (roomId) {
                if (!list.some(function (room) { return room && room.RoomId === roomId; })) {
                    delete page._wtDiagnosticOpen[roomId];
                }
            });
            renderRooms(page, list);
            if (cards) scheduleCardExpiry(page);
            if (page._wtVisibleRoute === 'rooms') list.filter(function (room) {
                return (room.ParticipantUserIds || []).some(function (id) { return idKey(id) === idKey(page._wtIdentity.user); });
            }).forEach(function (room) { loadRoomMedia(page, room); });
            syncForm(page);
            return list;
        }).catch(function (err) {
            if (isPermissionError(err)) {
                clearPageData(page);
                setRoomsStatus(page, t('当前用户权限暂时无法确认，请重新打开页面。'));
                return [];
            }
            (page._wtRooms || []).forEach(function (room) {
                clearRoomStateExpiry(page, room.RoomId);
                page._wtRoomSequences[room.RoomId] = (page._wtRoomSequences[room.RoomId] || 0) + 1;
                page._wtRoomStates[room.RoomId] = { failed: true };
                delete page._wtMediaTargets[room.RoomId];
                delete page._wtMedia[room.RoomId];
                updateRoomPresentation(page, room);
            });
            page._wtRooms = null;
            page._wtRoomsPollFailures = Math.min(4, page._wtRoomsPollFailures + 1);
            page._wtRoomsRetryAt = Date.now() + Math.min(5000, 1000 * Math.pow(2, page._wtRoomsPollFailures - 1));
            if (cards) renderRooms(page, []);
            syncForm(page);
            setRoomsStatus(page, page._wtRoomsLoaded
                ? t('房间列表刷新失败，当前列表可能不是最新。将自动重试。')
                : t('房间列表加载失败，将自动重试。'));
            return [];
        }).then(function (result) {
            page._wtRoomsLoading = false;
            if (container) container.setAttribute('aria-busy', 'false');
            return result;
        });
    }

    function setButtonBusy(button, isBusy, busyLabel) {
        if (!button) {
            return;
        }

        if (isBusy) {
            button._wtOriginalLabel = button.textContent;
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            button.textContent = busyLabel;
        } else {
            button.disabled = false;
            button.removeAttribute('aria-busy');
            if (button._wtOriginalLabel) {
                button.textContent = button._wtStaticLabelKey ? t(button._wtStaticLabelKey) : button._wtOriginalLabel;
                button._wtOriginalLabel = null;
            }
        }
    }

    function control(page, roomId, action, button) {
        if (!pageActionAllowed(page)) return;
        if (action === 'resync') {
            return confirmAction(page, { title: t('重新同步'),
                text: t('暂时暂停双方并重新对齐，继续吗？'),
                confirmText: t('重新同步') }, roomId, function () { controlConfirmed(page, roomId, action); });
        }
        controlConfirmed(page, roomId, action);
    }

    function controlConfirmed(page, roomId, action) {
        if (!pageActionAllowed(page)) return;
        var label = actionLabels[action] || t('操作');
        setRoomBusy(page, roomId, true);
        clearRoomFeedback(page, roomId);
        roomFeedback(page, roomId, label + t('处理中…'), false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Action', 'POST', { Action: action })
            .then(function (result) {
                if (result && result.Error) {
                    roomFeedback(page, roomId, label + t('已发起，但部分客户端未完成，请检查客户端。'), true, true);
                } else if (action === 'resync') {
                    roomFeedback(page, roomId, t('重新同步已开始，可能暂停播放，请稍候。'), false, false);
                } else if (result && result.Users && result.Users.length > 0) {
                    roomFeedback(page, roomId, label + t('已向 ') + result.Users.length + t(' 个当前会话发送。'), false, false);
                } else if (result && result.Users) {
                    roomFeedback(page, roomId, label + t('没有可控制的当前会话。'), true, true);
                } else {
                    roomFeedback(page, roomId, label + t('已开始，等待完成。'), false, false);
                }
                return loadRooms(page);
            })
            .catch(function (err) {
                roomFeedback(page, roomId, label + t('失败：') + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, roomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function participantResync(page, roomId, button) {
        if (!pageActionAllowed(page)) return;
        setRoomBusy(page, roomId, true);
        clearRoomFeedback(page, roomId);
        roomFeedback(page, roomId, t('正在请求重新同步…'), false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Resync', 'POST')
            .then(function (result) {
                var status = result && result.Status;
                if (status === 'accepted') {
                    roomFeedback(page, roomId, t('请求已受理，将尝试暂停双方并对齐。'), false, false);
                } else if (status === 'busy') {
                    roomFeedback(page, roomId, result.Reason === 'resync_cooldown'
                        ? t('已请求重新同步，请稍后再试。')
                        : t('同步中，请等待完成。'), true, true);
                } else if (status === 'unavailable') {
                    roomFeedback(page, roomId, result.Reason === 'snapshot_unavailable'
                        ? t('当前播放会话暂时不可读，请稍后再试。')
                        : t('当前房间暂时不可用，请稍后再试。'), true, true);
                } else {
                    roomFeedback(page, roomId, t('重新同步请求未完成，请稍后再试。'), true, true);
                }
                return loadRooms(page);
            })
            .catch(function (err) {
                roomFeedback(page, roomId, t('重新同步请求失败：') + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, roomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function deleteRoom(page, room, button) {
        if (!pageActionAllowed(page)) return;
        var roomName = room.Name || t('未命名房间');
        var ending = room.CanEnd === true;
        return confirmAction(page, { title: page._wtVisibleRoute === 'admin' ? t('结束同步') : ending ? t('结束房间') : t('删除房间'),
            text: (ending ? t('结束“') : t('删除“')) + roomName + t('”？双方将退出房间。') + '\n' + t('参与者：') +
                (room.ParticipantUserIds || []).map(function (id) { return roomUserName(page, room, id); }).join(' / ') +
                (page._wtVisibleRoute === 'admin' ? '\n' + t('双方设备会继续各自播放。') : ''),
            confirmText: ending ? t('结束') : t('删除') }, room.RoomId, function () { deleteRoomConfirmed(page, room); });
    }

    function deleteRoomConfirmed(page, room) {
        if (!pageActionAllowed(page)) return;
        var roomName = room.Name || t('未命名房间');
        var ending = room.CanEnd === true;
        setRoomBusy(page, room.RoomId, true);
        roomFeedback(page, room.RoomId, ending ? t('结束中…') : t('删除中…'), false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(room.RoomId), 'DELETE')
            .then(function (result) {
                if (result && result.Deleted === false) {
                    if (page._wtIsAdmin !== true) throw new Error(t('房间不存在或已删除'));
                    clearRoomFeedback(page, room.RoomId);
                    notifyToast(page, t('房间已结束或已由其他管理员结束'));
                    return loadRooms(page);
                }
                clearRoomFeedback(page, room.RoomId);
                notifyToast(page, ending ? t('房间已结束') : t('房间已删除'));
                return loadRooms(page);
            })
            .catch(function (err) {
                roomFeedback(page, room.RoomId, (ending ? t('结束失败：') : t('删除失败：')) + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, room.RoomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function membership(page, room, action, button) {
        if (!pageActionAllowed(page)) return;
        if (action === 'leave') {
            return confirmAction(page, { title: t('暂离房间'),
                text: t('暂离“') + (room.Name || t('未命名房间')) + t('”？可重新加入，并尝试暂停对方。'),
                confirmText: t('暂离') }, room.RoomId, function () { membershipConfirmed(page, room, action); });
        }
        membershipConfirmed(page, room, action);
    }

    function membershipConfirmed(page, room, action) {
        if (!pageActionAllowed(page)) return;
        setRoomBusy(page, room.RoomId, true);
        clearRoomFeedback(page, room.RoomId);
        roomFeedback(page, room.RoomId, action === 'join' ? t('加入中…') : t('暂离中…'), false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(room.RoomId) + '/' + (action === 'join' ? 'Join' : 'Leave'), 'POST')
            .then(function (result) {
                var text = action === 'join'
                    ? t('已加入，请双方打开同一视频。')
                    : getLeaveFeedback(result);
                if (action === 'leave' && Number(result && result.PauseFailed) > 0) {
                    roomFeedback(page, room.RoomId, text, true, true);
                } else {
                    clearRoomFeedback(page, room.RoomId);
                    notifyToast(page, text);
                }
                return loadRooms(page);
            })
            .catch(function (err) {
                roomFeedback(page, room.RoomId, (action === 'join' ? t('加入') : t('暂离')) + t('失败：') + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, room.RoomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function getLeaveFeedback(result) {
        var attempted = Number(result && result.PauseAttempted) || 0;
        var succeeded = Number(result && result.PauseSucceeded) || 0;
        var failed = Number(result && result.PauseFailed) || 0;
        if (failed > 0) {
            return t('已暂离，对方暂停失败，请检查客户端。');
        }
        if (attempted > 0 && succeeded === attempted) {
            return t('已暂离，对方已暂停，可重新加入。');
        }
        return t('已暂离，自动同步已停止；对方暂停未确认。');
    }

    function createRoom(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        if (page.querySelector('#wtCreate').getAttribute('aria-busy') === 'true') return;
        page._wtCreateSubmitted = true;
        var validation = syncForm(page);
        if (validation.unavailable) return;
        var invalid = validation.ids.find(function (id) { return !!validation.errors[id]; });
        if (invalid) { page.querySelector('#' + invalid).focus(); return; }
        var nameInput = page.querySelector('#wtRoomName');
        var participantA = page.querySelector('#wtUserA');
        var participantB = page.querySelector('#wtUserB');
        var primary = page.querySelector('#wtPrimary');
        var createButton = page.querySelector('#wtCreate');
        var rawName = nameInput ? nameInput.value : '';
        var name = rawName.trim() ? rawName : '';
        var a = participantA ? participantA.value : '';
        var b = participantB ? participantB.value : '';

        if (page._wtUsers.length < 2 || !a || !b) {
            setStatus(page, t('至少需要选择两名参与者。'), true);
            return;
        }
        if (a === b) {
            setStatus(page, t('请选择两名不同的参与者。'), true);
            return;
        }
        var conflict = findRoomConflict(page, [a, b]);
        if (conflict) {
            var message = conflictMessage(page, conflict);
            setStatus(page, message, true);
            setFormHint(page, message, true);
            syncForm(page);
            return;
        }
        if (!primary || !primary.value || (primary.value !== a && primary.value !== b)) {
            setStatus(page, t('请选择参与者中的一人为主用户。'), true);
            return;
        }

        setButtonBusy(createButton, true, t('创建中…'));
        setCreateStatus(page, t('正在创建房间…'), false);
        apiSend(page, 'WatchTogether/Rooms', 'POST', {
            Name: name,
            Language: locale,
            ParticipantUserIds: [a, b],
            PrimaryUserId: primary.value
        }).then(function (result) {
            // A successful POST is a completed fact, independent of list refresh.
            setCreateStatus(page, '', false);
            nameInput.value = '';
            participantA.selectedIndex = 0;
            participantB.selectedIndex = page._wtUsers.length > 1 ? 1 : 0;
            primary.selectedIndex = 0;
            page._wtCreateTouched = {};
            page._wtCreateSubmitted = false;
            syncForm(page);
            notifyToast(page, t('房间已创建'));
            return loadRooms(page);
        }).catch(function (err) {
            setCreateStatus(page, t('创建失败：') + errorMessage(err), true);
            setStatus(page, t('创建失败：') + errorMessage(err), true);
        }).then(function () {
            setButtonBusy(createButton, false);
            syncForm(page);

        });
    }

    function bindPageEvents(page) {
        if (page._wtEventsBound) {
            return;
        }

        dom.addEventListener(page.querySelector('#wtSaveConfig'), 'click', function () {
            savePluginConfiguration(page);
        });
        dom.addEventListener(page.querySelector('#wtSaveGitHubToken'), 'click', function () {
            saveGitHubToken(page);
        });
        dom.addEventListener(page.querySelector('#wtClearGitHubToken'), 'click', function () {
            clearGitHubToken(page);
        });
        dom.addEventListener(page.querySelector('#wtCreate'), 'click', function () {
            createRoom(page);
        });
        dom.addEventListener(page.querySelector('#wtCreateInvitation'), 'click', function () {
            createInvitation(page);
        });
        dom.addEventListener(page.querySelector('#wtAcceptInvitation'), 'click', function () {
            acceptInvitation(page);
        });
        dom.addEventListener(page.querySelector('#wtInvitationAcceptCode'), 'input', function () {
            setAcceptInvitationError(page, '');
        });
        dom.addEventListener(page.querySelector('#wtCopyInvitationCode'), 'click', function (event) {
            copyInvitationCode(page, event.currentTarget);
        });
        dom.addEventListener(page.querySelector('#wtRoomName'), 'input', function () {
            page._wtCreateTouched = page._wtCreateTouched || {};
            page._wtCreateTouched['wtRoomName'] = true;
            syncForm(page);
        });
        dom.addEventListener(page.querySelector('#wtUserA'), 'change', function () {
            page._wtCreateTouched = page._wtCreateTouched || {};
            page._wtCreateTouched['wtUserA'] = true;
            syncForm(page);
        });
        dom.addEventListener(page.querySelector('#wtUserB'), 'change', function () {
            page._wtCreateTouched = page._wtCreateTouched || {};
            page._wtCreateTouched['wtUserB'] = true;
            syncForm(page);
        });
        dom.addEventListener(page.querySelector('#wtPrimary'), 'change', function () {
            page._wtCreateTouched = page._wtCreateTouched || {};
            page._wtCreateTouched['wtPrimary'] = true;
            syncForm(page);
        });
        page._wtEventsBound = true;
    }

    function View() {
        BaseView.apply(this, arguments);
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function () {
        BaseView.prototype.onResume.apply(this, arguments);
        var page = this.view;
        locale = currentLocale();
        page._wtActive = false;
        page._wtGeneration = (page._wtGeneration || 0) + 1;
        page._wtRoute = currentRoute(page);
        clearPageData(page);
        translateStatic(page);
        page._wtIdentity = pageIdentity();
        page._wtActive = true;
        if (!pageActionAllowed(page)) return;
        applyRoute(page);
        bindPageEvents(page);
        syncForm(page);
        // Best effort: preference failure must not interrupt the room UI.
        var languageStatus = page.querySelector('#wtLanguageStatus');
        if (languageStatus) { languageStatus.textContent = ''; languageStatus.hidden = true; }
        apiSend(page, 'WatchTogether/NotificationLanguage', 'POST', { Language: locale }).catch(function () {
            if (languageStatus && pageActionAllowed(page)) {
                languageStatus.textContent = t('通知语言未能保存，播放通知将继续使用之前的语言。');
                languageStatus.hidden = false;
            }
        });
        loading.show();

        loadUsers(page).then(function () {
            if (page.ownerDocument.hidden && (page._wtVisibleRoute === 'rooms' || page._wtVisibleRoute === 'admin')) return null;
            return loadRooms(page);
        }).then(function () {
            if (page._wtVisibleRoute === 'invite') return loadInvitations(page);
        }).then(function () {
            if (page._wtVisibleRoute === 'settings') {
                return loadPluginConfiguration(page).catch(function () { return null; }).then(function () { return loadPluginInfo(page); });
            }
        }).then(function () {
            loading.hide();
        }, function () {
            loading.hide();
        });

        page._wtRefreshVisible = function () {
            if (page.ownerDocument.hidden || !pageActionAllowed(page)) return;
            if (page._wtVisibleRoute === 'rooms' || page._wtVisibleRoute === 'admin') {
                if (Date.now() >= page._wtRoomsRetryAt) loadRooms(page);
            } else if (page._wtVisibleRoute === 'invite') {
                loadRooms(page);
                loadInvitations(page);
            }
        };
        var roomView = page._wtRoute === 'rooms' || page._wtRoute === 'admin';
        page._wtTimer = setInterval(page._wtRefreshVisible, roomView ? 1000 : 5000);
        window.addEventListener('focus', page._wtRefreshVisible);
        page.ownerDocument.addEventListener('visibilitychange', page._wtRefreshVisible);
    };

    View.prototype.onPause = function () {
        var page = this.view;
        page._wtActive = false;
        page._wtGeneration = (page._wtGeneration || 0) + 1;
        clearPageData(page);
        loading.hide();
        BaseView.prototype.onPause.apply(this, arguments);
    };

    return View;
});
