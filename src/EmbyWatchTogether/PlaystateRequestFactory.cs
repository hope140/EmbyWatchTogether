using System;
using MediaBrowser.Model.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Pure factory for Emby playstate requests. Seek requires a non-negative
    /// PositionTicks (same rule as the Python reference send_command).
    /// </summary>
    public static class PlaystateRequestFactory
    {
        public static PlaystateRequest Pause(string controllingUserId)
        {
            return new PlaystateRequest { Command = PlaystateCommand.Pause, ControllingUserId = controllingUserId };
        }

        public static PlaystateRequest Unpause(string controllingUserId)
        {
            return new PlaystateRequest { Command = PlaystateCommand.Unpause, ControllingUserId = controllingUserId };
        }

        public static PlaystateRequest PlayPause(string controllingUserId)
        {
            return new PlaystateRequest { Command = PlaystateCommand.PlayPause, ControllingUserId = controllingUserId };
        }

        public static PlaystateRequest Stop(string controllingUserId)
        {
            return new PlaystateRequest { Command = PlaystateCommand.Stop, ControllingUserId = controllingUserId };
        }

        public static PlaystateRequest Seek(string controllingUserId, long positionTicks)
        {
            if (positionTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionTicks), "Seek requires a non-negative PositionTicks.");
            }

            return new PlaystateRequest
            {
                Command = PlaystateCommand.Seek,
                SeekPositionTicks = positionTicks,
                ControllingUserId = controllingUserId,
            };
        }
    }
}
