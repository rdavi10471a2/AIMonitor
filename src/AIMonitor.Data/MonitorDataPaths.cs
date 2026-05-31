using AIMonitor.Core;

namespace AIMonitor.Data;

public static class MonitorDataPaths
{
    public static string GetDefaultIndexDatabasePath(MonitorSettings settings)
    {
        return Path.Combine(settings.RuntimeRoot, "data", "solution-index.sqlite");
    }
}
