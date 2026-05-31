using System.ComponentModel;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Logging;
using AIMonitor.MSBuild;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class SolutionIndexControl : UserControl
{
    private const int FriendlySplitterWidth = 12;

    private readonly Button rebuildButton;
    private readonly Button refreshButton;
    private readonly TextBox watchedSolutionBox;
    private readonly TextBox databasePathBox;
    private readonly Label statusLabel;
    private readonly TreeView indexTree;
    private readonly DataGridView projectsGrid;
    private readonly DataGridView documentsGrid;
    private readonly DataGridView symbolsGrid;
    private readonly DataGridView referencesGrid;
    private readonly DataGridView packagesGrid;
    private readonly TextBox rawBox;
    private readonly SplitContainer mainSplit;
    private readonly SplitContainer detailSplit;
    private MonitorSettings? settings;
    private SolutionIndexStore? store;
    private bool splitterLayoutSized;

    public SolutionIndexControl()
    {
        Dock = DockStyle.Fill;

        rebuildButton = new Button { Text = "Rebuild Index", AutoSize = true };
        refreshButton = new Button { Text = "Refresh", AutoSize = true };
        watchedSolutionBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        databasePathBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        indexTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false
        };
        projectsGrid = CreateGrid();
        documentsGrid = CreateGrid();
        symbolsGrid = CreateGrid();
        referencesGrid = CreateGrid();
        packagesGrid = CreateGrid();
        rawBox = new TextBox
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
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark
        };
        detailSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark
        };
        mainSplit.Panel1.BackColor = SystemColors.Control;
        mainSplit.Panel2.BackColor = SystemColors.Control;
        detailSplit.Panel1.BackColor = SystemColors.Control;
        detailSplit.Panel2.BackColor = SystemColors.Control;

        Controls.Add(BuildLayout());
        WireEvents();
        Load += (_, _) =>
        {
            BeginInvoke(ApplyInitialSplitterLayout);
            LoadSettingsAndRefresh();
        };
    }

    public event Action<string>? StatusChanged;

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        toolbar.Controls.Add(rebuildButton);
        toolbar.Controls.Add(refreshButton);

        TableLayoutPanel watchedRow = BuildLabeledRow("Watched Solution", watchedSolutionBox);
        TableLayoutPanel databaseRow = BuildLabeledRow("Database", databasePathBox);

        GroupBox treeGroup = new()
        {
            Text = "MSBuild Projects",
            Dock = DockStyle.Fill
        };
        treeGroup.Controls.Add(indexTree);

        TabControl detailTabs = new()
        {
            Dock = DockStyle.Fill
        };
        detailTabs.TabPages.Add(BuildGridTab("Projects", projectsGrid));
        detailTabs.TabPages.Add(BuildGridTab("Documents", documentsGrid));
        detailTabs.TabPages.Add(BuildGridTab("Symbols", symbolsGrid));
        detailTabs.TabPages.Add(BuildGridTab("References", referencesGrid));
        detailTabs.TabPages.Add(BuildGridTab("Packages", packagesGrid));
        detailTabs.TabPages.Add(BuildRawTab());

        detailSplit.Panel1.Controls.Add(detailTabs);
        detailSplit.Panel2.Controls.Add(statusLabel);
        mainSplit.Panel1.Controls.Add(treeGroup);
        mainSplit.Panel2.Controls.Add(detailSplit);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(watchedRow, 0, 1);
        root.Controls.Add(databaseRow, 0, 2);
        root.Controls.Add(mainSplit, 0, 3);
        return root;
    }

    private static TableLayoutPanel BuildLabeledRow(string label, Control control)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private static TabPage BuildGridTab(string title, DataGridView grid)
    {
        TabPage page = new(title);
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildRawTab()
    {
        TabPage page = new("Raw");
        page.Controls.Add(rawBox);
        return page;
    }

    private void WireEvents()
    {
        rebuildButton.Click += async (_, _) => await RebuildIndexAsync();
        refreshButton.Click += (_, _) => LoadSettingsAndRefresh();
        indexTree.AfterSelect += (_, args) => SelectTreeNode(args.Node);
        symbolsGrid.SelectionChanged += (_, _) => LoadReferencesForSelectedSymbol();
    }

    private void LoadSettingsAndRefresh()
    {
        try
        {
            string repositoryRoot = AppPathResolver.FindRepositoryRoot();
            settings = MonitorSettingsLoader.Load(repositoryRoot);
            string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
            store = new SolutionIndexStore(new SolutionIndexDatabase(databasePath));
            watchedSolutionBox.Text = settings.WatchedSolutionPath;
            databasePathBox.Text = databasePath;
            RefreshFromStore();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task RebuildIndexAsync()
    {
        if (settings is null || store is null)
        {
            LoadSettingsAndRefresh();
        }

        if (settings is null || store is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            JsonLinesMonitorLogger logger = new(MonitorLogPaths.GetDefaultLogPath(settings));
            logger.Write(MonitorLogLevel.Information, "AIMonitor.App", "index.rebuild.started", "WinForms index rebuild started.");
            SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
            SolutionIndexSummary summary = await builder.RebuildAsync(settings);
            logger.Write(MonitorLogLevel.Information, "AIMonitor.App", "index.rebuild.completed", "WinForms index rebuild completed.");
            RefreshFromStore();
            SetStatus($"Indexed {summary.ProjectCount} projects, {summary.DocumentCount} documents, {summary.DiagnosticCount} diagnostics.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshFromStore()
    {
        if (store is null)
        {
            return;
        }

        SolutionIndexSummary summary = store.GetSummary();
        IReadOnlyList<IndexedProjectRow> projects = store.ListProjects();
        IReadOnlyList<IndexedDocumentRow> documents = store.ListDocuments();
        IReadOnlyList<IndexedSymbolRow> symbols = store.ListSymbols();
        IReadOnlyList<IndexedReferenceRow> references = store.ListReferences();
        IReadOnlyList<IndexedPackageReferenceRow> packages = store.ListPackageReferences();

        projectsGrid.DataSource = projects.ToList();
        documentsGrid.DataSource = documents.ToList();
        symbolsGrid.DataSource = symbols.ToList();
        referencesGrid.DataSource = references.ToList();
        packagesGrid.DataSource = packages.ToList();
        rawBox.Text = $"Projects: {projects.Count}{Environment.NewLine}Documents: {documents.Count}{Environment.NewLine}Symbols: {symbols.Count}{Environment.NewLine}References: {references.Count}";
        LoadTree(projects, documents);
        SetStatus($"Current index | Projects: {summary.ProjectCount} | Documents: {summary.DocumentCount} | Symbols: {symbols.Count} | References: {references.Count} | C# symbols only for now.");
    }

    private void LoadTree(
        IReadOnlyList<IndexedProjectRow> projects,
        IReadOnlyList<IndexedDocumentRow> documents)
    {
        indexTree.BeginUpdate();
        try
        {
            indexTree.Nodes.Clear();
            foreach (IndexedProjectRow project in projects)
            {
                TreeNode projectNode = new($"{project.Name} ({project.TargetFramework})")
                {
                    Tag = project.ProjectPath
                };
                foreach (IndexedDocumentRow document in documents.Where(document => string.Equals(document.ProjectPath, project.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                {
                    projectNode.Nodes.Add(new TreeNode(Path.GetFileName(document.FilePath))
                    {
                        Tag = document.FilePath
                    });
                }

                indexTree.Nodes.Add(projectNode);
            }
        }
        finally
        {
            indexTree.EndUpdate();
        }
    }

    private void SelectTreeNode(TreeNode? node)
    {
        if (node?.Tag is not string path)
        {
            return;
        }

        rawBox.Text = path;
    }

    private void LoadReferencesForSelectedSymbol()
    {
        if (store is null || symbolsGrid.CurrentRow?.DataBoundItem is not IndexedSymbolRow symbol)
        {
            return;
        }

        IReadOnlyList<IndexedReferenceRow> references = store.ListReferences(symbol.StableKey);
        referencesGrid.DataSource = references.ToList();
        rawBox.Text = symbol.StableKey;
        SetStatus($"Selected {symbol.Kind} {symbol.Name} | References: {references.Count}");
    }

    private void SetBusy(bool busy)
    {
        rebuildButton.Enabled = !busy;
        refreshButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (busy)
        {
            SetStatus("Rebuilding index...");
        }
    }

    private void SetStatus(string status)
    {
        statusLabel.Text = status;
        StatusChanged?.Invoke(status);
    }

    private void ApplyInitialSplitterLayout()
    {
        if (splitterLayoutSized || mainSplit.Width < 700 || detailSplit.Height < 300)
        {
            return;
        }

        splitterLayoutSized = true;
        mainSplit.Panel1MinSize = 220;
        mainSplit.Panel2MinSize = 500;
        mainSplit.SplitterDistance = Math.Clamp(320, mainSplit.Panel1MinSize, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth);
        detailSplit.Panel1MinSize = 260;
        detailSplit.Panel2MinSize = 60;
        detailSplit.SplitterDistance = Math.Clamp(detailSplit.Height - 90, detailSplit.Panel1MinSize, detailSplit.Height - detailSplit.Panel2MinSize - detailSplit.SplitterWidth);
    }

    private static DataGridView CreateGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
    }
}
