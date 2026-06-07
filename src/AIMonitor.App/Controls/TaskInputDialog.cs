using System.ComponentModel;
using AIMonitor.Planning;

namespace AIMonitor.App.Controls
{
    [DesignerCategory("Code")]
    internal sealed class TaskInputDialog : Form
    {
        private readonly TextBox titleBox;
        private readonly TextBox bodyBox;
        private readonly TextBox humanNotesBox;
        private readonly TextBox constraintsBox;
        private readonly TextBox acceptanceCriteriaBox;
        private readonly ErrorProvider errorProvider;
        private readonly Label validationSummaryLabel;
        private readonly bool showTaskFields;

        private TaskInputDialog(string title, bool showTaskFields)
        {
            this.showTaskFields = showTaskFields;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = showTaskFields ? new Size(900, 640) : new Size(760, 480);
            Size = showTaskFields ? new Size(1024, 768) : new Size(900, 560);

            titleBox = new TextBox
            {
                Dock = DockStyle.Fill
            };
            bodyBox = CreateMultilineBox();
            humanNotesBox = CreateMultilineBox();
            constraintsBox = CreateMultilineBox();
            acceptanceCriteriaBox = CreateMultilineBox();
            errorProvider = new ErrorProvider
            {
                BlinkStyle = ErrorBlinkStyle.NeverBlink,
                ContainerControl = this
            };
            validationSummaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(176, 0, 32),
                BackColor = Color.FromArgb(255, 245, 246),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };

            Controls.Add(showTaskFields ? BuildTaskLayout() : BuildSingleTextLayout(title));
        }

        public string TitleText => titleBox.Text.Trim();

        public string BodyText => bodyBox.Text.Trim();

        public string HumanNotesText => humanNotesBox.Text.Trim();

        public string ConstraintsText => constraintsBox.Text.Trim();

        public string AcceptanceCriteriaText => acceptanceCriteriaBox.Text.Trim();

        public static TaskInputDialog CreateNewTaskDialog()
        {
            return new TaskInputDialog("New Task", true);
        }

        public static TaskInputDialog CreateEditTaskDialog(PlanningTaskRow task)
        {
            TaskInputDialog dialog = new TaskInputDialog("Edit Task", true);
            dialog.titleBox.Text = task.Title;
            dialog.bodyBox.Text = task.Goal;
            dialog.humanNotesBox.Text = task.HumanContext;
            dialog.constraintsBox.Text = task.Constraints;
            dialog.acceptanceCriteriaBox.Text = task.AcceptanceCriteria;
            return dialog;
        }

        public static TaskInputDialog CreateStatusNoteDialog()
        {
            return CreateStatusNoteDialog("Current");
        }

        public static TaskInputDialog CreateStatusNoteDialog(string transition)
        {
            TaskInputDialog dialog = new TaskInputDialog("Status Note Required", false);
            dialog.bodyBox.Text = string.Empty;
            return dialog;
        }

        public static TaskInputDialog CreateHumanNotesDialog(string existingNotes)
        {
            TaskInputDialog dialog = new TaskInputDialog("Edit Human Notes", false);
            dialog.bodyBox.Text = existingNotes;
            dialog.Shown += (_, _) =>
            {
                dialog.BeginInvoke((MethodInvoker)(() =>
                {
                    dialog.bodyBox.SelectionStart = dialog.bodyBox.TextLength;
                    dialog.bodyBox.SelectionLength = 0;
                }));
            };
            return dialog;
        }

        private Control BuildTaskLayout()
        {
            TableLayoutPanel root = CreateRootLayout(7);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            root.Controls.Add(validationSummaryLabel, 0, 0);
            root.Controls.Add(BuildLabeledControl("Title *", titleBox), 0, 1);
            root.Controls.Add(BuildLabeledControl("Rough Intent / Goal *", bodyBox), 0, 2);
            root.Controls.Add(BuildLabeledControl("Human Notes", humanNotesBox), 0, 3);
            root.Controls.Add(BuildLabeledControl("Constraints / Do Not Touch", constraintsBox), 0, 4);
            root.Controls.Add(BuildLabeledControl("Acceptance Signals", acceptanceCriteriaBox), 0, 5);
            root.Controls.Add(BuildButtons(), 0, 6);
            return root;
        }

        private Control BuildSingleTextLayout(string title)
        {
            TableLayoutPanel root = CreateRootLayout(4);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            Label helper = new Label
            {
                Text = title.Equals("Edit Human Notes", StringComparison.Ordinal)
                    ? "Only manual human notes are editable here. Generated status updates are preserved separately."
                    : "State changes require a comment. Enter the reason or restart note to save this transition.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(validationSummaryLabel, 0, 0);
            root.Controls.Add(helper, 0, 1);
            root.Controls.Add(bodyBox, 0, 2);
            root.Controls.Add(BuildButtons(), 0, 3);
            return root;
        }

        private static TableLayoutPanel CreateRootLayout(int rows)
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = rows,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return root;
        }

        private static Control BuildLabeledControl(string labelText, Control control)
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            panel.Controls.Add(control, 0, 1);
            return panel;
        }

        private Control BuildButtons()
        {
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            Button save = new Button
            {
                Text = "Save",
                Width = 88,
                Height = 30
            };
            save.Click += (_, _) => SaveIfValid();
            Button cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 88,
                Height = 30
            };
            AcceptButton = save;
            CancelButton = cancel;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            return buttons;
        }

        private void SaveIfValid()
        {
            errorProvider.Clear();
            validationSummaryLabel.Visible = false;
            if (showTaskFields && string.IsNullOrWhiteSpace(titleBox.Text))
            {
                ShowValidationError(titleBox, "Title is required.");
                titleBox.Focus();
                return;
            }

            if (showTaskFields && string.IsNullOrWhiteSpace(bodyBox.Text))
            {
                ShowValidationError(bodyBox, "Goal is required.");
                bodyBox.Focus();
                return;
            }

            if (!showTaskFields && string.IsNullOrWhiteSpace(bodyBox.Text))
            {
                ShowValidationError(bodyBox, "A note is required.");
                bodyBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowValidationError(Control control, string message)
        {
            validationSummaryLabel.Text = message;
            validationSummaryLabel.Visible = true;
            errorProvider.SetError(control, message);
        }

        private static TextBox CreateMultilineBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = true
            };
        }
    }
}
