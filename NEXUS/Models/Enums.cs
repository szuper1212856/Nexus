namespace NEXUS.Models
{
    /// <summary>Ordered by severity so sorting ascending puts Critical first.</summary>
    public enum TaskPriority
    {
        Critical = 0,
        High = 1,
        Normal = 2,
        Low = 3
    }

    /// <summary>Named TaskState rather than TaskStatus to avoid clashing with System.Threading.Tasks.</summary>
    public enum TaskState
    {
        Planned = 0,
        InProgress = 1,
        Blocked = 2,
        Completed = 3
    }

    public enum ActivityKind
    {
        System = 0,
        Created = 1,
        Completed = 2,
        Reopened = 3,
        Edited = 4,
        Deleted = 5,
        Priority = 6,
        Status = 7,
        Focus = 8,
        Data = 9
    }
}
