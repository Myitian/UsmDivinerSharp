namespace UsmDivinerSharp;

public sealed class CrackReport
{
    public int VideoBlocksFound { get; init; }
    public int? ChunksSeen { get; init; }
    public string? Reason { get; init; }
    public string? Solver { get; init; }
    public int? SolverScore { get; init; }
    public int? Samples { get; init; }
    public bool FastEnabled { get; init; }
    public int VideoCrackBytesLimit { get; init; }
    public int VideoCrackBytesUsed { get; init; }
    public int Bigram00Weight { get; init; }
    public int BigramFFWeight { get; init; }
    public int OddBigram00 { get; init; }
    public int OddBigramFF { get; init; }
    public double OddBigramRatio { get; init; }
    public int BeamSize { get; init; }
    public int L1BeamSize { get; init; }
    public Vp9Report? Vp9 { get; init; }
}