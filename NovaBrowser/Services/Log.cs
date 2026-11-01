using System.IO;
using System.Text;

namespace NovaBrowser.Services;

public static class Log
{
    private static readonly object Sync = new();
    private static readonly string LogFile = Path.Combine(BrowserData.DataRoot, "nova.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(BrowserData.DataRoot);
            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);
            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            lock (Sync)
            {
                File.AppendAllText(LogFile, builder.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the browser.
        }
    }
}
