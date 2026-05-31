using System.ComponentModel;

namespace AIMonitor.App.Controls;

[DesignerCategory("Code")]
public sealed class SelectionDetailsControl : UserControl
{
    private readonly Label titleLabel;
    private readonly Label subtitleLabel;
    private readonly PropertyGrid propertyGrid;
    private readonly SplitContainer contentSplit;
    private readonly TabControl relatedTabs;
    private readonly Label primaryLabel;
    private readonly DataGridView primaryGrid;
    private readonly Label secondaryLabel;
    private readonly DataGridView secondaryGrid;
    private readonly TextBox notesBox;
    private bool splitterSized;

    public SelectionDetailsControl()
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
        contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 10,
            BackColor = SystemColors.ControlDark
        };
        relatedTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        primaryLabel = CreateSectionLabel();
        primaryGrid = CreateGrid();
        secondaryLabel = CreateSectionLabel();
        secondaryGrid = CreateGrid();
        notesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };

        Controls.Add(BuildLayout());
        Load += (_, _) => BeginInvoke(ApplyInitialSplitterLayout);
    }

    public void ShowDetails<TPrimary, TSecondary>(
        string title,
        string subtitle,
        IEnumerable<(string Name, string Value)> properties,
        string primaryTitle,
        IEnumerable<TPrimary> primaryRows,
        string secondaryTitle,
        IEnumerable<TSecondary> secondaryRows)
    {
        titleLabel.Text = title;
        subtitleLabel.Text = subtitle;
        propertyGrid.SelectedObject = new DetailPropertyBag(properties);
        primaryLabel.Text = primaryTitle;
        primaryGrid.DataSource = primaryRows.ToList();
        secondaryLabel.Text = secondaryTitle;
        secondaryGrid.DataSource = secondaryRows.ToList();
        notesBox.Text = string.Join(
            Environment.NewLine,
            properties.Select(property => $"{property.Name}: {property.Value}"));
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

        relatedTabs.TabPages.Add(BuildTab(primaryLabel, primaryGrid));
        relatedTabs.TabPages.Add(BuildTab(secondaryLabel, secondaryGrid));
        relatedTabs.TabPages.Add(BuildTab("Selected Values", notesBox));

        contentSplit.Panel1.Controls.Add(WrapSection("Inspector", propertyGrid));
        contentSplit.Panel2.Controls.Add(relatedTabs);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(contentSplit, 0, 1);
        return root;
    }

    private static Label CreateSectionLabel()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
    }

    private static Control WrapSection(string title, Control content)
    {
        Label label = CreateSectionLabel();
        label.Text = title;
        return WrapSection(label, content);
    }

    private static Control WrapSection(Label label, Control content)
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
        section.Controls.Add(label, 0, 0);
        section.Controls.Add(content, 0, 1);
        return section;
    }

    private static TabPage BuildTab(Label label, Control content)
    {
        TabPage page = new(label.Text);
        label.TextChanged += (_, _) => page.Text = label.Text;
        page.Controls.Add(content);
        return page;
    }

    private static TabPage BuildTab(string title, Control content)
    {
        TabPage page = new(title);
        page.Controls.Add(content);
        return page;
    }

    private void ApplyInitialSplitterLayout()
    {
        if (splitterSized || contentSplit.Width < 700)
        {
            return;
        }

        splitterSized = true;
        contentSplit.Panel1MinSize = 320;
        contentSplit.Panel2MinSize = 360;
        contentSplit.SplitterDistance = Math.Clamp(420, contentSplit.Panel1MinSize, contentSplit.Width - contentSplit.Panel2MinSize - contentSplit.SplitterWidth);
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

    private sealed class DetailPropertyBag : ICustomTypeDescriptor
    {
        private readonly IReadOnlyList<DetailProperty> properties;

        public DetailPropertyBag(IEnumerable<(string Name, string Value)> properties)
        {
            this.properties = properties
                .Select(property => new DetailProperty(property.Name, property.Value))
                .ToList();
        }

        public AttributeCollection GetAttributes() => AttributeCollection.Empty;

        public string? GetClassName() => null;

        public string? GetComponentName() => null;

        public TypeConverter GetConverter() => new TypeConverter();

        public EventDescriptor? GetDefaultEvent() => null;

        public PropertyDescriptor? GetDefaultProperty() => null;

        public object? GetEditor(Type editorBaseType) => null;

        public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;

        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

        public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        {
            return new PropertyDescriptorCollection(
                properties.Select(property => new DetailPropertyDescriptor(property)).ToArray());
        }

        public PropertyDescriptorCollection GetProperties() => GetProperties(null);

        public object? GetPropertyOwner(PropertyDescriptor? pd) => this;
    }

    private sealed record DetailProperty(string Name, string Value);

    private sealed class DetailPropertyDescriptor : PropertyDescriptor
    {
        private readonly DetailProperty property;

        public DetailPropertyDescriptor(DetailProperty property)
            : base(property.Name, null)
        {
            this.property = property;
        }

        public override Type ComponentType => typeof(DetailPropertyBag);

        public override bool IsReadOnly => true;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component) => false;

        public override object GetValue(object? component) => property.Value;

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object? component, object? value)
        {
        }

        public override bool ShouldSerializeValue(object component) => false;
    }
}
