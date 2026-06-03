namespace CodexWindows.Configuration;

public sealed class CodexWorkflowProbe
{
    public string Name { get; set; } = "codex";

    public int Count { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public static CodexWorkflowProbe CreateDefault() =>
        new()
        {
            Name = "codex",
            Count = 1,
            IsEnabled = true
        };

    public string Describe() =>
        $"{Name}:{Count}:{IsEnabled}";
}
