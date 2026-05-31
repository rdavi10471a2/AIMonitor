using System.ComponentModel;
using AIMonitor.Core;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class MonitorDashboardControl : UserControl
{
    private const int FriendlySplitterWidth = 12;

    private readonly SplitContainer verticalSplit;
    private readonly TabControl mainTabs;
    private readonly SolutionIndexControl solutionIndexControl;
    private readonly TextBox logBox;
    private readonly Label statusLabel;
    private bool splitterLayoutSized;

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
        verticalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark,
            Panel2MinSize = 120
        };
        verticalSplit.Panel1.BackColor = SystemColors.Control;
        verticalSplit.Panel2.BackColor = SystemColors.Control;

        solutionIndexControl = new SolutionIndexControl
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(850, 360)
        };
        solutionIndexControl.StatusChanged += status => statusLabel.Text = status;

        logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };

        mainTabs.TabPages.Add(BuildTab("Solution Index", solutionIndexControl));
        mainTabs.TabPages.Add(BuildTab("Logs", logBox));

        verticalSplit.Panel1.Controls.Add(mainTabs);
        verticalSplit.Panel2.Controls.Add(BuildStatusPanel());
        Controls.Add(verticalSplit);

        Load += (_, _) =>
        {
            BeginInvoke(ApplyInitialSplitterLayout);
            RefreshLogPreview();
        };
    }

    private Control BuildStatusPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button refreshLogsButton = new()
        {
            Text = "Refresh Logs",
            AutoSize = true,
            Anchor = AnchorStyles.Right
        };
        refreshLogsButton.Click += (_, _) => RefreshLogPreview();

        panel.Controls.Add(statusLabel, 0, 0);
        panel.Controls.Add(refreshLogsButton, 1, 0);
        return panel;
    }

    private static TabPage BuildTab(string title, Control control)
    {
        TabPage page = new(title);
        page.Controls.Add(control);
        return page;
    }

    private void ApplyInitialSplitterLayout()
    {
        if (splitterLayoutSized)
        {
            return;
        }

        if (verticalSplit.Height < 500)
        {
            BeginInvoke(ApplyInitialSplitterLayout);
            return;
        }

        splitterLayoutSized = true;
        int maxDistance = Math.Max(25, verticalSplit.Height - verticalSplit.Panel2MinSize - verticalSplit.SplitterWidth);
        verticalSplit.SplitterDistance = Math.Clamp(verticalSplit.Height - 160, verticalSplit.Panel1MinSize, maxDistance);
    }

    private void RefreshLogPreview()
    {
        try
        {
            string repositoryRoot = AppPathResolver.FindRepositoryRoot();
            MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot);
            string logPath = Logging.MonitorLogPaths.GetDefaultLogPath(settings);
            logBox.Text = File.Exists(logPath)
                ? string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(500))
                : $"No log file yet: {logPath}";
        }
        catch (Exception ex)
        {
            logBox.Text = ex.Message;
        }
    }
}
