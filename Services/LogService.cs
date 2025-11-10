using System.Runtime.CompilerServices;

namespace GoldbergMasterServer.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
///     Provides centralized logging functionality with support for different verbosity levels
/// </summary>
public class LogService(
    LogLevel minimumLevel = LogLevel.Info,
    bool includeTimestamps = true,
    bool includeSourceInfo = true)
{
    private static readonly object Lock = new();

    private void WriteLog(LogLevel level, string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (level < minimumLevel) return;

        lock (Lock)
        {
            var color = level switch
            {
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.DarkRed,
                _ => ConsoleColor.White
            };

            var timestamp = includeTimestamps ? $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] " : "";
            var levelStr = $"[{level}] ";
            var sourceInfo = includeSourceInfo ? $"[{Path.GetFileName(file)}:{lineNumber} in {caller}] " : "";
            var sourceStr = !string.IsNullOrEmpty(source) ? $"[{source}] " : "";

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;

            Console.WriteLine($"{timestamp}{levelStr}{sourceInfo}{sourceStr}{message}");

            Console.ForegroundColor = originalColor;
        }
    }

    public void Debug(string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog(LogLevel.Debug, message, source, caller, file, lineNumber);
    }

    public void Info(string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog(LogLevel.Info, message, source, caller, file, lineNumber);
    }

    public void Warning(string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog(LogLevel.Warning, message, source, caller, file, lineNumber);
    }

    public void Error(string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog(LogLevel.Error, message, source, caller, file, lineNumber);
    }

    public void Critical(string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog(LogLevel.Critical, message, source, caller, file, lineNumber);
    }
}