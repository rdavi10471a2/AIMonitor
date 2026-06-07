using AIMonitor.Core;

namespace AIMonitor.Planning.Tests
{
    internal sealed class PlanningTestWorkspace : IDisposable
    {
        private PlanningTestWorkspace(string root, MonitorSettings settings)
        {
            Root = root;
            Settings = settings;
        }

        public string Root { get; }

        public MonitorSettings Settings { get; }

        public static PlanningTestWorkspace Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "AIMonitor.Planning.Tests", Guid.NewGuid().ToString("N"));
            string watchedRoot = Path.Combine(root, "watched");
            Directory.CreateDirectory(watchedRoot);
            string solutionPath = Path.Combine(watchedRoot, "Watched.sln");
            File.WriteAllText(solutionPath, string.Empty);

            MonitorSettings settings = MonitorSettings.Create(
                root,
                solutionPath,
                Path.Combine(root, "runtime"));

            return new PlanningTestWorkspace(root, settings);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
