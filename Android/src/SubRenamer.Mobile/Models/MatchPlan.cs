namespace SubRenamer.Mobile.Models;

public enum PlanItemStatus
{
    Ready,
    ExistingDestination,
    AmbiguousSource,
    Unmatched,
    Failed,
    Applied,
}

public sealed record MatchPlanItem(
    string Key,
    string VideoName,
    string SourceKey,
    string SourceDisplayName,
    string DestinationName,
    PlanItemStatus Status,
    string? Message = null);

public sealed record MatchPlan(
    SubtitleSource Source,
    VideoTarget Target,
    IReadOnlyList<MatchPlanItem> Items,
    IReadOnlyList<string> Diagnostics)
{
    public int ReadyCount => Items.Count(x => x.Status == PlanItemStatus.Ready);
    public int ConflictCount => Items.Count(x => x.Status == PlanItemStatus.ExistingDestination || x.Status == PlanItemStatus.AmbiguousSource);
}
