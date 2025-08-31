using Microsoft.Extensions.Logging;

namespace MDTadusMod.Services;

public sealed class BufferLoggerProvider : ILoggerProvider
{
    private readonly ILogBuffer _buffer;
    private readonly LogLevel _min;

    public BufferLoggerProvider(ILogBuffer buffer)
    {
        _buffer = buffer;
        _min = LogLevel.Information; // adjust if you want Debug too
    }

    public ILogger CreateLogger(string categoryName) => new BufferLogger(categoryName, _buffer, _min);

    public void Dispose() { }

    private sealed class BufferLogger : ILogger
    {
        private readonly string _category;
        private readonly ILogBuffer _buffer;
        private readonly LogLevel _min;

        public BufferLogger(string category, ILogBuffer buffer, LogLevel min)
        {
            _category = category;
            _buffer = buffer;
            _min = min;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _min;

        public void Log<TState>(LogLevel logLevel, EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            _buffer.Log(logLevel, msg, exception, _category);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}