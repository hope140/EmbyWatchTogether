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
    }
}
