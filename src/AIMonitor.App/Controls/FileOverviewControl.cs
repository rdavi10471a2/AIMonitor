using System.ComponentModel;
using AIMonitor.Data;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class FileOverviewControl : UserControl
{
    private readonly Label titleLabel;
    private readonly Label subtitleLabel;
    private readonly PropertyGrid propertyGrid;
    private readonly TreeView symbolsTree;
    private readonly TabControl referenceTabs;
    private readonly DataGridView localReferencesGrid;
    private readonly DataGridView externalReferencesGrid;
    private readonly DataGridView incomingReferencesGrid;
    private readonly SplitContainer outerSplit;
    private readonly SplitContainer rightSplit;
    private bool splitterSized;

    public FileOverviewControl()
    {
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Window;

        titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        subtitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            HelpVisible = false,
            ToolbarVisible = false,
            PropertySort = PropertySort.CategorizedAlphabetical
        };
        symbolsTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false
        };
        referenceTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        localReferencesGrid = CreateGrid();
        externalReferencesGrid = CreateGrid();
        incomingReferencesGrid = CreateGrid();
        outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 10,
            BackColor = SystemColors.ControlDark
        };
        rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10,
            BackColor = SystemColors.ControlDark
        };

        Controls.Add(BuildLayout());
        Load += (_, _) => BeginInvoke(ApplyInitialSplitterLayout);
        symbolsTree.AfterSelect += (_, args) =>
        {
            if (args.Node?.Tag is SymbolTreeTag symbol)
            {
                SymbolSelected?.Invoke(symbol.StableKey);
            }
        };
        localReferencesGrid.CellDoubleClick += (_, _) => SelectReferenceTarget(localReferencesGrid);
        externalReferencesGrid.CellDoubleClick += (_, _) => SelectReferenceTarget(externalReferencesGrid);
        incomingReferencesGrid.CellDoubleClick += (_, _) => SelectReferenceTarget(incomingReferencesGrid);
    }

    public event Action<string>? SymbolSelected;

    public void ShowFile(
        IndexedDocumentRow document,
        IReadOnlyList<IndexedSymbolRow> symbols,
        IReadOnlyList<ReferenceView> localReferences,
        IReadOnlyList<ReferenceView> externalReferences,
        IReadOnlyList<ReferenceView> incomingReferences)
    {
        titleLabel.Text = $"File: {document.Name}";
        subtitleLabel.Text = document.FilePath;
        propertyGrid.SelectedObject = new FilePropertyBag(
            document,
            symbols.Count,
            localReferences.Count,
            externalReferences.Count,
            incomingReferences.Count);
        LoadSymbolTree(symbols);
        localReferencesGrid.DataSource = localReferences.ToList();
        externalReferencesGrid.DataSource = externalReferences.ToList();
        incomingReferencesGrid.DataSource = incomingReferences.ToList();
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        header.Controls.Add(titleLabel, 0, 0);
        header.Controls.Add(subtitleLabel, 0, 1);

        referenceTabs.TabPages.Add(BuildGridTab("Local References", localReferencesGrid));
        referenceTabs.TabPages.Add(BuildGridTab("External References", externalReferencesGrid));
        referenceTabs.TabPages.Add(BuildGridTab("Incoming References", incomingReferencesGrid));

        outerSplit.Panel1.Controls.Add(WrapSection("File Properties", propertyGrid));
        rightSplit.Panel1.Controls.Add(WrapSection("File Symbol Hierarchy", symbolsTree));
        rightSplit.Panel2.Controls.Add(WrapSection("References", referenceTabs));
        outerSplit.Panel2.Controls.Add(rightSplit);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(outerSplit, 0, 1);
        return root;
    }

    private void LoadSymbolTree(IReadOnlyList<IndexedSymbolRow> symbols)
    {
        symbolsTree.BeginUpdate();
        try
        {
            symbolsTree.Nodes.Clear();
            Dictionary<string, TreeNode> typeNodes = new(StringComparer.Ordinal);

            foreach (IndexedSymbolRow symbol in symbols.OrderBy(symbol => symbol.StartLine).ThenBy(symbol => symbol.Name))
            {
                TreeNode node = new(FormatSymbolNode(symbol))
                {
                    Tag = new SymbolTreeTag(symbol.StableKey)
                };

                if (symbol.Kind.Equals("NamedType", StringComparison.Ordinal))
                {
                    symbolsTree.Nodes.Add(node);
                    typeNodes[symbol.Signature] = node;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(symbol.ContainingType)
                    && typeNodes.TryGetValue(symbol.ContainingType, out TreeNode? parent))
                {
                    parent.Nodes.Add(node);
                }
                else
                {
                    symbolsTree.Nodes.Add(node);
                }
            }

            foreach (TreeNode node in symbolsTree.Nodes)
            {
                node.Expand();
            }
        }
        finally
        {
            symbolsTree.EndUpdate();
        }
    }

    private static string FormatSymbolNode(IndexedSymbolRow symbol)
    {
        return $"{symbol.Kind} {symbol.Name}  [{symbol.StartLine}-{symbol.EndLine}]";
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

    private static TabPage BuildGridTab(string title, DataGridView grid)
    {
        TabPage tab = new(title);
        tab.Controls.Add(grid);
        return tab;
    }

    private void SelectReferenceTarget(DataGridView grid)
    {
        if (grid.CurrentRow?.DataBoundItem is ReferenceView reference)
        {
            SymbolSelected?.Invoke(reference.TargetStableKey);
        }
    }

    private void ApplyInitialSplitterLayout()
    {
        if (splitterSized || outerSplit.Width < 800 || rightSplit.Height < 400)
        {
            return;
        }

        splitterSized = true;
        outerSplit.Panel1MinSize = 320;
        outerSplit.Panel2MinSize = 420;
        outerSplit.SplitterDistance = Math.Clamp(390, outerSplit.Panel1MinSize, outerSplit.Width - outerSplit.Panel2MinSize - outerSplit.SplitterWidth);
        rightSplit.Panel1MinSize = 180;
        rightSplit.Panel2MinSize = 180;
        rightSplit.SplitterDistance = Math.Clamp(rightSplit.Height / 2, rightSplit.Panel1MinSize, rightSplit.Height - rightSplit.Panel2MinSize - rightSplit.SplitterWidth);
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

    private sealed record SymbolTreeTag(string StableKey);

    public sealed record ReferenceView(
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

    private sealed class FilePropertyBag
    {
        public FilePropertyBag(
            IndexedDocumentRow document,
            int symbolCount,
            int localReferenceCount,
            int externalReferenceCount,
            int incomingReferenceCount)
        {
            StableKey = document.StableKey;
            Name = document.Name;
            FilePath = document.FilePath;
            ProjectPath = document.ProjectPath;
            Folders = document.Folders;
            DeclaredSymbols = symbolCount;
            LocalReferences = localReferenceCount;
            ExternalReferences = externalReferenceCount;
            IncomingReferences = incomingReferenceCount;
        }

        [Category("Identity")]
        public string StableKey { get; }

        [Category("Identity")]
        public string Name { get; }

        [Category("Location")]
        public string FilePath { get; }

        [Category("Location")]
        public string ProjectPath { get; }

        [Category("Location")]
        public string Folders { get; }

        [Category("Index")]
        public int DeclaredSymbols { get; }

        [Category("Index")]
        public int LocalReferences { get; }

        [Category("Index")]
        public int ExternalReferences { get; }

        [Category("Index")]
        public int IncomingReferences { get; }
    }
}
