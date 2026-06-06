using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Indexing;
using AIMonitor.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class SolutionIndexControl : UserControl
{
    private const int FriendlySplitterWidth = 12;
    private const string MonacoHostName = "aimonitor.local";

    private readonly ToolStripMenuItem rebuildIndexMenuItem;
    private readonly ToolStripMenuItem refreshMenuItem;
    private readonly ToolStripMenuItem chooseSolutionMenuItem;
    private readonly ToolStripMenuItem openWatchedFolderMenuItem;
    private readonly ToolStripMenuItem openDatabaseFolderMenuItem;
    private readonly TextBox watchedSolutionBox;
    private readonly TextBox databasePathBox;
    private readonly Label statusLabel;
    private readonly TreeView indexTree;
    private readonly Label sourcePathLabel;
    private readonly ComboBox sourceThemeBox;
    private readonly WebView2 sourceEditor;
    private readonly TreeView sourceReferencesTree;
    private readonly TreeView sourceReferencedByTree;
    private readonly Button indexTreeToggleButton;
    private readonly Button referencesTreeToggleButton;
    private readonly Button referencedByTreeToggleButton;
    private readonly Label treeHintLabel;
    private readonly Label sourceHintLabel;
    private readonly ToolTip uiToolTip;
    private readonly FileOverviewControl fileOverviewControl;
    private readonly DataGridView projectsGrid;
    private readonly DataGridView documentsGrid;
    private readonly DataGridView symbolsGrid;
    private readonly DataGridView referencesGrid;
    private readonly DataGridView relationshipsGrid;
    private readonly DataGridView packagesGrid;
    private readonly TextBox rawBox;
    private readonly string? settingsPath;
    private readonly SplitContainer mainSplit;
    private readonly SplitContainer sourceSplit;
    private readonly TabControl detailTabs;
    private readonly TabPage overviewTab;
    private readonly TabPage projectsTab;
    private readonly TabPage documentsTab;
    private readonly TabPage symbolsTab;
    private readonly TabPage referencesTab;
    private readonly TabPage relationshipsTab;
    private readonly TabPage packagesTab;
    private readonly TabPage rawTab;
    private MonitorSettings? settings;
    private SolutionIndexStore? store;
    private IMonitorLogger? logger;
    private bool splitterLayoutSized;
    private bool sourceSplitterLayoutSized;
    private bool sourceEditorInitialized;
    private bool sourceEditorShellLoaded;
    private TaskCompletionSource<bool>? sourceEditorReadyCompletion;
    private SolutionIndexSummary currentSummary = new(string.Empty, DateTimeOffset.MinValue, 0, 0, 0);
    private IReadOnlyList<IndexedProjectRow> projects = [];
    private IReadOnlyList<IndexedDocumentRow> documents = [];
    private IReadOnlyList<IndexedSymbolRow> symbols = [];
    private IReadOnlyList<IndexedReferenceRow> references = [];
    private IReadOnlyList<IndexedPackageReferenceRow> packages = [];

    public SolutionIndexControl(string? settingsPath = null)
    {
        Dock = DockStyle.Fill;
        this.settingsPath = settingsPath;

        rebuildIndexMenuItem = new ToolStripMenuItem("Rebuild Index");
        refreshMenuItem = new ToolStripMenuItem("Refresh");
        chooseSolutionMenuItem = new ToolStripMenuItem("Choose Watched Solution...");
        openWatchedFolderMenuItem = new ToolStripMenuItem("Open Watched Folder");
        openDatabaseFolderMenuItem = new ToolStripMenuItem("Open Database Folder");
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
        indexTreeToggleButton = new Button
        {
            Text = "Collapse All"
        };
        referencesTreeToggleButton = new Button
        {
            Text = "Collapse All"
        };
        referencedByTreeToggleButton = new Button
        {
            Text = "Collapse All"
        };
        treeHintLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Text = "Double-click nodes to expand/collapse.",
            TextAlign = ContentAlignment.MiddleLeft
        };
        sourceHintLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Text = "Uses can include same-file symbols. Used By groups by symbol for files, by file for members.",
            TextAlign = ContentAlignment.MiddleLeft
        };
        uiToolTip = new ToolTip();
        sourcePathLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        sourceThemeBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        sourceThemeBox.Items.AddRange(["Dark", "Light", "High Contrast"]);
        sourceThemeBox.SelectedIndex = 0;
        sourceEditor = new WebView2
        {
            Dock = DockStyle.Fill
        };
        sourceReferencesTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            ShowNodeToolTips = true
        };
        sourceReferencedByTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            ShowNodeToolTips = true
        };
        fileOverviewControl = new FileOverviewControl();
        projectsGrid = CreateGrid();
        documentsGrid = CreateGrid();
        symbolsGrid = CreateGrid();
        referencesGrid = CreateGrid();
        relationshipsGrid = CreateGrid();
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
        sourceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None,
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark
        };
        detailTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        overviewTab = BuildControlTab("File Overview", fileOverviewControl);
        projectsTab = BuildGridTab("Projects", projectsGrid);
        documentsTab = BuildGridTab("Documents", documentsGrid);
        symbolsTab = BuildGridTab("Symbols", symbolsGrid);
        referencesTab = BuildGridTab("References", referencesGrid);
        relationshipsTab = BuildGridTab("Relationships", relationshipsGrid);
        packagesTab = BuildGridTab("Packages", packagesGrid);
        rawTab = BuildTextTab("Raw", rawBox);

        mainSplit.Panel1.BackColor = SystemColors.Control;
        mainSplit.Panel2.BackColor = SystemColors.Control;
        uiToolTip.SetToolTip(indexTree, "Project structure from the solution index. Select files or symbols to view source and references.");
        uiToolTip.SetToolTip(indexTreeToggleButton, "Toggle all Solution Explorer nodes.");
        uiToolTip.SetToolTip(sourceReferencesTree, "Uses: symbols the selected item points at. Same-file symbols can appear here.");
        uiToolTip.SetToolTip(sourceReferencedByTree, "Used By: source locations that point at symbols declared by the selected item.");
        uiToolTip.SetToolTip(referencesTreeToggleButton, "Toggle all Uses nodes.");
        uiToolTip.SetToolTip(referencedByTreeToggleButton, "Toggle all Used By nodes.");

        Controls.Add(BuildLayout());
        WireEvents();
        Load += (_, _) =>
        {
            BeginInvoke(ApplyInitialSplitterLayout);
            BeginInvoke(ApplyInitialSourceSplitterLayout);
            LoadSettingsAndRefresh();
        };
    }

    public event Action<string>? StatusChanged;
    public event Action? SettingsChanged;

    public void SetLogger(IMonitorLogger monitorLogger)
    {
        logger = monitorLogger;
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        GroupBox treeGroup = new()
        {
            Text = "Solution Explorer",
            Dock = DockStyle.Fill
        };
        TableLayoutPanel treePanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        treePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        treePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        treePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        treePanel.Controls.Add(indexTree, 0, 0);
        treePanel.Controls.Add(BuildTreeToggleRow(indexTreeToggleButton), 0, 1);
        treePanel.Controls.Add(treeHintLabel, 0, 2);
        treeGroup.Controls.Add(treePanel);

        mainSplit.Panel1.Controls.Add(treeGroup);
        mainSplit.Panel2.Controls.Add(BuildSourcePanel());

        root.Controls.Add(BuildMenu(), 0, 0);
        root.Controls.Add(BuildLabeledRow("Watched", watchedSolutionBox), 0, 1);
        root.Controls.Add(BuildLabeledRow("Database", databasePathBox), 0, 2);
        root.Controls.Add(mainSplit, 0, 3);
        return root;
    }

    private Control BuildSourcePanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        sourceSplit.Panel1.Controls.Add(sourceEditor);
        sourceSplit.Panel2.Controls.Add(BuildReferencesPanel());
        panel.Controls.Add(BuildSourceHeader(), 0, 0);
        panel.Controls.Add(sourceSplit, 0, 1);
        panel.Controls.Add(BuildSourceStatusRow(), 0, 2);
        return panel;
    }

    private static Control BuildTreeToggleRow(Button toggleButton)
    {
        Panel row = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 5, 6, 5)
        };
        toggleButton.AutoSize = false;
        toggleButton.Size = new Size(120, 24);
        toggleButton.Location = new Point(row.Padding.Left, row.Padding.Top);
        toggleButton.Margin = Padding.Empty;
        toggleButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        row.Controls.Add(toggleButton);
        return row;
    }

    private Control BuildSourceStatusRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(4, 2, 4, 2)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(statusLabel, 0, 0);
        row.Controls.Add(sourceHintLabel, 1, 0);
        return row;
    }

    private Control BuildSourceHeader()
    {
        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.Controls.Add(sourcePathLabel, 0, 0);
        header.Controls.Add(sourceThemeBox, 1, 0);
        return header;
    }

    private Control BuildReferencesPanel()
    {
        TabControl referencesTabs = new()
        {
            Dock = DockStyle.Fill
        };
        referencesTabs.TabPages.Add(BuildControlTab("Uses", BuildReferenceTreePanel(sourceReferencesTree, referencesTreeToggleButton)));
        referencesTabs.TabPages.Add(BuildControlTab("Used By", BuildReferenceTreePanel(sourceReferencedByTree, referencedByTreeToggleButton)));
        return referencesTabs;
    }

    private static Control BuildReferenceTreePanel(TreeView tree, Button toggleButton)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(BuildTreeToggleRow(toggleButton), 0, 0);
        panel.Controls.Add(tree, 0, 1);
        return panel;
    }

    private MenuStrip BuildMenu()
    {
        MenuStrip menu = new()
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden
        };

        ToolStripMenuItem fileMenu = new("File");
        fileMenu.DropDownItems.Add(chooseSolutionMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(openWatchedFolderMenuItem);
        fileMenu.DropDownItems.Add(openDatabaseFolderMenuItem);

        ToolStripMenuItem indexMenu = new("Index");
        indexMenu.DropDownItems.Add(rebuildIndexMenuItem);
        indexMenu.DropDownItems.Add(refreshMenuItem);

        menu.Items.Add(fileMenu);
        menu.Items.Add(indexMenu);
        return menu;
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

    private static TabPage BuildTextTab(string title, TextBox textBox)
    {
        TabPage page = new(title);
        page.Controls.Add(textBox);
        return page;
    }

    private static TabPage BuildControlTab(string title, Control control)
    {
        TabPage page = new(title);
        page.Controls.Add(control);
        return page;
    }

    private void WireEvents()
    {
        rebuildIndexMenuItem.Click += async (_, _) => await RebuildIndexAsync();
        refreshMenuItem.Click += (_, _) => LoadSettingsAndRefresh();
        chooseSolutionMenuItem.Click += (_, _) => ChooseWatchedSolution();
        openWatchedFolderMenuItem.Click += (_, _) => OpenFolder(settings?.WatchedProjectFolder);
        openDatabaseFolderMenuItem.Click += (_, _) => OpenFolder(Path.GetDirectoryName(databasePathBox.Text));
        indexTreeToggleButton.Click += (_, _) => ToggleTreeExpansion(indexTree, indexTreeToggleButton);
        referencesTreeToggleButton.Click += (_, _) => ToggleTreeExpansion(sourceReferencesTree, referencesTreeToggleButton);
        referencedByTreeToggleButton.Click += (_, _) => ToggleTreeExpansion(sourceReferencedByTree, referencedByTreeToggleButton);
        indexTree.AfterSelect += (_, args) => SelectTreeNode(args.Node);
        projectsGrid.CellDoubleClick += (_, _) => SelectProjectFromGrid();
        documentsGrid.CellDoubleClick += (_, _) => SelectDocumentFromGrid();
        symbolsGrid.CellDoubleClick += (_, _) => SelectSymbolFromGrid();
        referencesGrid.CellDoubleClick += (_, _) => SelectReferenceTargetFromGrid(referencesGrid);
        relationshipsGrid.CellDoubleClick += (_, _) => SelectReferenceTargetFromGrid(relationshipsGrid);
        sourceThemeBox.SelectedIndexChanged += async (_, _) => await ApplySourceThemeAsync();
        sourceReferencesTree.NodeMouseDoubleClick += (_, args) =>
        {
            if (args.Node is not null)
            {
                SelectSourceReferenceNode(args.Node);
            }
        };
        sourceReferencedByTree.NodeMouseDoubleClick += (_, args) =>
        {
            if (args.Node is not null)
            {
                SelectSourceReferenceNode(args.Node);
            }
        };
        indexTree.AfterExpand += (_, _) => UpdateTreeToggleButton(indexTree, indexTreeToggleButton);
        indexTree.AfterCollapse += (_, _) => UpdateTreeToggleButton(indexTree, indexTreeToggleButton);
        sourceReferencesTree.AfterExpand += (_, _) => UpdateTreeToggleButton(sourceReferencesTree, referencesTreeToggleButton);
        sourceReferencesTree.AfterCollapse += (_, _) => UpdateTreeToggleButton(sourceReferencesTree, referencesTreeToggleButton);
        sourceReferencedByTree.AfterExpand += (_, _) => UpdateTreeToggleButton(sourceReferencedByTree, referencedByTreeToggleButton);
        sourceReferencedByTree.AfterCollapse += (_, _) => UpdateTreeToggleButton(sourceReferencedByTree, referencedByTreeToggleButton);
    }

    private void LoadSettingsAndRefresh()
    {
        try
        {
            string repositoryRoot = AppPathResolver.FindRepositoryRoot();
            settings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
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

    private void ChooseWatchedSolution()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Choose watched solution",
            Filter = "Solution files (*.sln;*.slnx)|*.sln;*.slnx|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (settings is not null && File.Exists(settings.WatchedSolutionPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(settings.WatchedSolutionPath);
            dialog.FileName = Path.GetFileName(settings.WatchedSolutionPath);
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            string repositoryRoot = AppPathResolver.FindRepositoryRoot();
            string runtimeRoot = settings is null
                ? "runtime"
                : Path.GetRelativePath(repositoryRoot, settings.RuntimeRoot);
            string settingsPath = MonitorSettingsLoader.SaveLocal(
                repositoryRoot,
                dialog.FileName,
                runtimeRoot,
                this.settingsPath);
            logger?.Write(
                MonitorLogLevel.Information,
                "AIMonitor.App",
                "settings.watched_solution.changed",
                "Watched solution path saved.",
                new Dictionary<string, string>
                {
                    ["settingsPath"] = settingsPath,
                    ["watchedSolutionPath"] = dialog.FileName
                });
            LoadSettingsAndRefresh();
            SettingsChanged?.Invoke();
            SetStatus($"Watched solution saved: {dialog.FileName}");
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
            logger?.Write(MonitorLogLevel.Information, "AIMonitor.App", "index.rebuild.started", "WinForms index rebuild started.");
            SolutionIndexSummary summary = await new SolutionIndexRebuildService().RebuildAsync(settings);
            logger?.Write(MonitorLogLevel.Information, "AIMonitor.App", "index.rebuild.completed", "WinForms index rebuild completed.");
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

        currentSummary = store.GetSummary();
        projects = store.ListProjects();
        documents = store.ListDocuments();
        symbols = store.ListSymbols();
        references = store.ListReferences();
        packages = store.ListPackageReferences();

        ShowSolutionOverview();
        LoadTree();
        SetStatus($"Current index | Projects: {currentSummary.ProjectCount} | Documents: {currentSummary.DocumentCount} | Symbols: {symbols.Count} | References: {references.Count} | C# symbols only for now.");
    }

    private void LoadTree()
    {
        indexTree.BeginUpdate();
        try
        {
            indexTree.Nodes.Clear();
            string solutionName = settings is null
                ? "Solution"
                : Path.GetFileNameWithoutExtension(settings.WatchedSolutionPath);
            TreeNode solutionNode = new($"{solutionName} (solution)")
            {
                Tag = new SolutionNodeTag()
            };

            foreach (IndexedProjectRow project in projects)
            {
                TreeNode projectNode = new($"{project.Name} (project, {project.TargetFramework})")
                {
                    Tag = new ProjectNodeTag(project.ProjectPath)
                };
                TreeNode dependenciesNode = new("Dependencies")
                {
                    Tag = new DependencyFolderNodeTag(project.ProjectPath)
                };
                foreach (IndexedPackageReferenceRow package in packages.Where(package => PathEquals(package.ProjectPath, project.ProjectPath)))
                {
                    dependenciesNode.Nodes.Add(new TreeNode($"{package.Include} ({package.Version})")
                    {
                        Tag = new PackageNodeTag(project.ProjectPath, package.Include)
                    });
                }

                projectNode.Nodes.Add(dependenciesNode);

                foreach (IndexedDocumentRow document in documents.Where(document => PathEquals(document.ProjectPath, project.ProjectPath)))
                {
                    AddDocumentNode(projectNode, project.ProjectPath, document);
                }

                solutionNode.Nodes.Add(projectNode);
                projectNode.Expand();
            }

            indexTree.Nodes.Add(solutionNode);
            solutionNode.Expand();
        }
        finally
        {
            indexTree.EndUpdate();
        }

        UpdateTreeToggleButton(indexTree, indexTreeToggleButton);
    }

    private void AddDocumentNode(TreeNode projectNode, string projectPath, IndexedDocumentRow document)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        string relativePath = Path.GetRelativePath(projectDirectory, document.FilePath);
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(IsHiddenBuildFolder))
        {
            return;
        }

        TreeNode parent = projectNode;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            parent = GetOrAddFolderNode(parent, projectPath, segments[index]);
        }

        TreeNode documentNode = new($"{segments.LastOrDefault() ?? Path.GetFileName(document.FilePath)} (file)")
        {
            Tag = new DocumentNodeTag(document.StableKey)
        };

        Dictionary<string, TreeNode> typeNodes = new(StringComparer.Ordinal);
        foreach (IndexedSymbolRow symbol in symbols
            .Where(symbol => PathEquals(symbol.FilePath, document.FilePath))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal))
        {
            TreeNode symbolNode = new(FormatSymbolNode(symbol))
            {
                Tag = new SymbolNodeTag(symbol.StableKey)
            };
            if (IsTypeLikeSymbol(symbol))
            {
                documentNode.Nodes.Add(symbolNode);
                typeNodes[symbol.Signature] = symbolNode;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(symbol.ContainingType)
                && typeNodes.TryGetValue(symbol.ContainingType, out TreeNode? typeNode))
            {
                GetOrAddSymbolGroupNode(typeNode, symbol).Nodes.Add(symbolNode);
            }
            else
            {
                GetOrAddSymbolGroupNode(documentNode, symbol).Nodes.Add(symbolNode);
            }
        }

        parent.Nodes.Add(documentNode);
    }

    private static TreeNode GetOrAddFolderNode(TreeNode parent, string projectPath, string folderName)
    {
        foreach (TreeNode child in parent.Nodes)
        {
            if (child.Tag is FolderNodeTag tag
                && PathEquals(tag.ProjectPath, projectPath)
                && tag.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        TreeNode folder = new($"{folderName} (folder)")
        {
            Tag = new FolderNodeTag(projectPath, folderName)
        };

        int insertIndex = 0;
        while (insertIndex < parent.Nodes.Count
            && parent.Nodes[insertIndex].Tag is DependencyFolderNodeTag)
        {
            insertIndex++;
        }

        parent.Nodes.Insert(insertIndex, folder);
        return folder;
    }

    private void SelectTreeNode(TreeNode? node)
    {
        switch (node?.Tag)
        {
            case SolutionNodeTag:
                ShowSolutionOverview();
                break;
            case ProjectNodeTag projectTag:
                ShowProject(projectTag.ProjectPath);
                break;
            case DependencyFolderNodeTag dependencyTag:
                ShowDependencies(dependencyTag.ProjectPath);
                break;
            case PackageNodeTag packageTag:
                ShowPackage(packageTag.ProjectPath, packageTag.Include);
                break;
            case FolderNodeTag folderTag:
                ShowFolder(folderTag.ProjectPath, node);
                break;
            case DocumentNodeTag documentTag:
                ShowDocument(documentTag.StableKey);
                break;
            case SymbolNodeTag symbolTag:
                ShowSymbol(symbolTag.StableKey);
                break;
        }
    }

    private void ShowSolutionOverview()
    {
        SetGrid(projectsGrid, CreateProjectViews(projects));
        SetDocumentsGrid(documents);
        SetSymbolsGrid(symbols);
        SetReferencesGrid(references);
        SetRelationshipsGrid(references);
        SetGrid(packagesGrid, packages);
        rawBox.Text = BuildRawText(
            ("Solution", settings?.WatchedSolutionPath ?? currentSummary.InputPath),
            ("Indexed", FormatIndexedAt(currentSummary.IndexedAtUtc)),
            ("Database", databasePathBox.Text));
        SetSourceReferenceTrees([], [], SourceReferenceGrouping.TargetSymbol);
        sourcePathLabel.Text = settings?.WatchedSolutionPath ?? currentSummary.InputPath;
        LoadSourceText(
            sourcePathLabel.Text,
            string.Join(Environment.NewLine, projects.Select(project => $"{project.Name} ({project.TargetFramework})")),
            ".txt",
            1);
    }

    private void ShowProject(string projectPath)
    {
        IndexedProjectRow? project = projects.FirstOrDefault(project => PathEquals(project.ProjectPath, projectPath));
        if (project is null)
        {
            return;
        }

        List<IndexedDocumentRow> projectDocuments = documents.Where(document => PathEquals(document.ProjectPath, projectPath)).ToList();
        List<IndexedSymbolRow> projectSymbols = symbols.Where(symbol => PathEquals(symbol.ProjectPath, projectPath)).ToList();
        List<IndexedReferenceRow> projectReferences = references.Where(reference => PathEquals(reference.ProjectPath, projectPath)).ToList();
        List<IndexedPackageReferenceRow> projectPackages = packages.Where(package => PathEquals(package.ProjectPath, projectPath)).ToList();

        SetGrid(projectsGrid, CreateProjectViews(new[] { project }));
        SetDocumentsGrid(projectDocuments);
        SetSymbolsGrid(projectSymbols);
        SetReferencesGrid(projectReferences);
        SetRelationshipsGrid(projectReferences);
        SetGrid(packagesGrid, projectPackages);
        rawBox.Text = BuildRawText(
            ("Stable Key", project.StableKey),
            ("Name", project.Name),
            ("Project", project.ProjectPath),
            ("Target Framework", project.TargetFramework),
            ("Preprocessor Symbols", project.PreprocessorSymbols));
        SetSourceReferenceTrees(projectReferences, [], SourceReferenceGrouping.TargetSymbol);
        SetStatus($"Project {project.Name} | Documents: {projectDocuments.Count} | Symbols: {projectSymbols.Count} | References: {projectReferences.Count}");
    }

    private void ShowDependencies(string projectPath)
    {
        List<IndexedPackageReferenceRow> projectPackages = packages.Where(package => PathEquals(package.ProjectPath, projectPath)).ToList();
        SetGrid(packagesGrid, projectPackages);
        SetGrid(projectsGrid, CreateProjectViews(projects.Where(project => PathEquals(project.ProjectPath, projectPath))));
        rawBox.Text = string.Join(Environment.NewLine, projectPackages.Select(package => $"{package.Include} {package.Version}"));
        SetSourceReferenceTrees([], [], SourceReferenceGrouping.TargetSymbol);
        SetStatus($"Dependencies | Packages: {projectPackages.Count}");
    }

    private void ShowPackage(string projectPath, string include)
    {
        IndexedPackageReferenceRow? package = packages.FirstOrDefault(package =>
            PathEquals(package.ProjectPath, projectPath)
            && package.Include.Equals(include, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            return;
        }

        SetGrid(packagesGrid, new[] { package });
        rawBox.Text = BuildRawText(
            ("Include", package.Include),
            ("Version", package.Version),
            ("Project", package.ProjectPath));
        SetSourceReferenceTrees([], [], SourceReferenceGrouping.TargetSymbol);
        SetStatus($"Package {package.Include} {package.Version}");
    }

    private void ShowFolder(string projectPath, TreeNode folderNode)
    {
        HashSet<string> documentStableKeys = [];
        CollectDocumentStableKeys(folderNode, documentStableKeys);
        List<IndexedDocumentRow> folderDocuments = documents.Where(document => documentStableKeys.Contains(document.StableKey)).ToList();
        HashSet<string> filePaths = folderDocuments
            .Select(document => document.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<IndexedSymbolRow> folderSymbols = symbols.Where(symbol => filePaths.Contains(symbol.FilePath)).ToList();

        SetDocumentsGrid(folderDocuments);
        SetSymbolsGrid(folderSymbols);
        SetGrid(projectsGrid, CreateProjectViews(projects.Where(project => PathEquals(project.ProjectPath, projectPath))));
        rawBox.Text = string.Join(Environment.NewLine, folderDocuments.Select(document => document.FilePath));
        SetSourceReferenceTrees(references.Where(reference => filePaths.Contains(reference.FilePath)), [], SourceReferenceGrouping.TargetSymbol);
        SetStatus($"Folder {folderNode.Text} | Documents: {folderDocuments.Count} | Symbols: {folderSymbols.Count}");
    }

    private void ShowDocument(string stableKey)
    {
        IndexedDocumentRow? document = documents.FirstOrDefault(document => document.StableKey == stableKey);
        if (document is null)
        {
            return;
        }

        List<IndexedSymbolRow> documentSymbols = symbols.Where(symbol => PathEquals(symbol.FilePath, document.FilePath)).ToList();
        List<IndexedReferenceRow> documentReferences = references.Where(reference => PathEquals(reference.FilePath, document.FilePath)).ToList();
        HashSet<string> documentSymbolKeys = documentSymbols.Select(symbol => symbol.StableKey).ToHashSet(StringComparer.Ordinal);
        List<IndexedReferenceRow> incomingReferences = references
            .Where(reference => documentSymbolKeys.Contains(reference.TargetStableKey)
                && !PathEquals(reference.FilePath, document.FilePath))
            .ToList();
        (List<IndexedReferenceRow> localReferences, List<IndexedReferenceRow> externalReferences) = SplitFileReferences(document, documentReferences);
        SetDocumentsGrid(new[] { document });
        SetSymbolsGrid(documentSymbols);
        SetReferencesGrid(documentReferences);
        SetRelationshipsGrid(documentReferences);
        SetGrid(projectsGrid, CreateProjectViews(projects.Where(project => PathEquals(project.ProjectPath, document.ProjectPath))));
        rawBox.Text = BuildRawText(
            ("Stable Key", document.StableKey),
            ("Name", document.Name),
            ("File", document.FilePath),
            ("Project", document.ProjectPath),
            ("Folders", document.Folders));
        SetSourceReferenceTrees(documentReferences, incomingReferences, SourceReferenceGrouping.TargetSymbol);
        LoadSourceFile(document.FilePath, 1);
        SetStatus($"File {document.Name} | Symbols: {documentSymbols.Count} | Local refs: {localReferences.Count} | External refs: {externalReferences.Count} | Incoming refs: {incomingReferences.Count}");
    }

    private void ShowSymbol(string stableKey)
    {
        IndexedSymbolRow? symbol = symbols.FirstOrDefault(symbol => symbol.StableKey == stableKey);
        if (symbol is null)
        {
            return;
        }

        List<IndexedReferenceRow> symbolReferences = references.Where(reference => reference.TargetStableKey == stableKey).ToList();
        SetSymbolsGrid(new[] { symbol });
        SetReferencesGrid(symbolReferences);
        SetRelationshipsGrid(symbolReferences);
        SetDocumentsGrid(documents.Where(document => PathEquals(document.FilePath, symbol.FilePath)));
        SetGrid(projectsGrid, CreateProjectViews(projects.Where(project => PathEquals(project.ProjectPath, symbol.ProjectPath))));
        rawBox.Text = BuildRawText(
            ("Stable Key", symbol.StableKey),
            ("Kind", symbol.Kind),
            ("Name", symbol.Name),
            ("Signature", symbol.Signature),
            ("File", symbol.FilePath),
            ("Lines", $"{symbol.StartLine}-{symbol.EndLine}"));
        SetSourceReferenceTrees([], symbolReferences, SourceReferenceGrouping.SourceFile);
        LoadSourceFile(symbol.FilePath, symbol.StartLine);
        SetStatus($"Symbol {symbol.Kind} {symbol.Name} | References: {symbolReferences.Count}");
    }

    private async void LoadSourceFile(string filePath, int line)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SetStatus($"Source file not found: {filePath}");
            return;
        }

        try
        {
            string text = File.ReadAllText(filePath);
            sourcePathLabel.Text = $"{filePath} | line {Math.Max(line, 1)}";
            await LoadSourceTextAsync(filePath, text, filePath, line);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load source file: {ex.Message}");
        }
    }

    private async void LoadSourceText(string displayPath, string text, string languageHint, int line)
    {
        try
        {
            sourcePathLabel.Text = displayPath;
            await LoadSourceTextAsync(displayPath, text, languageHint, line);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load Monaco source view: {ex.Message}");
        }
    }

    private async Task LoadSourceTextAsync(string displayPath, string text, string languageHint, int line)
    {
        await EnsureSourceEditorAsync();
        if (sourceEditor.CoreWebView2 is null || !sourceEditorShellLoaded)
        {
            return;
        }

        SetStatus($"Loading source viewer | {Path.GetFileName(displayPath)} | line {Math.Max(line, 1)}");
        string payload = JsonSerializer.Serialize(new
        {
            path = displayPath,
            text,
            language = GetMonacoLanguage(languageHint),
            line = Math.Max(line, 1),
            theme = GetSelectedMonacoTheme()
        });
        await sourceEditor.CoreWebView2.ExecuteScriptAsync($"window.aimonitorSetSource({payload});");
    }

    private async Task EnsureSourceEditorAsync()
    {
        if (sourceEditorInitialized && sourceEditorShellLoaded)
        {
            return;
        }

        CoreWebView2Environment environment = await CreateWebViewEnvironmentAsync();
        await sourceEditor.EnsureCoreWebView2Async(environment);
        if (sourceEditor.CoreWebView2 is not null)
        {
            string monacoAssetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "monaco");
            if (Directory.Exists(monacoAssetsFolder))
            {
                sourceEditor.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    MonacoHostName,
                    monacoAssetsFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            sourceEditor.CoreWebView2.Settings.AreDevToolsEnabled = true;
            sourceEditor.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            sourceEditor.CoreWebView2.Settings.IsStatusBarEnabled = false;
            sourceEditor.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    SetStatus($"Monaco navigation failed: {args.WebErrorStatus}");
                    sourceEditorReadyCompletion?.TrySetException(new InvalidOperationException($"Monaco navigation failed: {args.WebErrorStatus}"));
                }
            };
            sourceEditor.CoreWebView2.ProcessFailed += (_, args) =>
            {
                SetStatus($"Monaco WebView2 process failed: {args.ProcessFailedKind}");
                sourceEditorReadyCompletion?.TrySetException(new InvalidOperationException($"Monaco WebView2 process failed: {args.ProcessFailedKind}"));
            };
            sourceEditor.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (args.WebMessageAsJson.Contains("\"ready\"", StringComparison.OrdinalIgnoreCase))
                {
                    sourceEditorShellLoaded = true;
                    sourceEditorReadyCompletion?.TrySetResult(true);
                }

                if (args.WebMessageAsJson.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus($"Monaco source viewer error: {args.WebMessageAsJson}");
                }
            };
        }

        sourceEditorInitialized = true;
        sourceEditorReadyCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        sourceEditorShellLoaded = false;
        sourceEditor.NavigateToString(RenderMonacoShellHtml(GetSelectedMonacoTheme()));
        await sourceEditorReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        string repositoryRoot = AppPathResolver.FindRepositoryRoot();
        string userDataFolder = Path.Combine(repositoryRoot, "runtime", "self-analysis-codex", "webview2-user-data");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }

    private async Task ApplySourceThemeAsync()
    {
        if (!sourceEditorShellLoaded || sourceEditor.CoreWebView2 is null)
        {
            return;
        }

        await sourceEditor.CoreWebView2.ExecuteScriptAsync($"window.aimonitorSetTheme({ToScriptJson(GetSelectedMonacoTheme())});");
    }

    private string GetSelectedMonacoTheme()
    {
        return sourceThemeBox.SelectedItem?.ToString() switch
        {
            "Light" => "aimonitor-light",
            "High Contrast" => "hc-black",
            _ => "aimonitor-dark"
        };
    }

    private static string RenderMonacoShellHtml(string theme)
    {
        string encodedTheme = ToScriptJson(theme);
        return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    html, body, #container {
      width: 100%;
      height: 100%;
      margin: 0;
      overflow: hidden;
      background: #1e1e1e;
    }
  </style>
  <script src="https://aimonitor.local/vs/loader.js"></script>
</head>
<body>
  <div id="container"></div>
  <script>
    let editor;
    let model;
    let decorations = [];
    let activeTheme = {{encodedTheme}};

    function post(kind, details) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ kind, details: details || '' });
      }
    }

    function uriFromPath(path) {
      return monaco.Uri.parse('file:///' + String(path || 'source.txt').replaceAll('\\', '/'));
    }

    window.aimonitorSetTheme = function(theme) {
      activeTheme = theme || 'aimonitor-dark';
      if (window.monaco) {
        monaco.editor.setTheme(activeTheme);
      }
    };

    window.aimonitorSetSource = function(payload) {
      if (!editor || !window.monaco) {
        post('error', 'Editor is not ready.');
        return;
      }

      const sourceText = payload.text || '';
      const language = payload.language || 'plaintext';
      const targetLine = Math.max(payload.line || 1, 1);
      if (model) {
        model.dispose();
      }

      model = monaco.editor.createModel(sourceText, language, uriFromPath(payload.path));
      editor.setModel(model);
      window.aimonitorSetTheme(payload.theme || activeTheme);
      const maxLine = Math.max(1, model.getLineCount());
      const selectedLine = Math.min(Math.max(targetLine, 1), maxLine);
      editor.setSelection(new monaco.Range(selectedLine, 1, selectedLine, 1));
      editor.revealLineInCenter(selectedLine);
      decorations = editor.deltaDecorations(decorations, [{
        range: new monaco.Range(selectedLine, 1, selectedLine, 1),
        options: { isWholeLine: true, className: 'selected-source-line' }
      }]);
    };

    require.config({ paths: { vs: 'https://aimonitor.local/vs' } });
    require(['vs/editor/editor.main'], function () {
      monaco.editor.defineTheme('aimonitor-dark', {
        base: 'vs-dark',
        inherit: true,
        rules: [],
        colors: {
          'editor.lineHighlightBackground': '#253b57',
          'editorLineNumber.foreground': '#7f9bbd',
          'editorCursor.foreground': '#9cdcfe'
        }
      });
      monaco.editor.defineTheme('aimonitor-light', {
        base: 'vs',
        inherit: true,
        rules: [],
        colors: {
          'editor.lineHighlightBackground': '#dbeafe',
          'editorLineNumber.foreground': '#59708f',
          'editorCursor.foreground': '#1d4ed8'
        }
      });
      model = monaco.editor.createModel('', 'plaintext', uriFromPath('source.txt'));
      editor = monaco.editor.create(document.getElementById('container'), {
        model: model,
        theme: activeTheme,
        readOnly: true,
        automaticLayout: true,
        minimap: { enabled: true },
        lineNumbers: 'on',
        scrollBeyondLastLine: false,
        wordWrap: 'off',
        fontFamily: 'Cascadia Code, Consolas, monospace',
        fontSize: 14,
        renderWhitespace: 'selection'
      });
      post('ready');
    }, function(error) {
      post('error', error && error.message ? error.message : String(error));
    });
  </script>
  <style>
    .selected-source-line {
      background: rgba(76, 139, 245, 0.28);
    }
  </style>
</body>
</html>
""";
    }

    private static string ToScriptJson(string value)
    {
        return JsonSerializer.Serialize(value).Replace("</", "<\\/", StringComparison.Ordinal);
    }

    private static string GetMonacoLanguage(string pathOrExtension)
    {
        string extension = Path.GetExtension(pathOrExtension);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csx", StringComparison.OrdinalIgnoreCase))
        {
            return "csharp";
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        if (extension.Equals(".razor", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "html";
        }

        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return "markdown";
        }

        return "plaintext";
    }

    private void SelectProjectFromGrid()
    {
        if (projectsGrid.CurrentRow?.DataBoundItem is ProjectView project)
        {
            ShowProject(project.ProjectPath);
        }
    }

    private void SelectDocumentFromGrid()
    {
        if (documentsGrid.CurrentRow?.DataBoundItem is DocumentView document)
        {
            ShowDocument(document.StableKey);
        }
    }

    private void SelectSymbolFromGrid()
    {
        if (symbolsGrid.CurrentRow?.DataBoundItem is SymbolView symbol)
        {
            ShowSymbol(symbol.StableKey);
        }
    }

    private void SelectReferenceTargetFromGrid(DataGridView grid)
    {
        if (grid.CurrentRow?.DataBoundItem is ReferenceView reference)
        {
            ShowSymbol(reference.TargetStableKey);
        }
    }

    private void SelectSourceReferenceNode(TreeNode node)
    {
        if (node.Tag is SourceReferenceNodeTag reference)
        {
            GoToSourceReference(reference);
            return;
        }

        if (node.Nodes.Count > 0)
        {
            if (node.IsExpanded)
            {
                node.Collapse();
            }
            else
            {
                node.Expand();
            }
        }
    }

    private void GoToSourceReference(SourceReferenceNodeTag reference)
    {
        LoadSourceFile(reference.FilePath, reference.Line);
        SetStatus($"Reference {reference.ReferenceKind} | {reference.FileName}:{reference.Line}");
    }

    private static string FormatSymbolNode(IndexedSymbolRow symbol)
    {
        string signature = FormatLocalSignature(symbol);
        return $"{signature} [{symbol.StartLine}-{symbol.EndLine}]";
    }

    private static string FormatLocalSignature(IndexedSymbolRow symbol)
    {
        if (IsTypeLikeSymbol(symbol))
        {
            return symbol.Name;
        }

        string signature = string.IsNullOrWhiteSpace(symbol.Signature)
            ? symbol.Name
            : symbol.Signature;
        string localSignature = RemoveContainingTypePrefix(signature, symbol);
        return SimplifySignatureTypes(localSignature);
    }

    private static string RemoveContainingTypePrefix(string signature, IndexedSymbolRow symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol.ContainingType))
        {
            string containingPrefix = symbol.ContainingType + ".";
            if (signature.StartsWith(containingPrefix, StringComparison.Ordinal))
            {
                return signature[containingPrefix.Length..];
            }
        }

        if (!string.IsNullOrWhiteSpace(symbol.Namespace))
        {
            string namespacePrefix = symbol.Namespace + ".";
            if (signature.StartsWith(namespacePrefix, StringComparison.Ordinal))
            {
                return signature[namespacePrefix.Length..];
            }
        }

        return signature;
    }

    private static string SimplifySignatureTypes(string signature)
    {
        int parameterStart = signature.IndexOf('(', StringComparison.Ordinal);
        int parameterEnd = signature.LastIndexOf(')');
        if (parameterStart < 0 || parameterEnd <= parameterStart)
        {
            return GetUnqualifiedTypeName(signature);
        }

        string name = signature[..parameterStart];
        string parameters = signature[(parameterStart + 1)..parameterEnd];
        string suffix = signature[(parameterEnd + 1)..];
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return $"{GetUnqualifiedTypeName(name)}(){suffix}";
        }

        string[] parts = parameters.Split(',');
        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = SimplifyParameter(parts[index].Trim());
        }

        return $"{GetUnqualifiedTypeName(name)}({string.Join(", ", parts)}){suffix}";
    }

    private static string SimplifyParameter(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return parameter;
        }

        string[] tokens = parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < tokens.Length; index++)
        {
            tokens[index] = SimplifyTypeExpression(tokens[index]);
        }

        return string.Join(" ", tokens);
    }

    private static string SimplifyTypeExpression(string text)
    {
        return text
            .Replace("Microsoft.Data.Sqlite.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Collections.Generic.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.Tasks.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.", string.Empty, StringComparison.Ordinal)
            .Replace("System.", string.Empty, StringComparison.Ordinal);
    }

    private static string GetUnqualifiedTypeName(string name)
    {
        int genericIndex = name.IndexOf('<', StringComparison.Ordinal);
        string rootName = genericIndex >= 0
            ? name[..genericIndex]
            : name;
        int lastDot = rootName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < rootName.Length - 1)
        {
            rootName = rootName[(lastDot + 1)..];
        }

        return genericIndex >= 0
            ? rootName + name[genericIndex..]
            : rootName;
    }

    private static TreeNode GetOrAddSymbolGroupNode(TreeNode parent, IndexedSymbolRow symbol)
    {
        string groupName = FormatSymbolGroupName(symbol);
        foreach (TreeNode child in parent.Nodes)
        {
            if (child.Text.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        TreeNode group = new(groupName);
        int insertIndex = GetSymbolGroupInsertIndex(parent, groupName);
        parent.Nodes.Insert(insertIndex, group);
        return group;
    }

    private static int GetSymbolGroupInsertIndex(TreeNode parent, string groupName)
    {
        int groupRank = GetSymbolGroupRank(groupName);
        int insertIndex = 0;
        while (insertIndex < parent.Nodes.Count
            && GetSymbolGroupRank(parent.Nodes[insertIndex].Text) <= groupRank)
        {
            insertIndex++;
        }

        return insertIndex;
    }

    private static int GetSymbolGroupRank(string groupName)
    {
        if (groupName.Contains("constructors", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (groupName.Contains("types", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (groupName.Contains("methods", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (groupName.Contains("members", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
    }

    private static string FormatSymbolGroupName(IndexedSymbolRow symbol)
    {
        string access = FormatAccessibility(symbol.Accessibility);
        string category = FormatSymbolCategory(symbol);
        return $"{access} {category}";
    }

    private static string FormatSymbolCategory(IndexedSymbolRow symbol)
    {
        if (IsTypeLikeSymbol(symbol))
        {
            return "types";
        }

        if (IsConstructorSymbol(symbol))
        {
            return "constructors";
        }

        if (symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase))
        {
            return "methods";
        }

        return "members";
    }

    private static string FormatAccessibility(string accessibility)
    {
        return string.IsNullOrWhiteSpace(accessibility)
            ? "access unknown"
            : accessibility.ToLowerInvariant();
    }

    private static bool IsTypeLikeSymbol(IndexedSymbolRow symbol)
    {
        return symbol.Kind.Equals("NamedType", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Struct", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Interface", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Enum", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Record", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConstructorSymbol(IndexedSymbolRow symbol)
    {
        return symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase)
            && (symbol.MethodKind.Equals("Constructor", StringComparison.OrdinalIgnoreCase)
                || symbol.MethodKind.Equals("StaticConstructor", StringComparison.OrdinalIgnoreCase)
                || symbol.Name.Equals(".ctor", StringComparison.Ordinal)
                || symbol.Name.Equals(".cctor", StringComparison.Ordinal));
    }

    private void SetBusy(bool busy)
    {
        rebuildIndexMenuItem.Enabled = !busy;
        refreshMenuItem.Enabled = !busy;
        chooseSolutionMenuItem.Enabled = !busy;
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

    private static void ToggleTreeExpansion(TreeView tree, Button toggleButton)
    {
        if (HasExpandedNode(tree.Nodes))
        {
            tree.CollapseAll();
        }
        else
        {
            tree.ExpandAll();
        }

        UpdateTreeToggleButton(tree, toggleButton);
    }

    private static void UpdateTreeToggleButton(TreeView tree, Button toggleButton)
    {
        toggleButton.Text = HasExpandedNode(tree.Nodes)
            ? "Collapse All"
            : "Expand All";
    }

    private static bool HasExpandedNode(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.IsExpanded || HasExpandedNode(node.Nodes))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyInitialSplitterLayout()
    {
        if (splitterLayoutSized || mainSplit.Width < 700)
        {
            return;
        }

        splitterLayoutSized = true;
        mainSplit.Panel1MinSize = 260;
        mainSplit.Panel2MinSize = 500;
        mainSplit.SplitterDistance = Math.Clamp(340, mainSplit.Panel1MinSize, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth);
    }

    private void ApplyInitialSourceSplitterLayout()
    {
        if (sourceSplitterLayoutSized || sourceSplit.Height < 320)
        {
            return;
        }

        sourceSplitterLayoutSized = true;
        sourceSplit.Panel1MinSize = 180;
        sourceSplit.Panel2MinSize = 70;
        int referenceHeight = Math.Clamp(120, sourceSplit.Panel2MinSize, Math.Max(sourceSplit.Height / 2, sourceSplit.Panel2MinSize));
        sourceSplit.SplitterDistance = Math.Clamp(
            sourceSplit.Height - referenceHeight - sourceSplit.SplitterWidth,
            sourceSplit.Panel1MinSize,
            sourceSplit.Height - sourceSplit.Panel2MinSize - sourceSplit.SplitterWidth);
    }

    private static void CollectDocumentStableKeys(TreeNode node, HashSet<string> documentStableKeys)
    {
        if (node.Tag is DocumentNodeTag tag)
        {
            documentStableKeys.Add(tag.StableKey);
        }

        foreach (TreeNode child in node.Nodes)
        {
            CollectDocumentStableKeys(child, documentStableKeys);
        }
    }

    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private static void SetGrid<T>(DataGridView grid, IEnumerable<T> rows)
    {
        grid.DataSource = rows.ToList();
    }

    private void SetDocumentsGrid(IEnumerable<IndexedDocumentRow> rows)
    {
        documentsGrid.DataSource = CreateDocumentViews(rows).ToList();
    }

    private void SetSymbolsGrid(IEnumerable<IndexedSymbolRow> rows)
    {
        symbolsGrid.DataSource = CreateSymbolViews(rows).ToList();
    }

    private void SetReferencesGrid(IEnumerable<IndexedReferenceRow> rows)
    {
        referencesGrid.DataSource = CreateReferenceViews(rows).ToList();
    }

    private void SetRelationshipsGrid(IEnumerable<IndexedReferenceRow> rows)
    {
        relationshipsGrid.DataSource = CreateReferenceViews(rows.Where(IsRelationshipReference)).ToList();
    }

    private void SetSourceReferenceTrees(
        IEnumerable<IndexedReferenceRow> outgoingReferences,
        IEnumerable<IndexedReferenceRow> incomingReferences,
        SourceReferenceGrouping grouping)
    {
        SetSourceReferenceTree(sourceReferencesTree, outgoingReferences, SourceReferenceGrouping.TargetSymbol);
        SetSourceReferenceTree(sourceReferencedByTree, incomingReferences, grouping);
    }

    private void SetSourceReferenceTree(
        TreeView tree,
        IEnumerable<IndexedReferenceRow> rows,
        SourceReferenceGrouping grouping)
    {
        tree.BeginUpdate();
        try
        {
            tree.Nodes.Clear();
            IEnumerable<TreeNode> nodes = grouping == SourceReferenceGrouping.SourceFile
                ? BuildSourceReferenceFileNodes(rows)
                : BuildSourceReferenceSymbolNodes(rows);
            foreach (TreeNode node in nodes)
            {
                tree.Nodes.Add(node);
                node.Expand();
            }

            if (tree.Nodes.Count == 0)
            {
                tree.Nodes.Add(new TreeNode("None"));
            }
        }
        finally
        {
            tree.EndUpdate();
        }

        if (ReferenceEquals(tree, sourceReferencesTree))
        {
            UpdateTreeToggleButton(sourceReferencesTree, referencesTreeToggleButton);
        }
        else if (ReferenceEquals(tree, sourceReferencedByTree))
        {
            UpdateTreeToggleButton(sourceReferencedByTree, referencedByTreeToggleButton);
        }
    }

    private IEnumerable<TreeNode> BuildSourceReferenceSymbolNodes(IEnumerable<IndexedReferenceRow> rows)
    {
        List<SourceReferenceNodeTag> referenceNodes = CreateSourceReferenceNodes(rows).ToList();
        foreach (IGrouping<string, SourceReferenceNodeTag> symbolGroup in referenceNodes
            .GroupBy(reference => reference.TargetStableKey, StringComparer.Ordinal)
            .OrderBy(group => FormatSourceReferenceTargetNode(group.Key), StringComparer.OrdinalIgnoreCase))
        {
            SourceReferenceNodeTag[] symbolReferences = symbolGroup.ToArray();
            TreeNode symbolNode = new($"{FormatSourceReferenceTargetNode(symbolGroup.Key)} ({symbolReferences.Length})")
            {
                ToolTipText = FormatSourceReferenceTargetToolTip(symbolGroup.Key)
            };
            foreach (IGrouping<string, SourceReferenceNodeTag> fileGroup in symbolReferences
                .GroupBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => Path.GetFileName(group.Key), StringComparer.OrdinalIgnoreCase))
            {
                SourceReferenceNodeTag[] fileReferences = fileGroup
                    .OrderBy(reference => reference.Line)
                    .ThenBy(reference => reference.Column)
                    .ToArray();
                TreeNode fileNode = new($"{Path.GetFileName(fileGroup.Key)} ({fileReferences.Length})")
                {
                    ToolTipText = fileGroup.Key
                };
                foreach (SourceReferenceNodeTag reference in fileReferences)
                {
                    TreeNode referenceNode = new(FormatSourceReferenceNode(reference))
                    {
                        Tag = reference,
                        ToolTipText = $"{reference.FilePath}:{reference.Line}:{reference.Column} | {reference.ReferenceText}"
                    };
                    fileNode.Nodes.Add(referenceNode);
                }

                symbolNode.Nodes.Add(fileNode);
                fileNode.Expand();
            }

            yield return symbolNode;
        }
    }

    private IEnumerable<TreeNode> BuildSourceReferenceFileNodes(IEnumerable<IndexedReferenceRow> rows)
    {
        List<SourceReferenceNodeTag> referenceNodes = CreateSourceReferenceNodes(rows).ToList();
        foreach (IGrouping<string, SourceReferenceNodeTag> fileGroup in referenceNodes
            .GroupBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => Path.GetFileName(group.Key), StringComparer.OrdinalIgnoreCase))
        {
            SourceReferenceNodeTag[] fileReferences = fileGroup
                .OrderBy(reference => reference.Line)
                .ThenBy(reference => reference.Column)
                .ToArray();
            TreeNode fileNode = new($"{Path.GetFileName(fileGroup.Key)} ({fileReferences.Length})")
            {
                ToolTipText = fileGroup.Key
            };
            foreach (SourceReferenceNodeTag reference in fileReferences)
            {
                TreeNode referenceNode = new(FormatSourceReferenceNode(reference))
                {
                    Tag = reference,
                    ToolTipText = $"{reference.FilePath}:{reference.Line}:{reference.Column} | {reference.ReferenceText}"
                };
                fileNode.Nodes.Add(referenceNode);
            }

            yield return fileNode;
        }
    }

    private IEnumerable<ProjectView> CreateProjectViews(IEnumerable<IndexedProjectRow> rows)
    {
        foreach (IndexedProjectRow project in rows)
        {
            yield return new ProjectView(
                project.StableKey,
                project.Name,
                project.ProjectPath,
                project.Language,
                project.TargetFramework,
                documents.Count(document => PathEquals(document.ProjectPath, project.ProjectPath)),
                symbols.Count(symbol => PathEquals(symbol.ProjectPath, project.ProjectPath)),
                references.Count(reference => PathEquals(reference.ProjectPath, project.ProjectPath)),
                packages.Count(package => PathEquals(package.ProjectPath, project.ProjectPath)));
        }
    }

    private IEnumerable<DocumentView> CreateDocumentViews(IEnumerable<IndexedDocumentRow> rows)
    {
        foreach (IndexedDocumentRow document in rows)
        {
            yield return new DocumentView(
                document.StableKey,
                document.Name,
                document.FilePath,
                document.ProjectPath,
                document.Folders,
                symbols.Count(symbol => PathEquals(symbol.FilePath, document.FilePath)),
                references.Count(reference => PathEquals(reference.FilePath, document.FilePath)));
        }
    }

    private IEnumerable<SymbolView> CreateSymbolViews(IEnumerable<IndexedSymbolRow> rows)
    {
        foreach (IndexedSymbolRow symbol in rows)
        {
            yield return new SymbolView(
                symbol.StableKey,
                symbol.Kind,
                symbol.Name,
                symbol.Signature,
                symbol.Namespace,
                symbol.ContainingType,
                symbol.FilePath,
                symbol.StartLine,
                symbol.EndLine,
                references.Count(reference => reference.TargetStableKey == symbol.StableKey));
        }
    }

    private IEnumerable<ReferenceView> CreateReferenceViews(IEnumerable<IndexedReferenceRow> rows)
    {
        foreach (IndexedReferenceRow reference in rows)
        {
            IndexedSymbolRow? target = symbols.FirstOrDefault(symbol => symbol.StableKey == reference.TargetStableKey);
            yield return new ReferenceView(
                target?.Name ?? string.Empty,
                target?.Kind ?? string.Empty,
                target?.Signature ?? string.Empty,
                reference.ReferenceKind,
                reference.FilePath,
                reference.Line,
                reference.Column,
                reference.Snippet,
                reference.TargetStableKey,
                reference.ProjectPath);
        }
    }

    private IEnumerable<SourceReferenceNodeTag> CreateSourceReferenceNodes(IEnumerable<IndexedReferenceRow> rows)
    {
        foreach (IndexedReferenceRow reference in rows
            .OrderBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Line)
            .ThenBy(reference => reference.Column))
        {
            string caller = string.IsNullOrWhiteSpace(reference.CallerName)
                ? "(unknown caller)"
                : reference.CallerName;
            yield return new SourceReferenceNodeTag(
                Path.GetFileName(reference.FilePath),
                reference.Line,
                reference.Column,
                caller,
                reference.CallerKind,
                reference.ReferenceKind,
                ShortenReferenceText(reference.Snippet),
                reference.FilePath,
                reference.TargetStableKey,
                reference.ProjectPath);
        }
    }

    private static string FormatSourceReferenceNode(SourceReferenceNodeTag reference)
    {
        return $"line {reference.Line} | {reference.Caller} | {reference.ReferenceKind} | {reference.ReferenceText}";
    }

    private string FormatSourceReferenceTargetNode(string targetStableKey)
    {
        IndexedSymbolRow? target = symbols.FirstOrDefault(symbol => symbol.StableKey == targetStableKey);
        if (target is null)
        {
            return string.IsNullOrWhiteSpace(targetStableKey)
                ? "(unknown target)"
                : targetStableKey;
        }

        return $"{target.Name} ({target.Kind})";
    }

    private string FormatSourceReferenceTargetToolTip(string targetStableKey)
    {
        IndexedSymbolRow? target = symbols.FirstOrDefault(symbol => symbol.StableKey == targetStableKey);
        if (target is null)
        {
            return targetStableKey;
        }

        string signature = string.IsNullOrWhiteSpace(target.Signature)
            ? target.Name
            : target.Signature;
        return $"{target.Kind} {signature} | {target.FilePath}:{target.StartLine}";
    }

    private static string ShortenReferenceText(string text)
    {
        string trimmed = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 110;
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength] + "...";
    }

    private IEnumerable<FileOverviewControl.ReferenceView> CreateFileReferenceViews(IEnumerable<IndexedReferenceRow> rows)
    {
        foreach (ReferenceView reference in CreateReferenceViews(rows))
        {
            yield return new FileOverviewControl.ReferenceView(
                reference.TargetName,
                reference.TargetKind,
                reference.TargetSignature,
                reference.ReferenceKind,
                reference.FilePath,
                reference.Line,
                reference.Column,
                reference.Snippet,
                reference.TargetStableKey,
                reference.ProjectPath);
        }
    }

    private (List<IndexedReferenceRow> LocalReferences, List<IndexedReferenceRow> ExternalReferences) SplitFileReferences(
        IndexedDocumentRow document,
        IEnumerable<IndexedReferenceRow> documentReferences)
    {
        List<IndexedReferenceRow> localReferences = [];
        List<IndexedReferenceRow> externalReferences = [];

        foreach (IndexedReferenceRow reference in documentReferences)
        {
            IndexedSymbolRow? target = symbols.FirstOrDefault(symbol => symbol.StableKey == reference.TargetStableKey);
            if (target is not null && PathEquals(target.FilePath, document.FilePath))
            {
                localReferences.Add(reference);
            }
            else
            {
                externalReferences.Add(reference);
            }
        }

        return (localReferences, externalReferences);
    }

    private static string FormatIndexedAt(DateTimeOffset indexedAtUtc)
    {
        return indexedAtUtc == DateTimeOffset.MinValue
            ? "never"
            : indexedAtUtc.ToString("O");
    }

    private static bool PathEquals(string first, string second)
    {
        return first.Equals(second, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHiddenBuildFolder(string segment)
    {
        return segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRelationshipReference(IndexedReferenceRow reference)
    {
        return reference.ReferenceKind.Equals("implements_interface_member", StringComparison.OrdinalIgnoreCase)
            || reference.ReferenceKind.Equals("overrides", StringComparison.OrdinalIgnoreCase)
            || reference.ReferenceKind.Equals("partial_declaration", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRawText(params (string Name, string Value)[] lines)
    {
        return string.Join(Environment.NewLine, lines.Select(line => $"{line.Name}: {line.Value}"));
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
            BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
        };
    }

    private sealed record SolutionNodeTag;

    private sealed record ProjectNodeTag(string ProjectPath);

    private sealed record DependencyFolderNodeTag(string ProjectPath);

    private sealed record PackageNodeTag(string ProjectPath, string Include);

    private sealed record FolderNodeTag(string ProjectPath, string Name);

    private sealed record DocumentNodeTag(string StableKey);

    private sealed record SymbolNodeTag(string StableKey);

    private enum SourceReferenceGrouping
    {
        TargetSymbol,
        SourceFile
    }

    private sealed record ProjectView(
        string StableKey,
        string Name,
        string ProjectPath,
        string Language,
        string TargetFramework,
        int Documents,
        int Symbols,
        int References,
        int Packages);

    private sealed record DocumentView(
        string StableKey,
        string Name,
        string FilePath,
        string ProjectPath,
        string Folders,
        int Symbols,
        int References);

    private sealed record SymbolView(
        string StableKey,
        string Kind,
        string Name,
        string Signature,
        string Namespace,
        string ContainingType,
        string FilePath,
        int StartLine,
        int EndLine,
        int References);

    private sealed record ReferenceView(
        string TargetName,
        string TargetKind,
        string TargetSignature,
        string ReferenceKind,
        string FilePath,
        int Line,
        int Column,
        string Snippet,
        string TargetStableKey,
        string ProjectPath);

    private sealed record SourceReferenceNodeTag(
        string FileName,
        int Line,
        int Column,
        string Caller,
        string CallerKind,
        string ReferenceKind,
        string ReferenceText,
        string FilePath,
        string TargetStableKey,
        string ProjectPath);
}
