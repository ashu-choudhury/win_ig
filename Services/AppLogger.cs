using System.IO;

namespace WinInstagram.Services;

public static class AppLogger
{
    private static readonly object _lock = new();
    private static readonly string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.txt");

    public static void Info(string category, string message) => Log("INFO", category, message, ConsoleColor.Cyan);
    public static void Success(string category, string message) => Log("SUCCESS", category, message, ConsoleColor.Green);
    public static void Warn(string category, string message) => Log("WARN", category, message, ConsoleColor.Yellow);
    public static void Error(string category, string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | Exception: {ex.Message}\n{ex.StackTrace}" : message;
        Log("ERROR", category, msg, ConsoleColor.Red);
    }

    private static void Log(string level, string category, string message, ConsoleColor color)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] [{category}] {message}";

        // Write to Console with color
        try
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(logLine);
            Console.ForegroundColor = oldColor;
        }
        catch { }

        // Write to log file
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFile, logLine + "\n");
            }
            catch { }
        }
    }
}
