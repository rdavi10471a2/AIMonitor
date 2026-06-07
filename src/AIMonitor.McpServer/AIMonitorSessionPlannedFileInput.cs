using System.ComponentModel;

namespace AIMonitor.McpServer;

public sealed class AIMonitorSessionPlannedFileInput
{
    [Description("Watched file path, absolute or relative to the watched project folder.")]
    public string Path { get; set; } = string.Empty;

    [Description("MSBuild project path that owns this planned file.")]
    public string OwningProjectPath { get; set; } = string.Empty;

    [Description("Planned file role such as edit, new-file, test, config, or context.")]
    public string Role { get; set; } = string.Empty;

    [Description("Short reason this file is expected to be part of the edit session.")]
    public string Reason { get; set; } = string.Empty;
}
