namespace AIMonitor.Planning.Tests
{
    public sealed class PlanningServiceTests
    {
        [Fact]
        public void CreateTask_creates_backlog_task_and_memory_file_with_memory_sections()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);

                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Plan Board foundation",
                    Goal = "Create planning storage.",
                    HumanContext = "Operator context survives restarts.",
                    Constraints = "Only one task is Current.",
                    AcceptanceCriteria = "- Create database\n- Create memory file"
                });

                Assert.Equal(PlanningTaskStatus.Backlog, task.Status);
                Assert.True(File.Exists(task.TaskMemoryMarkdownPath));

                string markdown = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("## Human Notes + Status Updates", markdown, StringComparison.Ordinal);
                Assert.Contains("<!-- HUMAN:BEGIN notes -->", markdown, StringComparison.Ordinal);
                Assert.Contains("<!-- SYSTEM:BEGIN status-updates -->", markdown, StringComparison.Ordinal);
                Assert.Contains("Operator context survives restarts.", markdown, StringComparison.Ordinal);
                Assert.Contains("<!-- TASK-GIT:BEGIN -->", markdown, StringComparison.Ordinal);
                Assert.Contains("<!-- AI:BEGIN decisions -->", markdown, StringComparison.Ordinal);
                Assert.Contains("<!-- AI:BEGIN notes -->", markdown, StringComparison.Ordinal);
                Assert.Contains("<!-- HUMAN:BEGIN closure -->", markdown, StringComparison.Ordinal);
                Assert.Contains("Only one task is Current.", markdown, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void MakeCurrent_rejects_switch_when_another_task_is_current()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow first = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "First task",
                    Goal = "Prove first task switching behavior."
                });
                PlanningTaskRow second = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Second task",
                    Goal = "Prove second task switching behavior."
                });

                service.MakeCurrent(first.TaskId, "Initial current task.");
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => service.MakeCurrent(second.TaskId, "Attempted switch while first task is current."));

                IReadOnlyList<PlanningTaskRow> tasks = service.ListTasks();
                Assert.Contains("already Current", exception.Message, StringComparison.Ordinal);
                Assert.Single(tasks, task => task.Status == PlanningTaskStatus.Current);
                Assert.Equal(PlanningTaskStatus.Current, tasks.Single(task => task.TaskId == first.TaskId).Status);
                Assert.Equal(PlanningTaskStatus.Backlog, tasks.Single(task => task.TaskId == second.TaskId).Status);
            }
        }

        [Fact]
        public void MakeCurrent_allows_reselecting_existing_current_task()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow first = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "First task",
                    Goal = "Track human note writes when current is reselected."
                });

                service.MakeCurrent(first.TaskId, "Initial current task.");
                service.MakeCurrent(first.TaskId, "Still current after operator review.");

                string firstMemory = File.ReadAllText(first.TaskMemoryMarkdownPath);
                Assert.Contains("Still current after operator review.", firstMemory, StringComparison.Ordinal);
                Assert.Contains("Moved task to Current", firstMemory, StringComparison.Ordinal);
                Assert.Contains("<!-- HUMAN:BEGIN notes -->", firstMemory, StringComparison.Ordinal);
                Assert.Contains("<!-- SYSTEM:BEGIN status-updates -->", firstMemory, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ChangeStatus_moves_current_task_out_of_current_and_records_status_note()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Pause current work",
                    Goal = "Record the paused state."
                });

                service.MakeCurrent(task.TaskId, "Initial current task.");
                PlanningTaskRow paused = service.ChangeStatus(task.TaskId, PlanningTaskStatus.Paused, "Paused after smoke testing.");

                Assert.Equal(PlanningTaskStatus.Paused, paused.Status);
                Assert.Null(service.GetCurrentTask());

                string markdown = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("Paused after smoke testing.", markdown, StringComparison.Ordinal);
                Assert.Contains("Moved task to Paused", markdown, StringComparison.Ordinal);
                Assert.Matches(@"### \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} : Moved task to Paused\s+Paused after smoke testing\.", markdown);
            }
        }

        [Fact]
        public void Human_note_editing_does_not_expose_or_overwrite_status_updates()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Separate editable notes",
                    Goal = "Keep generated status updates out of the human editor.",
                    HumanContext = "Initial manual note."
                });

                service.ChangeStatus(task.TaskId, PlanningTaskStatus.Ready, "Ready after operator review.");

                string editableNotes = service.ReadHumanNotes(task.TaskId);
                Assert.Contains("Initial manual note.", editableNotes, StringComparison.Ordinal);
                Assert.DoesNotContain("Moved task to Ready", editableNotes, StringComparison.Ordinal);
                Assert.DoesNotContain("Ready after operator review.", editableNotes, StringComparison.Ordinal);

                service.SaveHumanNotes(task.TaskId, "Edited manual note.");

                string markdown = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("Edited manual note.", markdown, StringComparison.Ordinal);
                Assert.Contains("Moved task to Ready", markdown, StringComparison.Ordinal);
                Assert.Contains("Ready after operator review.", markdown, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Task_transitions_require_status_notes()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Require transition notes",
                    Goal = "Keep human intent history for every state move."
                });

                Assert.Throws<ArgumentException>(() => service.MakeCurrent(task.TaskId, string.Empty));
                Assert.Throws<ArgumentException>(() => service.ChangeStatus(task.TaskId, PlanningTaskStatus.Ready, string.Empty));
            }
        }

        [Fact]
        public void ChangeStatus_closes_closed_task_and_clears_closed_timestamp_when_reopened()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Close and reopen work",
                    Goal = "Track closure state."
                });

                PlanningTaskRow closed = service.ChangeStatus(task.TaskId, PlanningTaskStatus.Closed, "Closed after operator review.");
                Assert.Equal(PlanningTaskStatus.Closed, closed.Status);
                Assert.False(string.IsNullOrWhiteSpace(closed.ClosedAtUtc));

                PlanningTaskRow ready = service.ChangeStatus(task.TaskId, PlanningTaskStatus.Ready, "Reopened after later app use.");
                Assert.Equal(PlanningTaskStatus.Ready, ready.Status);
                Assert.Equal(string.Empty, ready.ClosedAtUtc);
            }
        }

        [Fact]
        public void ChangeStatus_cancels_task_as_terminal_state()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Cancel work",
                    Goal = "Track stopped work."
                });

                PlanningTaskRow canceled = service.ChangeStatus(task.TaskId, PlanningTaskStatus.Canceled, "Not going to happen.");

                Assert.Equal(PlanningTaskStatus.Canceled, canceled.Status);
                Assert.False(string.IsNullOrWhiteSpace(canceled.ClosedAtUtc));

                string markdown = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("Moved task to Canceled", markdown, StringComparison.Ordinal);
                Assert.Contains("Not going to happen.", markdown, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void GetCurrentTaskContext_excludes_human_notes()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Current context",
                    Goal = "Expose AI-safe task context.",
                    HumanContext = "Private operator note.",
                    Constraints = "Only expose curated fields.",
                    AcceptanceCriteria = "Context omits human notes."
                });

                service.MakeCurrent(task.TaskId, "Start task.");

                CurrentTaskContext context = service.GetCurrentTaskContext();

                Assert.True(context.HasCurrentTask);
                Assert.Equal(task.TaskId, context.TaskId);
                Assert.Equal("Expose AI-safe task context.", context.Goal);
                Assert.Equal("Only expose curated fields.", context.Constraints);
                Assert.DoesNotContain("Private operator note.", context.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("Private operator note.", context.ReviewEvidenceSummary, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void AppendIterationGoalToCurrentTask_adds_one_normalized_iteration_row()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Iteration task",
                    Goal = "Initial goal."
                });
                service.MakeCurrent(task.TaskId, "Start task.");

                PlanningIterationAppendResult result = service.AppendIterationGoalToCurrentTask(" run a focused\r\niteration test ");

                Assert.True(result.Appended);
                Assert.Equal("run a focused iteration test", result.IterationGoal);
                Assert.NotNull(result.Iteration);
                Assert.Equal(1, result.Iteration.Sequence);
                Assert.Equal("open", result.Iteration.Status);
                CurrentTaskContext context = service.GetCurrentTaskContext();
                Assert.Equal("Initial goal.", context.Goal);
                Assert.NotNull(context.CurrentIteration);
                Assert.Equal(result.Iteration.IterationId, context.CurrentIteration.IterationId);
                Assert.Equal("run a focused iteration test", context.CurrentIterationGoal);
                Assert.Contains("#1 [open] run a focused iteration test", context.IterationSummary, StringComparison.Ordinal);
                string markdown = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("run a focused iteration test", markdown, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void AppendIterationGoalToCurrentTask_reports_when_no_task_is_current()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);

                PlanningIterationAppendResult result = service.AppendIterationGoalToCurrentTask("Run next test.");

                Assert.False(result.Appended);
                Assert.Contains("No Current task", result.Message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void AttachWorkflowDecisionToCurrentTask_records_decision_and_refreshes_task_memory()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Evidence task",
                    Goal = "Attach reviewed workflow evidence."
                });
                service.MakeCurrent(task.TaskId, "Start task.");

                PlanningEvidenceAttachmentResult result = service.AttachWorkflowDecisionToCurrentTask(new PlanningDecisionEvidence
                {
                    StagedRecordId = "stage-1",
                    SessionId = "session-1",
                    RelativePath = "Repositories/SchemaMCPRepository.cs",
                    StagedHash = "abc123",
                    Decision = "accepted",
                    Classification = "accepted",
                    Status = "accepted",
                    Message = "Accepted.",
                    LedgerSummary = "Add repository method so task execution can fetch selected schema rows.",
                    DecidedAtUtc = "2026-06-07T15:30:00.0000000Z",
                    PreMergeValidationStatus = "passed",
                    IndexRefreshStatus = "completed"
                });

                Assert.True(result.Attached);
                Assert.Equal(task.TaskId, result.TaskId);

                CurrentTaskContext context = service.GetCurrentTaskContext();
                Assert.Contains("Repositories/SchemaMCPRepository.cs", context.ReviewEvidenceSummary, StringComparison.Ordinal);
                Assert.Contains("stage-1", context.ReviewEvidenceSummary, StringComparison.Ordinal);
                string selectedTaskSummary = service.GetTaskReviewEvidenceSummary(task.TaskId);
                Assert.Contains("Repositories/SchemaMCPRepository.cs", selectedTaskSummary, StringComparison.Ordinal);
                Assert.Contains("stage-1", selectedTaskSummary, StringComparison.Ordinal);
                Assert.Contains("Add repository method", selectedTaskSummary, StringComparison.Ordinal);

                string markdown = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("Repositories/SchemaMCPRepository.cs", markdown, StringComparison.Ordinal);
                Assert.Contains("accepted / accepted", markdown, StringComparison.Ordinal);
                Assert.Contains("Add repository method", markdown, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RefreshTaskMemory_preserves_human_and_ai_note_sections_while_syncing_review_evidence()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);
                PlanningTaskRow task = service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Memory preservation",
                    Goal = "Preserve editable memory sections.",
                    HumanContext = "Original human context.",
                    Constraints = "Original constraint."
                });

                string edited = File.ReadAllText(task.TaskMemoryMarkdownPath)
                    .Replace("Original human context.", "Human edited context.", StringComparison.Ordinal)
                    .Replace("<!-- AI:BEGIN decisions -->", "<!-- AI:BEGIN decisions -->\n- Chose runtime task memory for v1.", StringComparison.Ordinal)
                    .Replace("Task created. No agent notes recorded yet.", "Agent resume note.", StringComparison.Ordinal)
                    .Replace("<!-- HUMAN:BEGIN closure -->", "<!-- HUMAN:BEGIN closure -->\nHuman closure draft.", StringComparison.Ordinal);
                File.WriteAllText(task.TaskMemoryMarkdownPath, edited);

                service.RefreshTaskMemory(task.TaskId, "Reviewed file: src/Example.cs");

                string refreshed = File.ReadAllText(task.TaskMemoryMarkdownPath);
                Assert.Contains("Human edited context.", refreshed, StringComparison.Ordinal);
                Assert.Contains("Chose runtime task memory for v1.", refreshed, StringComparison.Ordinal);
                Assert.Contains("Agent resume note.", refreshed, StringComparison.Ordinal);
                Assert.Contains("Human closure draft.", refreshed, StringComparison.Ordinal);
                Assert.Contains("Reviewed file: src/Example.cs", refreshed, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CreateTask_requires_title_and_goal()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningService service = new PlanningService(workspace.Settings);

                Assert.Throws<ArgumentException>(() => service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = string.Empty,
                    Goal = "Goal exists."
                }));
                Assert.Throws<ArgumentException>(() => service.CreateTask(new CreatePlanningTaskRequest
                {
                    Title = "Title exists.",
                    Goal = string.Empty
                }));
            }
        }
    }
}
