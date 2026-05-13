using System;
using System.IO;

namespace AndroidMcp.Logging;

// stdout is the JSON-RPC channel under stdio transport; never write to it.
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly TextWriter Sink = Console.Error;
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    private static void Write(LogLevel level, string message)
    {
        if (level < MinLevel)
        {
            return;
        }

        DateTime now = DateTime.Now;
        lock (Gate)
        {
            Sink.Write(now.ToString("HH:mm:ss.fff"));
            Sink.Write(' ');
            Sink.Write(LevelTag(level));
            Sink.Write(' ');
            Sink.WriteLine(message);
        }
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "?    "
    };
}

internal enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}
