using AIMonitor.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AIMonitor.Planning
{
    public sealed class PlanningService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly MonitorSettings settings;
        private readonly PlanningDatabase database;
        private readonly TaskMemoryWriter taskMemoryWriter;
        private readonly string taskMemoryRoot;

        public PlanningService(MonitorSettings settings)
            : this(
                settings,
                new PlanningDatabase(settings),
                new TaskMemoryWriter(),
                PlanningPaths.GetDefaultTaskMemoryRoot(settings))
        {
        }

        public PlanningService(
            MonitorSettings settings,
            PlanningDatabase database,
            TaskMemoryWriter taskMemoryWriter,
            string? taskMemoryRoot = null)
        {
            this.settings = settings;
            this.database = database;
            this.taskMemoryWriter = taskMemoryWriter;
            this.taskMemoryRoot = string.IsNullOrWhiteSpace(taskMemoryRoot)
                ? PlanningPaths.GetDefaultTaskMemoryRoot(settings)
                : Path.GetFullPath(taskMemoryRoot);
        }

        public void Initialize()
        {
            database.EnsureCreated();
        }

        public PlanningTaskRow CreateTask(CreatePlanningTaskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException("Task title is required.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                throw new ArgumentException("Task goal is required.", nameof(request));
            }

            database.EnsureCreated();

            string taskId = "task-" + Guid.NewGuid().ToString("N");
            string now = DateTimeOffset.UtcNow.ToString("O");
            PlanningTaskRow task = new PlanningTaskRow
            {
                TaskId = taskId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Goal = request.Goal,
                HumanContext = request.HumanContext,
                Constraints = request.Constraints,
                AcceptanceCriteria = request.AcceptanceCriteria,
                Status = PlanningTaskStatus.Backlog,
                TaskMemoryMarkdownPath = GetTaskMemoryPath(taskId, request.Title),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        insert into tasks (
                            task_id,
                            title,
                            description,
                            goal,
                            human_context,
                            constraints_text,
                            acceptance_criteria,
                            status,
                            task_memory_md_path,
                            created_at_utc,
                            updated_at_utc
                        )
                        values (
                            $task_id,
                            $title,
                            $description,
                            $goal,
                            $human_context,
                            $constraints_text,
                            $acceptance_criteria,
                            $status,
                            $task_memory_md_path,
                            $created_at_utc,
                            $updated_at_utc
                        );
                        """;
                    AddTaskParameters(command, task);
                    command.ExecuteNonQuery();
                }
            }

            taskMemoryWriter.Refresh(
                task,
                "No reviewed workflow evidence has been attached yet.",
                "Task created. No agent notes recorded yet.");
            return task;
        }

        public PlanningTaskRow UpdateTask(string taskId, CreatePlanningTaskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException("Task title is required.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                throw new ArgumentException("Task goal is required.", nameof(request));
            }

            database.EnsureCreated();
            PlanningTaskRow task = GetTask(taskId);
            string now = DateTimeOffset.UtcNow.ToString("O");
            task.Title = request.Title.Trim();
            task.Description = request.Description;
            task.Goal = request.Goal;
            task.HumanContext = request.HumanContext;
            task.Constraints = request.Constraints;
            task.AcceptanceCriteria = request.AcceptanceCriteria;
            task.UpdatedAtUtc = now;

            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        update tasks
                        set title = $title,
                            description = $description,
                            goal = $goal,
                            human_context = $human_context,
                            constraints_text = $constraints_text,
                            acceptance_criteria = $acceptance_criteria,
                            updated_at_utc = $updated_at_utc
                        where task_id = $task_id;
                        """;
                    command.Parameters.AddWithValue("$title", task.Title);
                    command.Parameters.AddWithValue("$description", task.Description);
                    command.Parameters.AddWithValue("$goal", task.Goal);
                    command.Parameters.AddWithValue("$human_context", task.HumanContext);
                    command.Parameters.AddWithValue("$constraints_text", task.Constraints);
                    command.Parameters.AddWithValue("$acceptance_criteria", task.AcceptanceCriteria);
                    command.Parameters.AddWithValue("$updated_at_utc", task.UpdatedAtUtc);
                    command.Parameters.AddWithValue("$task_id", task.TaskId);
                    command.ExecuteNonQuery();
                }
            }

            taskMemoryWriter.SaveHumanNotes(
                task,
                task.HumanContext,
                "Review evidence is synced from accepted/rejected workflow records.");
            return task;
        }

        public IReadOnlyList<PlanningTaskRow> ListTasks()
        {
            database.EnsureCreated();
            List<PlanningTaskRow> tasks = new List<PlanningTaskRow>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        select
                            task_id,
                            title,
                            description,
                            goal,
                            human_context,
                            constraints_text,
                            acceptance_criteria,
                            status,
                            task_memory_md_path,
                            created_at_utc,
                            updated_at_utc,
                            closed_at_utc
                        from tasks
                        order by created_at_utc, task_id;
                        """;

                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tasks.Add(ReadTask(reader));
                        }
                    }
                }
            }

            return tasks;
        }

        public PlanningTaskRow GetTask(string taskId)
        {
            database.EnsureCreated();
            using (SqliteConnection connection = database.OpenConnection())
            {
                PlanningTaskRow? task = TryGetTask(connection, taskId);
                if (task is null)
                {
                    throw new InvalidOperationException($"Planning task was not found: {taskId}");
                }

                return task;
            }
        }

        public PlanningTaskRow? GetCurrentTask()
        {
            database.EnsureCreated();
            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    string activeTaskId = GetActiveTaskId(connection, transaction);
                    PlanningTaskRow? task = string.IsNullOrWhiteSpace(activeTaskId)
                        ? null
                        : TryGetTask(connection, transaction, activeTaskId);
                    transaction.Commit();
                    return task;
                }
            }
        }

        public CurrentTaskContext GetCurrentTaskContext()
        {
            PlanningTaskRow? task = GetCurrentTask();
            if (task is null)
            {
                return new CurrentTaskContext
                {
                    HasCurrentTask = false,
                    Message = "No Current task is selected. Workflow evidence will not attach to task memory until the operator makes a task Current."
                };
            }

            return new CurrentTaskContext
            {
                HasCurrentTask = true,
                TaskId = task.TaskId,
                Title = task.Title,
                Status = task.Status,
                Goal = task.Goal,
                Constraints = task.Constraints,
                AcceptanceCriteria = task.AcceptanceCriteria,
                ReviewEvidenceSummary = BuildReviewEvidenceSummary(task.TaskId),
                TaskMemoryMarkdownPath = task.TaskMemoryMarkdownPath,
                Message = "Human notes are intentionally excluded from the AI-facing Current task context."
            };
        }

        public PlanningTaskRow MakeCurrent(string taskId, string switchContextSummary)
        {
            if (string.IsNullOrWhiteSpace(switchContextSummary))
            {
                throw new ArgumentException("A status note is required.", nameof(switchContextSummary));
            }

            database.EnsureCreated();
            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    PlanningTaskRow? nextTask = TryGetTask(connection, transaction, taskId);
                    if (nextTask is null)
                    {
                        throw new InvalidOperationException($"Planning task was not found: {taskId}");
                    }

                    string previousTaskId = GetActiveTaskId(connection, transaction);
                    string now = DateTimeOffset.UtcNow.ToString("O");
                    if (!string.IsNullOrWhiteSpace(previousTaskId) && previousTaskId != taskId)
                    {
                        throw new InvalidOperationException("A task is already Current. Move or close the Current task explicitly before making another task Current.");
                    }

                    UpdateTaskStatus(connection, transaction, taskId, PlanningTaskStatus.Current, now);
                    SetActiveTaskId(connection, transaction, taskId, now);
                    InsertEvent(connection, transaction, taskId, "made-current", "Task is now Current.", now);
                    transaction.Commit();

                    nextTask.Status = PlanningTaskStatus.Current;
                    nextTask.UpdatedAtUtc = now;
                    taskMemoryWriter.Refresh(
                        nextTask,
                        "Review evidence is synced from accepted/rejected workflow records.",
                        "Task is Current. Continue from the latest human notes and review evidence.");
                    taskMemoryWriter.AppendHumanStatusNote(
                        nextTask,
                        $"Moved task to {PlanningTaskStatus.Current}",
                        switchContextSummary,
                        now,
                        "Review evidence is synced from accepted/rejected workflow records.");
                    return nextTask;
                }
            }
        }

        public PlanningTaskRow ChangeStatus(string taskId, string status, string statusNote)
        {
            if (string.IsNullOrWhiteSpace(statusNote))
            {
                throw new ArgumentException("A status note is required.", nameof(statusNote));
            }

            if (!PlanningTaskStatus.IsKnown(status))
            {
                throw new ArgumentException($"Unknown planning task status: {status}", nameof(status));
            }

            if (status.Equals(PlanningTaskStatus.Current, StringComparison.Ordinal))
            {
                return MakeCurrent(taskId, statusNote);
            }

            database.EnsureCreated();
            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    PlanningTaskRow? task = TryGetTask(connection, transaction, taskId);
                    if (task is null)
                    {
                        throw new InvalidOperationException($"Planning task was not found: {taskId}");
                    }

                    string now = DateTimeOffset.UtcNow.ToString("O");
                    string activeTaskId = GetActiveTaskId(connection, transaction);
                    UpdateTaskStatus(connection, transaction, taskId, status, now);
                    if (PlanningTaskStatus.IsTerminal(status))
                    {
                        UpdateClosedAt(connection, transaction, taskId, now);
                    }
                    else
                    {
                        UpdateClosedAt(connection, transaction, taskId, string.Empty);
                    }

                    if (activeTaskId.Equals(taskId, StringComparison.Ordinal))
                    {
                        SetActiveTaskId(connection, transaction, string.Empty, now);
                    }

                    InsertEvent(connection, transaction, taskId, "status-changed", $"Task moved to {status}. {statusNote}".Trim(), now);
                    transaction.Commit();

                    task.Status = status;
                    task.UpdatedAtUtc = now;
                    if (PlanningTaskStatus.IsTerminal(status))
                    {
                        task.ClosedAtUtc = now;
                    }
                    else
                    {
                        task.ClosedAtUtc = string.Empty;
                    }

                    taskMemoryWriter.AppendHumanStatusNote(
                        task,
                        $"Moved task to {status}",
                        statusNote,
                        now,
                        "Review evidence is synced from accepted/rejected workflow records.");
                    return task;
                }
            }
        }

        public void RefreshTaskMemory(string taskId, string currentStateSummary)
        {
            PlanningTaskRow task = GetTask(taskId);
            taskMemoryWriter.Refresh(task, currentStateSummary);
        }

        public PlanningEvidenceAttachmentResult AttachWorkflowDecisionToCurrentTask(PlanningDecisionEvidence evidence)
        {
            database.EnsureCreated();
            PlanningTaskRow? currentTask = GetCurrentTask();
            if (currentTask is null)
            {
                return new PlanningEvidenceAttachmentResult
                {
                    Attached = false,
                    Message = "No Current task exists. Workflow decision was recorded but not attached to task memory."
                };
            }

            try
            {
                using (SqliteConnection connection = database.OpenConnection())
                {
                    using (SqliteTransaction transaction = connection.BeginTransaction())
                    {
                        InsertTaskStagedRecord(connection, transaction, currentTask.TaskId, evidence);
                        InsertTaskDecision(connection, transaction, currentTask.TaskId, evidence);
                        InsertEvent(
                            connection,
                            transaction,
                            currentTask.TaskId,
                            "workflow-decision",
                            CreateDecisionSummary(evidence),
                            GetEvidenceTimestamp(evidence),
                            JsonSerializer.Serialize(evidence, JsonOptions));
                        transaction.Commit();
                    }
                }

                string reviewEvidenceSummary = BuildReviewEvidenceSummary(currentTask.TaskId);
                taskMemoryWriter.Refresh(currentTask, reviewEvidenceSummary);
                return new PlanningEvidenceAttachmentResult
                {
                    Attached = true,
                    TaskId = currentTask.TaskId,
                    TaskTitle = currentTask.Title,
                    TaskMemoryMarkdownPath = currentTask.TaskMemoryMarkdownPath,
                    Message = "Workflow decision attached to the Current task."
                };
            }
            catch (Exception ex)
            {
                return new PlanningEvidenceAttachmentResult
                {
                    Attached = false,
                    TaskId = currentTask.TaskId,
                    TaskTitle = currentTask.Title,
                    TaskMemoryMarkdownPath = currentTask.TaskMemoryMarkdownPath,
                    IsError = true,
                    Message = "Planning evidence attachment failed after workflow decision was recorded: " + ex.Message
                };
            }
        }

        public string ReadHumanNotes(string taskId)
        {
            PlanningTaskRow task = GetTask(taskId);
            return taskMemoryWriter.ReadHumanNotes(task);
        }

        public void SaveHumanNotes(string taskId, string humanNotes)
        {
            PlanningTaskRow task = GetTask(taskId);
            string now = DateTimeOffset.UtcNow.ToString("O");
            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        update tasks
                        set human_context = $human_context,
                            updated_at_utc = $updated_at_utc
                        where task_id = $task_id;
                        """;
                    command.Parameters.AddWithValue("$human_context", humanNotes);
                    command.Parameters.AddWithValue("$updated_at_utc", now);
                    command.Parameters.AddWithValue("$task_id", taskId);
                    command.ExecuteNonQuery();
                }
            }

            task.HumanContext = humanNotes;
            task.UpdatedAtUtc = now;
            taskMemoryWriter.SaveHumanNotes(
                task,
                humanNotes,
                "Review evidence is synced from accepted/rejected workflow records.");
        }

        private string GetTaskMemoryPath(string taskId, string title)
        {
            string slug = Slug(title);
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(
                taskMemoryRoot,
                $"{slug}-{timestamp}.md");
        }

        private static string Slug(string title)
        {
            char[] characters = title.Trim().ToLowerInvariant().ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                char character = characters[index];
                if (!char.IsLetterOrDigit(character))
                {
                    characters[index] = '-';
                }
            }

            string slug = new string(characters).Trim('-');
            while (slug.Contains("--", StringComparison.Ordinal))
            {
                slug = slug.Replace("--", "-", StringComparison.Ordinal);
            }

            if (string.IsNullOrWhiteSpace(slug))
            {
                return "task";
            }

            return slug;
        }

        private static void AddTaskParameters(SqliteCommand command, PlanningTaskRow task)
        {
            command.Parameters.AddWithValue("$task_id", task.TaskId);
            command.Parameters.AddWithValue("$title", task.Title);
            command.Parameters.AddWithValue("$description", task.Description);
            command.Parameters.AddWithValue("$goal", task.Goal);
            command.Parameters.AddWithValue("$human_context", task.HumanContext);
            command.Parameters.AddWithValue("$constraints_text", task.Constraints);
            command.Parameters.AddWithValue("$acceptance_criteria", task.AcceptanceCriteria);
            command.Parameters.AddWithValue("$status", task.Status);
            command.Parameters.AddWithValue("$task_memory_md_path", task.TaskMemoryMarkdownPath);
            command.Parameters.AddWithValue("$created_at_utc", task.CreatedAtUtc);
            command.Parameters.AddWithValue("$updated_at_utc", task.UpdatedAtUtc);
        }

        private static PlanningTaskRow? TryGetTask(SqliteConnection connection, string taskId)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = GetTaskSql();
                command.Parameters.AddWithValue("$task_id", taskId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadTask(reader) : null;
                }
            }
        }

        private static PlanningTaskRow? TryGetTask(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = GetTaskSql();
                command.Parameters.AddWithValue("$task_id", taskId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadTask(reader) : null;
                }
            }
        }

        private static string GetTaskSql()
        {
            return """
                select
                    task_id,
                    title,
                    description,
                    goal,
                    human_context,
                    constraints_text,
                    acceptance_criteria,
                    status,
                    task_memory_md_path,
                    created_at_utc,
                    updated_at_utc,
                    closed_at_utc
                from tasks
                where task_id = $task_id;
                """;
        }

        private static PlanningTaskRow ReadTask(SqliteDataReader reader)
        {
            return new PlanningTaskRow
            {
                TaskId = reader.GetString(0),
                Title = reader.GetString(1),
                Description = reader.GetString(2),
                Goal = reader.GetString(3),
                HumanContext = reader.GetString(4),
                Constraints = reader.GetString(5),
                AcceptanceCriteria = reader.GetString(6),
                Status = reader.GetString(7),
                TaskMemoryMarkdownPath = reader.GetString(8),
                CreatedAtUtc = reader.GetString(9),
                UpdatedAtUtc = reader.GetString(10),
                ClosedAtUtc = reader.GetString(11)
            };
        }

        private static string GetActiveTaskId(SqliteConnection connection, SqliteTransaction transaction)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "select active_task_id from board_state where id = 1;";
                object? result = command.ExecuteScalar();
                return result?.ToString() ?? string.Empty;
            }
        }

        private static void SetActiveTaskId(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            string updatedAtUtc)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    update board_state
                    set active_task_id = $task_id,
                        updated_at_utc = $updated_at_utc
                    where id = 1;
                    """;
                command.Parameters.AddWithValue("$task_id", taskId);
                command.Parameters.AddWithValue("$updated_at_utc", updatedAtUtc);
                command.ExecuteNonQuery();
            }
        }

        private static void UpdateClosedAt(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            string closedAtUtc)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    update tasks
                    set closed_at_utc = $closed_at_utc
                    where task_id = $task_id;
                    """;
                command.Parameters.AddWithValue("$closed_at_utc", closedAtUtc);
                command.Parameters.AddWithValue("$task_id", taskId);
                command.ExecuteNonQuery();
            }
        }

        private static void ClearCurrentTasks(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskIdToKeep,
            string updatedAtUtc)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    update tasks
                    set status = $paused,
                        updated_at_utc = $updated_at_utc
                    where status = $current
                      and task_id <> $task_id_to_keep;
                    """;
                command.Parameters.AddWithValue("$paused", PlanningTaskStatus.Paused);
                command.Parameters.AddWithValue("$current", PlanningTaskStatus.Current);
                command.Parameters.AddWithValue("$task_id_to_keep", taskIdToKeep);
                command.Parameters.AddWithValue("$updated_at_utc", updatedAtUtc);
                command.ExecuteNonQuery();
            }
        }

        private static void UpdateTaskStatus(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            string status,
            string updatedAtUtc)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    update tasks
                    set status = $status,
                        updated_at_utc = $updated_at_utc
                    where task_id = $task_id;
                    """;
                command.Parameters.AddWithValue("$status", status);
                command.Parameters.AddWithValue("$updated_at_utc", updatedAtUtc);
                command.Parameters.AddWithValue("$task_id", taskId);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertEvent(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            string eventType,
            string summary,
            string createdAtUtc)
        {
            InsertEvent(connection, transaction, taskId, eventType, summary, createdAtUtc, string.Empty);
        }

        private static void InsertEvent(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            string eventType,
            string summary,
            string createdAtUtc,
            string payloadJson)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    insert into task_events (
                        task_id,
                        event_type,
                        summary,
                        payload_json,
                        created_at_utc
                    )
                    values (
                        $task_id,
                        $event_type,
                        $summary,
                        $payload_json,
                        $created_at_utc
                    );
                    """;
                command.Parameters.AddWithValue("$task_id", taskId);
                command.Parameters.AddWithValue("$event_type", eventType);
                command.Parameters.AddWithValue("$summary", summary);
                command.Parameters.AddWithValue("$payload_json", payloadJson);
                command.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertTaskStagedRecord(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            PlanningDecisionEvidence evidence)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    insert or ignore into task_staged_records (
                        task_id,
                        staged_record_id,
                        session_id,
                        relative_path,
                        staged_hash,
                        status,
                        created_at_utc
                    )
                    values (
                        $task_id,
                        $staged_record_id,
                        $session_id,
                        $relative_path,
                        $staged_hash,
                        $status,
                        $created_at_utc
                    );
                    """;
                command.Parameters.AddWithValue("$task_id", taskId);
                command.Parameters.AddWithValue("$staged_record_id", evidence.StagedRecordId);
                command.Parameters.AddWithValue("$session_id", evidence.SessionId);
                command.Parameters.AddWithValue("$relative_path", evidence.RelativePath);
                command.Parameters.AddWithValue("$staged_hash", evidence.StagedHash);
                command.Parameters.AddWithValue("$status", evidence.Status);
                command.Parameters.AddWithValue("$created_at_utc", GetEvidenceTimestamp(evidence));
                command.ExecuteNonQuery();
            }
        }

        private static void InsertTaskDecision(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string taskId,
            PlanningDecisionEvidence evidence)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    insert into task_decisions (
                        task_id,
                        staged_record_id,
                        decision,
                        classification,
                        relative_path,
                        status,
                        message,
                        decided_at_utc,
                        payload_json
                    )
                    values (
                        $task_id,
                        $staged_record_id,
                        $decision,
                        $classification,
                        $relative_path,
                        $status,
                        $message,
                        $decided_at_utc,
                        $payload_json
                    );
                    """;
                command.Parameters.AddWithValue("$task_id", taskId);
                command.Parameters.AddWithValue("$staged_record_id", evidence.StagedRecordId);
                command.Parameters.AddWithValue("$decision", evidence.Decision);
                command.Parameters.AddWithValue("$classification", evidence.Classification);
                command.Parameters.AddWithValue("$relative_path", evidence.RelativePath);
                command.Parameters.AddWithValue("$status", evidence.Status);
                command.Parameters.AddWithValue("$message", evidence.Message);
                command.Parameters.AddWithValue("$decided_at_utc", GetEvidenceTimestamp(evidence));
                command.Parameters.AddWithValue("$payload_json", JsonSerializer.Serialize(evidence, JsonOptions));
                command.ExecuteNonQuery();
            }
        }

        private string BuildReviewEvidenceSummary(string taskId)
        {
            database.EnsureCreated();
            List<string> lines = new List<string>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        select decided_at_utc, relative_path, decision, classification, staged_record_id, status
                        from task_decisions
                        where task_id = $task_id
                        order by decided_at_utc, id;
                        """;
                    command.Parameters.AddWithValue("$task_id", taskId);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lines.Add(
                                $"- {reader.GetString(0)}: {reader.GetString(2)} / {reader.GetString(3)} `{reader.GetString(1)}` ({reader.GetString(5)}, staged `{reader.GetString(4)}`)");
                        }
                    }
                }
            }

            return lines.Count == 0
                ? "No reviewed workflow evidence has been attached yet."
                : string.Join(Environment.NewLine, lines);
        }

        private static string CreateDecisionSummary(PlanningDecisionEvidence evidence)
        {
            return $"Workflow decision {evidence.Decision}/{evidence.Classification} for {evidence.RelativePath} ({evidence.StagedRecordId}).";
        }

        private static string GetEvidenceTimestamp(PlanningDecisionEvidence evidence)
        {
            return string.IsNullOrWhiteSpace(evidence.DecidedAtUtc)
                ? DateTimeOffset.UtcNow.ToString("O")
                : evidence.DecidedAtUtc;
        }
    }
}
