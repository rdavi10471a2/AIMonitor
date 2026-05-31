using System.ComponentModel;
using AIMonitor.Core;
using AIMonitor.Logging;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class MonitorDashboardControl : UserControl
{
    private readonly TabControl mainTabs;
    private readonly SolutionIndexControl solutionIndexControl;
    private readonly SharedLogControl sharedLogControl;
    private readonly Label statusLabel;
    private MonitorLogService? logService;

    public MonitorDashboardControl()
    {
        Dock = DockStyle.Fill;

        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        mainTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        solutionIndexControl = new SolutionIndexControl
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(850, 360)
        };
        solutionIndexControl.StatusChanged += status => statusLabel.Text = status;
        sharedLogControl = new SharedLogControl
        {
            Dock = DockStyle.Fill
        };

        mainTabs.TabPages.Add(BuildTab("Solution Index", solutionIndexControl));
        mainTabs.TabPages.Add(BuildTab("Shared Log", sharedLogControl));

        Controls.Add(BuildLayout());

        Load += (_, _) =>
        {
            ConfigureLogViewer();
        };
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        Panel statusPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 2, 8, 2)
        };
        statusPanel.Controls.Add(statusLabel);

        root.Controls.Add(mainTabs, 0, 0);
        root.Controls.Add(statusPanel, 0, 1);
        return root;
    }

    private static TabPage BuildTab(string title, Control control)
    {
        TabPage page = new(title);
        page.Controls.Add(control);
        return page;
    }

    private void ConfigureLogViewer()
    {
        try
        {
            string repositoryRoot = AppPathResolver.FindRepositoryRoot();
            MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot);
            logService = new MonitorLogService(MonitorLogPaths.GetDefaultLogPath(settings));
            solutionIndexControl.SetLogger(logService);
            sharedLogControl.Connect(logService.LogPath, logService);
            logService.Write(MonitorLogLevel.Information, "AIMonitor.App", "app.started", "AIMonitor UI logging service connected.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }
}
