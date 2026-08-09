using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenCode.Workspace.RemoteBridge;

namespace OpenCode.Workspace.RemoteBridge.Tests;

public sealed class CloudflareAccessJwtValidatorTests : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly RemoteBridgeOptions _options = TestOptions.Create();

    [Fact]
    public async Task ValidToken_IsCryptographicallyAuthenticated()
    {
        var principal = await CreateValidator(_key).ValidateAsync(CreateToken(_key), default);

        Assert.True(principal?.Identity?.IsAuthenticated);
        Assert.Equal("person@example.test", principal!.FindFirst("email")?.Value);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
        => Assert.Null(await CreateValidator(_key).ValidateAsync(CreateToken(_key, expires: DateTimeOffset.UtcNow.AddMinutes(-1)), default));

    [Fact]
    public async Task WrongIssuer_IsRejected()
        => Assert.Null(await CreateValidator(_key).ValidateAsync(CreateToken(_key, issuer: "https://other.example.test"), default));

    [Fact]
    public async Task WrongAudience_IsRejected()
        => Assert.Null(await CreateValidator(_key).ValidateAsync(CreateToken(_key, audience: "other-audience"), default));

    [Fact]
    public async Task WrongSignature_IsRejected()
    {
        using var other = RSA.Create(2048);
        Assert.Null(await CreateValidator(_key).ValidateAsync(CreateToken(other), default));
    }

    [Fact]
    public async Task TokenWithoutStableIdentity_IsRejected()
        => Assert.Null(await CreateValidator(_key).ValidateAsync(CreateToken(_key, includeIdentity: false), default));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task MissingOrMalformedToken_IsRejected(string token)
        => Assert.Null(await CreateValidator(_key).ValidateAsync(token, default));

    private CloudflareAccessJwtValidator CreateValidator(RSA verificationKey)
    {
        var parameters = verificationKey.ExportParameters(false);
        var jwk = new CloudflareJwk("test-key", "RSA", "RS256", Encode(parameters.Modulus!), Encode(parameters.Exponent!));
        return new CloudflareAccessJwtValidator(new StaticJwksProvider(jwk), Options.Create(_options));
    }

    private string CreateToken(RSA signingKey, DateTimeOffset? expires = null, string? issuer = null, string? audience = null, bool includeIdentity = true)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "test-key", typ = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer ?? _options.Cloudflare.Issuer,
            aud = audience ?? _options.Cloudflare.Audience,
            sub = includeIdentity ? "identity-1" : null,
            email = includeIdentity ? "person@example.test" : null,
            nbf = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            exp = (expires ?? DateTimeOffset.UtcNow.AddMinutes(5)).ToUnixTimeSeconds(),
        }));
        var input = $"{header}.{payload}";
        return $"{input}.{Encode(signingKey.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() => _key.Dispose();

    private sealed class StaticJwksProvider(params CloudflareJwk[] keys) : ICloudflareJwksProvider
    {
        public Task<IReadOnlyList<CloudflareJwk>> GetKeysAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CloudflareJwk>>(keys);
    }
}

internal static class TestOptions
{
    public static RemoteBridgeOptions Create() => new()
    {
        RemoteAccess = new RemoteAccessOptions
        {
            Enabled = true,
            PublicOrigin = "https://remote.example.test",
            ListenerUrl = "http://127.0.0.1:38443",
            LocalHostBaseUrl = "http://127.0.0.1:38444",
        },
        Cloudflare = new CloudflareAccessOptions
        {
            TeamDomain = "https://team.cloudflareaccess.com",
            Issuer = "https://team.cloudflareaccess.com",
            Audience = "test-audience",
        },
    };
}
