# Emby Watch Together

[中文](README.md) · English

Watch a video with another person on the same Emby Server. Once both of you open the same video, the plugin coordinates pauses, resumes, and playback position. It installs directly on Emby Server.

Each room has two people with separate accounts. Each account can join one room at a time. Both clients need remote pause, resume, and seek controls. We recommend 1× playback speed on both clients.

For your first install, choose the stable release and download `EmbyWatchTogether.zip` under **Assets**.

**[Download stable](https://github.com/hope140/EmbyWatchTogether/releases/latest)** · [Install and start watching](#install) · [Client compatibility](docs/client-compatibility.md)

## Install

1. Stop Emby Server. Extract the stable release package and place `Emby.Plugins.WatchTogether.dll` directly in the `plugins` folder in the server data directory.
2. Start Emby Server. Check **Dashboard → Plugins → Watch Together** to confirm that the plugin and its settings page are available.

Back up the old DLL before upgrading so you can roll back.

### Get started

1. Sign in to the same server with separate accounts and open **Watch Together** from the Emby user menu. It may appear as **同映**.
2. Ask an administrator to create a room, or create a one-time code and have the other person accept it on the **Invitations** page.
3. Open the same video on both clients. Select **Join room** under **My rooms** if needed and wait for playback to align.

![Illustration of the Watch Together room page](docs/images/watch-together-preview.png)

## What it does

Under **My rooms**, you can check both users’ playback status, temporarily leave sync, or request a resync. Administrators can see all rooms and their playback status and can end sync. When the primary user switches videos, the plugin tries to open the same video on the other client and realign playback.

<a id="interface-language-and-translations"></a>

The interface is available in Simplified Chinese, Traditional Chinese, and English. See the [user guide](docs/user-guide.md#english) for invitations, room controls, settings, and language behavior.

## Compatibility and help

- The plugin is built against the Emby Server 4.9 API. See the [compatibility record](docs/client-compatibility.md) for client observations and scenarios that still need testing. Sync accuracy depends on the playback state reported by the clients.
- If the room keeps waiting, check that both users are online, have joined the room, have opened the same video, and are using clients with the required remote controls. Request a resync on the room page if needed.
- When reporting a problem, include redacted diagnostic JSON, the plugin and both client versions, the time, the steps, and what you expected and observed. Remove tokens and private log details before sharing.

## Choose a version

| Channel | Current version | For whom |
| --- | --- | --- |
| Stable | [v1.8.0.0](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.8.0.0) · [notes](docs/releases/v1.8.0.0.md) | First installs and everyday use |
| Beta | [v1.8.0.1](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.8.0.1) · [notes](docs/releases/v1.8.0.1.md) | For users who want to test |

[All release notes](docs/releases/README.md) · [Documentation index](docs/README.md) · [Contribute translations](docs/translations.md)

The project uses the [Apache License 2.0](LICENSE). See the [image notes](docs/images/README.md) for screenshot sources and licensing.
