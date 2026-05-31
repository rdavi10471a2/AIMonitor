namespace AIMonitor.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("AIMonitor CLI scaffold");
            Console.WriteLine("Commands planned: status, load-solution, rebuild-index, stage, launch-diff, record-decision.");
            return 0;
        }

        Console.Error.WriteLine($"Unknown command: {args[0]}");
        return 2;
    }
}
