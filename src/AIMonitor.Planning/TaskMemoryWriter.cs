using System.Globalization;

namespace AIMonitor.Planning
{
    public sealed class TaskMemoryWriter
    {
        private const string HumanNotesBegin = "<!-- HUMAN:BEGIN notes -->";
        private const string HumanNotesEnd = "<!-- HUMAN:END notes -->";
        private const string StatusUpdatesBegin = "<!-- SYSTEM:BEGIN status-updates -->";
        private const string StatusUpdatesEnd = "<!-- SYSTEM:END status-updates -->";
        private const string HumanClosureBegin = "<!-- HUMAN:BEGIN closure -->";
        private const string HumanClosureEnd = "<!-- HUMAN:END closure -->";
        private const string TaskGitBegin = "<!-- TASK-GIT:BEGIN -->";
        private const string TaskGitEnd = "<!-- TASK-GIT:END -->";
        private const string AiDecisionsBegin = "<!-- AI:BEGIN decisions -->";
        private const string AiDecisionsEnd = "<!-- AI:END decisions -->";
        private const string AiNotesBegin = "<!-- AI:BEGIN notes -->";
        private const string AiNotesEnd = "<!-- AI:END notes -->";

        public void Refresh(
            PlanningTaskRow task,
            string reviewEvidenceSummary,
            string initialAiNotes = "No agent notes recorded.")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(task.TaskMemoryMarkdownPath) ?? ".");

            TaskMemorySections sections = ReadSections(task, initialAiNotes);
            File.WriteAllText(task.TaskMemoryMarkdownPath, BuildMarkdown(task, sections, reviewEvidenceSummary));
        }

        public void AppendHumanStatusNote(
            PlanningTaskRow task,
            string statusDescription,
            string comment,
            string createdAtUtc,
            string reviewEvidenceSummary)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(task.TaskMemoryMarkdownPath) ?? ".");

            TaskMemorySections sections = ReadSections(task, "No agent notes recorded.");
            string trimmedComment = comment.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedComment))
            {
                string appended = $"""
                    ### {FormatStatusTimestamp(createdAtUtc)} : {statusDescription.Trim()}

                    {trimmedComment}
                    """;
                sections.StatusUpdates = string.IsNullOrWhiteSpace(sections.StatusUpdates)
                    ? appended
                    : sections.StatusUpdates + Environment.NewLine + Environment.NewLine + appended;
            }

            File.WriteAllText(task.TaskMemoryMarkdownPath, BuildMarkdown(task, sections, reviewEvidenceSummary));
        }

        public string ReadHumanNotes(PlanningTaskRow task)
        {
            TaskMemorySections sections = ReadSections(task, string.Empty);
            return sections.HumanNotes;
        }

        public void SaveHumanNotes(
            PlanningTaskRow task,
            string humanNotes,
            string reviewEvidenceSummary)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(task.TaskMemoryMarkdownPath) ?? ".");

            TaskMemorySections sections = ReadSections(task, "No agent notes recorded.");
            sections.HumanNotes = humanNotes;
            File.WriteAllText(task.TaskMemoryMarkdownPath, BuildMarkdown(task, sections, reviewEvidenceSummary));
        }

        private static TaskMemorySections ReadSections(PlanningTaskRow task, string initialAiNotes)
        {
            TaskMemorySections sections = new TaskMemorySections
            {
                HumanNotes = task.HumanContext,
                StatusUpdates = string.Empty,
                HumanClosure = string.Empty,
                AiDecisions = string.Empty,
                AiNotes = initialAiNotes
            };

            if (File.Exists(task.TaskMemoryMarkdownPath))
            {
                string existing = File.ReadAllText(task.TaskMemoryMarkdownPath);
                sections.HumanNotes = ExtractSection(existing, HumanNotesBegin, HumanNotesEnd, sections.HumanNotes);
                sections.StatusUpdates = ExtractSection(existing, StatusUpdatesBegin, StatusUpdatesEnd, sections.StatusUpdates);
                if (string.IsNullOrWhiteSpace(sections.StatusUpdates))
                {
                    SplitLegacyHumanNotes(sections);
                }

                sections.HumanClosure = ExtractSection(existing, HumanClosureBegin, HumanClosureEnd, sections.HumanClosure);
                sections.AiDecisions = ExtractSection(existing, AiDecisionsBegin, AiDecisionsEnd, sections.AiDecisions);
                sections.AiNotes = ExtractSection(existing, AiNotesBegin, AiNotesEnd, sections.AiNotes);
            }

            return sections;
        }

        private static string BuildMarkdown(
            PlanningTaskRow task,
            TaskMemorySections sections,
            string reviewEvidenceSummary)
        {
            return $$"""
                # Task: {{task.Title}}

                | Field | Value |
                | --- | --- |
                | Task ID | `{{task.TaskId}}` |
                | Status | **{{task.Status}}** |

                ## Goal

                {{task.Goal}}

                ## Constraints

                {{task.Constraints}}

                ## Acceptance Criteria

                {{task.AcceptanceCriteria}}

                ## Human Notes + Status Updates

                ### Human Notes

                {{HumanNotesBegin}}
                {{sections.HumanNotes}}
                {{HumanNotesEnd}}

                ### Status Updates

                {{StatusUpdatesBegin}}
                {{sections.StatusUpdates}}
                {{StatusUpdatesEnd}}

                ## Review Evidence

                {{TaskGitBegin}}
                {{reviewEvidenceSummary}}
                {{TaskGitEnd}}

                ## AI Decision History

                {{AiDecisionsBegin}}
                {{sections.AiDecisions}}
                {{AiDecisionsEnd}}

                ## AI General Notes

                {{AiNotesBegin}}
                {{sections.AiNotes}}
                {{AiNotesEnd}}

                ## Closure

                {{HumanClosureBegin}}
                {{sections.HumanClosure}}
                {{HumanClosureEnd}}
                """;
        }

        private sealed class TaskMemorySections
        {
            public string HumanNotes { get; set; } = string.Empty;

            public string StatusUpdates { get; set; } = string.Empty;

            public string HumanClosure { get; set; } = string.Empty;

            public string AiDecisions { get; set; } = string.Empty;

            public string AiNotes { get; set; } = string.Empty;
        }

        private static string ExtractSection(string content, string beginMarker, string endMarker, string fallback)
        {
            int beginIndex = content.IndexOf(beginMarker, StringComparison.Ordinal);
            int endIndex = content.IndexOf(endMarker, StringComparison.Ordinal);
            if (beginIndex < 0 || endIndex < 0 || endIndex < beginIndex)
            {
                return fallback;
            }

            int contentStart = beginIndex + beginMarker.Length;
            return content.Substring(contentStart, endIndex - contentStart).Trim();
        }

        private static void SplitLegacyHumanNotes(TaskMemorySections sections)
        {
            string humanNotes = sections.HumanNotes;
            int statusStart = humanNotes.IndexOf("### ", StringComparison.Ordinal);
            if (statusStart < 0)
            {
                return;
            }

            string possibleStatusUpdates = humanNotes.Substring(statusStart).Trim();
            if (!possibleStatusUpdates.Contains("Moved task to ", StringComparison.Ordinal))
            {
                return;
            }

            sections.HumanNotes = humanNotes.Substring(0, statusStart).Trim();
            sections.StatusUpdates = possibleStatusUpdates;
        }

        private static string FormatStatusTimestamp(string createdAtUtc)
        {
            if (DateTimeOffset.TryParse(createdAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
            {
                return timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return createdAtUtc;
        }
    }
}
