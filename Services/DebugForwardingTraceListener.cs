using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MDTadusMod.Services;

public sealed class DebugForwardingTraceListener : TraceListener
{
    private readonly ILogBuffer _buffer;
    private readonly object _lineLock = new();
    private string _partial = string.Empty;

    public DebugForwardingTraceListener(ILogBuffer buffer)
    {
        _buffer = buffer;
    }

    public override void Write(string? message)
    {
        if (message is null) return;
        lock (_lineLock)
        {
            _partial += message;
        }
    }

    public override void WriteLine(string? message)
    {
        lock (_lineLock)
        {
            var full = _partial + (message ?? string.Empty);
            _partial = string.Empty;
            if (string.IsNullOrWhiteSpace(full)) return;
            // You can change LogLevel.Debug to Information if you want them visible when min level = Information
            _buffer.Log(LogLevel.Debug, full, scope: "Debug");
        }
    }
}