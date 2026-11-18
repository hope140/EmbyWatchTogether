# Emby Watch Together

[中文](README.md) · English

Watch together with another person on the same Emby Server. Create a room, open the same video, and let the server coordinate playback and position. The plugin runs inside Emby Server and needs no separate synchronization service.

[Choose a version](#choose-a-version) · [Install](#install) · [Get started](#get-started) · [Language](#interface-language-and-translations) · [Help](#compatibility-and-help)

![Illustration of the Watch Together room page](docs/images/watch-together-preview.png)

## Choose a version

| Channel | Current version | For whom |
| --- | --- | --- |
| Stable | [v1.7.3.0](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.7.3.0) · [notes](docs/releases/v1.7.3.0.md) | Users who prefer stable releases |
| Beta | [v1.7.3.3](https://github.com/hope140/EmbyWatchTogether/releases/tag/v1.7.3.3) · [notes](docs/releases/v1.7.3.3.md) | Users testing recent improvements |

The beta adds Simplified Chinese, Traditional Chinese, and English interfaces and saves playback-notification language per account. Playback notifications have not yet been accepted in real-client testing. See all [release notes](docs/releases/README.md) and the [client compatibility record](docs/client-compatibility.md).

## What it does

- Administrators can create a room for two users and select a primary user. Users can also create a self-service room with a one-time invitation code.
- Once both users open the same video, the plugin coordinates pause, seek, and resume, and synchronizes explicit playback controls during viewing.
- When the primary user changes media, the plugin attempts to open it for the other participant and realign playback. A participant changing media does not make the primary follow. Recovery from brief disconnections and significant drift is bounded; the plugin does not continuously seek to chase small differences.
- View room status, control playback, and export redacted diagnostics. Administrators can adjust notifications, stop behavior, and the update channel.

## Install

1. Download `EmbyWatchTogether.zip` from the release page for the version chosen above.
2. Stop Emby Server. Extract the archive and place `Emby.Plugins.WatchTogether.dll` directly in the server data directory's `plugins` folder, without an extra subfolder.
3. Start Emby Server and check **Dashboard → Plugins → Watch Together** for the plugin and its settings page.

Back up the old DLL before upgrading so you can roll back. The package contains one DLL; no source checkout or sidecar process is needed.

## Get started

Open **Watch Together** from the Emby user menu (on a Chinese-language server, the beta menu is named “同映”; actual display remains unconfirmed). Administrators can use the following pages. Administration is available in the beta.

| Page | Purpose |
| --- | --- |
| Rooms | View status, join or leave, control playback, resync, and view diagnostics. |
| Create and invite | Create a room for two users and select a primary, or create and accept invitations. |
| Administration | View all rooms, playback state and primary user progress. Confirm to end synchronization. |
| Settings | Configure stop behavior, notifications, and the update channel. |

Regular users can access rooms and invitations. Administrators can also enter from the plugin configuration page.

1. An administrator creates a room, or one user creates an invitation code and sends it promptly to the other user to accept.
2. Both users sign in to the same server and open the same video on their clients. Join the room if needed.
3. Wait for the status to refresh and synchronization to finish. Allow the clients and server time to process pauses, resumes, and seeks.

An invitation expires after 15 minutes, can be accepted once, and is lost if the server restarts before acceptance. Leaving a room preserves membership so you can rejoin; ending it removes both members' relationship and does not delete media.

## Settings and updates

Administrators can control whether stopping pauses the other participant, stop notices, and synchronization notices; all are enabled by default. Saved settings take effect on the next synchronization cycle.

Use **Dashboard → Scheduled Tasks → Watch Together → Update Plugin** to schedule, disable, or run updates. The `stable` channel uses GitHub Latest; `beta` selects the highest canonical version among public non-draft stable and prerelease releases. The plugin verifies the signature, size, hash, assembly name, and version before installation. Restart Emby when prompted. An optional GitHub token is used for beta Releases API queries; it is not displayed again, and anonymous queries also work.

## Interface language and translations

**Version 1.7.3.3 beta:** The page first tries the Emby host's interface language, then the page culture, page language, and browser language. Simplified Chinese and Traditional Chinese locales use their respective scripts; other locales currently use English. The language is checked when the page is opened; reopen or refresh the page after changing Emby's language. Some clients or hosts may not expose the language API, so the fallback may differ from your Emby setting.

Playback notifications use the account’s language from the last successful page visit. Across devices, the last save received by the server wins; the default is Simplified Chinese. Custom administrator messages remain unchanged; administrator update broadcasts remain Chinese.

An empty name is generated in the creating page’s language and remains fixed; an invitation creates the room in the accepting page’s language. Existing and custom room names are not renamed. If saving the language fails, the page reports it and the previous language remains in effect.

Contributions for other languages are welcome; see the [translation guide](docs/translations.md).

## Compatibility and help

- Each room has exactly two users on the same Emby Server. Multiple participants and cross-server synchronization are not supported.
- The plugin is built against the Emby Server 4.9 API. Clients need remote pause, resume, and seek support, and both users should play at 1× speed. Synchronization relies on server session snapshots and is not frame-accurate. See the [client compatibility record](docs/client-compatibility.md) for evidence and outstanding validation.
- If a room keeps waiting, check that both users have joined, are online, have opened the same video, and have clients that support remote control. Status may take a refresh cycle to update; use resync if needed.
- For troubleshooting, export redacted diagnostic JSON and provide the time, client names and versions, room status, and expected and actual behavior. Do not publish tokens or full server logs.

Developers and maintainers can start at the [documentation index](docs/README.md); build, API, and troubleshooting details are in the [technical documentation](docs/technical.md).

The project is licensed under [Apache License 2.0](LICENSE). See the [image notes](docs/images/README.md) for image sources and rights.
