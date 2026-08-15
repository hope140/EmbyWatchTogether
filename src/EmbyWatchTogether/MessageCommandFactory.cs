using System.Globalization;
using MediaBrowser.Model.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Pure factory for Emby DisplayMessage commands.
    /// </summary>
    public static class MessageCommandFactory
    {
        public static MessageCommand DisplayMessage(string header, string text, int? timeoutMs = null)
        {
            return new MessageCommand
            {
                Header = header,
                Text = text,
                TimeoutMs = timeoutMs,
            };
        }

        public static GeneralCommand DisplayMessageCommand(string header, string text, int? timeoutMs = null)
        {
            var message = DisplayMessage(header, text, timeoutMs);
            var command = new GeneralCommand
            {
                Name = GeneralCommandType.DisplayMessage.ToString(),
            };
            command.Arguments["Header"] = message.Header;
            command.Arguments["Text"] = message.Text;
            if (message.TimeoutMs.HasValue)
            {
                command.Arguments["TimeoutMs"] = message.TimeoutMs.Value.ToString(CultureInfo.InvariantCulture);
            }

            return command;
        }
    }
}
