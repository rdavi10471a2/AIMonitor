using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIMonitor.Core;

public static class MonitorSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    static MonitorSettingsLoader()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static MonitorSettings Load(string repositoryRoot, string? settingsPath = null)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = ResolveSettingsPath(resolvedRepositoryRoot, settingsPath);

        if (!File.Exists(resolvedSettingsPath))
        {
            throw new FileNotFoundException("AIMonitor settings file was not found.", resolvedSettingsPath);
        }

        using FileStream stream = File.OpenRead(resolvedSettingsPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement monitor = document.RootElement.GetProperty("Monitor");
        string watchedSolutionPath = RequireString(monitor, "WatchedSolutionPath");
        string runtimeRoot = GetString(monitor, "RuntimeRoot") ?? "runtime";
        IReadOnlyList<string> winMergeCandidatePaths = GetStringArray(monitor, "WinMergeCandidatePaths");
        string settingsDirectory = Path.GetDirectoryName(resolvedSettingsPath) ?? resolvedRepositoryRoot;

        return MonitorSettings.Create(
            resolvedRepositoryRoot,
            ResolvePath(watchedSolutionPath, settingsDirectory),
            ResolvePath(runtimeRoot, resolvedRepositoryRoot),
            ResolvePaths(winMergeCandidatePaths, settingsDirectory));
    }

    public static string SaveLocal(
        string repositoryRoot,
        string watchedSolutionPath,
        string? runtimeRoot = null,
        string? settingsPath = null)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = ResolveSettingsPath(resolvedRepositoryRoot, settingsPath);
        string resolvedWatchedSolutionPath = Path.GetFullPath(watchedSolutionPath);
        string resolvedRuntimeRoot = string.IsNullOrWhiteSpace(runtimeRoot)
            ? "runtime"
            : runtimeRoot;

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedSettingsPath) ?? resolvedRepositoryRoot);
        IReadOnlyList<string> existingWinMergeCandidatePaths = LoadExistingWinMergeCandidatePaths(resolvedSettingsPath);
        LocalSettingsFile file = new(
            new LocalMonitorSettings(
                resolvedWatchedSolutionPath,
                resolvedRuntimeRoot,
                existingWinMergeCandidatePaths));
        File.WriteAllText(resolvedSettingsPath, JsonSerializer.Serialize(file, SerializerOptions) + Environment.NewLine);
        return resolvedSettingsPath;
    }

    private static string ResolveSettingsPath(string resolvedRepositoryRoot, string? settingsPath)
    {
        return Path.GetFullPath(
            settingsPath ?? Path.Combine(resolvedRepositoryRoot, "config", "appsettings.json"));
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

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> items = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                items.Add(item.GetString()!);
            }
        }

        return items;
    }

    private static IReadOnlyList<string> LoadExistingWinMergeCandidatePaths(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        using FileStream stream = File.OpenRead(settingsPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("Monitor", out JsonElement monitor)
            ? GetStringArray(monitor, "WinMergeCandidatePaths")
            : [];
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static IReadOnlyList<string> ResolvePaths(IReadOnlyList<string> paths, string baseDirectory)
    {
        return paths
            .Select(path => ResolvePath(path, baseDirectory))
            .ToArray();
    }

    private sealed record LocalSettingsFile(LocalMonitorSettings Monitor);

    private sealed record LocalMonitorSettings(
        string WatchedSolutionPath,
        string RuntimeRoot,
        IReadOnlyList<string> WinMergeCandidatePaths);
}
