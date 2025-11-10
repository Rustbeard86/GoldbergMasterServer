using System.Runtime.CompilerServices;
using GoldbergMasterServer.Configuration;

namespace GoldbergMasterServer.Services;

/// <summary>
///     Provides centralized logging functionality with support for different verbosity levels
/// </summary>
public class LogService(
    LogLevel minimumLevel = LogLevel.Information,
    bool includeTimestamps = true,
    bool includeSourceInfo = true)
{
    private static readonly object Lock = new();

    private void WriteLog(
        LogLevel level,
        string message,
        string? source,
        string caller,
        string file,
        int lineNumber)
    {
        if (level < minimumLevel) return;

        lock (Lock)
        {
            var color = level switch
            {
                LogLevel.Trace or LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Information => ConsoleColor.White,
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

    public void Trace(string message, string? source = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog(LogLevel.Trace, message, source, caller, file, lineNumber);
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
        WriteLog(LogLevel.Information, message, source, caller, file, lineNumber);
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