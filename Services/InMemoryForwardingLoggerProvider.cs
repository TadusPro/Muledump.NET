using Microsoft.Extensions.Logging;

public sealed class InMemoryForwardingLoggerProvider : ILoggerProvider
{
    private readonly ILogBuffer _buffer;

    public InMemoryForwardingLoggerProvider()
    {
        // Late resolve via static accessor; or better: refactor to DI factory
        _buffer = ServiceLocator.Resolve<ILogBuffer>();
    }

    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(categoryName, _buffer);

    public void Dispose() { }

    private sealed class ForwardingLogger : ILogger
    {
        private readonly string _category;
        private readonly ILogBuffer _buffer;
        public ForwardingLogger(string category, ILogBuffer buffer)
        {
            _category = category;
            _buffer = buffer;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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

// Minimal service locator ONLY for bridging provider; prefer proper DI in production.
public static class ServiceLocator
{
    private static IServiceProvider? _provider;
    public static void SetProvider(IServiceProvider provider) => _provider = provider;
    public static T Resolve<T>() where T : notnull =>
        (T)(_provider?.GetService(typeof(T)) ?? throw new InvalidOperationException("Service not available."));
}