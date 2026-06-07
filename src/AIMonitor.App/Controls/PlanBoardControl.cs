using System.ComponentModel;
using AIMonitor.Core;
using AIMonitor.Planning;

namespace AIMonitor.App.Controls
{
    [DesignerCategory("Code")]
    public sealed class PlanBoardControl : UserControl
    {
        private readonly Label statusLabel;
        private readonly Label titleLabel;
        private readonly TreeView taskTree;
        private readonly MarkdownPreviewControl taskMemoryPreview;
        private readonly Label taskGitLabel;
        private readonly ToolStripButton addTaskButton;
        private readonly ToolStripButton makeCurrentButton;
        private readonly ToolStripButton refreshButton;

        private string? settingsPath;
        private PlanningService? planningService;
        private PlanningTaskRow? currentTask;
        private bool populatingTaskTree;
        private bool formReady;
        private bool refreshPending;

        public PlanBoardControl(string? settingsPath)
        {
            this.settingsPath = settingsPath;
            Dock = DockStyle.Fill;
            MinimumSize = new Size(900, 460);

            statusLabel = CreateValueLabel();
            titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 14.0f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            taskTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                FullRowSelect = true,
                HideSelection = false,
                ItemHeight = 22,
                ShowLines = true,
                ShowNodeToolTips = true,
                ShowPlusMinus = true,
                ShowRootLines = true
            };
            taskTree.AfterSelect += (_, _) =>
            {
                if (populatingTaskTree)
                {
                    return;
                }

                if (GetSelectedTaskId().Length == 0)
                {
                    ClearTaskMemoryPreview();
                }
                else
                {
                    ShowSelectedTaskMemory();
                }

                UpdateActionState();
            };
            taskTree.NodeMouseClick += (_, args) =>
            {
                if (args.Node is null)
                {
                    return;
                }

                taskTree.SelectedNode = args.Node;
                if (args.Button == MouseButtons.Right)
                {
                    if (args.Node.Tag is PlanningTaskRow)
                    {
                        ShowTaskItemMenu(args.Location);
                    }
                    else
                    {
                        ShowTaskBoardMenu(taskTree, args.Location);
                    }
                }
            };
            taskTree.DoubleClick += (_, _) => MakeSelectedTaskCurrent();

            taskMemoryPreview = new MarkdownPreviewControl();
            taskGitLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopLeft
            };
            addTaskButton = CreateCommandButton("Add Task", "Create a new planning task for this watched solution.", CreateTask);
            makeCurrentButton = CreateCommandButton("Make Current", GetStatusDescription(PlanningTaskStatus.Current), MakeSelectedTaskCurrent);
            refreshButton = CreateCommandButton("Refresh", "Reload task state and memory from the planning database.", RefreshPlanningStatus);

            Controls.Add(BuildLayout());
            Load += (_, _) =>
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    formReady = true;
                    if (refreshPending)
                    {
                        refreshPending = false;
                        RefreshPlanningStatus();
                    }
                }));
            };
        }

        public void SetSettingsPath(string? path)
        {
            settingsPath = path;
            RefreshPlanningStatus();
        }

        public void RefreshPlanningStatus()
        {
            if (!formReady)
            {
                refreshPending = true;
                return;
            }

            RefreshPlanningStatusCore();
        }

        private void RefreshPlanningStatusCore()
        {
            try
            {
                string repositoryRoot = AppPathResolver.FindRepositoryRoot();
                MonitorSettings monitorSettings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
                PlanningSettings planningSettings = MonitorSettingsLoader.LoadPlanning(repositoryRoot, settingsPath);
                titleLabel.Text = "Tasks for " + Path.GetFileNameWithoutExtension(monitorSettings.WatchedSolutionPath);

                if (!planningSettings.Enabled)
                {
                    ShowDisabled(monitorSettings, planningSettings);
                    return;
                }

                if (string.IsNullOrWhiteSpace(planningSettings.DatabasePath)
                    || string.IsNullOrWhiteSpace(planningSettings.TaskMemoryRoot))
                {
                    ShowMisconfigured(monitorSettings, planningSettings);
                    return;
                }

                Directory.CreateDirectory(planningSettings.TaskMemoryRoot);
                PlanningDatabase database = new PlanningDatabase(monitorSettings, planningSettings.DatabasePath);
                database.EnsureCreated();
                planningService = new PlanningService(
                    monitorSettings,
                    database,
                    new TaskMemoryWriter(),
                    planningSettings.TaskMemoryRoot);
                currentTask = planningService.GetCurrentTask();

                statusLabel.Text = "Enabled";
                PopulateTaskTree(planningService.ListTasks());
                if (GetSelectedTaskId().Length > 0)
                {
                    ShowSelectedTaskMemory();
                }
                else
                {
                    ClearTaskMemoryPreview();
                }

                taskGitLabel.Text = "Task Git is synced from accepted/rejected workflow decisions. No reviewed records have been loaded into this shell yet.";
                UpdateActionState();
            }
            catch (Exception ex)
            {
                titleLabel.Text = "Tasks";
                statusLabel.Text = "Error";
                taskTree.Nodes.Clear();
                ShowDiagnosticText("Planning status failed to load." + Environment.NewLine + Environment.NewLine + ex);
                taskGitLabel.Text = "Planning status failed to load." + Environment.NewLine + Environment.NewLine + ex;
                planningService = null;
                currentTask = null;
                UpdateActionState();
            }
        }

        private Control BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildCommandStrip(), 0, 1);
            root.Controls.Add(BuildBody(), 0, 2);
            return root;
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            header.Controls.Add(titleLabel, 0, 0);
            header.Controls.Add(CreateCaption("Planning"), 1, 0);
            header.Controls.Add(statusLabel, 2, 0);
            return header;
        }

        private ToolStrip BuildCommandStrip()
        {
            ToolStrip strip = new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(2, 4, 2, 4),
                RenderMode = ToolStripRenderMode.System,
                ShowItemToolTips = true
            };
            strip.Items.Add(addTaskButton);
            strip.Items.Add(makeCurrentButton);
            strip.Items.Add(refreshButton);
            return strip;
        }

        private static ToolStripButton CreateCommandButton(string text, string toolTipText, Action action)
        {
            ToolStripButton button = new ToolStripButton(text)
            {
                AutoSize = true,
                ToolTipText = toolTipText
            };
            button.Click += (_, _) => action();
            return button;
        }

        private Control BuildBody()
        {
            SplitContainer outer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6
            };
            outer.Panel1.Controls.Add(BuildTitledPanel("Tasks", taskTree));

            SplitContainer inner = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6
            };
            inner.Panel1.Controls.Add(BuildTitledPanel("Task Memory", taskMemoryPreview));
            inner.Panel2.Controls.Add(BuildTitledPanel("Review Evidence", taskGitLabel));
            outer.Panel2.Controls.Add(inner);

            outer.HandleCreated += (_, _) =>
            {
                ConfigureSplitContainer(outer, 220, 500, Math.Max(240, outer.Width / 4));
                ConfigureSplitContainer(inner, 360, 220, Math.Max(420, (inner.Width * 2) / 3));
            };
            return outer;
        }

        private static void ConfigureSplitContainer(
            SplitContainer splitContainer,
            int panel1MinSize,
            int panel2MinSize,
            int preferredSplitterDistance)
        {
            if (splitContainer.Width <= panel1MinSize + panel2MinSize + splitContainer.SplitterWidth)
            {
                splitContainer.Panel1MinSize = 25;
                splitContainer.Panel2MinSize = 25;
                return;
            }

            splitContainer.Panel1MinSize = panel1MinSize;
            splitContainer.Panel2MinSize = panel2MinSize;
            int maximumDistance = splitContainer.Width - splitContainer.SplitterWidth - panel2MinSize;
            splitContainer.SplitterDistance = Math.Max(
                panel1MinSize,
                Math.Min(preferredSplitterDistance, maximumDistance));
        }

        private static TableLayoutPanel BuildTitledPanel(string title, Control content)
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label label = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(content, 0, 1);
            return panel;
        }

        private static Label CreateCaption(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
            };
        }

        private static Label CreateValueLabel()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void ShowDisabled(MonitorSettings monitorSettings, PlanningSettings planningSettings)
        {
            planningService = null;
            currentTask = null;
            titleLabel.Text = "Tasks for " + Path.GetFileNameWithoutExtension(monitorSettings.WatchedSolutionPath);
            statusLabel.Text = "Disabled";
            PopulateTaskTree();
            SetTaskMemoryMarkdown(BuildDisabledPreview(monitorSettings));
            taskGitLabel.Text = "Planning is disabled. Safe edit workflow continues unchanged.";
            UpdateActionState();
        }

        private void ShowMisconfigured(MonitorSettings monitorSettings, PlanningSettings planningSettings)
        {
            planningService = null;
            currentTask = null;
            titleLabel.Text = "Tasks for " + Path.GetFileNameWithoutExtension(monitorSettings.WatchedSolutionPath);
            statusLabel.Text = "Misconfigured";
            PopulateTaskTree();
            SetTaskMemoryMarkdown(BuildMisconfiguredPreview(monitorSettings));
            taskGitLabel.Text = "Planning is enabled, but DatabasePath and TaskMemoryRoot are required.";
            UpdateActionState();
        }

        private void PopulateTaskTree(IReadOnlyList<PlanningTaskRow>? tasks = null)
        {
            string selectedTaskId = GetSelectedTaskId();
            populatingTaskTree = true;
            try
            {
                taskTree.BeginUpdate();
                taskTree.Nodes.Clear();
                AddStatusNode(PlanningTaskStatus.Backlog, tasks);
                AddStatusNode(PlanningTaskStatus.Ready, tasks);
                AddStatusNode(PlanningTaskStatus.Current, tasks);
                AddStatusNode(PlanningTaskStatus.Paused, tasks);
                AddStatusNode(PlanningTaskStatus.Closed, tasks);
                AddStatusNode(PlanningTaskStatus.Canceled, tasks);
                taskTree.ExpandAll();
                SelectTaskNode(selectedTaskId);
                if (taskTree.SelectedNode is null && currentTask is not null)
                {
                    SelectTaskNode(currentTask.TaskId);
                }
            }
            finally
            {
                taskTree.EndUpdate();
                populatingTaskTree = false;
            }
        }

        private void AddStatusNode(string status, IReadOnlyList<PlanningTaskRow>? tasks)
        {
            IReadOnlyList<PlanningTaskRow> statusTasks = tasks is null
                ? Array.Empty<PlanningTaskRow>()
                : tasks.Where(task => IsTaskInStatusGroup(task, status)).ToArray();
            TreeNode statusNode = new TreeNode($"{GetStatusDisplayName(status)} ({statusTasks.Count})")
            {
                Name = status,
                Tag = status,
                ToolTipText = GetStatusDescription(status)
            };
            foreach (PlanningTaskRow task in statusTasks)
            {
                TreeNode taskNode = new TreeNode(task.Title)
                {
                    Name = task.TaskId,
                    Tag = task,
                    ToolTipText = task.Title
                };
                statusNode.Nodes.Add(taskNode);
            }

            taskTree.Nodes.Add(statusNode);
        }

        private void ShowTaskBoardMenu(Control owner, Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem newTask = new ToolStripMenuItem("Add Task");
            newTask.Click += (_, _) => CreateTask();
            newTask.Enabled = planningService is not null;
            menu.Items.Add(newTask);
            ToolStripMenuItem refresh = new ToolStripMenuItem("Refresh");
            refresh.Click += (_, _) => RefreshPlanningStatus();
            menu.Items.Add(refresh);
            menu.Show(owner, location);
        }

        private void ShowTaskItemMenu(Point location)
        {
            UpdateActionState();
            ShowSelectedTaskMemory();

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem editItem = new ToolStripMenuItem("Edit Task");
            editItem.Click += (_, _) => EditSelectedTask();
            editItem.Enabled = planningService is not null;
            menu.Items.Add(editItem);

            ToolStripMenuItem notesItem = new ToolStripMenuItem("Edit Human Notes");
            notesItem.Click += (_, _) => EditHumanNotesForSelectedTask();
            notesItem.Enabled = planningService is not null;
            menu.Items.Add(notesItem);

            menu.Items.Add(new ToolStripSeparator());
            AddMoveMenuItem(menu, PlanningTaskStatus.Backlog);
            AddMoveMenuItem(menu, PlanningTaskStatus.Ready);
            AddMoveMenuItem(menu, PlanningTaskStatus.Current);
            AddMoveMenuItem(menu, PlanningTaskStatus.Paused);
            AddMoveMenuItem(menu, PlanningTaskStatus.Closed);
            AddMoveMenuItem(menu, PlanningTaskStatus.Canceled);
            if (menu.Items.Count == 3)
            {
                ToolStripMenuItem unavailable = new ToolStripMenuItem("No moves available")
                {
                    Enabled = false
                };
                menu.Items.Add(unavailable);
            }

            menu.Show(taskTree, location);
        }

        private void AddMoveMenuItem(ContextMenuStrip menu, string status)
        {
            string selectedStatus = GetSelectedTaskStatus();
            if (status.Equals(PlanningTaskStatus.Current, StringComparison.Ordinal) && currentTask is not null)
            {
                return;
            }

            if (!CanMoveTo(selectedStatus, status))
            {
                return;
            }

            ToolStripMenuItem item = new ToolStripMenuItem(GetMoveCommandText(status))
            {
                ToolTipText = GetStatusDescription(status)
            };
            item.Click += (_, _) => MoveSelectedTaskTo(status);
            menu.Items.Add(item);
        }

        private void SelectTaskNode(string taskIdToSelect)
        {
            if (string.IsNullOrWhiteSpace(taskIdToSelect))
            {
                return;
            }

            foreach (TreeNode statusNode in taskTree.Nodes)
            {
                foreach (TreeNode taskNode in statusNode.Nodes)
                {
                    if (taskNode.Tag is PlanningTaskRow task && task.TaskId.Equals(taskIdToSelect, StringComparison.Ordinal))
                    {
                        taskTree.SelectedNode = taskNode;
                        taskNode.EnsureVisible();
                        return;
                    }
                }
            }
        }

        private void UpdateActionState()
        {
            bool enabled = planningService is not null;
            string selectedStatus = GetSelectedTaskStatus();
            addTaskButton.Enabled = enabled;
            makeCurrentButton.Enabled = enabled
                && currentTask is null
                && CanMoveTo(selectedStatus, PlanningTaskStatus.Current);
            refreshButton.Enabled = formReady;
        }

        private string GetSelectedTaskStatus()
        {
            return taskTree.SelectedNode?.Parent?.Name ?? string.Empty;
        }

        private PlanningTaskRow? GetSelectedTask()
        {
            return taskTree.SelectedNode?.Tag as PlanningTaskRow;
        }

        private string GetSelectedTaskId()
        {
            return GetSelectedTask()?.TaskId ?? string.Empty;
        }

        private static bool CanMoveTo(string currentStatus, string nextStatus)
        {
            return !string.IsNullOrWhiteSpace(currentStatus)
                && !currentStatus.Equals(nextStatus, StringComparison.Ordinal);
        }

        private void SetTaskMemoryMarkdown(string markdown)
        {
            taskMemoryPreview.SetMarkdown(markdown);
        }

        private void ClearTaskMemoryPreview()
        {
            taskMemoryPreview.SetMarkdown(string.Empty);
        }

        private void ShowSelectedTaskMemory()
        {
            string taskId = GetSelectedTaskId();
            if (planningService is null || taskId.Length == 0)
            {
                return;
            }

            try
            {
                PlanningTaskRow task = planningService.GetTask(taskId);
                SetTaskMemoryMarkdown(File.ReadAllText(task.TaskMemoryMarkdownPath));
            }
            catch (Exception ex)
            {
                ShowDiagnosticText("Selected task memory failed to load." + Environment.NewLine + Environment.NewLine + ex);
            }
        }

        private void ShowDiagnosticText(string text)
        {
            taskMemoryPreview.ShowDiagnostic(text);
        }

        private void CreateTask()
        {
            if (planningService is null)
            {
                return;
            }

            using (TaskInputDialog dialog = TaskInputDialog.CreateNewTaskDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                PlanningTaskRow task = planningService.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = dialog.TitleText,
                    Goal = dialog.BodyText,
                    HumanContext = dialog.HumanNotesText,
                    Constraints = dialog.ConstraintsText,
                    AcceptanceCriteria = dialog.AcceptanceCriteriaText
                });
                currentTask = planningService.GetCurrentTask();
                if (currentTask is null)
                {
                    currentTask = planningService.MakeCurrent(task.TaskId, "Created and made current from Plan Board.");
                }

                RefreshPlanningStatus();
            }
        }

        private void EditSelectedTask()
        {
            string taskId = GetSelectedTaskId();
            if (planningService is null || taskId.Length == 0)
            {
                return;
            }

            PlanningTaskRow task = planningService.GetTask(taskId);
            using (TaskInputDialog dialog = TaskInputDialog.CreateEditTaskDialog(task))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                PlanningTaskRow updatedTask = planningService.UpdateTask(task.TaskId, new CreatePlanningTaskRequest
                {
                    Title = dialog.TitleText,
                    Goal = dialog.BodyText,
                    HumanContext = dialog.HumanNotesText,
                    Constraints = dialog.ConstraintsText,
                    AcceptanceCriteria = dialog.AcceptanceCriteriaText
                });
                if (currentTask is not null && currentTask.TaskId.Equals(updatedTask.TaskId, StringComparison.Ordinal))
                {
                    currentTask = updatedTask;
                }

                RefreshPlanningStatus();
            }
        }

        private void MakeSelectedTaskCurrent()
        {
            string taskId = GetSelectedTaskId();
            if (planningService is null || taskId.Length == 0)
            {
                return;
            }

            using (TaskInputDialog dialog = TaskInputDialog.CreateStatusNoteDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    currentTask = planningService.MakeCurrent(taskId, dialog.BodyText);
                    RefreshPlanningStatus();
                }
                catch (InvalidOperationException ex)
                {
                    ShowPlanningOperationError(ex.Message);
                    RefreshPlanningStatus();
                }
            }
        }

        private void MoveSelectedTaskTo(string status)
        {
            string taskId = GetSelectedTaskId();
            if (planningService is null || taskId.Length == 0)
            {
                return;
            }

            if (status.Equals(PlanningTaskStatus.Current, StringComparison.Ordinal))
            {
                MakeSelectedTaskCurrent();
                return;
            }

            using (TaskInputDialog dialog = TaskInputDialog.CreateStatusNoteDialog(status))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    planningService.ChangeStatus(taskId, status, dialog.BodyText);
                }
                catch (InvalidOperationException ex)
                {
                    ShowPlanningOperationError(ex.Message);
                }
            }

            RefreshPlanningStatus();
        }

        private void ShowPlanningOperationError(string message)
        {
            MessageBox.Show(
                this,
                message,
                "Plan Board",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static string GetStatusDescription(string status)
        {
            if (status.Equals(PlanningTaskStatus.Backlog, StringComparison.Ordinal))
            {
                return "Needs to be done eventually.";
            }

            if (status.Equals(PlanningTaskStatus.Ready, StringComparison.Ordinal))
            {
                return "Should be done soon.";
            }

            if (status.Equals(PlanningTaskStatus.Current, StringComparison.Ordinal))
            {
                return "Being done right now. Workflow evidence attaches here.";
            }

            if (status.Equals(PlanningTaskStatus.Paused, StringComparison.Ordinal))
            {
                return "On hold with a written context note for restart.";
            }

            if (status.Equals(PlanningTaskStatus.Closed, StringComparison.Ordinal)
                || status.Equals(PlanningTaskStatus.Done, StringComparison.Ordinal))
            {
                return "Done.";
            }

            if (status.Equals(PlanningTaskStatus.Canceled, StringComparison.Ordinal))
            {
                return "Out of the way, with notes kept for memory.";
            }

            return string.Empty;
        }

        private static string GetStatusDisplayName(string status)
        {
            if (status.Equals(PlanningTaskStatus.Ready, StringComparison.Ordinal))
            {
                return "Next Up";
            }

            if (status.Equals(PlanningTaskStatus.Paused, StringComparison.Ordinal))
            {
                return "Parked";
            }

            if (status.Equals(PlanningTaskStatus.Closed, StringComparison.Ordinal)
                || status.Equals(PlanningTaskStatus.Done, StringComparison.Ordinal))
            {
                return "Closed";
            }

            if (status.Equals(PlanningTaskStatus.Canceled, StringComparison.Ordinal))
            {
                return "Canceled";
            }

            return status;
        }

        private static string GetMoveCommandText(string status)
        {
            if (status.Equals(PlanningTaskStatus.Closed, StringComparison.Ordinal))
            {
                return "Close Task";
            }

            if (status.Equals(PlanningTaskStatus.Canceled, StringComparison.Ordinal))
            {
                return "Cancel Task";
            }

            return "Move to " + GetStatusDisplayName(status);
        }

        private static bool IsTaskInStatusGroup(PlanningTaskRow task, string status)
        {
            if (status.Equals(PlanningTaskStatus.Closed, StringComparison.Ordinal)
                && task.Status.Equals(PlanningTaskStatus.Done, StringComparison.Ordinal))
            {
                return true;
            }

            return task.Status.Equals(status, StringComparison.Ordinal);
        }

        private void EditHumanNotesForSelectedTask()
        {
            string taskId = GetSelectedTaskId();
            if (planningService is null || taskId.Length == 0)
            {
                return;
            }

            string notes = planningService.ReadHumanNotes(taskId);
            using (TaskInputDialog dialog = TaskInputDialog.CreateHumanNotesDialog(notes))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                planningService.SaveHumanNotes(taskId, dialog.BodyText);
                if (currentTask is not null && currentTask.TaskId.Equals(taskId, StringComparison.Ordinal))
                {
                    currentTask = planningService.GetTask(taskId);
                }

                RefreshPlanningStatus();
            }
        }

        private static string BuildDisabledPreview(MonitorSettings settings)
        {
            return $"""
                # Plan Board

                Planning is disabled for this watched solution.

                Watched solution:
                {settings.WatchedSolutionPath}

                Safe edit workflow is unchanged. Enable Planning in config to initialize task memory.
                """;
        }

        private static string BuildMisconfiguredPreview(MonitorSettings settings)
        {
            return $$"""
                # Plan Board

                Planning is enabled but not configured.

                Watched solution:
                {{settings.WatchedSolutionPath}}

                Required config:

                "Planning": {
                  "Enabled": true,
                  "DatabasePath": "...",
                  "TaskMemoryRoot": "..."
                }
                """;
        }
    }
}
