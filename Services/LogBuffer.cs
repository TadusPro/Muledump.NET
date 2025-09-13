using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

public sealed class LogBuffer : ILogBuffer
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly object _fileLock = new();

    private static string LogDir => FileSystem.AppDataDirectory;
    private static string CurrentFile => Path.Combine(LogDir, "session.log");

    public void Log(LogLevel level, string message, Exception? ex = null, string? scope = null)
    {
        // Compact local-time timestamp (HHmmss)
        var ts = DateTime.Now.ToString("hh:mm:ss");

        var sb = new StringBuilder()
            .Append('[').Append(ts).Append(']')
            .Append(' ')
            .Append('[').Append(level).Append(']');

        if (!string.IsNullOrEmpty(scope))
            sb.Append(" [").Append(scope).Append(']');

        sb.Append(' ').Append(message);

        if (ex != null)
        {
            sb.Append(" :: ")
              .Append(ex.GetType().Name).Append(": ").Append(ex.Message)
              .Append('\n').Append(ex.StackTrace);
        }

        var line = sb.ToString();

        _entries.Enqueue(line);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }

        lock (_fileLock)
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(CurrentFile, line + Environment.NewLine);
        }
    }

    public IReadOnlyList<string> Snapshot() => _entries.ToArray();

    public Task<string> PersistAsync()
    {
        lock (_fileLock)
        {
            // Return current file path
            return Task.FromResult(CurrentFile);
        }
    }
}