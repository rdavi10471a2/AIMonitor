namespace CodexWindows.Configuration;

public sealed class CodexWorkflowProbe
{
    public string Name { get; set; } = "codex";

    public string Name_removed { get; set; } = "remove";

    public int Count { get; set; } = 1;

    public int Count_removed { get; set; } = -1;

    public bool IsEnabled { get; set; } = true;

    public bool IsEnabled_removed { get; set; }

    public static CodexWorkflowProbe CreateDefault() =>
        new()
        {
            Name = "codex",
            Count = 1,
            IsEnabled = true
        };

    public static CodexWorkflowProbe CreateDefault_removed() =>
        new()
        {
            Name_removed = "remove",
            Count_removed = -1,
            IsEnabled_removed = false
        };

    public string Describe() =>
        $"{Name}:{Count}:{IsEnabled}";

    public string Describe_removed() =>
        $"{Name_removed}:{Count_removed}:{IsEnabled_removed}";
}
