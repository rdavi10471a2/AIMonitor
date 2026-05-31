using System.ComponentModel;
using AIMonitor.Logging;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class SharedLogControl : UserControl
{
    private readonly TextBox logBox;
    private readonly Label pathLabel;
    private readonly Button refreshButton;
    private readonly List<string> inMemoryLines = [];
    private IMonitorLogEventSource? eventSource;
    private string? logPath;

    public SharedLogControl()
    {
        Dock = DockStyle.Fill;

        pathLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            Anchor = AnchorStyles.Right
        };
        logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        refreshButton.Click += (_, _) => Reload();

        Controls.Add(BuildLayout());
    }

    public void Connect(string path, IMonitorLogEventSource source)
    {
        if (eventSource is not null)
        {
            eventSource.EntryWritten -= OnEntryWritten;
        }

        logPath = Path.GetFullPath(path);
        pathLabel.Text = logPath;
        eventSource = source;
        eventSource.EntryWritten += OnEntryWritten;

        Reload();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (eventSource is not null)
            {
                eventSource.EntryWritten -= OnEntryWritten;
            }
        }

        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        TableLayoutPanel top = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(pathLabel, 0, 0);
        top.Controls.Add(refreshButton, 1, 0);

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(logBox, 0, 1);
        return root;
    }

    private void OnEntryWritten(MonitorLogEntry entry)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            AppendLine($"{entry.TimestampUtc:O} [{entry.Level}] {entry.Source} {entry.EventName}: {entry.Message}");
        });
    }

    private void AppendLine(string line)
    {
        inMemoryLines.Add(line);
        if (inMemoryLines.Count > 500)
        {
            inMemoryLines.RemoveAt(0);
        }

        logBox.Text = string.Join(Environment.NewLine, inMemoryLines);
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
    }

    private void Reload()
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            logBox.Text = "No log path configured.";
            return;
        }

        if (!File.Exists(logPath))
        {
            logBox.Text = $"No log file yet: {logPath}";
            return;
        }

        try
        {
            using FileStream stream = new(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            inMemoryLines.Clear();
            while (reader.ReadLine() is { } line)
            {
                inMemoryLines.Add(line);
                if (inMemoryLines.Count > 500)
                {
                    inMemoryLines.RemoveAt(0);
                }
            }

            logBox.Text = string.Join(Environment.NewLine, inMemoryLines);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
        catch (IOException ex)
        {
            logBox.Text = ex.Message;
        }
    }
}
