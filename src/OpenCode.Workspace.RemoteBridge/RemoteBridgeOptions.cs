using System.Net;

namespace OpenCode.Workspace.RemoteBridge;

public sealed class RemoteBridgeOptions
{
    public RemoteAccessOptions RemoteAccess { get; init; } = new();
    public CloudflareAccessOptions Cloudflare { get; init; } = new();
}

public sealed class RemoteAccessOptions
{
    public bool Enabled { get; init; }
    public string PublicOrigin { get; init; } = string.Empty;
    public string ListenerUrl { get; init; } = string.Empty;
    public string LocalHostBaseUrl { get; init; } = string.Empty;
}

public sealed class CloudflareAccessOptions
{
    public string TeamDomain { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string AccessAssertionHeader { get; init; } = "Cf-Access-Jwt-Assertion";
    public int JwksRefreshMinutes { get; init; } = 15;
    public int JwksRefreshFailureRetrySeconds { get; init; } = 30;
}

public static class RemoteBridgeOptionsValidator
{
    public static void Validate(RemoteBridgeOptions options)
    {
        if (!options.RemoteAccess.Enabled) return;

        var listener = RequireAbsoluteHttpUri(options.RemoteAccess.ListenerUrl, nameof(options.RemoteAccess.ListenerUrl));
        if (!IPAddress.TryParse(listener.Host, out var listenerAddress) || !IPAddress.IsLoopback(listenerAddress))
            throw new InvalidOperationException("RemoteAccess:ListenerUrl must use an explicit loopback IP address, not a hostname or non-loopback address.");
        if (!string.IsNullOrEmpty(listener.AbsolutePath.Trim('/')) || !string.IsNullOrEmpty(listener.Query) || !string.IsNullOrEmpty(listener.Fragment))
            throw new InvalidOperationException("RemoteAccess:ListenerUrl must contain only a scheme, explicit loopback IP, and port.");

        var publicOrigin = RequireAbsoluteHttpUri(options.RemoteAccess.PublicOrigin, nameof(options.RemoteAccess.PublicOrigin));
        if (publicOrigin.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("RemoteAccess:PublicOrigin must use HTTPS so browser terminal connections use WSS.");
        if (!string.IsNullOrEmpty(publicOrigin.AbsolutePath.Trim('/')) || !string.IsNullOrEmpty(publicOrigin.Query) || !string.IsNullOrEmpty(publicOrigin.Fragment))
            throw new InvalidOperationException("RemoteAccess:PublicOrigin must be an exact origin without a path, query, or fragment.");

        var localHost = RequireAbsoluteHttpUri(options.RemoteAccess.LocalHostBaseUrl, nameof(options.RemoteAccess.LocalHostBaseUrl));
        if (!IPAddress.TryParse(localHost.Host, out var localAddress) || !IPAddress.IsLoopback(localAddress))
            throw new InvalidOperationException("RemoteAccess:LocalHostBaseUrl must use an explicit loopback IP address.");

        var teamDomain = RequireAbsoluteHttpsUri(options.Cloudflare.TeamDomain, nameof(options.Cloudflare.TeamDomain));
        _ = RequireAbsoluteHttpsUri(options.Cloudflare.Issuer, nameof(options.Cloudflare.Issuer));
        if (!string.IsNullOrEmpty(teamDomain.AbsolutePath.Trim('/')) || !string.IsNullOrEmpty(teamDomain.Query) || !string.IsNullOrEmpty(teamDomain.Fragment))
            throw new InvalidOperationException("Cloudflare:TeamDomain must be an HTTPS origin.");
        if (string.IsNullOrWhiteSpace(options.Cloudflare.Audience)) throw new InvalidOperationException("Cloudflare:Audience is required.");
        if (string.IsNullOrWhiteSpace(options.Cloudflare.AccessAssertionHeader)) throw new InvalidOperationException("Cloudflare:AccessAssertionHeader is required.");
        if (options.Cloudflare.JwksRefreshMinutes < 1 || options.Cloudflare.JwksRefreshFailureRetrySeconds < 1)
            throw new InvalidOperationException("Cloudflare JWKS refresh settings must be positive.");
    }

    private static Uri RequireAbsoluteHttpUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.Port <= 0 || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException($"{name} must be an absolute HTTP(S) URI with a port.");
        return uri;
    }

    private static Uri RequireAbsoluteHttpsUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{name} must be an absolute HTTPS URI.");
        return uri;
    }
}
