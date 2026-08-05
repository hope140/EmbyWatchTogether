using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Known Emby remote-control command names used by the plugin. The names match
    /// the strings declared by Emby clients in ClientCapabilities.SupportedCommands.
    /// </summary>
    public static class RemoteCommands
    {
        public const string Pause = "Pause";
        public const string Unpause = "Unpause";
        public const string PlayPause = "PlayPause";
        public const string Seek = "Seek";
        public const string Stop = "Stop";
        public const string DisplayMessage = "DisplayMessage";
    }

    /// <summary>
    /// Immutable result of probing one Emby session for remote-control capability.
    /// </summary>
    public sealed class SessionCapabilityReport
    {
        public SessionCapabilityReport(bool supportsRemoteControl, IEnumerable<string> supportedCommands)
        {
            SupportsRemoteControl = supportsRemoteControl;
            SupportedCommands = new HashSet<string>(
                supportedCommands ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool SupportsRemoteControl { get; }

        public IReadOnlyCollection<string> SupportedCommands { get; }

        public bool CanPause => SupportsRemoteControl && SupportedCommands.Contains(RemoteCommands.Pause);

        public bool CanUnpause => SupportsRemoteControl && SupportedCommands.Contains(RemoteCommands.Unpause);

        public bool CanPlayPause => SupportsRemoteControl && SupportedCommands.Contains(RemoteCommands.PlayPause);

        public bool CanSeek => SupportsRemoteControl && SupportedCommands.Contains(RemoteCommands.Seek);

        public bool CanStop => SupportsRemoteControl && SupportedCommands.Contains(RemoteCommands.Stop);

        public bool CanDisplayMessage => SupportsRemoteControl && SupportedCommands.Contains(RemoteCommands.DisplayMessage);

        /// <summary>
        /// A session can drive a two-person room only when pause, unpause and seek
        /// are all available (same gate as the Python reference implementation).
        /// </summary>
        public bool CanControlPlayback => SupportsRemoteControl && CanPause && CanUnpause && CanSeek;
    }

    /// <summary>
    /// Pure capability decision logic, kept free of Emby runtime objects so it can
    /// be unit tested without a running server.
    /// </summary>
    public static class CapabilityProbe
    {
        /// <summary>
        /// Merges session-level and capability-level command lists, then decides
        /// remote-control support. A non-empty command list is treated as evidence
        /// of a capable connection, mirroring the Python reference fallback.
        /// </summary>
        public static SessionCapabilityReport Probe(
            bool supportsRemoteControl,
            IEnumerable<string> sessionCommands,
            IEnumerable<string> capabilityCommands)
        {
            var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddRange(commands, sessionCommands);
            AddRange(commands, capabilityCommands);

            bool remoteControl = supportsRemoteControl || commands.Count > 0;
            return new SessionCapabilityReport(remoteControl, commands);
        }

        /// <summary>
        /// Thin adapter over the Emby 4.9 SessionInfo model.
        /// </summary>
        public static SessionCapabilityReport Probe(SessionInfo session)
        {
            if (session == null)
            {
                return new SessionCapabilityReport(false, Enumerable.Empty<string>());
            }

            return Probe(
                session.SupportsRemoteControl,
                session.SupportedCommands,
                session.Capabilities?.SupportedCommands);
        }

        private static void AddRange(ISet<string> target, IEnumerable<string> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (string item in source)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    target.Add(item.Trim());
                }
            }
        }
    }
}
