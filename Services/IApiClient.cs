public interface IApiClient
{
    Task<T?> GetJsonAsync<T>(string path, CancellationToken ct = default);
}