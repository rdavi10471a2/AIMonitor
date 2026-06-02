using System.ComponentModel;
using System.Diagnostics;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Indexing;
using AIMonitor.Logging;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class SolutionIndexControl : UserControl
{
    private const int FriendlySplitterWidth = 12;

    private readonly ToolStripMenuItem rebuildIndexMenuItem;
    private readonly ToolStripMenuItem refreshMenuItem;
    private readonly ToolStripMenuItem chooseSolutionMenuItem;
    private readonly ToolStripMenuItem openWatchedFolderMenuItem;
    private readonly ToolStripMenuItem openDatabaseFolderMenuItem;
    private readonly TextBox watchedSolutionBox;
    private readonly TextBox databasePathBox;
    private readonly Label statusLabel;
    private readonly TreeView indexTree;
    private readonly FileOverviewControl fileOverviewControl;
    private readonly DataGridView projectsGrid;
    private readonly DataGridView documentsGrid;
    private readonly DataGridView symbolsGrid;
    private readonly DataGridView referencesGrid;
    private readonly DataGridView relationshipsGrid;
    private readonly DataGridView packagesGrid;
    private readonly TextBox rawBox;
    private readonly SplitContainer mainSplit;
    private readonly SplitContainer detailSplit;
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
    private SolutionIndexSummary currentSummary = new(string.Empty, DateTimeOffset.MinValue, 0, 0, 0);
    private IReadOnlyList<IndexedProjectRow> projects = [];
    private IReadOnlyList<IndexedDocumentRow> documents = [];
    private IReadOnlyList<IndexedSymbolRow> symbols = [];
    private IReadOnlyList<IndexedReferenceRow> references = [];
    private IReadOnlyList<IndexedPackageReferenceRow> packages = [];

    public SolutionIndexControl()
    {
        Dock = DockStyle.Fill;

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
        detailSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
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
        detailSplit.Panel1.BackColor = SystemColors.Control;
        detailSplit.Panel2.BackColor = SystemColors.Control;

        Controls.Add(BuildLayout());
        fileOverviewControl.SymbolSelected += ShowSymbol;
        WireEvents();
        Load += (_, _) =>
        {
            BeginInvoke(ApplyInitialSplitterLayout);
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

        detailTabs.TabPages.Add(overviewTab);
        detailTabs.TabPages.Add(projectsTab);
        detailTabs.TabPages.Add(documentsTab);
        detailTabs.TabPages.Add(symbolsTab);
        detailTabs.TabPages.Add(referencesTab);
        detailTabs.TabPages.Add(relationshipsTab);
        detailTabs.TabPages.Add(packagesTab);
        detailTabs.TabPages.Add(rawTab);

        GroupBox treeGroup = new()
        {
            Text = "Solution Explorer",
            Dock = DockStyle.Fill
        };
        treeGroup.Controls.Add(indexTree);

        detailSplit.Panel1.Controls.Add(detailTabs);
        detailSplit.Panel2.Controls.Add(statusLabel);
        mainSplit.Panel1.Controls.Add(treeGroup);
        mainSplit.Panel2.Controls.Add(detailSplit);

        root.Controls.Add(BuildMenu(), 0, 0);
        root.Controls.Add(BuildLabeledRow("Watched", watchedSolutionBox), 0, 1);
        root.Controls.Add(BuildLabeledRow("Database", databasePathBox), 0, 2);
        root.Controls.Add(mainSplit, 0, 3);
        return root;
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

        ToolStripMenuItem viewMenu = new("View");
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Overview", null, (_, _) => detailTabs.SelectedTab = overviewTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Projects", null, (_, _) => detailTabs.SelectedTab = projectsTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Documents", null, (_, _) => detailTabs.SelectedTab = documentsTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Symbols", null, (_, _) => detailTabs.SelectedTab = symbolsTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("References", null, (_, _) => detailTabs.SelectedTab = referencesTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Relationships", null, (_, _) => detailTabs.SelectedTab = relationshipsTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Packages", null, (_, _) => detailTabs.SelectedTab = packagesTab));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Raw", null, (_, _) => detailTabs.SelectedTab = rawTab));

        menu.Items.Add(fileMenu);
        menu.Items.Add(indexMenu);
        menu.Items.Add(viewMenu);
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
        indexTree.AfterSelect += (_, args) => SelectTreeNode(args.Node);
        projectsGrid.CellDoubleClick += (_, _) => SelectProjectFromGrid();
        documentsGrid.CellDoubleClick += (_, _) => SelectDocumentFromGrid();
        symbolsGrid.CellDoubleClick += (_, _) => SelectSymbolFromGrid();
        referencesGrid.CellDoubleClick += (_, _) => SelectReferenceTargetFromGrid(referencesGrid);
        relationshipsGrid.CellDoubleClick += (_, _) => SelectReferenceTargetFromGrid(relationshipsGrid);
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
                runtimeRoot);
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
            TreeNode solutionNode = new(solutionName)
            {
                Tag = new SolutionNodeTag()
            };

            foreach (IndexedProjectRow project in projects)
            {
                TreeNode projectNode = new($"{project.Name} ({project.TargetFramework})")
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

        TreeNode documentNode = new(segments.LastOrDefault() ?? Path.GetFileName(document.FilePath))
        {
            Tag = new DocumentNodeTag(document.StableKey)
        };

        foreach (IndexedSymbolRow symbol in symbols.Where(symbol => PathEquals(symbol.FilePath, document.FilePath)))
        {
            documentNode.Nodes.Add(new TreeNode($"{symbol.Kind} {symbol.Name}")
            {
                Tag = new SymbolNodeTag(symbol.StableKey)
            });
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

        TreeNode folder = new(folderName)
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
        detailTabs.SelectedTab = projectsTab;
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
        detailTabs.SelectedTab = documentsTab;
        SetStatus($"Project {project.Name} | Documents: {projectDocuments.Count} | Symbols: {projectSymbols.Count} | References: {projectReferences.Count}");
    }

    private void ShowDependencies(string projectPath)
    {
        List<IndexedPackageReferenceRow> projectPackages = packages.Where(package => PathEquals(package.ProjectPath, projectPath)).ToList();
        SetGrid(packagesGrid, projectPackages);
        SetGrid(projectsGrid, CreateProjectViews(projects.Where(project => PathEquals(project.ProjectPath, projectPath))));
        rawBox.Text = string.Join(Environment.NewLine, projectPackages.Select(package => $"{package.Include} {package.Version}"));
        detailTabs.SelectedTab = packagesTab;
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
        detailTabs.SelectedTab = packagesTab;
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
        detailTabs.SelectedTab = documentsTab;
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
        fileOverviewControl.ShowFile(
            document,
            documentSymbols,
            CreateFileReferenceViews(localReferences).ToArray(),
            CreateFileReferenceViews(externalReferences).ToArray(),
            CreateFileReferenceViews(incomingReferences).ToArray());
        rawBox.Text = BuildRawText(
            ("Stable Key", document.StableKey),
            ("Name", document.Name),
            ("File", document.FilePath),
            ("Project", document.ProjectPath),
            ("Folders", document.Folders));
        detailTabs.SelectedTab = overviewTab;
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
        detailTabs.SelectedTab = referencesTab;
        SetStatus($"Symbol {symbol.Kind} {symbol.Name} | References: {symbolReferences.Count}");
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

    private void ApplyInitialSplitterLayout()
    {
        if (splitterLayoutSized || mainSplit.Width < 700 || detailSplit.Height < 300)
        {
            return;
        }

        splitterLayoutSized = true;
        mainSplit.Panel1MinSize = 260;
        mainSplit.Panel2MinSize = 500;
        mainSplit.SplitterDistance = Math.Clamp(340, mainSplit.Panel1MinSize, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth);
        detailSplit.Panel1MinSize = 260;
        detailSplit.Panel2MinSize = 48;
        detailSplit.SplitterDistance = Math.Clamp(detailSplit.Height - 72, detailSplit.Panel1MinSize, detailSplit.Height - detailSplit.Panel2MinSize - detailSplit.SplitterWidth);
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
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private sealed record SolutionNodeTag;

    private sealed record ProjectNodeTag(string ProjectPath);

    private sealed record DependencyFolderNodeTag(string ProjectPath);

    private sealed record PackageNodeTag(string ProjectPath, string Include);

    private sealed record FolderNodeTag(string ProjectPath, string Name);

    private sealed record DocumentNodeTag(string StableKey);

    private sealed record SymbolNodeTag(string StableKey);

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
}
