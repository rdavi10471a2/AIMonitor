using System.ComponentModel;
using System.Diagnostics;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Logging;
using AIMonitor.MSBuild;

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
    private readonly TextBox overviewBox;
    private readonly DataGridView projectsGrid;
    private readonly DataGridView documentsGrid;
    private readonly DataGridView symbolsGrid;
    private readonly DataGridView referencesGrid;
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
        overviewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
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
        detailTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        overviewTab = BuildTextTab("Overview", overviewBox);
        projectsTab = BuildGridTab("Projects", projectsGrid);
        documentsTab = BuildGridTab("Documents", documentsGrid);
        symbolsTab = BuildGridTab("Symbols", symbolsGrid);
        referencesTab = BuildGridTab("References", referencesGrid);
        packagesTab = BuildGridTab("Packages", packagesGrid);
        rawTab = BuildTextTab("Raw", rawBox);

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

    private void WireEvents()
    {
        rebuildIndexMenuItem.Click += async (_, _) => await RebuildIndexAsync();
        refreshMenuItem.Click += (_, _) => LoadSettingsAndRefresh();
        chooseSolutionMenuItem.Click += (_, _) => ChooseWatchedSolution();
        openWatchedFolderMenuItem.Click += (_, _) => OpenFolder(settings?.WatchedProjectFolder);
        openDatabaseFolderMenuItem.Click += (_, _) => OpenFolder(Path.GetDirectoryName(databasePathBox.Text));
        indexTree.AfterSelect += (_, args) => SelectTreeNode(args.Node);
        symbolsGrid.CellDoubleClick += (_, _) => LoadReferencesForSelectedSymbol();
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
            SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
            SolutionIndexSummary summary = await builder.RebuildAsync(settings);
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
        SetGrid(projectsGrid, projects);
        SetGrid(documentsGrid, documents);
        SetSymbolsGrid(symbols);
        SetGrid(referencesGrid, references);
        SetGrid(packagesGrid, packages);
        overviewBox.Text = $"""
            Solution
            ========
            Path: {settings?.WatchedSolutionPath ?? currentSummary.InputPath}
            Indexed: {FormatIndexedAt(currentSummary.IndexedAtUtc)}

            Projects: {projects.Count}
            Documents: {documents.Count}
            Symbols: {symbols.Count}
            References: {references.Count}
            Packages: {packages.Count}
            Diagnostics: {currentSummary.DiagnosticCount}
            """;
        rawBox.Text = overviewBox.Text;
        detailTabs.SelectedTab = overviewTab;
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

        SetGrid(projectsGrid, new[] { project });
        SetGrid(documentsGrid, projectDocuments);
        SetSymbolsGrid(projectSymbols);
        SetGrid(referencesGrid, projectReferences);
        SetGrid(packagesGrid, projectPackages);
        overviewBox.Text = $"""
            Project
            =======
            Name: {project.Name}
            Path: {project.ProjectPath}
            Stable Key: {project.StableKey}
            Language: {project.Language}
            Target Framework: {project.TargetFramework}
            Target Frameworks: {project.TargetFrameworks}
            Output Type: {project.OutputType}
            SDK: {project.Sdk}
            Assembly Name: {project.AssemblyName}
            Root Namespace: {project.RootNamespace}
            Nullable: {project.Nullable}
            Implicit Usings: {project.ImplicitUsings}
            Lang Version: {project.LangVersion}

            Documents: {projectDocuments.Count}
            Symbols: {projectSymbols.Count}
            References: {projectReferences.Count}
            Packages: {projectPackages.Count}
            """;
        rawBox.Text = project.ToString();
        detailTabs.SelectedTab = overviewTab;
        SetStatus($"Project {project.Name} | Documents: {projectDocuments.Count} | Symbols: {projectSymbols.Count} | References: {projectReferences.Count}");
    }

    private void ShowDependencies(string projectPath)
    {
        List<IndexedPackageReferenceRow> projectPackages = packages.Where(package => PathEquals(package.ProjectPath, projectPath)).ToList();
        SetGrid(packagesGrid, projectPackages);
        SetGrid(projectsGrid, projects.Where(project => PathEquals(project.ProjectPath, projectPath)).ToList());
        overviewBox.Text = $"""
            Dependencies
            ============
            Project: {projectPath}
            Package References: {projectPackages.Count}
            """;
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
        overviewBox.Text = $"""
            Package
            =======
            Include: {package.Include}
            Version: {package.Version}
            Project: {package.ProjectPath}
            """;
        rawBox.Text = package.ToString();
        detailTabs.SelectedTab = overviewTab;
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

        SetGrid(documentsGrid, folderDocuments);
        SetSymbolsGrid(folderSymbols);
        SetGrid(projectsGrid, projects.Where(project => PathEquals(project.ProjectPath, projectPath)).ToList());
        overviewBox.Text = $"""
            Folder
            ======
            Name: {folderNode.Text}
            Project: {projectPath}

            Documents: {folderDocuments.Count}
            Symbols: {folderSymbols.Count}
            """;
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
        SetGrid(documentsGrid, new[] { document });
        SetSymbolsGrid(documentSymbols);
        SetGrid(referencesGrid, documentReferences);
        SetGrid(projectsGrid, projects.Where(project => PathEquals(project.ProjectPath, document.ProjectPath)).ToList());
        overviewBox.Text = $"""
            File
            ====
            Name: {document.Name}
            Path: {document.FilePath}
            Stable Key: {document.StableKey}
            Project: {document.ProjectPath}
            Folders: {document.Folders}

            Declared Symbols: {documentSymbols.Count}
            References In File: {documentReferences.Count}
            """;
        rawBox.Text = document.ToString();
        detailTabs.SelectedTab = overviewTab;
        SetStatus($"File {document.Name} | Symbols: {documentSymbols.Count} | References in file: {documentReferences.Count}");
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
        SetGrid(referencesGrid, symbolReferences);
        SetGrid(documentsGrid, documents.Where(document => PathEquals(document.FilePath, symbol.FilePath)).ToList());
        SetGrid(projectsGrid, projects.Where(project => PathEquals(project.ProjectPath, symbol.ProjectPath)).ToList());
        overviewBox.Text = $"""
            Symbol
            ======
            Kind: {symbol.Kind}
            Name: {symbol.Name}
            Namespace: {symbol.Namespace}
            Containing Type: {symbol.ContainingType}
            Signature: {symbol.Signature}
            Stable Key: {symbol.StableKey}
            File: {symbol.FilePath}
            Lines: {symbol.StartLine}-{symbol.EndLine}

            References: {symbolReferences.Count}
            """;
        rawBox.Text = symbol.ToString();
        detailTabs.SelectedTab = overviewTab;
        SetStatus($"Symbol {symbol.Kind} {symbol.Name} | References: {symbolReferences.Count}");
    }

    private void LoadReferencesForSelectedSymbol()
    {
        if (symbolsGrid.CurrentRow?.DataBoundItem is not IndexedSymbolRow symbol)
        {
            return;
        }

        List<IndexedReferenceRow> symbolReferences = references.Where(reference => reference.TargetStableKey == symbol.StableKey).ToList();
        SetGrid(referencesGrid, symbolReferences);
        rawBox.Text = symbol.StableKey;
        SetStatus($"Selected {symbol.Kind} {symbol.Name} | References: {symbolReferences.Count}");
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

    private void SetSymbolsGrid(IEnumerable<IndexedSymbolRow> rows)
    {
        symbolsGrid.DataSource = rows.ToList();
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
}
