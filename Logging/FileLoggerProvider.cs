using System.Text;
using System.Text.Json;

namespace SunatXmlApi.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string directory;

    public FileLoggerProvider(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        // Verify write access at startup so persistence failures are visible immediately.
        using var stream = File.Open(
            Path.Combine(directory, $"sunat-{DateTime.UtcNow:yyyy-MM-dd}.log"),
            FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = JsonSerializer.Serialize(new
        {
            Timestamp = now,
            Level = level.ToString(),
            Category = category,
            EventId = eventId.Id,
            Message = message,
            Exception = exception?.ToString()
        });

        lock (gate)
        {
            File.AppendAllText(
                Path.Combine(directory, $"sunat-{now:yyyy-MM-dd}.log"),
                entry + Environment.NewLine,
                Encoding.UTF8);
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
