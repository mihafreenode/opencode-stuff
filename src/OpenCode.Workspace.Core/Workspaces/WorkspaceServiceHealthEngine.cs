using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public interface IWorkspaceServiceHealthProvider
{
    bool CanHandle(WorkspaceSnapshot snapshot);
    IReadOnlyList<WorkspaceServiceHealthDefinition> DescribeServices(WorkspaceSnapshot snapshot);
}

public sealed class WorkspaceServiceHealthDefinition
{
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ActionLabel { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public WorkspaceServiceProbeType ProbeType { get; init; } = WorkspaceServiceProbeType.Custom;
    public string ProviderKey { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public TimeSpan RefreshInterval { get; init; }
    public Func<WorkspaceServiceProbeResult, WorkspaceServiceProbeClassification>? Validator { get; init; }
    public bool RequiresRunningRuntime { get; init; } = true;
    public string OpenUrl { get; init; } = string.Empty;
}

public sealed class WorkspaceServiceProbeResult
{
    public bool IsReachable { get; init; }
    public HttpStatusCode? StatusCode { get; init; }
    public TimeSpan? Latency { get; init; }
    public string RedirectLocation { get; init; } = string.Empty;
    public string ResponseHeaders { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string ResponseSample { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
}

public sealed class WorkspaceServiceProbeClassification
{
    public WorkspaceHealthStatus Status { get; init; } = WorkspaceHealthStatus.Attention;
    public string StatusLabel { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string PrimaryUrl { get; init; } = string.Empty;
    public string ContentValidation { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceHealthFact> Highlights { get; init; } = Array.Empty<WorkspaceHealthFact>();
    public IReadOnlyList<WorkspaceHealthFact> Evidence { get; init; } = Array.Empty<WorkspaceHealthFact>();
    public string Confidence { get; init; } = string.Empty;
}

public interface IWorkspaceServiceProbeRunner
{
    Task<WorkspaceServiceProbeResult> ProbeTcpAsync(string host, int port, CancellationToken cancellationToken);
    Task<WorkspaceServiceProbeResult> ProbeHttpAsync(Uri endpoint, CancellationToken cancellationToken);
}

public static class WorkspaceServiceHealthEngine
{
    private static readonly IWorkspaceServiceHealthProvider[] Providers =
    [
        new OracleWorkspaceServiceHealthProvider(),
        new PostgreSqlWorkspaceServiceHealthProvider(),
        new AnalyticsWorkspaceServiceHealthProvider(),
    ];

    public static async Task<IReadOnlyList<WorkspaceServiceHealthSnapshot>> BuildAsync(WorkspaceSnapshot snapshot, IWorkspaceServiceProbeRunner? probeRunner = null, CancellationToken cancellationToken = default)
    {
        probeRunner ??= new DefaultWorkspaceServiceProbeRunner();
        var definitions = Providers
            .Where(provider => provider.CanHandle(snapshot))
            .SelectMany(provider => provider.DescribeServices(snapshot))
            .GroupBy(item => item.ServiceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var services = new List<WorkspaceServiceHealthSnapshot>(definitions.Count);
        foreach (var definition in definitions)
        {
            services.Add(await ProbeServiceAsync(snapshot, definition, probeRunner, cancellationToken).ConfigureAwait(false));
        }

        return DecorateDiscoveredApplications(services);
    }

    internal static WorkspaceServiceProbeClassification ClassifyProbeResult(WorkspaceServiceHealthDefinition definition, WorkspaceServiceProbeResult result)
    {
        if (definition.Validator is not null)
        {
            return definition.Validator(result);
        }

        return definition.ProbeType switch
        {
            WorkspaceServiceProbeType.Tcp or WorkspaceServiceProbeType.Database => ClassifyTcpResult(definition, result),
            WorkspaceServiceProbeType.Http or WorkspaceServiceProbeType.Https => ClassifyHttpResult(definition, result),
            _ => new WorkspaceServiceProbeClassification
            {
                Status = result.IsReachable ? WorkspaceHealthStatus.Healthy : WorkspaceHealthStatus.Unavailable,
                StatusLabel = result.IsReachable ? "Available" : "Unavailable",
                Summary = result.IsReachable ? "Service is responding." : "Service is unavailable.",
                Recommendation = string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
                PrimaryUrl = FormatEndpoint(string.IsNullOrWhiteSpace(definition.OpenUrl) ? definition.Endpoint : definition.OpenUrl),
                Highlights = BuildLatencyHighlight(result.Latency),
                Evidence = BuildProviderEvidence(result),
                Confidence = result.IsReachable ? "MEDIUM" : "HIGH",
            },
        };
    }

    private static async Task<WorkspaceServiceHealthSnapshot> ProbeServiceAsync(WorkspaceSnapshot snapshot, WorkspaceServiceHealthDefinition definition, IWorkspaceServiceProbeRunner probeRunner, CancellationToken cancellationToken)
    {
        if (definition.RequiresRunningRuntime && snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            var unavailableTimestamp = DateTimeOffset.UtcNow;
            return new WorkspaceServiceHealthSnapshot
            {
                ServiceId = definition.ServiceId,
                Name = definition.Name,
                Category = definition.Category,
                StatusLabel = "Unavailable",
                Summary = "Start the workspace runtime to use this service.",
                Applications = Array.Empty<string>(),
                Endpoint = definition.Endpoint,
                PrimaryUrl = ResolvePrimaryUrl(definition, null, null),
                ProbeType = definition.ProbeType,
                Status = WorkspaceHealthStatus.Attention,
                Highlights = Array.Empty<WorkspaceHealthFact>(),
                Evidence =
                [
                    new WorkspaceHealthFact { Label = "Runtime", Value = "Workspace runtime is not running." },
                    new WorkspaceHealthFact { Label = "Last checked", Value = FormatTimestamp(unavailableTimestamp) },
                ],
                Confidence = "HIGH",
                Timestamp = unavailableTimestamp,
                Recommendation = "Open Workspace.",
                ActionLabel = ResolveActionLabel(definition),
                OpenUrl = string.Empty,
                RefreshInterval = definition.RefreshInterval,
                ProviderKey = definition.ProviderKey,
            };
        }

        var result = await ExecuteProbeAsync(definition, probeRunner, cancellationToken).ConfigureAwait(false);
        var classification = ClassifyProbeResult(definition, result);
        var timestamp = DateTimeOffset.UtcNow;
        return new WorkspaceServiceHealthSnapshot
        {
            ServiceId = definition.ServiceId,
            Name = definition.Name,
            Category = definition.Category,
            StatusLabel = string.IsNullOrWhiteSpace(classification.StatusLabel) ? FormatStatusLabel(classification.Status) : classification.StatusLabel,
            Summary = string.IsNullOrWhiteSpace(classification.Summary) ? definition.Description : classification.Summary,
            Applications = Array.Empty<string>(),
            Endpoint = definition.Endpoint,
            PrimaryUrl = ResolvePrimaryUrl(definition, classification, result),
            ProbeType = definition.ProbeType,
            Status = classification.Status,
            Latency = result.Latency,
            Highlights = classification.Highlights,
            Evidence = BuildDetailedEvidence(result, classification, timestamp),
            Confidence = classification.Confidence,
            Timestamp = timestamp,
            Recommendation = string.IsNullOrWhiteSpace(classification.Recommendation) ? definition.Recommendation : classification.Recommendation,
            ActionLabel = ResolveActionLabel(definition),
            OpenUrl = ResolveOpenUrl(definition, classification, result),
            RefreshInterval = definition.RefreshInterval,
            ProviderKey = definition.ProviderKey,
        };
    }

    private static Task<WorkspaceServiceProbeResult> ExecuteProbeAsync(WorkspaceServiceHealthDefinition definition, IWorkspaceServiceProbeRunner probeRunner, CancellationToken cancellationToken)
    {
        if (definition.ProbeType is WorkspaceServiceProbeType.Http or WorkspaceServiceProbeType.Https)
        {
            return probeRunner.ProbeHttpAsync(new Uri(definition.Endpoint), cancellationToken);
        }

        if (!TryParseHostAndPort(definition.Endpoint, out var host, out var port))
        {
            return Task.FromResult(new WorkspaceServiceProbeResult { FailureReason = "Endpoint is not valid for TCP probing." });
        }

        return probeRunner.ProbeTcpAsync(host, port, cancellationToken);
    }

    private static WorkspaceServiceProbeClassification ClassifyTcpResult(WorkspaceServiceHealthDefinition definition, WorkspaceServiceProbeResult result)
        => result.IsReachable
            ? new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Healthy,
                StatusLabel = "Running",
                Summary = string.IsNullOrWhiteSpace(definition.Description) ? "TCP endpoint available." : definition.Description,
                Recommendation = "Open Workspace.",
                PrimaryUrl = FormatEndpoint(definition.Endpoint),
                ContentValidation = "TCP endpoint accepted a connection.",
                Highlights = BuildLatencyHighlight(result.Latency, "Connection latency"),
                Evidence = BuildProviderEvidence(result),
                Confidence = "HIGH",
            }
            : new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Unavailable,
                StatusLabel = "Unavailable",
                Summary = DescribeTcpFailure(result),
                Recommendation = string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
                PrimaryUrl = FormatEndpoint(definition.Endpoint),
                Highlights = Array.Empty<WorkspaceHealthFact>(),
                Evidence = BuildProviderEvidence(result),
                Confidence = "HIGH",
            };

    private static WorkspaceServiceProbeClassification ClassifyHttpResult(WorkspaceServiceHealthDefinition definition, WorkspaceServiceProbeResult result)
    {
        if (!result.IsReachable)
        {
            return new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Unavailable,
                StatusLabel = "Unavailable",
                Summary = DescribeHttpFailure(result),
                Recommendation = string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
                PrimaryUrl = FormatEndpoint(string.IsNullOrWhiteSpace(definition.OpenUrl) ? definition.Endpoint : definition.OpenUrl),
                Highlights = Array.Empty<WorkspaceHealthFact>(),
                Evidence = BuildProviderEvidence(result),
                Confidence = "HIGH",
            };
        }

        var statusCode = (int?)result.StatusCode ?? 0;
        var status = statusCode switch
        {
            >= 200 and <= 299 => WorkspaceHealthStatus.Healthy,
            >= 300 and <= 399 => WorkspaceHealthStatus.Healthy,
            401 or 403 => WorkspaceHealthStatus.Attention,
            404 => WorkspaceHealthStatus.Attention,
            >= 500 and <= 599 => WorkspaceHealthStatus.Degraded,
            _ => WorkspaceHealthStatus.Attention,
        };
        var statusLabel = statusCode switch
        {
            >= 200 and <= 399 => "Available",
            401 => "Authentication required",
            403 => "Access denied",
            404 => "Not configured",
            >= 500 and <= 599 => "Application error",
            _ => "Needs attention",
        };
        var summary = statusCode switch
        {
            >= 200 and <= 399 => string.IsNullOrWhiteSpace(definition.Description) ? "HTTP service responding." : definition.Description,
            401 => "Service is responding but requires authentication.",
            403 => "Service is responding but denied access.",
            404 => "Service is reachable but this application is not configured.",
            >= 500 and <= 599 => "Service is reachable but the application reported a failure.",
            _ => $"Service responded with HTTP {(int)result.StatusCode!}.",
        };
        var recommendation = statusCode switch
        {
            >= 500 and <= 599 or 404 => string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
            _ => "Open Workspace.",
        };

        return new WorkspaceServiceProbeClassification
        {
            Status = status,
            StatusLabel = statusLabel,
            Summary = summary,
            Recommendation = recommendation,
            PrimaryUrl = FormatEndpoint(string.IsNullOrWhiteSpace(definition.OpenUrl) ? definition.Endpoint : definition.OpenUrl),
            ContentValidation = DescribeHttpValidation(statusCode, result),
            Highlights = BuildLatencyHighlight(result.Latency),
            Evidence = BuildProviderEvidence(result),
            Confidence = status is WorkspaceHealthStatus.Healthy or WorkspaceHealthStatus.Attention ? "HIGH" : "MEDIUM",
        };
    }

    private static IReadOnlyList<WorkspaceHealthFact> BuildProviderEvidence(WorkspaceServiceProbeResult result)
    {
        var evidence = new List<WorkspaceHealthFact>();
        if (!string.IsNullOrWhiteSpace(result.ResponseSample))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Response sample", Value = result.ResponseSample });
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Failure reason", Value = result.FailureReason });
        }

        return evidence;
    }

    private static IReadOnlyList<WorkspaceHealthFact> BuildDetailedEvidence(WorkspaceServiceProbeResult result, WorkspaceServiceProbeClassification classification, DateTimeOffset timestamp)
    {
        var evidence = new List<WorkspaceHealthFact>();
        if (result.StatusCode is not null)
        {
            evidence.Add(new WorkspaceHealthFact { Label = "HTTP status", Value = $"{(int)result.StatusCode.Value} {result.StatusCode.Value}" });
        }

        if (!string.IsNullOrWhiteSpace(result.RedirectLocation))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Redirect", Value = result.RedirectLocation });
        }

        if (!string.IsNullOrWhiteSpace(result.ResponseHeaders))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Headers", Value = result.ResponseHeaders });
        }

        if (!string.IsNullOrWhiteSpace(result.ContentType))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Content type", Value = result.ContentType });
        }

        if (!string.IsNullOrWhiteSpace(classification.ContentValidation))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Content validation", Value = classification.ContentValidation });
        }

        if (result.Latency is not null)
        {
            evidence.Add(new WorkspaceHealthFact { Label = "Probe duration", Value = FormatLatency(result.Latency.Value) });
        }

        evidence.Add(new WorkspaceHealthFact { Label = "Last checked", Value = FormatTimestamp(timestamp) });
        evidence.AddRange(classification.Evidence);
        return evidence;
    }

    private static IReadOnlyList<WorkspaceServiceHealthSnapshot> DecorateDiscoveredApplications(IReadOnlyList<WorkspaceServiceHealthSnapshot> services)
    {
        var updated = services.ToDictionary(item => item.ServiceId, StringComparer.Ordinal);
        if (updated.TryGetValue("ords", out var ords))
        {
            var applicationIds = new[] { "sql-developer-web", "rest-apis", "apex" };
            var applications = applicationIds
                .Where(updated.ContainsKey)
                .Select(id => updated[id])
                .ToList();

            var labels = applications
                .Select(application => $"{FormatApplicationMarker(application.Status)} {application.Name}")
                .ToList();

            updated["ords"] = CloneService(
                ords,
                applications: labels,
                summary: applications.Count == 0 ? ords.Summary : "Application gateway is responding and published workspace applications were discovered.");
        }

        return services.Select(item => updated[item.ServiceId]).ToList();
    }

    private static WorkspaceServiceHealthSnapshot CloneService(
        WorkspaceServiceHealthSnapshot source,
        string? summary = null,
        IReadOnlyList<string>? applications = null,
        string? actionLabel = null)
        => new()
        {
            ServiceId = source.ServiceId,
            Name = source.Name,
            Category = source.Category,
            StatusLabel = source.StatusLabel,
            Summary = summary ?? source.Summary,
            Applications = applications ?? source.Applications,
            Endpoint = source.Endpoint,
            PrimaryUrl = source.PrimaryUrl,
            ProbeType = source.ProbeType,
            Status = source.Status,
            Latency = source.Latency,
            Highlights = source.Highlights,
            Evidence = source.Evidence,
            Confidence = source.Confidence,
            Timestamp = source.Timestamp,
            Recommendation = source.Recommendation,
            ActionLabel = actionLabel ?? source.ActionLabel,
            OpenUrl = source.OpenUrl,
            RefreshInterval = source.RefreshInterval,
            ProviderKey = source.ProviderKey,
        };

    private static string ResolveActionLabel(WorkspaceServiceHealthDefinition definition)
        => string.IsNullOrWhiteSpace(definition.ActionLabel) ? $"Open {definition.Name}" : definition.ActionLabel;

    private static string FormatApplicationMarker(WorkspaceHealthStatus status)
        => status switch
        {
            WorkspaceHealthStatus.Healthy => "✓",
            WorkspaceHealthStatus.Attention => "⚠",
            WorkspaceHealthStatus.Degraded => "⚠",
            WorkspaceHealthStatus.Unavailable => "⚠",
            WorkspaceHealthStatus.Provisioning => "…",
            WorkspaceHealthStatus.Investigating => "…",
            _ => "•",
        };

    private static bool TryParseHostAndPort(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port;
            return port > 0;
        }

        var parts = endpoint.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[1], out port))
        {
            host = parts[0];
            return true;
        }

        return false;
    }

    private sealed class OracleWorkspaceServiceHealthProvider : IWorkspaceServiceHealthProvider
    {
        public bool CanHandle(WorkspaceSnapshot snapshot)
            => snapshot.Definition.Services.Any(service => service.Contains("oracle", StringComparison.OrdinalIgnoreCase) || service.Contains("ords", StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<WorkspaceServiceHealthDefinition> DescribeServices(WorkspaceSnapshot snapshot)
        {
            var settings = OracleWorkspaceSettings.From(snapshot.Definition);
            var ordsEndpoint = $"http://localhost:{settings.OrdsPort}/ords/";
            var ordsLanding = $"http://localhost:{settings.OrdsPort}/ords/_/landing";
            return
            [
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "oracle-database",
                    Name = "Oracle Database",
                    Category = "Database",
                    Description = "TCP endpoint available.",
                    ActionLabel = "Open Database Endpoint",
                    Endpoint = $"tcp://localhost:{settings.HostPort}",
                    ProbeType = WorkspaceServiceProbeType.Tcp,
                    ProviderKey = "oracle",
                    Recommendation = "Investigate Oracle runtime.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                },
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "ords",
                    Name = "Oracle REST Data Services",
                    Category = "Service",
                    Description = "Application gateway is responding.",
                    ActionLabel = "Open Oracle REST Data Services",
                    Endpoint = ordsEndpoint,
                    OpenUrl = ordsLanding,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "oracle",
                    Recommendation = "Investigate Oracle runtime.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = ClassifyOrds,
                },
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "sql-developer-web",
                    Name = "SQL Developer Web",
                    Category = "Application",
                    Description = "Browser-based database tooling is available.",
                    ActionLabel = "Open SQL Developer Web",
                    Endpoint = ordsEndpoint,
                    OpenUrl = ordsLanding,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "oracle",
                    Recommendation = "Troubleshoot Workspace.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = ClassifySqlDeveloperWeb,
                },
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "rest-apis",
                    Name = "REST APIs",
                    Category = "Application",
                    Description = "REST endpoints are available.",
                    ActionLabel = "Open REST APIs",
                    Endpoint = ordsEndpoint,
                    OpenUrl = ordsEndpoint,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "oracle",
                    Recommendation = "Troubleshoot Workspace.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = ClassifyRestApis,
                },
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "apex",
                    Name = "Oracle APEX",
                    Category = "Application",
                    Description = "Oracle APEX landing page is reachable.",
                    ActionLabel = "Open Oracle APEX",
                    Endpoint = ordsEndpoint,
                    OpenUrl = ordsLanding,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "oracle",
                    Recommendation = "Complete APEX installation.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = ClassifyApex,
                },
            ];
        }
    }

    private static string ResolveOpenUrl(WorkspaceServiceHealthDefinition definition, WorkspaceServiceProbeClassification classification, WorkspaceServiceProbeResult result)
    {
        if (string.IsNullOrWhiteSpace(definition.OpenUrl))
        {
            return string.Empty;
        }

        if (string.Equals(definition.ServiceId, "apex", StringComparison.OrdinalIgnoreCase))
        {
            return classification.Status == WorkspaceHealthStatus.Healthy ? definition.OpenUrl : string.Empty;
        }

        if (result.StatusCode is not null && (int)result.StatusCode.Value is >= 300 and <= 399)
        {
            var redirect = ResolveRedirectUrl(definition.Endpoint, result.RedirectLocation);
            if (!string.IsNullOrWhiteSpace(redirect))
            {
                return redirect;
            }
        }

        return definition.OpenUrl;
    }

    private sealed class PostgreSqlWorkspaceServiceHealthProvider : IWorkspaceServiceHealthProvider
    {
        public bool CanHandle(WorkspaceSnapshot snapshot)
            => snapshot.Definition.Services.Any(service => service.Contains("postgres", StringComparison.OrdinalIgnoreCase) || service.Contains("pgadmin", StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<WorkspaceServiceHealthDefinition> DescribeServices(WorkspaceSnapshot snapshot)
            =>
            [
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "postgres",
                    Name = "PostgreSQL",
                    Category = "Database",
                    Description = "TCP endpoint available.",
                    ActionLabel = "Open Database Endpoint",
                    Endpoint = "tcp://localhost:15432",
                    ProbeType = WorkspaceServiceProbeType.Tcp,
                    ProviderKey = "postgres",
                    Recommendation = "Inspect PostgreSQL runtime.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                },
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "pgadmin",
                    Name = "pgAdmin",
                    Category = "Application",
                    Description = "HTTP service responding.",
                    ActionLabel = "Open pgAdmin",
                    Endpoint = "http://localhost:18080/",
                    OpenUrl = "http://localhost:18080/",
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "postgres",
                    Recommendation = "Inspect pgAdmin.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                },
            ];
    }

    private sealed class AnalyticsWorkspaceServiceHealthProvider : IWorkspaceServiceHealthProvider
    {
        public bool CanHandle(WorkspaceSnapshot snapshot)
            => snapshot.Definition.Features.Any(feature => feature.Contains("education", StringComparison.OrdinalIgnoreCase) || feature.Contains("analytics", StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<WorkspaceServiceHealthDefinition> DescribeServices(WorkspaceSnapshot snapshot)
        {
            var settings = AnalyticsWorkspaceSettings.From(snapshot.Definition);
            var endpoint = $"http://localhost:{settings.MarimoPort}/";
            return
            [
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "marimo",
                    Name = "Marimo",
                    Category = "Application",
                    Description = "Application page responding.",
                    ActionLabel = "Open Marimo",
                    Endpoint = endpoint,
                    OpenUrl = endpoint,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "analytics",
                    Recommendation = "Inspect Marimo.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = result =>
                    {
                        var classification = ClassifyHttpResult(new WorkspaceServiceHealthDefinition { Name = "Marimo", Description = "Application page responding.", Recommendation = "Inspect Marimo.", OpenUrl = endpoint, Endpoint = endpoint }, result);
                        if (classification.Status == WorkspaceHealthStatus.Healthy
                            && !result.ResponseSample.Contains("marimo", StringComparison.OrdinalIgnoreCase)
                            && !result.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                        {
                            return new WorkspaceServiceProbeClassification
                            {
                                Status = WorkspaceHealthStatus.Attention,
                                StatusLabel = "Needs attention",
                                Summary = "Application is reachable but Marimo was not confirmed.",
                                Recommendation = "Inspect Marimo.",
                                PrimaryUrl = FormatEndpoint(endpoint),
                                ContentValidation = "Expected Marimo page markers were not found.",
                                Highlights = BuildLatencyHighlight(result.Latency),
                                Evidence = [new WorkspaceHealthFact { Label = "Provider evidence", Value = "Expected Marimo content markers were not found." }],
                                Confidence = "MEDIUM",
                            };
                        }

                        return classification;
                    },
                },
            ];
        }
    }

    private sealed class DefaultWorkspaceServiceProbeRunner : IWorkspaceServiceProbeRunner
    {
        private readonly HttpClient _httpClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };

        public async Task<WorkspaceServiceProbeResult> ProbeTcpAsync(string host, int port, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                stopwatch.Stop();
                return new WorkspaceServiceProbeResult
                {
                    IsReachable = true,
                    Latency = stopwatch.Elapsed,
                };
            }
            catch (OperationCanceledException)
            {
                return new WorkspaceServiceProbeResult { FailureReason = "Timeout" };
            }
            catch (SocketException exception)
            {
                return new WorkspaceServiceProbeResult { FailureReason = exception.SocketErrorCode == SocketError.ConnectionRefused ? "Connection refused" : exception.Message };
            }
        }

        public async Task<WorkspaceServiceProbeResult> ProbeHttpAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                var sample = string.Empty;
                if (response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength <= 2048)
                {
                    sample = await ReadResponseSampleAsync(response, cancellationToken).ConfigureAwait(false);
                }

                return new WorkspaceServiceProbeResult
                {
                    IsReachable = true,
                    StatusCode = response.StatusCode,
                    Latency = stopwatch.Elapsed,
                    RedirectLocation = response.Headers.Location?.ToString() ?? string.Empty,
                    ResponseHeaders = FormatHeaders(response),
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty,
                    ResponseSample = sample,
                };
            }
            catch (TaskCanceledException)
            {
                return new WorkspaceServiceProbeResult { FailureReason = "Timeout" };
            }
            catch (HttpRequestException exception)
            {
                return new WorkspaceServiceProbeResult { FailureReason = exception.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ? "Connection refused" : exception.Message };
            }
        }

        private static async Task<string> ReadResponseSampleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var buffer = new char[256];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            return new string(buffer, 0, read);
        }
    }

    private static WorkspaceServiceProbeClassification ClassifyOrds(WorkspaceServiceProbeResult result)
        => ClassifyHttpResult(new WorkspaceServiceHealthDefinition
        {
            Name = "Oracle REST Data Services",
            Description = "Application gateway is responding.",
            Recommendation = "Investigate Oracle runtime.",
        }, result);

    private static WorkspaceServiceProbeClassification ClassifySqlDeveloperWeb(WorkspaceServiceProbeResult result)
        => ClassifyHttpResult(new WorkspaceServiceHealthDefinition
        {
            Name = "SQL Developer Web",
            Description = "Browser-based database tooling is available.",
            Recommendation = "Troubleshoot Workspace.",
        }, result);

    private static WorkspaceServiceProbeClassification ClassifyRestApis(WorkspaceServiceProbeResult result)
        => ClassifyHttpResult(new WorkspaceServiceHealthDefinition
        {
            Name = "REST APIs",
            Description = "REST endpoints are available.",
            Recommendation = "Troubleshoot Workspace.",
        }, result);

    private static WorkspaceServiceProbeClassification ClassifyApex(WorkspaceServiceProbeResult result)
    {
        var baseClassification = ClassifyHttpResult(new WorkspaceServiceHealthDefinition
        {
            Name = "Oracle APEX",
            Description = "Oracle APEX landing page is reachable.",
            Recommendation = "Complete APEX installation.",
        }, result);

        if (result.ResponseSample.Contains("App Unavailable", StringComparison.OrdinalIgnoreCase)
            || result.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Attention,
                StatusLabel = "Unavailable",
                Summary = "ORDS is available but APEX application is not currently configured.",
                Recommendation = "Complete APEX installation.",
                ContentValidation = "APEX returned the App Unavailable marker.",
                Highlights = BuildLatencyHighlight(result.Latency),
                Evidence = [new WorkspaceHealthFact { Label = "Provider evidence", Value = "APEX returned the App Unavailable page." }],
                Confidence = "HIGH",
            };
        }

        return baseClassification;
    }

    private static IReadOnlyList<WorkspaceHealthFact> BuildLatencyHighlight(TimeSpan? latency, string label = "Latency")
        => latency is null ? Array.Empty<WorkspaceHealthFact>() : [new WorkspaceHealthFact { Label = label, Value = FormatLatency(latency.Value) }];

    private static string DescribeTcpFailure(WorkspaceServiceProbeResult result)
        => result.FailureReason switch
        {
            "Timeout" => "Timed out while checking the service.",
            "Connection refused" => "TCP endpoint is not accepting connections.",
            _ when !string.IsNullOrWhiteSpace(result.FailureReason) => result.FailureReason,
            _ => "TCP endpoint is unavailable.",
        };

    private static string DescribeHttpFailure(WorkspaceServiceProbeResult result)
        => result.FailureReason switch
        {
            "Timeout" => "Timed out while checking the application.",
            "Connection refused" => "Application is not responding on its configured URL.",
            _ when !string.IsNullOrWhiteSpace(result.FailureReason) => result.FailureReason,
            _ => "HTTP service is unavailable.",
        };

    private static string DescribeHttpValidation(int statusCode, WorkspaceServiceProbeResult result)
        => statusCode switch
        {
            >= 200 and <= 299 => string.IsNullOrWhiteSpace(result.ResponseSample) ? "The service returned a successful response." : "The service returned a successful application response.",
            >= 300 and <= 399 => string.IsNullOrWhiteSpace(result.RedirectLocation) ? "The service redirected the request." : $"The service redirected to {result.RedirectLocation}.",
            401 => "The application is online and requested authentication.",
            403 => "The application is online but denied access.",
            404 => "The application route was not found.",
            >= 500 and <= 599 => "The application returned a server error.",
            _ => "The service returned an unexpected response.",
        };

    private static string ResolvePrimaryUrl(WorkspaceServiceHealthDefinition definition, WorkspaceServiceProbeClassification? classification, WorkspaceServiceProbeResult? result)
    {
        if (!string.IsNullOrWhiteSpace(classification?.PrimaryUrl))
        {
            return classification.PrimaryUrl;
        }

        var preferred = string.IsNullOrWhiteSpace(definition.OpenUrl) ? definition.Endpoint : definition.OpenUrl;
        if (result?.StatusCode is not null && (int)result.StatusCode.Value is >= 300 and <= 399)
        {
            var redirect = ResolveRedirectUrl(definition.Endpoint, result.RedirectLocation);
            if (!string.IsNullOrWhiteSpace(redirect))
            {
                preferred = redirect;
            }
        }

        return FormatEndpoint(preferred);
    }

    private static string ResolveRedirectUrl(string endpoint, string redirectLocation)
    {
        if (string.IsNullOrWhiteSpace(redirectLocation))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(redirectLocation, UriKind.Absolute, out var absoluteRedirect))
        {
            return absoluteRedirect.ToString();
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            && Uri.TryCreate(endpointUri, redirectLocation, out var relativeRedirect))
        {
            return relativeRedirect.ToString();
        }

        return string.Empty;
    }

    private static string FormatEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                return $"{uri.Host}:{uri.Port}";
            }

            return uri.ToString();
        }

        return endpoint;
    }

    private static string FormatLatency(TimeSpan latency)
        => $"{Math.Round(latency.TotalMilliseconds)} ms";

    private static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string FormatStatusLabel(WorkspaceHealthStatus status)
        => status switch
        {
            WorkspaceHealthStatus.Healthy => "Available",
            WorkspaceHealthStatus.Attention => "Needs attention",
            WorkspaceHealthStatus.Degraded => "Application error",
            WorkspaceHealthStatus.Unavailable => "Unavailable",
            WorkspaceHealthStatus.Provisioning => "Provisioning",
            WorkspaceHealthStatus.Investigating => "Investigating",
            _ => status.ToString(),
        };

    private static string FormatHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}")
            .ToList();

        return string.Join("; ", headers);
    }
}
