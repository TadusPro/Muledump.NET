using Microsoft.Extensions.Logging;

public interface ILogBuffer
{
    void Log(LogLevel level, string message, Exception? ex = null, string? scope = null);
    void LogError(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
    void LogCritical(string message, Exception? ex = null) => Log(LogLevel.Critical, message, ex);
    IReadOnlyList<string> Snapshot();
    Task<string> PersistAsync();
}