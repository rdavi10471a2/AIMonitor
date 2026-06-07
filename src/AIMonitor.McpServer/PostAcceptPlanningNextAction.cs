namespace AIMonitor.McpServer;

public enum PostAcceptPlanningNextAction
{
    Stop,
    KeepCurrentIterationOpen,
    CompleteCurrentIteration,
    AppendNextIteration,
    ReplaceCurrentIteration,
    PauseTask,
    CloseTask,
    CancelTask
}
