using Microsoft.Extensions.Logging;
 

namespace QTP.Common.Plugins
{
    public class PluginLogEventArgs : EventArgs
    {
        public string Message { get; }
        public LogLevel Level { get; }

        public PluginLogEventArgs(string message, LogLevel level = LogLevel.Information)
        {
            Message = message;
            Level = level;
        }
    }
}
