namespace AIMonitor.Planning
{
    public sealed class PlanningEvidenceAttachmentResult
    {
        public bool Attached { get; set; }

        public string TaskId { get; set; } = string.Empty;

        public string TaskTitle { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string TaskMemoryMarkdownPath { get; set; } = string.Empty;

        public bool IsError { get; set; }
    }
}
