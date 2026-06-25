using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Platform.Windows;

/// <summary>
/// Uses Windows Terminal fragment extensions so OpenCode Stuff can manage only its
/// own profile definitions without editing unrelated user terminal profiles.
/// </summary>
public sealed class WindowsTerminalProfileManager
{
    private const string FragmentDirectoryName = "OpenCodeWorkspaceManager";
    private const string FragmentFileName = "profiles.json";

    public string GetFragmentsDirectoryPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "Fragments", FragmentDirectoryName);
    }

    public string GetFragmentFilePath() => Path.Combine(GetFragmentsDirectoryPath(), FragmentFileName);

    public void EnsureManagedProfile(WorkspaceDefinition definition, TerminalFontPreferences fontPreferences)
    {
        EnsureManagedProfile(definition, fontPreferences, fontPreferences.Family);
    }

    public void EnsureManagedProfile(WorkspaceDefinition definition, TerminalFontPreferences fontPreferences, string resolvedFace)
    {
        var directory = GetFragmentsDirectoryPath();
        Directory.CreateDirectory(directory);

        var fragmentPath = GetFragmentFilePath();
        var root = File.Exists(fragmentPath)
            ? JsonNode.Parse(File.ReadAllText(fragmentPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();

        var profiles = root["profiles"] as JsonArray ?? new JsonArray();
        root["profiles"] = profiles;
        var schemes = root["schemes"] as JsonArray ?? new JsonArray();
        root["schemes"] = schemes;

        var profileName = GetProfileName(definition);
        var profileGuid = GetProfileGuid(definition);
        JsonObject? existingProfile = null;
        foreach (var item in profiles.OfType<JsonObject>())
        {
            if (string.Equals(item["name"]?.GetValue<string>(), profileName, StringComparison.OrdinalIgnoreCase))
            {
                existingProfile = item;
                break;
            }
        }

        if (existingProfile is null)
        {
            existingProfile = new JsonObject();
            profiles.Add(existingProfile);
        }

        existingProfile["name"] = profileName;
        existingProfile["guid"] = profileGuid;
        existingProfile["colorScheme"] = "OpenCode Stuff Dark";
        existingProfile["tabTitle"] = definition.Workspace.Name;
        existingProfile["suppressApplicationTitle"] = false;
        existingProfile["font"] = new JsonObject
        {
            ["face"] = resolvedFace,
        };

        JsonObject? existingScheme = null;
        foreach (var item in schemes.OfType<JsonObject>())
        {
            if (string.Equals(item["name"]?.GetValue<string>(), "OpenCode Stuff Dark", StringComparison.OrdinalIgnoreCase))
            {
                existingScheme = item;
                break;
            }
        }

        if (existingScheme is null)
        {
            existingScheme = new JsonObject();
            schemes.Add(existingScheme);
        }

        existingScheme["name"] = "OpenCode Stuff Dark";
        existingScheme["background"] = "#111111";
        existingScheme["foreground"] = "#F3F4F6";
        existingScheme["black"] = "#6B7280";
        existingScheme["blue"] = "#60A5FA";
        existingScheme["brightBlack"] = "#9CA3AF";
        existingScheme["brightBlue"] = "#93C5FD";
        existingScheme["brightCyan"] = "#67E8F9";
        existingScheme["brightGreen"] = "#86EFAC";
        existingScheme["brightPurple"] = "#D8B4FE";
        existingScheme["brightRed"] = "#FCA5A5";
        existingScheme["brightWhite"] = "#FFFFFF";
        existingScheme["brightYellow"] = "#FDE68A";
        existingScheme["cursorColor"] = "#F8FAFC";
        existingScheme["cyan"] = "#22D3EE";
        existingScheme["green"] = "#4ADE80";
        existingScheme["purple"] = "#C084FC";
        existingScheme["red"] = "#F87171";
        existingScheme["selectionBackground"] = "#374151";
        existingScheme["white"] = "#E5E7EB";
        existingScheme["yellow"] = "#FBBF24";

        File.WriteAllText(fragmentPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool ManagedProfileExists() => File.Exists(GetFragmentFilePath());

    public string GetProfileName(WorkspaceDefinition definition) => $"OpenCode Stuff - {definition.Workspace.Name}";

    public string GetProfileGuid(WorkspaceDefinition definition)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(GetProfileName(definition)));
        return new Guid(bytes).ToString("B");
    }

    public string? GetConfiguredFontFace(WorkspaceDefinition definition)
    {
        if (!ManagedProfileExists())
        {
            return null;
        }

        var root = JsonNode.Parse(File.ReadAllText(GetFragmentFilePath()))?.AsObject();
        var profiles = root?["profiles"] as JsonArray;
        if (profiles is null)
        {
            return null;
        }

        foreach (var item in profiles.OfType<JsonObject>())
        {
            if (string.Equals(item["name"]?.GetValue<string>(), GetProfileName(definition), StringComparison.OrdinalIgnoreCase))
            {
                return item["font"]?["face"]?.GetValue<string>();
            }
        }

        return null;
    }
}
