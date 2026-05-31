using System.Text.Json;

namespace AIMonitor.Core;

public static class MonitorSettingsLoader
{
    public static MonitorSettings Load(string repositoryRoot, string? settingsPath = null)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = Path.GetFullPath(
            settingsPath ?? Path.Combine(resolvedRepositoryRoot, "config", "appsettings.json"));

        if (!File.Exists(resolvedSettingsPath))
        {
            throw new FileNotFoundException("AIMonitor settings file was not found.", resolvedSettingsPath);
        }

        using FileStream stream = File.OpenRead(resolvedSettingsPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement monitor = document.RootElement.GetProperty("Monitor");
        string watchedSolutionPath = RequireString(monitor, "WatchedSolutionPath");
        string runtimeRoot = GetString(monitor, "RuntimeRoot") ?? "runtime";
        string settingsDirectory = Path.GetDirectoryName(resolvedSettingsPath) ?? resolvedRepositoryRoot;

        return MonitorSettings.Create(
            resolvedRepositoryRoot,
            ResolvePath(watchedSolutionPath, settingsDirectory),
            ResolvePath(runtimeRoot, resolvedRepositoryRoot));
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        string? value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Monitor:{propertyName} is required.");
        }

        return value;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
