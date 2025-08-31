using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

public sealed class LoggingHttpMessageHandler : DelegatingHandler
{
    private readonly ILogBuffer _log;

    public LoggingHttpMessageHandler(ILogBuffer log) => _log = log;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string requestId = Guid.NewGuid().ToString("N");

        await LogRequestAsync(request, requestId);

        HttpResponseMessage? response = null;
        Exception? error = null;
        try
        {
            response = await base.SendAsync(request, ct);
            return response;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            await LogResponseAsync(response, error, sw.Elapsed, requestId);
        }
    }

    private async Task LogRequestAsync(HttpRequestMessage req, string id)
    {
        string? body = null;
        if (req.Content != null)
        {
            body = await req.Content.ReadAsStringAsync();
            if (body.Length > 1000) body = body[..1000] + "...(truncated)";
        }

        _log.Log(LogLevel.Information,
            $"HTTP OUT {req.Method} {req.RequestUri} id={id} " +
            $"Headers=[{string.Join(",", req.Headers.Select(h=>$"{h.Key}:{string.Join('|',h.Value)}"))}] " +
            (body is null ? "" : $"Body={body}"));
    }

    private async Task LogResponseAsync(HttpResponseMessage? resp, Exception? ex, TimeSpan elapsed, string id)
    {
        if (ex != null)
        {
            _log.LogError($"HTTP ERR id={id} after {elapsed.TotalMilliseconds:F0}ms :: {ex.Message}", ex);
            return;
        }

        string? body = null;
        if (resp?.Content != null)
        {
            body = await resp.Content.ReadAsStringAsync();
            if (body.Length > 1000) body = body[..1000] + "...(truncated)";
        }

        var level = resp!.IsSuccessStatusCode ? LogLevel.Information :
            (int)resp.StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning;

        _log.Log(level,
            $"HTTP IN {resp.StatusCode} id={id} {elapsed.TotalMilliseconds:F0}ms " +
            $"Headers=[{string.Join(",", resp.Headers.Select(h=>$"{h.Key}:{string.Join('|',h.Value)}"))}] " +
            (body is null ? "" : $"Body={body}"));
    }
}