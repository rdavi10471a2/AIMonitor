namespace AIMonitor.App;

public sealed record AppStartupOptions(string? SettingsPath)
{
    public static AppStartupOptions Parse(string[] args)
    {
        string? settingsPath = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--config", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                settingsPath = args[++index];
            }
        }

        return new AppStartupOptions(settingsPath);
    }
}
