using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleXdbReadinessEvidenceClassifierTests
{
    [Fact]
    public void ShouldTreatAsFailure_RegistryInvalidWithZeroInvalidObjectsAndSuccessfulFunctionalProbes_ReturnsFalse()
    {
        var evidence = "root_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; pdb_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; invalid_object_count=0; dba_errors=none; pdb_plug_in_violations=none; root_functional_probe=XMLTYPE=ok|HTTPPORT=0; pdb_functional_probe=XMLTYPE=ok|HTTPPORT=0";

        Assert.False(OracleXdbReadinessEvidenceClassifier.ShouldTreatAsFailure(evidence));
    }

    [Fact]
    public void ShouldTreatAsFailure_RegistryInvalidWithRealInvalidObjects_ReturnsTrue()
    {
        var evidence = "root_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; pdb_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; invalid_object_count=2; dba_errors=none; pdb_plug_in_violations=none; root_functional_probe=XMLTYPE=ok|HTTPPORT=0; pdb_functional_probe=XMLTYPE=ok|HTTPPORT=0";

        Assert.True(OracleXdbReadinessEvidenceClassifier.ShouldTreatAsFailure(evidence));
    }

    [Fact]
    public void ShouldTreatAsFailure_RegistryInvalidWithDbaErrors_ReturnsTrue()
    {
        var evidence = "root_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; pdb_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; invalid_object_count=0; dba_errors=XDB|OBJ1|PACKAGE BODY|1|1|PLS-00302; pdb_plug_in_violations=none; root_functional_probe=XMLTYPE=ok|HTTPPORT=0; pdb_functional_probe=XMLTYPE=ok|HTTPPORT=0";

        Assert.True(OracleXdbReadinessEvidenceClassifier.ShouldTreatAsFailure(evidence));
    }

    [Fact]
    public void ShouldTreatAsFailure_RegistryInvalidButFailedFunctionalProbe_ReturnsTrue()
    {
        var evidence = "root_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; pdb_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; invalid_object_count=0; dba_errors=none; pdb_plug_in_violations=none; root_functional_probe=failed; pdb_functional_probe=XMLTYPE=ok|HTTPPORT=0";

        Assert.True(OracleXdbReadinessEvidenceClassifier.ShouldTreatAsFailure(evidence));
    }

    [Fact]
    public void ShouldTreatAsFailure_RegistryValid_ReturnsFalse()
    {
        var evidence = "root_registry=XDB|Oracle XML Database|23.0.0.0.0|VALID|2026-07-14; pdb_registry=XDB|Oracle XML Database|23.0.0.0.0|VALID|2026-07-14; invalid_object_count=0; dba_errors=none; pdb_plug_in_violations=none";

        Assert.False(OracleXdbReadinessEvidenceClassifier.ShouldTreatAsFailure(evidence));
    }
}
