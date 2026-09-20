namespace Mod.Collector.Health;

// ILoggerProvider that captures recent log lines for GetDiagnostics replies.
internal sealed class LogCapture : ILoggerProvider
{
    private readonly Queue<string> _lines = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> GetLines(int max)
    {
        lock (_lock)
            return _lines.TakeLast(max).ToList();
    }

    internal void Write(string line)
    {
        lock (_lock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > 200) _lines.Dequeue();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    void IDisposable.Dispose() { }

    private sealed class CaptureLogger(LogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;

        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var msg = $"{DateTimeOffset.UtcNow:HH:mm:ss} [{level}] {category}: {formatter(state, exception)}";
            if (exception is not null) msg += $" -- {exception.Message}";
            capture.Write(msg);
        }
    }
}
