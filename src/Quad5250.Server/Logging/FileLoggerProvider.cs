using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Quad5250.Server.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
        Write($"=== Log opened {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal void Write(string line)
    {
        lock (_writeLock) _writer.WriteLine(line);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string category, FileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"{ts} {Level(logLevel)}: {_category}[{eventId.Id}] {msg}";
            if (exception is not null) line += Environment.NewLine + exception;
            _provider.Write(line);
        }

        private static string Level(LogLevel l) => l switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "    ",
        };
    }
}
