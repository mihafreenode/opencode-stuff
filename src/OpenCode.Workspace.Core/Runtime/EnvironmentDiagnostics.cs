using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

/// <summary>
/// Runs host-side checks before destructive or user-visible actions. The checks
/// intentionally normalize tool-specific failures into stable problem codes and
/// plain-language messages that the UI can display directly.
/// </summary>
public sealed class EnvironmentDiagnostics
{
    private readonly ProcessRunner _processRunner;

    public EnvironmentDiagnostics(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosticResult>
        {
            await CheckDockerCliAsync(cancellationToken),
            await CheckDockerEngineAsync(cancellationToken),
            await CheckWindowsTerminalAsync(cancellationToken),
            CheckInternetAccess(),
        };

        return results;
    }

    private async Task<DiagnosticResult> CheckDockerCliAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync("docker", new[] { "--version" }, cancellationToken: cancellationToken);
            return result.IsSuccess
                ? Success("docker.cli", "Docker CLI", "Docker CLI is installed.", result.StandardOutput.Trim())
                : Error("docker.cli", "Docker CLI", "Docker is not available. Install Docker Desktop and try again.", result.StandardError);
        }
        catch (Exception exception)
        {
            return Error("docker.cli", "Docker CLI", "Docker is not available. Install Docker Desktop and try again.", exception.Message);
        }
    }

    private async Task<DiagnosticResult> CheckDockerEngineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync("docker", new[] { "info" }, cancellationToken: cancellationToken);
            return result.IsSuccess
                ? Success("docker.engine", "Docker Engine", "Docker Desktop is running.", result.StandardOutput)
                : Error("docker.engine", "Docker Engine", "Docker Desktop is installed but the engine is not responding. Start Docker Desktop and wait for it to finish starting.", result.StandardError);
        }
        catch (Exception exception)
        {
            return Error("docker.engine", "Docker Engine", "Docker Desktop is installed but the engine is not responding. Start Docker Desktop and wait for it to finish starting.", exception.Message);
        }
    }

    private async Task<DiagnosticResult> CheckWindowsTerminalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync("cmd.exe", new[] { "/c", "where", "wt" }, cancellationToken: cancellationToken);
            return result.IsSuccess
                ? Success("terminal.wt", "Windows Terminal", "Windows Terminal is available for attach sessions.", result.StandardOutput.Trim())
                : Error("terminal.wt", "Windows Terminal", "Windows Terminal is not available. Install it or enable its App Execution Alias.", result.StandardError);
        }
        catch (Exception exception)
        {
            return Error("terminal.wt", "Windows Terminal", "Windows Terminal is not available. Install it or enable its App Execution Alias.", exception.Message);
        }
    }

    private static DiagnosticResult CheckInternetAccess()
    {
        try
        {
            var connected = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            return connected
                ? Success("network.internet", "Internet Access", "A network connection is available for provisioning.", null)
                : Error("network.internet", "Internet Access", "No network connection was detected. Provisioning may fail until internet access is restored.", null, DiagnosticSeverity.Warning);
        }
        catch (Exception exception)
        {
            return Error("network.internet", "Internet Access", "Internet access could not be validated. Provisioning may fail if the machine is offline.", exception.Message, DiagnosticSeverity.Warning);
        }
    }

    private static DiagnosticResult Success(string code, string title, string message, string? technicalDetails) => new()
    {
        Code = code,
        Title = title,
        Message = message,
        Severity = DiagnosticSeverity.Information,
        IsSuccess = true,
        TechnicalDetails = technicalDetails,
    };

    private static DiagnosticResult Error(string code, string title, string message, string? technicalDetails, DiagnosticSeverity severity = DiagnosticSeverity.Error) => new()
    {
        Code = code,
        Title = title,
        Message = message,
        Severity = severity,
        IsSuccess = false,
        TechnicalDetails = technicalDetails,
    };
}
