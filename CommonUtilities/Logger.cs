using Microsoft.Extensions.Logging;

namespace CommonUtilities
{
    public static class Logger
    {
        private static readonly string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installation.log");

        public static void LogMessage(ILogger? logger, string appName, string message)
        {
            string timestampedMessage = $"{appName} - {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            logger?.LogInformation(timestampedMessage);
            File.AppendAllText(logFilePath, timestampedMessage + Environment.NewLine);
        }

        public static void LogError(ILogger? logger, string appName, string message)
        {
            string timestampedMessage = $"{appName} - {DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR: {message}";
            logger?.LogError(timestampedMessage);
            File.AppendAllText(logFilePath, timestampedMessage + Environment.NewLine);
        }

        public static void LogWarning(ILogger? logger, string appName, string message)
        {
            string timestampedMessage = $"{appName} - {DateTime.Now:yyyy-MM-dd HH:mm:ss} - WARNING: {message}";
            logger?.LogWarning(timestampedMessage);
            File.AppendAllText(logFilePath, timestampedMessage + Environment.NewLine);
        }

        // Other logging methods
    }
}