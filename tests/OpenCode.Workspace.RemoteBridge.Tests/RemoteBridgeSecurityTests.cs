using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Workspace.RemoteBridge;

namespace OpenCode.Workspace.RemoteBridge.Tests;

public sealed class RemoteBridgeSecurityTests
{
    [Fact]
    public async Task MissingAssertion_IsUnauthorized()
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Get, "/api/v1/remote/sessions", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task WrongHost_IsRejectedBeforeAuthentication()
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Get, "/api/v1/remote/sessions");
        request.Headers.Host = "evil.example.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task WrongOrigin_IsForbidden()
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Get, "/terminal/session-1");
        request.Headers.Add("Origin", "https://evil.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task StateChangingRequestWithoutOrigin_IsForbidden()
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Post, "/api/v1/remote/sessions/session-1/attachment-grants");
        request.Content = new StringContent("{\"takeover\":false}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(request)).StatusCode);
        Assert.Equal(0, host.Backend.AttachCalls);
    }

    [Fact]
    public async Task NormalNavigationWithoutOrigin_IsAllowedAndHasExactCsp()
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Get, "/terminal/session-1");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("person@example.test", await response.Content.ReadAsStringAsync());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("wss://remote.example.test", csp);
        Assert.DoesNotContain("*", csp);
    }

    [Fact]
    public async Task BrowserScript_HandlesGapResizeAndPresentationState_WithSequenceOnlyPersistence()
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Get, "/terminal/terminal.js");
        var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("c.type===\"gap\"", script, StringComparison.Ordinal);
        Assert.Contains("c.earliestAvailableSequence-1", script, StringComparison.Ordinal);
        Assert.Contains("new ResizeObserver", script, StringComparison.Ordinal);
        Assert.Contains("type:\"resize\",columns:xterm.cols,rows:xterm.rows", script, StringComparison.Ordinal);
        Assert.Contains("setState(\"Detached\")", script, StringComparison.Ordinal);
        Assert.Contains("setState(\"Disconnected\")", script, StringComparison.Ordinal);
        Assert.Contains("sessionStorage.setItem(sequenceKey", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage.setItem(\"attachment", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/workspaces")]
    [InlineData("/api/v1/remote/sessions/session-1/terminal/input")]
    [InlineData("/proxy/http://127.0.0.1")]
    public async Task NonAllowlistedRoutes_AreNotReachable(string path)
    {
        await using var host = await BridgeTestHost.StartAsync();
        using var request = BridgeTestHost.Request(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public void NonLoopbackOrHostnameListener_IsRejected()
    {
        var options = TestOptions.Create();
        options = options.WithRemote(listener: "http://localhost:38443");
        Assert.Throws<InvalidOperationException>(() => RemoteBridgeOptionsValidator.Validate(options));
    }
}

internal static class OptionsExtensions
{
    public static RemoteBridgeOptions WithRemote(this RemoteBridgeOptions options, string listener) => new()
    {
        RemoteAccess = new RemoteAccessOptions { Enabled = true, ListenerUrl = listener, PublicOrigin = options.RemoteAccess.PublicOrigin, LocalHostBaseUrl = options.RemoteAccess.LocalHostBaseUrl },
        Cloudflare = options.Cloudflare,
    };
}

internal sealed class BridgeTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    public FakeBackend Backend { get; }
    public WebSocketClient WebSockets => _app.GetTestServer().CreateWebSocketClient();

    private BridgeTestHost(WebApplication app, HttpClient client, FakeBackend backend) => (_app, Client, Backend) = (app, client, backend);

    public static async Task<BridgeTestHost> StartAsync()
    {
        var backend = new FakeBackend();
        var app = RemoteBridgeApplication.Build(customize: builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RemoteAccess:Enabled"] = "true",
                ["RemoteAccess:PublicOrigin"] = "https://remote.example.test",
                ["RemoteAccess:ListenerUrl"] = "http://127.0.0.1:38443",
                ["RemoteAccess:LocalHostBaseUrl"] = "http://127.0.0.1:38444",
                ["Cloudflare:TeamDomain"] = "https://team.cloudflareaccess.com",
                ["Cloudflare:Issuer"] = "https://team.cloudflareaccess.com",
                ["Cloudflare:Audience"] = "test-audience",
            });
            builder.Services.AddSingleton<ICloudflareAccessJwtValidator, AcceptingValidator>();
            builder.Services.AddSingleton<IRemoteBridgeBackend>(backend);
        });
        await app.StartAsync();
        return new BridgeTestHost(app, app.GetTestClient(), backend);
    }

    public static HttpRequestMessage Request(HttpMethod method, string path, bool authenticated = true, bool origin = false)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = "remote.example.test";
        if (authenticated) request.Headers.Add("Cf-Access-Jwt-Assertion", "valid");
        if (origin) request.Headers.Add("Origin", "https://remote.example.test");
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class AcceptingValidator : ICloudflareAccessJwtValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string assertion, CancellationToken cancellationToken)
        {
            ClaimsPrincipal? result = assertion == "valid" ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "identity-1"), new Claim("email", "person@example.test")], "test")) : null;
            return Task.FromResult(result);
        }
    }
}
