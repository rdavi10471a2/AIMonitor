using System.ComponentModel.DataAnnotations;

namespace AIMonitor.Planning
{
    public sealed class CreatePlanningTaskRequest
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required]
        public string Goal { get; set; } = string.Empty;

        public string HumanContext { get; set; } = string.Empty;

        public string Constraints { get; set; } = string.Empty;

        public string AcceptanceCriteria { get; set; } = string.Empty;
    }
}
