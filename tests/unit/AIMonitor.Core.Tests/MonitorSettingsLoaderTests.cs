using AIMonitor.Core;

namespace AIMonitor.Core.Tests;

public sealed class MonitorSettingsLoaderTests
{
    [Fact]
    public async Task Load_reads_single_watched_solution_path()
    {
        string root = Path.Combine(Path.GetTempPath(), "AIMonitorTests", Guid.NewGuid().ToString("N"));
        string config = Path.Combine(root, "config");
        Directory.CreateDirectory(config);
        string solutionPath = Path.Combine(root, "Fixture.slnx");
        string settingsPath = Path.Combine(config, "appsettings.json");
        await File.WriteAllTextAsync(solutionPath, "<Solution />");
        await File.WriteAllTextAsync(settingsPath, $$"""
            {
              "Monitor": {
                "WatchedSolutionPath": "{{solutionPath.Replace("\\", "\\\\")}}",
                "RuntimeRoot": "runtime"
              }
            }
            """);

        MonitorSettings settings = MonitorSettingsLoader.Load(root, settingsPath);

        Assert.Equal(Path.GetFullPath(solutionPath), settings.WatchedSolutionPath);
        Assert.Equal(Path.Combine(root, "runtime"), settings.RuntimeRoot);
    }
}
