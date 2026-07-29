namespace SeanShell.Core;

public enum TaskbarMiddleClickAction
{
    None,
    Open,
    Choose,
}

public static class TaskbarMiddleClickResolver
{
    public static TaskbarMiddleClickAction Resolve(int applicationCandidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(applicationCandidateCount);
        return applicationCandidateCount switch
        {
            0 => TaskbarMiddleClickAction.None,
            1 => TaskbarMiddleClickAction.Open,
            _ => TaskbarMiddleClickAction.Choose,
        };
    }
}
