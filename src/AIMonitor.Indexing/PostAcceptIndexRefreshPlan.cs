namespace AIMonitor.Indexing;

public sealed class PostAcceptIndexRefreshPlan
{
    public IReadOnlyList<string> OwningProjectPaths { get; set; } = [];
}
