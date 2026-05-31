using System.Diagnostics;

namespace AIMonitor.Runtime;

public sealed class WinMergeDiffToolLauncher
{
    private static readonly string[] WinMergeCandidates =
    [
        @"C:\Program Files\WinMerge\WinMergeU.exe",
        @"C:\Program Files (x86)\WinMerge\WinMergeU.exe"
    ];

    public DiffLaunchResult Launch(DiffLaunchRequest request)
    {
        string? toolPath = ResolveToolPath(request.ExplicitToolPath);
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            return new DiffLaunchResult
            {
                Launched = false,
                Tool = "WinMerge",
                Message = "WinMerge was not found. Install WinMerge or pass --diff-tool <path>."
            };
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = toolPath,
            UseShellExecute = true
        };
        string displayName = GetDisplayName(request.OriginalFilePath);
        AddArguments(
            startInfo,
            "/u",
            "/ignoreeol:1",
            "/dl",
            $"New/Proposed (Working) - {displayName}",
            "/dr",
            $"Existing Source (Right) - {displayName}",
            request.ProposedFilePath,
            request.OriginalFilePath);

        Process? process = Process.Start(startInfo);
        return new DiffLaunchResult
        {
            Launched = process is not null,
            Tool = "WinMerge",
            ToolPath = toolPath,
            ProcessId = process?.Id ?? 0,
            Message = process is null ? "WinMerge process did not start." : "WinMerge launched."
        };
    }

    private static string? ResolveToolPath(string? explicitToolPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitToolPath))
        {
            return File.Exists(explicitToolPath) ? explicitToolPath : null;
        }

        return WinMergeCandidates.FirstOrDefault(File.Exists);
    }

    private static string GetDisplayName(string originalFilePath)
    {
        string fileName = Path.GetFileName(originalFilePath);
        string relativeHint = Path.GetFileName(Path.GetDirectoryName(originalFilePath) ?? string.Empty);
        return string.IsNullOrWhiteSpace(relativeHint) ? fileName : $"{relativeHint}\\{fileName}";
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }
}
