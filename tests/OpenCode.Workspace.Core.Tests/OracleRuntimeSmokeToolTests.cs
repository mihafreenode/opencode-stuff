using System.Reflection;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleRuntimeSmokeToolTests
{
    [Theory]
    [InlineData("oracle-plsql-demo")]
    [InlineData("oracle-apex-demo")]
    [InlineData("oracle-apexlang-demo")]
    public void Parse_AcceptsSupportedTemplateIds(string templateId)
    {
        var options = OracleRuntimeSmokeCli.Parse(["--template", templateId, "--dry-run"]);

        Assert.Equal(templateId, options.TemplateId);
        Assert.True(options.DryRun);
        Assert.Equal(SmokeValidationHost.Auto, options.Host);
    }

    [Fact]
    public void Parse_RejectsUnsupportedTemplateIds()
    {
        var exception = Assert.Throws<ArgumentException>(() => OracleRuntimeSmokeCli.Parse(["--template", "oracle-unknown-demo"]));
        Assert.Contains("Unsupported template", exception.Message);
    }

    [Fact]
    public void Parse_RecognizesWorkspaceArtifactsHostAndDryRunArguments()
    {
        var options = OracleRuntimeSmokeCli.Parse(
        [
            "--template", "oracle-apex-demo",
            "--workspace-root", "/tmp/workspace",
            "--artifacts-root", "/tmp/artifacts",
            "--host", "windows",
            "--dry-run",
            "--invoked-from-wrapper",
        ]);

        Assert.Equal("/tmp/workspace", options.WorkspaceRoot);
        Assert.Equal("/tmp/artifacts", options.ArtifactsRoot);
        Assert.Equal(SmokeValidationHost.Windows, options.Host);
        Assert.True(options.DryRun);
        Assert.True(options.InvokedFromWrapper);
    }

    [Fact]
    public void ArtifactRunDirectoryName_IsDeterministic()
    {
        var timestamp = new DateTimeOffset(2026, 6, 16, 20, 15, 42, TimeSpan.Zero);
        Assert.Equal("20260616-201542", OracleRuntimeSmokeCli.CreateArtifactRunDirectoryName(timestamp));
    }

    [Fact]
    public void FailureClassificationLabels_Exist()
    {
        Assert.Equal(
        [
            "ValidationToolingFailure",
            "EnvironmentFailure",
            "ProductFailure",
            "OracleRuntimeFailure",
        ],
            Enum.GetNames<SmokeFailureClassification>());
    }

    [Fact]
    public void ClassifyOrdsFailure_RecognizesConfigurationIssue()
    {
        var classification = OracleRuntimeSmokeCli.ClassifyOrdsFailure(
            "ERROR: The container can't find a valid configuration in Oracle REST Data Services config directory /etc/ords/config. To install the product declare CONN_STRING and ORACLE_PWD or DBHOST, DBPORT, DBSERVICENAME, and ORACLE_PWD.",
            "{}");

        Assert.Equal("configuration issue", classification);
    }

    [Fact]
    public void ClassifyOrdsFailure_RecognizesConfigurationVolumeIssue()
    {
        var classification = OracleRuntimeSmokeCli.ClassifyOrdsFailure(
            "ERROR: The ORDS config directory /etc/ords/config is empty, please validate you ords config volume.",
            "{}");

        Assert.Equal("ORDS configuration volume issue", classification);
    }

    [Fact]
    public void ClassifyApexInstallationState_RecognizesMissingApex()
    {
        var classification = OracleRuntimeSmokeCli.ClassifyApexInstallationState("==REGISTRY==\n==USERS==\n==INVALID==\n");

        Assert.Equal("APEX not installed", classification);
    }

    [Fact]
    public void ClassifyApexInstallationState_RecognizesInstalledApex()
    {
        var classification = OracleRuntimeSmokeCli.ClassifyApexInstallationState("==REGISTRY==\nAPEX\nOracle APEX\n24.2\nVALID\n==USERS==\nAPEX_240200\n==INVALID==\n==VERSION==\n24.2\n");

        Assert.Equal("APEX installed", classification);
    }

    [Fact]
    public void FormatApexRouteDiagnostics_EmitsExpectedFields()
    {
        var content = OracleRuntimeSmokeCli.FormatApexRouteDiagnostics([
            new RouteProbeResult("/ords/apex", "http://localhost:8181/ords/apex", 200, null, "<html>apex</html>"),
            new RouteProbeResult("/ords/apex", "http://localhost:8181/ords/apex", 404, null, "not found"),
        ]);

        Assert.Contains("URL=http://localhost:8181/ords/apex", content);
        Assert.Contains("STATUS=200", content);
        Assert.Contains("URL=http://localhost:8181/ords/apex", content);
        Assert.Contains("STATUS=404", content);
        Assert.Contains("BODY=not found", content);
    }

    [Fact]
    public void DockerContainerRuntimeState_FromInspectJson_ExtractsRestartAndExitDetails()
    {
        const string inspectJson = """
        [
          {
            "RestartCount": 9,
            "Image": "sha256:test",
            "State": {
              "Status": "restarting",
              "Running": true,
              "ExitCode": 1,
              "Health": {
                "Status": "unhealthy"
              }
            },
            "Config": {
              "Image": "container-registry.oracle.com/database/ords:latest",
              "Env": ["DB_HOSTNAME=oracle-demo", "DB_PORT=1521"],
              "Entrypoint": ["docker-entrypoint.sh"],
              "WorkingDir": "/opt/oracle/ords"
            },
            "HostConfig": {
              "PortBindings": {
                "8080/tcp": [{ "HostPort": "8181" }]
              }
            },
            "Mounts": [
              { "Source": "/host/config", "Destination": "/etc/ords/config" }
            ],
            "NetworkSettings": {
              "Networks": {
                "default": {
                  "IPAddress": "172.31.0.3"
                }
              }
            }
          }
        ]
        """;

        var state = DockerContainerRuntimeState.FromInspectJson(inspectJson);

        Assert.Equal("restarting", state.Status);
        Assert.True(state.Running);
        Assert.Equal(9, state.RestartCount);
        Assert.Equal(1, state.ExitCode);
        Assert.Equal("unhealthy", state.HealthStatus);
        Assert.Contains("container-registry.oracle.com/database/ords:latest", state.ImageConfig);
        Assert.Contains("8080/tcp=>8181", state.PublishedPorts);
        Assert.Contains("/host/config->/etc/ords/config", state.Mounts);
        Assert.Contains("DB_HOSTNAME=oracle-demo", state.Environment);
        Assert.Equal("172.31.0.3", state.NetworkAddress);
    }

    [Fact]
    public void WriteSummary_IncludesOrdsDiagnosticsFields()
    {
        var artifactsRoot = Path.Combine(Path.GetTempPath(), $"ords-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactsRoot);

        try
        {
            var summary = new SmokeRunSummary("oracle-apex-demo", artifactsRoot)
            {
                FailureClassification = SmokeFailureClassification.OracleRuntimeFailure.ToString(),
                OrdsFailureClassification = "configuration issue",
                OrdsRestartCount = 9,
                OrdsExitCode = 1,
                OrdsLastLogLine = "ERROR: The container can't find a valid configuration in Oracle REST Data Services config directory /etc/ords/config.",
                OrdsHostPort = 8181,
                OrdsContainerPort = 8080,
                OrdsBaseUrlTested = "http://localhost:8181/ords",
                ApexUrlTested = "http://localhost:8181/ords/apex",
                OrdsHttpStatusCode = 200,
                ApexHttpStatusCode = 302,
                ApexMediaFound = true,
                ApexMediaPath = "/workspace/.local/oracle/downloads/apex/apex.zip",
                ApexInstalled = true,
                ApexVersion = "24.2",
                ApexRegistryStatus = "VALID",
                ApexSchemasPresent = true,
                ApexInstallationState = "APEX installed",
                Result = "Workspace validation failed. Service 'oracle-ords' is not reported as running by Docker Compose.",
            };

            var method = typeof(OracleRuntimeSmokeCli).GetMethod("WriteSummary", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            method!.Invoke(null, [artifactsRoot, summary]);

            var content = File.ReadAllText(Path.Combine(artifactsRoot, "summary.txt"));
            Assert.Contains("ords_failure_classification=configuration issue", content);
            Assert.Contains("ords_restart_count=9", content);
            Assert.Contains("ords_exit_code=1", content);
            Assert.Contains("ords_last_log_line=ERROR: The container can't find a valid configuration", content);
            Assert.Contains("ords_host_port=8181", content);
            Assert.Contains("ords_container_port=8080", content);
            Assert.Contains("ords_base_url_tested=http://localhost:8181/ords", content);
            Assert.Contains("apex_url_tested=http://localhost:8181/ords/apex", content);
            Assert.Contains("ords_http_status_code=200", content);
            Assert.Contains("apex_http_status_code=302", content);
            Assert.Contains("apex_media_found=True", content);
            Assert.Contains("apex_media_path=/workspace/.local/oracle/downloads/apex/apex.zip", content);
            Assert.Contains("apex_installed=True", content);
            Assert.Contains("apex_version=24.2", content);
            Assert.Contains("apex_registry_status=VALID", content);
            Assert.Contains("apex_schemas_present=True", content);
            Assert.Contains("apex_installation_state=APEX installed", content);
        }
        finally
        {
            if (Directory.Exists(artifactsRoot))
            {
                Directory.Delete(artifactsRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RuntimeSmokeDocs_ExplainWslAndWindowsHostSelection()
    {
        var root = TestPaths.RepositoryRoot;
        var smokeDoc = File.ReadAllText(Path.Combine(root, "docs", "testing", "oracle-apex-runtime-smoke.md"));
        var agentsDoc = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var troubleshootingDoc = File.ReadAllText(Path.Combine(root, "docs", "troubleshooting", "wsl-windows-interop.md"));

        Assert.Contains("docker version", smokeDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("powershell.exe -NoProfile -Command \"docker version\"", smokeDoc);
        Assert.Contains("Windows Docker Desktop", smokeDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Static Tests", smokeDoc);
        Assert.Contains("Smoke Runner Dry Run", smokeDoc);
        Assert.Contains("Live Runtime Smoke", smokeDoc);
        Assert.Contains("Validation Tooling Failure", smokeDoc);
        Assert.Contains("Environment Failure", smokeDoc);
        Assert.Contains("Product Failure", smokeDoc);
        Assert.Contains("Oracle Runtime Failure", smokeDoc);

        Assert.Contains("Runtime Validation: WSL vs Windows Host", agentsDoc);
        Assert.Contains("Runtime Validation Ladder", agentsDoc);
        Assert.Contains("Validation Tooling Is Part Of The Product", agentsDoc);
        Assert.Contains("Use Windows Docker Desktop result as authoritative", agentsDoc);
        Assert.Contains("tools/OracleRuntimeSmoke/", agentsDoc);
        Assert.Contains("scripts/testing/oracle-runtime-smoke.ps1", agentsDoc);

        Assert.Contains("Windows host validation as authoritative", troubleshootingDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("official Oracle APEX ZIP placed under `.local/oracle/downloads/apex/`", smokeDoc);
    }

    [Fact]
    public void CaptureGeneratedArtifacts_RedactsOraclePassword_AndPreservesCorrectedOrdsConfig()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"oracle-smoke-artifacts-{Guid.NewGuid():N}");
        var artifactsRoot = Path.Combine(Path.GetTempPath(), $"oracle-smoke-artifacts-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(artifactsRoot);

        try
        {
            var paths = WorkspacePathBuilder.Build(workspaceRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ComposePath)!);
            File.WriteAllText(paths.ComposePath, "services:\n  oracle-ords:\n    environment:\n      ORACLE_ADMIN_USER: ${ORACLE_ADMIN_USER}\n      ORACLE_PASSWORD: ${ORACLE_PASSWORD}\n      ORACLE_HOST: ${ORACLE_HOST}\n      ORACLE_PORT: ${ORACLE_PORT}\n      ORACLE_SERVICE_NAME: ${ORACLE_SERVICE_NAME}\n      ORACLE_ORDS_PUBLIC_PASSWORD: ${ORACLE_ORDS_PUBLIC_PASSWORD}\n");
            File.WriteAllText(paths.WorkspaceYamlPath, "workspace:\n  name: smoke\n");
            File.WriteAllText(paths.EnvironmentFilePath, "ORACLE_ADMIN_USER=SYS\nORACLE_PASSWORD=change-on-first-demo\nORACLE_DEMO_PASSWORD=demo-password\nORACLE_HOST=oracle-demo\nORACLE_PORT=1521\nORACLE_SERVICE_NAME=FREEPDB1\nORACLE_ORDS_PUBLIC_PASSWORD=change-on-first-demo\nORACLE_ORDS_PORT=8181\n");

            var method = typeof(OracleRuntimeSmokeCli).GetMethod("CaptureGeneratedArtifacts", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            method!.Invoke(null, [paths, artifactsRoot]);

            var redactedEnv = File.ReadAllText(Path.Combine(artifactsRoot, "env.redacted"));
            var copiedCompose = File.ReadAllText(Path.Combine(artifactsRoot, "compose.yaml"));

            Assert.Contains("ORACLE_PASSWORD=<redacted>", redactedEnv);
            Assert.Contains("ORACLE_DEMO_PASSWORD=<redacted>", redactedEnv);
            Assert.Contains("ORACLE_ORDS_PORT=8181", redactedEnv);
            Assert.Contains("ORACLE_ADMIN_USER: ${ORACLE_ADMIN_USER}", copiedCompose);
            Assert.Contains("ORACLE_PASSWORD: ${ORACLE_PASSWORD}", copiedCompose);
            Assert.Contains("ORACLE_HOST: ${ORACLE_HOST}", copiedCompose);
            Assert.Contains("ORACLE_PORT: ${ORACLE_PORT}", copiedCompose);
            Assert.Contains("ORACLE_SERVICE_NAME: ${ORACLE_SERVICE_NAME}", copiedCompose);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(artifactsRoot))
            {
                Directory.Delete(artifactsRoot, recursive: true);
            }
        }
    }
}
