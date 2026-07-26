namespace UsmDivinerSharp;

public readonly struct ConflictCounts
{
    public SortedDictionary<string, int> SameTrust { get; init; }
    public SortedDictionary<string, int> CrossTrust { get; init; }
}