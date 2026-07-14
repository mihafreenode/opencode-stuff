namespace OpenCode.Workspace.Core.Runtime;

internal static class ComposeVolumeReferenceParser
{
    public static string GetSource(string volumeReference)
    {
        if (string.IsNullOrWhiteSpace(volumeReference))
        {
            return string.Empty;
        }

        var trimmed = volumeReference.Trim();
        if (trimmed.Length >= 3
            && char.IsLetter(trimmed[0])
            && trimmed[1] == ':'
            && (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            var separatorIndex = trimmed.IndexOf(':', 3);
            return separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        }

        var firstSeparatorIndex = trimmed.IndexOf(':');
        return firstSeparatorIndex >= 0 ? trimmed[..firstSeparatorIndex] : trimmed;
    }
}
