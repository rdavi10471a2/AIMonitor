using System.Security.Cryptography;
using System.Text;
using AIMonitor.Core;

namespace AIMonitor.Logging;

public static class MonitorMcpProxyPipeNames
{
    public static string GetDefaultPipeName(MonitorSettings settings)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(settings.RuntimeRoot));
        string hash = Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
        return $"AIMonitor.McpProxy.{hash}";
    }
}
