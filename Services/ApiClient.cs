using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MDTadusMod.Services;

public sealed class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    private readonly ILogBuffer _log;

    public ApiClient(HttpClient http, ILogBuffer log)
    {
        _http = http;
        _log = log;
    }

    public async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct = default)
    {
        try
        {
            // Accept absolute or relative path
            var resp = await _http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.Log(LogLevel.Warning, $"GET {resp.RequestMessage?.RequestUri} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogError($"GET {path} failed", ex);
            throw;
        }
    }

    // (Optional) helper for raw responses
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.SendAsync(request, ct);
            return resp;
        }
        catch (Exception ex)
        {
            _log.LogError($"Request {request.Method} {request.RequestUri} failed", ex);
            throw;
        }
    }
}