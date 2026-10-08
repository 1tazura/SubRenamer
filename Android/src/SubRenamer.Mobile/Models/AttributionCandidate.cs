namespace SubRenamer.Mobile.Models;

public sealed record AttributionCandidate(
    VideoTarget Target,
    double Score,
    IReadOnlyList<string> Evidence)
{
    public string Display =>
        $"{Target.RelativePath}  ·  {Target.VideoCount} 视频  ·  {Score:P0}";
}
