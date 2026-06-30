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
    public string ContentType { get; init; } = string.Empty;
    public string ResponseSample { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
}

public sealed class WorkspaceServiceProbeClassification
{
    public WorkspaceHealthStatus Status { get; init; } = WorkspaceHealthStatus.Attention;
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
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
            services.Add(await ProbeServiceAsync(snapshot, definition, probeRunner, cancellationToken));
        }

        return services;
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
                Summary = result.IsReachable ? $"{definition.Name} reachable." : $"{definition.Name} unavailable.",
                Recommendation = string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
                Evidence = BuildBaseEvidence(result),
                Confidence = result.IsReachable ? "MEDIUM" : "HIGH",
            },
        };
    }

    private static async Task<WorkspaceServiceHealthSnapshot> ProbeServiceAsync(WorkspaceSnapshot snapshot, WorkspaceServiceHealthDefinition definition, IWorkspaceServiceProbeRunner probeRunner, CancellationToken cancellationToken)
    {
        if (definition.RequiresRunningRuntime && snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            return new WorkspaceServiceHealthSnapshot
            {
                ServiceId = definition.ServiceId,
                Name = definition.Name,
                Category = definition.Category,
                Endpoint = definition.Endpoint,
                ProbeType = definition.ProbeType,
                Status = WorkspaceHealthStatus.Attention,
                Evidence = [new WorkspaceHealthFact { Label = "runtime", Value = "Workspace runtime is not running." }],
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Recommendation = "Open Workspace.",
                OpenUrl = definition.OpenUrl,
                RefreshInterval = definition.RefreshInterval,
                ProviderKey = definition.ProviderKey,
            };
        }

        var result = await ExecuteProbeAsync(definition, probeRunner, cancellationToken);
        var classification = ClassifyProbeResult(definition, result);
        return new WorkspaceServiceHealthSnapshot
        {
            ServiceId = definition.ServiceId,
            Name = definition.Name,
            Category = definition.Category,
            Endpoint = definition.Endpoint,
            ProbeType = definition.ProbeType,
            Status = classification.Status,
            Latency = result.Latency,
            Evidence = classification.Evidence,
            Confidence = classification.Confidence,
            Timestamp = DateTimeOffset.UtcNow,
            Recommendation = string.IsNullOrWhiteSpace(classification.Recommendation) ? definition.Recommendation : classification.Recommendation,
            OpenUrl = definition.OpenUrl,
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
                Summary = $"{definition.Name} reachable.",
                Recommendation = "Open Workspace.",
                Evidence = BuildBaseEvidence(result),
                Confidence = "HIGH",
            }
            : new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Unavailable,
                Summary = $"{definition.Name} unavailable.",
                Recommendation = string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
                Evidence = BuildBaseEvidence(result),
                Confidence = "HIGH",
            };

    private static WorkspaceServiceProbeClassification ClassifyHttpResult(WorkspaceServiceHealthDefinition definition, WorkspaceServiceProbeResult result)
    {
        if (!result.IsReachable)
        {
            return new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Unavailable,
                Summary = $"{definition.Name} unavailable.",
                Recommendation = string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
                Evidence = BuildBaseEvidence(result),
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
        var summary = statusCode switch
        {
            >= 200 and <= 299 => $"{definition.Name} HTTP {(int)result.StatusCode!}.",
            >= 300 and <= 399 => $"{definition.Name} redirect HTTP {(int)result.StatusCode!}.",
            401 or 403 => $"{definition.Name} reachable, authentication required.",
            404 => $"{definition.Name} reachable, application unavailable.",
            >= 500 and <= 599 => $"{definition.Name} application failure HTTP {(int)result.StatusCode!}.",
            _ => $"{definition.Name} responded with HTTP {(int)result.StatusCode!}.",
        };
        var recommendation = statusCode switch
        {
            >= 500 and <= 599 or 404 => string.IsNullOrWhiteSpace(definition.Recommendation) ? "Troubleshoot Workspace." : definition.Recommendation,
            _ => "Open Workspace.",
        };

        return new WorkspaceServiceProbeClassification
        {
            Status = status,
            Summary = summary,
            Recommendation = recommendation,
            Evidence = BuildBaseEvidence(result),
            Confidence = status is WorkspaceHealthStatus.Healthy or WorkspaceHealthStatus.Attention ? "HIGH" : "MEDIUM",
        };
    }

    private static IReadOnlyList<WorkspaceHealthFact> BuildBaseEvidence(WorkspaceServiceProbeResult result)
    {
        var evidence = new List<WorkspaceHealthFact>();
        if (result.StatusCode is not null)
        {
            evidence.Add(new WorkspaceHealthFact { Label = "status", Value = ((int)result.StatusCode.Value).ToString() });
        }

        if (result.Latency is not null)
        {
            evidence.Add(new WorkspaceHealthFact { Label = "latency", Value = $"{Math.Round(result.Latency.Value.TotalMilliseconds)} ms" });
        }

        if (!string.IsNullOrWhiteSpace(result.RedirectLocation))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "redirect", Value = result.RedirectLocation });
        }

        if (!string.IsNullOrWhiteSpace(result.ContentType))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "content type", Value = result.ContentType });
        }

        if (!string.IsNullOrWhiteSpace(result.ResponseSample))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "sample", Value = result.ResponseSample });
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            evidence.Add(new WorkspaceHealthFact { Label = "failure", Value = result.FailureReason });
        }

        return evidence;
    }

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
            return
            [
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "oracle-database",
                    Name = "Oracle Database",
                    Category = "Database",
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
                    Endpoint = ordsEndpoint,
                    OpenUrl = ordsEndpoint,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "oracle",
                    Recommendation = "Investigate Oracle runtime.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = result => ClassifyOrds(result),
                },
                new WorkspaceServiceHealthDefinition
                {
                    ServiceId = "apex",
                    Name = "Oracle APEX",
                    Category = "Application",
                    Endpoint = ordsEndpoint,
                    OpenUrl = ordsEndpoint,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "oracle",
                    Recommendation = "Investigate APEX installation.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = result => ClassifyApex(result),
                },
            ];
        }
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
                    Endpoint = endpoint,
                    OpenUrl = endpoint,
                    ProbeType = WorkspaceServiceProbeType.Http,
                    ProviderKey = "analytics",
                    Recommendation = "Inspect Marimo.",
                    RefreshInterval = TimeSpan.FromSeconds(30),
                    Validator = result =>
                    {
                        var classification = ClassifyHttpResult(new WorkspaceServiceHealthDefinition { Name = "Marimo", Recommendation = "Inspect Marimo." }, result);
                        if (classification.Status == WorkspaceHealthStatus.Healthy
                            && !result.ResponseSample.Contains("marimo", StringComparison.OrdinalIgnoreCase)
                            && !result.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                        {
                            return new WorkspaceServiceProbeClassification
                            {
                                Status = WorkspaceHealthStatus.Attention,
                                Summary = "Marimo reachable, application metadata not confirmed.",
                                Recommendation = "Inspect Marimo.",
                                Evidence = BuildBaseEvidence(result),
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
                await client.ConnectAsync(host, port, timeout.Token);
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
                using var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                stopwatch.Stop();
                var sample = string.Empty;
                if (response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength <= 2048)
                {
                    sample = await ReadResponseSampleAsync(response, cancellationToken);
                }

                return new WorkspaceServiceProbeResult
                {
                    IsReachable = true,
                    StatusCode = response.StatusCode,
                    Latency = stopwatch.Elapsed,
                    RedirectLocation = response.Headers.Location?.ToString() ?? string.Empty,
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
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var buffer = new char[256];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            return new string(buffer, 0, read);
        }
    }

    private static WorkspaceServiceProbeClassification ClassifyOrds(WorkspaceServiceProbeResult result)
    {
        var baseClassification = ClassifyHttpResult(new WorkspaceServiceHealthDefinition { Name = "Oracle REST Data Services", Recommendation = "Investigate Oracle runtime." }, result);
        return baseClassification;
    }

    private static WorkspaceServiceProbeClassification ClassifyApex(WorkspaceServiceProbeResult result)
    {
        var baseClassification = ClassifyHttpResult(new WorkspaceServiceHealthDefinition { Name = "Oracle APEX", Recommendation = "Investigate APEX installation." }, result);
        if (result.ResponseSample.Contains("App Unavailable", StringComparison.OrdinalIgnoreCase)
            || result.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return new WorkspaceServiceProbeClassification
            {
                Status = WorkspaceHealthStatus.Attention,
                Summary = "Oracle APEX application unavailable.",
                Recommendation = "Investigate APEX installation.",
                Evidence = BuildBaseEvidence(result),
                Confidence = "HIGH",
            };
        }

        return baseClassification;
    }
}
