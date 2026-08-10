using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class OracleDatabaseImageCatalog
{
    public const string DefaultDatabaseImage = "gvenzl/oracle-free:23.26.2@sha256:e2763af84ecc345d48e4e3fe7999d0b7bb57885b6e0166051acd5f62e27eb605";
    public const string KnownIncompatibleApexDatabaseImage = "gvenzl/oracle-free:23-slim-faststart";

    public static string ResolveDatabaseImage(WorkspaceDefinition definition)
    {
        if (definition.Oracle.DatabaseImage is null)
        {
            return DefaultDatabaseImage;
        }

        if (string.IsNullOrWhiteSpace(definition.Oracle.DatabaseImage))
        {
            throw new InvalidOperationException("workspace.yaml contains an empty 'oracle.databaseImage'. Remove the field to use the default Oracle image or provide a non-empty image reference.");
        }

        return definition.Oracle.DatabaseImage.Trim();
    }

    public static bool IsKnownApexIncompatibleImage(string? image)
        => !string.IsNullOrWhiteSpace(image)
           && (image.Contains(KnownIncompatibleApexDatabaseImage, StringComparison.OrdinalIgnoreCase)
               || image.Contains("sha256:d8913e4e4769b6e60197949bef30a4391713afe662b4b4e71a2665c881bdac8b", StringComparison.OrdinalIgnoreCase));
}
