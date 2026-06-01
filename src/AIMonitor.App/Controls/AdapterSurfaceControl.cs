using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIMonitor.Logging;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class AdapterSurfaceControl : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly Label pathLabel;
    private readonly Button refreshButton;
    private readonly DataGridView eventGrid;
    private readonly PropertyGrid propertyGrid;
    private readonly TextBox responseBox;
    private readonly SplitContainer mainSplit;
    private readonly SplitContainer detailSplit;
    private readonly BindingList<AdapterEventView> eventRows = [];
    private IMonitorLogEventSource? eventSource;
    private string? logPath;

    public AdapterSurfaceControl()
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
            Text = "Clear",
            AutoSize = true,
            Anchor = AnchorStyles.Right
        };
        eventGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = true,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            DataSource = eventRows
        };
        propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            HelpVisible = false,
            ToolbarVisible = false,
            PropertySort = PropertySort.CategorizedAlphabetical
        };
        responseBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10,
            BackColor = SystemColors.ControlDark
        };
        detailSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 10,
            BackColor = SystemColors.ControlDark
        };

        Controls.Add(BuildLayout());
        refreshButton.Click += (_, _) => ClearEvents();
        eventGrid.DataBindingComplete += (_, _) => ConfigureGridColumns();
        eventGrid.SelectionChanged += (_, _) => ShowSelectedEvent();
    }

    public event Action<string>? StatusChanged;

    public void Connect(string path, IMonitorLogEventSource source)
    {
        if (eventSource is not null)
        {
            eventSource.EntryWritten -= OnEntryWritten;
        }

        logPath = Path.GetFullPath(path);
        pathLabel.Text = $"Live adapter events; persisted log: {logPath}";
        eventSource = source;
        eventSource.EntryWritten += OnEntryWritten;
        ClearEvents();
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

        detailSplit.Panel1.Controls.Add(WrapSection("Selected Event", propertyGrid));
        detailSplit.Panel2.Controls.Add(WrapSection("Response JSON", responseBox));
        mainSplit.Panel1.Controls.Add(WrapSection("Live Adapter Requests and Responses", eventGrid));
        mainSplit.Panel2.Controls.Add(detailSplit);

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(mainSplit, 0, 1);
        return root;
    }

    private void OnEntryWritten(MonitorLogEntry entry)
    {
        if (!IsAdapterEvent(entry) || IsDisposed)
        {
            return;
        }

        string rawJson = JsonSerializer.Serialize(entry, JsonOptions);
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed)
                    {
                        AddEntry(entry, rawJson);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        AddEntry(entry, rawJson);
    }

    private void ClearEvents()
    {
        eventRows.Clear();
        propertyGrid.SelectedObject = null;
        responseBox.Clear();
        SetStatus("Adapter observer | Live events: 0");
    }

    private void AddEntry(MonitorLogEntry entry, string rawJson, bool selectNewRow = true)
    {
        AdapterEventView row = new(entry, rawJson);
        bool shouldSelectRow = selectNewRow && ShouldAutoSelect(row);
        if (!string.IsNullOrWhiteSpace(row.FullRequestId))
        {
            for (int index = 0; index < eventRows.Count; index++)
            {
                if (eventRows[index].FullRequestId.Equals(row.FullRequestId, StringComparison.Ordinal)
                    && eventRows[index].EventName.Equals("adapter.query.started", StringComparison.OrdinalIgnoreCase)
                    && row.EventName.Equals("adapter.query.completed", StringComparison.OrdinalIgnoreCase))
                {
                    eventRows[index] = row;
                    if (shouldSelectRow)
                    {
                        SelectRow(index);
                    }

                    return;
                }
            }
        }

        eventRows.Add(row);
        if (eventRows.Count > 500)
        {
            eventRows.RemoveAt(0);
        }

        if (shouldSelectRow)
        {
            SelectRow(eventRows.Count - 1);
        }

        SetStatus($"Adapter observer | Live events: {eventRows.Count}");
    }

    private void SelectNewestRow()
    {
        if (eventRows.Count == 0 || eventGrid.Rows.Count == 0)
        {
            return;
        }

        SelectRow(eventRows.Count - 1);
    }

    private void SelectRow(int index)
    {
        if (index >= 0 && index < eventGrid.Rows.Count)
        {
            eventGrid.ClearSelection();
            eventGrid.Rows[index].Selected = true;
            eventGrid.CurrentCell = eventGrid.Rows[index].Cells[0];
            eventGrid.FirstDisplayedScrollingRowIndex = index;
        }
    }

    private static bool ShouldAutoSelect(AdapterEventView row)
    {
        if (row.Phase.Equals("Response", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(row.PrettyResponseJson)
                || !string.IsNullOrWhiteSpace(row.ContentTextPreview)))
        {
            return true;
        }

        return row.Phase.Equals("Warning", StringComparison.OrdinalIgnoreCase)
            || row.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase)
            || row.Level.Equals("Error", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureGridColumns()
    {
        SetColumnWidth(nameof(AdapterEventView.Time), 120);
        SetColumnWidth(nameof(AdapterEventView.Level), 90);
        SetColumnWidth(nameof(AdapterEventView.Source), 150);
        SetColumnWidth(nameof(AdapterEventView.EventName), 180);
        SetColumnWidth(nameof(AdapterEventView.Outcome), 150);
        SetColumnWidth(nameof(AdapterEventView.Review), 120);
        SetColumnWidth(nameof(AdapterEventView.RequestId), 120);
        SetColumnWidth(nameof(AdapterEventView.WorkflowSessionId), 140);
        SetColumnWidth(nameof(AdapterEventView.TransportSessionId), 140);
        SetColumnWidth(nameof(AdapterEventView.Command), 160);
        SetColumnWidth(nameof(AdapterEventView.File), 260);
        SetColumnWidth(nameof(AdapterEventView.Record), 140);
        SetColumnWidth(nameof(AdapterEventView.IsError), 70);
        SetColumnWidth(nameof(AdapterEventView.DurationMs), 90);
        SetColumnWidth(nameof(AdapterEventView.ContentCount), 90);
        SetColumnWidth(nameof(AdapterEventView.Message), 360);
        SetColumnWidth(nameof(AdapterEventView.ContentTextPreview), 520);
        SetColumnHeader(nameof(AdapterEventView.Time), "Time");
        SetColumnHeader(nameof(AdapterEventView.Level), "Level");
        SetColumnHeader(nameof(AdapterEventView.Source), "Source");
        SetColumnHeader(nameof(AdapterEventView.Phase), "Phase");
        SetColumnHeader(nameof(AdapterEventView.EventName), "Runtime Event");
        SetColumnHeader(nameof(AdapterEventView.Outcome), "Outcome");
        SetColumnHeader(nameof(AdapterEventView.Review), "Review");
        SetColumnHeader(nameof(AdapterEventView.RequestId), "Request ID");
        SetColumnHeader(nameof(AdapterEventView.WorkflowSessionId), "Workflow Session");
        SetColumnHeader(nameof(AdapterEventView.TransportSessionId), "Transport Session");
        SetColumnHeader(nameof(AdapterEventView.Command), "Tool / Command");
        SetColumnHeader(nameof(AdapterEventView.File), "File");
        SetColumnHeader(nameof(AdapterEventView.Record), "Record");
        SetColumnHeader(nameof(AdapterEventView.IsError), "Error");
        SetColumnHeader(nameof(AdapterEventView.DurationMs), "ms");
        SetColumnHeader(nameof(AdapterEventView.ContentCount), "Items");
        SetColumnHeader(nameof(AdapterEventView.Message), "Runtime Message");
        SetColumnHeader(nameof(AdapterEventView.ContentTextPreview), "Response Preview");
        ApplyRowStyles();
    }

    private void SetColumnWidth(string name, int width)
    {
        if (eventGrid.Columns[name] is { } column)
        {
            column.Width = width;
            column.Resizable = DataGridViewTriState.True;
        }
    }

    private void SetColumnHeader(string name, string header)
    {
        if (eventGrid.Columns[name] is { } column)
        {
            column.HeaderText = header;
        }
    }

    private void ShowSelectedEvent()
    {
        if (eventGrid.CurrentRow?.DataBoundItem is not AdapterEventView row)
        {
            return;
        }

        propertyGrid.SelectedObject = row.Detail;
        responseBox.Text = row.PrettyResponseJson;
    }

    private void ApplyRowStyles()
    {
        foreach (DataGridViewRow gridRow in eventGrid.Rows)
        {
            if (gridRow.DataBoundItem is not AdapterEventView row)
            {
                continue;
            }

            if (row.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                || row.Level.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                gridRow.DefaultCellStyle.BackColor = Color.LemonChiffon;
                continue;
            }

            if (row.Outcome.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase))
            {
                gridRow.DefaultCellStyle.BackColor = Color.MistyRose;
                continue;
            }

            if (row.Outcome.Equals("accepted-normalized", StringComparison.OrdinalIgnoreCase))
            {
                gridRow.DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
                continue;
            }

            if (row.Outcome.Equals("accepted", StringComparison.OrdinalIgnoreCase))
            {
                gridRow.DefaultCellStyle.BackColor = Color.Honeydew;
                continue;
            }

            if (row.Outcome.Equals("rejected", StringComparison.OrdinalIgnoreCase))
            {
                gridRow.DefaultCellStyle.BackColor = Color.Gainsboro;
            }
        }
    }

    private void SetStatus(string status)
    {
        StatusChanged?.Invoke(status);
    }

    private static bool IsAdapterEvent(MonitorLogEntry entry)
    {
        return entry.Source.Contains(".Cli", StringComparison.OrdinalIgnoreCase)
            || entry.Source.Contains(".Mcp", StringComparison.OrdinalIgnoreCase)
            || entry.EventName.StartsWith("adapter.", StringComparison.OrdinalIgnoreCase);
    }

    private static string PrettyJsonOrText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static Control WrapSection(string title, Control content)
    {
        TableLayoutPanel section = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 4, 0, 4)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        section.Controls.Add(content, 0, 1);
        return section;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class AdapterEventView
    {
        public AdapterEventView(MonitorLogEntry entry, string rawJson)
        {
            Time = entry.TimestampUtc.ToLocalTime().ToString("h:mm:ss tt");
            Level = entry.Level.ToString();
            Source = entry.Source;
            EventName = entry.EventName;
            Phase = GetPhase(entry);
            Message = entry.Message;
            FullRequestId = entry.Properties.TryGetValue("requestId", out string? requestId) ? requestId : string.Empty;
            RequestId = FullRequestId[..Math.Min(12, FullRequestId.Length)];
            string bridgeSessionId = entry.Properties.TryGetValue("sessionId", out string? transportSessionId)
                ? transportSessionId
                : string.Empty;
            TransportSessionId = ShortId(bridgeSessionId);
            Command = entry.Properties.TryGetValue("toolName", out string? toolName)
                ? toolName
                : entry.Properties.TryGetValue("command", out string? command)
                    ? command
                    : entry.Properties.TryGetValue("testName", out string? testName)
                        ? testName
                        : entry.EventName.StartsWith("index.refresh-after-accept.", StringComparison.OrdinalIgnoreCase)
                            ? "index refresh after accept"
                            : string.Empty;
            IsError = entry.Properties.TryGetValue("isError", out string? isError) ? isError : string.Empty;
            DurationMs = entry.Properties.TryGetValue("durationMs", out string? durationMs) ? durationMs : string.Empty;
            ContentCount = entry.Properties.TryGetValue("contentCount", out string? contentCount) ? contentCount : string.Empty;
            ContentTextPreview = entry.Properties.TryGetValue("contentTextPreview", out string? contentTextPreview) ? contentTextPreview : string.Empty;
            string responseText = entry.Properties.TryGetValue("contentText", out string? contentText)
                ? contentText
                : ContentTextPreview;
            string argumentText = entry.Properties.TryGetValue("arguments", out string? arguments)
                ? arguments
                : string.Empty;
            PrettyResponseJson = PrettyJsonOrText(responseText);
            using JsonDocument? responseDocument = TryParseJson(responseText);
            using JsonDocument? argumentDocument = TryParseJson(argumentText);
            WorkflowSessionId = ShortId(
                ExtractResponseString(responseDocument, "sessionId")
                ?? ExtractResponseString(argumentDocument, "sessionId")
                ?? string.Empty);
            Outcome = ExtractResponseString(responseDocument, "classification")
                ?? ExtractResponseString(responseDocument, "status")
                ?? ExtractResponseString(responseDocument, "indexRefresh.status")
                ?? (entry.Properties.TryGetValue("validationStatus", out string? validationStatus) ? validationStatus : null)
                ?? (entry.Properties.TryGetValue("projectCount", out _) ? "rebuilt" : null)
                ?? (entry.Properties.TryGetValue("outcome", out string? testOutcome) ? testOutcome : null)
                ?? string.Empty;
            Review = ExtractResponseString(responseDocument, "launchStatus")
                ?? ExtractResponseString(responseDocument, "stagedRecord.launchStatus")
                ?? string.Empty;
            File = ShortPath(
                ExtractResponseString(responseDocument, "watchedFilePath")
                ?? ExtractResponseString(responseDocument, "stagedRecord.watchedFilePath")
                ?? (entry.Properties.TryGetValue("watchedFilePath", out string? watchedFilePath) ? watchedFilePath : null)
                ?? string.Empty);
            Record = ShortId(
                ExtractResponseString(responseDocument, "stagedRecordId")
                ?? ExtractResponseString(responseDocument, "stagedRecord.stagedRecordId")
                ?? ExtractResponseString(responseDocument, "lastStagedRecordId")
                ?? (entry.Properties.TryGetValue("stagedRecordId", out string? stagedRecordId) ? stagedRecordId : null)
                ?? string.Empty);
            RawJson = rawJson;
            Detail = new AdapterEventDetail(entry);
        }

        public string Time { get; }

        public string Level { get; }

        public string Source { get; }

        public string EventName { get; }

        public string Phase { get; }

        public string Outcome { get; }

        public string Review { get; }

        public string RequestId { get; }

        public string WorkflowSessionId { get; }

        public string TransportSessionId { get; }

        [Browsable(false)]
        public string FullRequestId { get; }

        public string Command { get; }

        public string File { get; }

        public string Record { get; }

        public string IsError { get; }

        public string DurationMs { get; }

        public string ContentCount { get; }

        public string Message { get; }

        public string ContentTextPreview { get; }

        [Browsable(false)]
        public string PrettyResponseJson { get; }

        [Browsable(false)]
        public string RawJson { get; }

        [Browsable(false)]
        public AdapterEventDetail Detail { get; }
    }

    private sealed class AdapterEventDetail
    {
        public AdapterEventDetail(MonitorLogEntry entry)
        {
            Time = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm:ss tt");
            Level = entry.Level.ToString();
            Source = entry.Source;
            EventName = entry.EventName;
            Message = entry.Message;
            ContentTextPreview = entry.Properties.TryGetValue("contentTextPreview", out string? contentTextPreview)
                ? contentTextPreview
                : string.Empty;
            ResponseJson = PrettyJsonOrText(entry.Properties.TryGetValue("contentText", out string? contentText)
                ? contentText
                : ContentTextPreview);
            Properties = string.Join(Environment.NewLine, entry.Properties.Select(pair => $"{pair.Key}: {pair.Value}"));
        }

        [Category("Event")]
        public string Time { get; }

        [Category("Event")]
        public string Level { get; }

        [Category("Event")]
        public string Source { get; }

        [Category("Event")]
        public string EventName { get; }

        [Category("Event")]
        public string Message { get; }

        [Category("MCP-Like Response")]
        public string ContentTextPreview { get; }

        [Category("MCP-Like Response")]
        public string ResponseJson { get; }

        [Category("Properties")]
        public string Properties { get; }
    }

    private static string GetPhase(MonitorLogEntry entry)
    {
        if (entry.EventName.EndsWith(".started", StringComparison.OrdinalIgnoreCase))
        {
            return "Request";
        }

        if (entry.EventName.EndsWith(".completed", StringComparison.OrdinalIgnoreCase))
        {
            return "Response";
        }

        if (entry.EventName.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || entry.Level == MonitorLogLevel.Warning
            || entry.Level == MonitorLogLevel.Error)
        {
            return "Warning";
        }

        return "Runtime";
    }

    private static JsonDocument? TryParseJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractResponseString(JsonDocument? document, string path)
    {
        if (document is null)
        {
            return null;
        }

        JsonElement current = document.RootElement;
        foreach (string segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => current.ToString(),
            _ => null
        };
    }

    private static string ShortId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value[..Math.Min(14, value.Length)];
    }

    private static string ShortPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileName(value);
        string directoryName = Path.GetFileName(Path.GetDirectoryName(value) ?? string.Empty);
        return string.IsNullOrWhiteSpace(directoryName) ? fileName : $"{directoryName}\\{fileName}";
    }
}
