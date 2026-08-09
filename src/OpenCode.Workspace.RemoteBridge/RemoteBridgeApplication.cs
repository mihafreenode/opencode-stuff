using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.RemoteBridge;

public static class RemoteBridgeApplication
{
    public static WebApplication Build(string[]? args = null, Action<WebApplicationBuilder>? customize = null)
        => Build(args, customize, packageRoot: null, userConfigPath: null);

    internal static WebApplication Build(string[]? args, Action<WebApplicationBuilder>? customize, string? packageRoot, string? userConfigPath)
    {
        var commandLineArgs = args ?? Array.Empty<string>();
        var builder = WebApplication.CreateBuilder(commandLineArgs);
        AddConfiguration(builder.Configuration, commandLineArgs, packageRoot, userConfigPath);
        customize?.Invoke(builder);
        var settings = new RemoteBridgeOptions();
        builder.Configuration.Bind(settings);
        RemoteBridgeOptionsValidator.Validate(settings);
        builder.Services.AddSingleton(Options.Create(settings));
        builder.Services.AddHttpClient();
        builder.Services.TryAddSingleton<ICloudflareJwksProvider>(services => new CloudflareJwksProvider(services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CloudflareJwksProvider)), services.GetRequiredService<IOptions<RemoteBridgeOptions>>()));
        builder.Services.TryAddSingleton<ICloudflareAccessJwtValidator, CloudflareAccessJwtValidator>();
        builder.Services.TryAddSingleton<IRemoteBridgeBackend, LocalHostRemoteBridgeBackend>();
        builder.Services.TryAddSingleton<BridgeGrantStore>();
        builder.Services.TryAddSingleton<RemoteTerminalProxy>();

        if (settings.RemoteAccess.Enabled)
        {
            var listener = new Uri(settings.RemoteAccess.ListenerUrl);
            var address = IPAddress.Parse(listener.Host);
            builder.WebHost.ConfigureKestrel(server => server.Listen(address, listener.Port, listen =>
            {
                if (listener.Scheme == Uri.UriSchemeHttps) listen.UseHttps();
                listen.Protocols = HttpProtocols.Http1AndHttp2;
            }));
        }

        var app = builder.Build();
        if (!settings.RemoteAccess.Enabled) return app;

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (LocalHostClientException exception)
            {
                context.Response.StatusCode = exception.Code is "already_attached" or "transfer_rejected" ? StatusCodes.Status409Conflict : StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new { exception.Code, exception.Message, exception.Recommendation });
            }
        });
        app.Use(async (context, next) =>
        {
            var publicOrigin = new Uri(settings.RemoteAccess.PublicOrigin);
            if (!HostMatches(context.Request.Host, publicOrigin))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !string.Equals(origin, publicOrigin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if ((context.WebSockets.IsWebSocketRequest || HttpMethods.IsPost(context.Request.Method)) && string.IsNullOrEmpty(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var assertion = context.Request.Headers[settings.Cloudflare.AccessAssertionHeader].ToString();
            var principal = string.IsNullOrWhiteSpace(assertion)
                ? null
                : await context.RequestServices.GetRequiredService<ICloudflareAccessJwtValidator>().ValidateAsync(assertion, context.RequestAborted);
            if (principal?.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            context.User = principal;

            if (context.Request.Path.StartsWithSegments("/terminal")) SetBrowserHeaders(context.Response, publicOrigin);
            await next();
        });

        app.MapGet("/terminal/vendor/xterm.js", () => EmbeddedWebAsset("OpenCode.Workspace.RemoteBridge.WebAssets.xterm.js", "text/javascript; charset=utf-8"));
        app.MapGet("/terminal/vendor/xterm.css", () => EmbeddedWebAsset("OpenCode.Workspace.RemoteBridge.WebAssets.xterm.css", "text/css; charset=utf-8"));
        app.MapGet("/terminal/terminal.js", () => Results.Text(RemoteTerminalAssets.Script, "text/javascript; charset=utf-8"));
        app.MapGet("/terminal/terminal.css", () => Results.Text(RemoteTerminalAssets.Style, "text/css; charset=utf-8"));
        app.MapGet("/terminal/{sessionId}", (HttpContext context, string sessionId) =>
        {
            var identity = context.User.FindFirst("email")?.Value ?? context.User.FindFirst("sub")?.Value ?? "Authenticated user";
            return Results.Text(RemoteTerminalAssets.Html.Replace("{{identity}}", WebUtility.HtmlEncode(identity), StringComparison.Ordinal), "text/html; charset=utf-8");
        });

        var api = app.MapGroup("/api/v1/remote");
        api.MapGet("/sessions", async (IRemoteBridgeBackend backend, CancellationToken cancellationToken) => Results.Ok(await backend.ListSessionsAsync(cancellationToken)));
        api.MapGet("/sessions/{sessionId}", async (string sessionId, IRemoteBridgeBackend backend, CancellationToken cancellationToken) => Results.Ok(await backend.GetSessionAsync(sessionId, cancellationToken)));
        api.MapGet("/sessions/{sessionId}/terminal", async (string sessionId, IRemoteBridgeBackend backend, CancellationToken cancellationToken) => Results.Ok(await backend.GetTerminalAsync(sessionId, cancellationToken)));
        api.MapPost("/sessions/{sessionId}/attachment-grants", async (string sessionId, AttachmentGrantRequest request, HttpContext context, IRemoteBridgeBackend backend, BridgeGrantStore grants, CancellationToken cancellationToken) =>
        {
            if (request.Takeover is null) return Results.BadRequest(new { code = "takeover_choice_required", message = "The attachment request must explicitly set takeover to true or false." });
            var attachment = await backend.AttachAsync(sessionId, request.Takeover.Value, request.ExpectedAttachmentId, cancellationToken);
            var owner = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst("email")?.Value ?? throw new InvalidOperationException("Validated identity has no stable subject.");
            var grant = grants.Create(owner, attachment, request.Takeover.Value);
            return Results.Ok(new AttachmentGrantResponse(grant.Token, grant.ExpiresUtc, attachment.SessionId, attachment.RuntimeId));
        });
        api.Map("/sessions/{sessionId}/terminal/ws", async (HttpContext context, string sessionId, RemoteTerminalProxy proxy) => await proxy.HandleAsync(context, sessionId));

        return app;
    }

    private static void AddConfiguration(ConfigurationManager configuration, string[] args, string? packageRoot, string? userConfigPath)
    {
        var executableDirectory = AppContext.BaseDirectory;
        configuration.AddJsonFile(Path.Combine(executableDirectory, "appsettings.json"), optional: true, reloadOnChange: false);

        packageRoot ??= Path.GetFullPath(Path.Combine(executableDirectory, "..", ".."));
        configuration.AddJsonFile(Path.Combine(packageRoot, "config", "remote-bridge", "appsettings.json"), optional: true, reloadOnChange: false);

        userConfigPath ??= Path.Combine(WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot(), "remote-bridge", "appsettings.json");
        configuration.AddJsonFile(userConfigPath, optional: true, reloadOnChange: false);

        // CreateBuilder adds these providers, but packaged and user JSON are added later.
        // Re-add them so deployment overrides retain the documented highest precedence.
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }

    private static bool HostMatches(HostString host, Uri origin)
        => string.Equals(host.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && (host.Port ?? (origin.Scheme == Uri.UriSchemeHttps ? 443 : 80)) == origin.Port;

    private static void SetBrowserHeaders(HttpResponse response, Uri publicOrigin)
    {
        var websocketOrigin = new UriBuilder(publicOrigin) { Scheme = publicOrigin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" }.Uri.GetLeftPart(UriPartial.Authority);
        response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' {websocketOrigin}; img-src 'self'; font-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static IResult EmbeddedWebAsset(string name, string contentType)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded web asset '{name}' was not found.");
        return Results.Stream(stream, contentType);
    }
}

public sealed record AttachmentGrantRequest(bool? Takeover, string ExpectedAttachmentId = "");
public sealed record AttachmentGrantResponse(string GrantToken, DateTimeOffset ExpiresUtc, string SessionId, string RuntimeId);
