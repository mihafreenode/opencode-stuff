using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenCode.Workspace.RemoteBridge;

public sealed record BridgeGrant(string Token, string Owner, BackendAttachment Attachment, bool Takeover, DateTimeOffset ExpiresUtc);

public sealed class BridgeGrantStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, BridgeGrant> _grants = new(StringComparer.Ordinal);

    public BridgeGrant Create(string owner, BackendAttachment attachment, bool takeover)
    {
        RemoveExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var grant = new BridgeGrant(token, owner, attachment, takeover, DateTimeOffset.UtcNow.Add(Lifetime));
        _grants[token] = grant;
        return grant;
    }

    public BridgeGrant? Consume(string token, string owner)
    {
        RemoveExpired();
        if (!_grants.TryRemove(token, out var grant) || grant.ExpiresUtc <= DateTimeOffset.UtcNow || grant.Owner != owner)
        {
            return null;
        }
        return grant;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var grant in _grants)
            if (grant.Value.ExpiresUtc <= now)
                _grants.TryRemove(grant);
    }
}
