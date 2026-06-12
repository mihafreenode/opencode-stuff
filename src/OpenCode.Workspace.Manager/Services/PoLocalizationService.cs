using System.Globalization;
using System.IO;

namespace OpenCode.Workspace.Manager.Services;

/// <summary>
/// Minimal gettext-style PO reader for the MVP. The goal is to keep visible UI
/// strings out of code while keeping the implementation small and understandable.
/// </summary>
public sealed class PoLocalizationService
{
    private readonly IReadOnlyDictionary<string, string> _englishStrings;
    private readonly IReadOnlyDictionary<string, string> _activeStrings;

    public PoLocalizationService(string localizationRootPath, string languageCode)
    {
        _englishStrings = Load(Path.Combine(localizationRootPath, "en.po"));
        _activeStrings = languageCode.Equals("sl", StringComparison.OrdinalIgnoreCase)
            ? Load(Path.Combine(localizationRootPath, "sl.po"))
            : _englishStrings;
    }

    public string Get(string key)
    {
        if (_activeStrings.TryGetValue(key, out var translatedValue) && !string.IsNullOrWhiteSpace(translatedValue))
        {
            return translatedValue;
        }

        if (_englishStrings.TryGetValue(key, out var englishValue) && !string.IsNullOrWhiteSpace(englishValue))
        {
            return englishValue;
        }

        return key;
    }

    public static string DetectLanguageCode()
    {
        var overrideLanguage = Environment.GetEnvironmentVariable("OPENCODE_WORKSPACE_MANAGER_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(overrideLanguage))
        {
            return overrideLanguage.Trim();
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }

    private static IReadOnlyDictionary<string, string> Load(string filePath)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath))
        {
            return dictionary;
        }

        string? currentId = null;
        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                currentId = Unquote(line[6..]);
                continue;
            }

            if (line.StartsWith("msgstr ", StringComparison.Ordinal) && currentId is not null)
            {
                dictionary[currentId] = Unquote(line[7..]);
                currentId = null;
            }
        }

        return dictionary;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"')
            ? trimmed[1..^1]
            : trimmed;
    }
}
