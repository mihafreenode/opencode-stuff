using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OpenCode.Workspace.RemoteBridge;

public interface ICloudflareAccessJwtValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string assertion, CancellationToken cancellationToken);
}

public interface ICloudflareJwksProvider
{
    Task<IReadOnlyList<CloudflareJwk>> GetKeysAsync(CancellationToken cancellationToken);
}

public sealed record CloudflareJwk(string Kid, string Kty, string Alg, string N, string E);

public sealed class CloudflareAccessJwtValidator(ICloudflareJwksProvider jwks, IOptions<RemoteBridgeOptions> options) : ICloudflareAccessJwtValidator
{
    private readonly CloudflareAccessOptions _options = options.Value.Cloudflare;

    public async Task<ClaimsPrincipal?> ValidateAsync(string assertion, CancellationToken cancellationToken)
    {
        try
        {
            var parts = assertion.Split('.');
            if (parts.Length != 3) return null;
            using var header = JsonDocument.Parse(Decode(parts[0]));
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            if (header.RootElement.GetProperty("alg").GetString() != "RS256") return null;
            var kid = header.RootElement.GetProperty("kid").GetString();
            var key = (await jwks.GetKeysAsync(cancellationToken)).SingleOrDefault(item => item.Kid == kid && item.Kty == "RSA" && (string.IsNullOrEmpty(item.Alg) || item.Alg == "RS256"));
            if (key is null) return null;

            using var rsa = RSA.Create(new RSAParameters { Modulus = Decode(key.N), Exponent = Decode(key.E) });
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var root = payload.RootElement;
            if (!root.TryGetProperty("iss", out var iss) || iss.GetString() != _options.Issuer) return null;
            if (!HasExactAudience(root, _options.Audience)) return null;
            if (!root.TryGetProperty("exp", out var exp) || exp.GetInt64() <= now) return null;
            if (root.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > now) return null;

            var claims = new List<Claim>();
            AddStringClaim(root, claims, "sub");
            AddStringClaim(root, claims, "email");
            if (claims.Count == 0) return null;
            var identity = new ClaimsIdentity(claims, "CloudflareAccess", "email", ClaimTypes.Role);
            return new ClaimsPrincipal(identity);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static bool HasExactAudience(JsonElement root, string expected)
    {
        if (!root.TryGetProperty("aud", out var audience)) return false;
        return audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == expected
            : audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == expected);
    }

    private static void AddStringClaim(JsonElement root, List<Claim> claims, string name)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            claims.Add(new Claim(name, value.GetString()!));
    }

    internal static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

public sealed class CloudflareJwksProvider(HttpClient httpClient, IOptions<RemoteBridgeOptions> options) : ICloudflareJwksProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<CloudflareJwk> _keys = Array.Empty<CloudflareJwk>();
    private DateTimeOffset _refreshAfter;

    public async Task<IReadOnlyList<CloudflareJwk>> GetKeysAsync(CancellationToken cancellationToken)
    {
        if (_keys.Count > 0 && DateTimeOffset.UtcNow < _refreshAfter) return _keys;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_keys.Count > 0 && DateTimeOffset.UtcNow < _refreshAfter) return _keys;
            var settings = options.Value.Cloudflare;
            var endpoint = new Uri(new Uri(settings.TeamDomain.TrimEnd('/') + "/"), "cdn-cgi/access/certs");
            try
            {
                using var document = JsonDocument.Parse(await httpClient.GetByteArrayAsync(endpoint, cancellationToken));
                _keys = document.RootElement.GetProperty("keys").EnumerateArray().Select(item => new CloudflareJwk(
                    item.GetProperty("kid").GetString() ?? string.Empty,
                    item.GetProperty("kty").GetString() ?? string.Empty,
                    item.TryGetProperty("alg", out var alg) ? alg.GetString() ?? string.Empty : string.Empty,
                    item.GetProperty("n").GetString() ?? string.Empty,
                    item.GetProperty("e").GetString() ?? string.Empty)).ToArray();
                _refreshAfter = DateTimeOffset.UtcNow.AddMinutes(settings.JwksRefreshMinutes);
            }
            catch when (_keys.Count > 0)
            {
                _refreshAfter = DateTimeOffset.UtcNow.AddSeconds(settings.JwksRefreshFailureRetrySeconds);
            }
            return _keys;
        }
        finally
        {
            _gate.Release();
        }
    }
}
