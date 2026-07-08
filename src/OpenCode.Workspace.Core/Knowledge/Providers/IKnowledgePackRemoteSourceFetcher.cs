namespace OpenCode.Workspace.Core.Knowledge.Providers;

public interface IKnowledgePackRemoteSourceFetcher
{
    Task<string> FetchAsync(string url, CancellationToken cancellationToken = default);
}

internal sealed class HttpKnowledgePackRemoteSourceFetcher : IKnowledgePackRemoteSourceFetcher
{
    private readonly HttpClient _httpClient;

    public HttpKnowledgePackRemoteSourceFetcher(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public Task<string> FetchAsync(string url, CancellationToken cancellationToken = default)
        => _httpClient.GetStringAsync(url, cancellationToken);
}
