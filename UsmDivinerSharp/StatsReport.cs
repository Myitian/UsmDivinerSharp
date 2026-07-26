namespace UsmDivinerSharp;

public sealed class StatsReport
{
    public int AttemptedFrames { get; init; }
    public int MatchedFrames { get; init; }
    public int PlaintextConstraints { get; init; }
    public Dictionary<string, Dictionary<ConstraintTrust, int>>? ReasonCounts { get; init; }
    public SortedDictionary<string, int>? ExtractorCounts { get; init; }
    public ConflictCounts ConflictCounts { get; init; }
    public ConstraintTrust DisabledTrustThreshold { get; init; }
    public string[]? DisabledNames { get; init; }
}