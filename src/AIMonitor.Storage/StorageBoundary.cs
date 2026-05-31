namespace AIMonitor.Storage;

public static class StorageBoundary
{
    public const string Contract =
        "Storage owns SQLite schema and durable monitor state, not workflow decisions.";
}
