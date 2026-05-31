using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIMonitor.Logging;

public sealed class JsonLinesMonitorLogger : IMonitorLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    static JsonLinesMonitorLogger()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly object gate = new();
    private readonly string logPath;

    public JsonLinesMonitorLogger(string logPath)
    {
        this.logPath = Path.GetFullPath(logPath);
    }

    public string LogPath => logPath;

    public void Write(
        MonitorLogLevel level,
        string source,
        string eventName,
        string message,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        MonitorLogEntry entry = new(
            DateTimeOffset.UtcNow,
            level,
            source,
            eventName,
            message,
            properties ?? new Dictionary<string, string>());

        string line = JsonSerializer.Serialize(entry, SerializerOptions);

        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ".");
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }
}
